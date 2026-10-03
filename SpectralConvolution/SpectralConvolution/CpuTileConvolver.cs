using System.Runtime.ExceptionServices;
using ComputeWeave;

namespace SpectralConvolution;

internal sealed class CpuTileConvolver : IDisposable
{
    public const long WorkingBudgetBytes = 64L << 20;
    const int Channels = 4;

    readonly Worker[] workers;
    readonly Thread[] threads;
    readonly SemaphoreSlim start = new(0);
    readonly CountdownEvent countdown = new(1);
    bool disposed;
    int[] reversal = [];
    int reversalSize;
    int next;
    int claimed;
    int workingSize;
    Exception? failure;
    byte[] source = [];
    int sourceX;
    int sourceY;
    int sourceWidth;
    int sourceHeight;
    KernelSpectrum? spectrum;
    TilePlan plan;
    byte[] output = [];
    float gain;
    float[]? convolved;

    public CpuTileConvolver()
        : this(Environment.ProcessorCount)
    {
    }

    public CpuTileConvolver(int threadCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threadCount);
        workers = new Worker[threadCount];
        for (var index = 0; index < threadCount; index++)
            workers[index] = new Worker();
        threads = new Thread[threadCount - 1];
        for (var index = 0; index < threads.Length; index++)
        {
            threads[index] = new Thread(Serve) { IsBackground = true, Name = nameof(CpuTileConvolver) };
            threads[index].Start();
        }
    }

    public int Threads => workers.Length;

    public long WorkingBytes
    {
        get
        {
            var bytes = 0L;
            foreach (var worker in workers)
                bytes += worker.Bytes;
            return bytes;
        }
    }

    public static int ActiveThreads(int threads, int size)
        => (int)Math.Clamp(WorkingBudgetBytes / ((long)size * size * Channels * sizeof(float)), 1L, threads);

    public void Convolve(
        byte[] sourcePixels,
        int sourceLeft,
        int sourceTop,
        int sourceColumns,
        int sourceRows,
        KernelSpectrum kernelSpectrum,
        in TilePlan tilePlan,
        byte[] outputPixels,
        float outputGain,
        float[]? convolvedValues)
    {
        ArgumentNullException.ThrowIfNull(sourcePixels);
        ArgumentNullException.ThrowIfNull(kernelSpectrum);
        ArgumentNullException.ThrowIfNull(outputPixels);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceColumns);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceRows);
        if (sourcePixels.Length < (long)sourceColumns * sourceRows * Channels)
            throw new ArgumentException(null, nameof(sourcePixels));
        if (kernelSpectrum.Size != tilePlan.Size || kernelSpectrum.Radius != tilePlan.Radius)
            throw new ArgumentException(null, nameof(kernelSpectrum));
        var regionLength = (long)tilePlan.RegionWidth * tilePlan.RegionHeight * Channels;
        if (outputPixels.Length < regionLength)
            throw new ArgumentException(null, nameof(outputPixels));
        if (convolvedValues is not null && convolvedValues.Length < regionLength)
            throw new ArgumentException(null, nameof(convolvedValues));
        if (!float.IsFinite(outputGain))
            throw new ArgumentOutOfRangeException(nameof(outputGain));

        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureReversal(tilePlan.Size, tilePlan.Log2Size);
        EnsureWorkingSize(tilePlan.Size);
        var active = Math.Min(ActiveThreads(workers.Length, tilePlan.Size), tilePlan.TileCount);

        source = sourcePixels;
        sourceX = sourceLeft;
        sourceY = sourceTop;
        sourceWidth = sourceColumns;
        sourceHeight = sourceRows;
        spectrum = kernelSpectrum;
        plan = tilePlan;
        output = outputPixels;
        gain = outputGain;
        convolved = convolvedValues;
        next = 0;
        claimed = 0;
        failure = null;
        countdown.Reset(active);
        try
        {
            if (active > 1)
                start.Release(active - 1);
            Drain();
            countdown.Wait();
        }
        finally
        {
            source = [];
            spectrum = null;
            output = [];
            convolved = null;
        }

        if (failure is { } exception)
            ExceptionDispatchInfo.Throw(exception);
    }

    public static byte ToUnorm(float value)
    {
        if (float.IsNaN(value))
            return 0;
        return (byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (threads.Length > 0)
            start.Release(threads.Length);
        foreach (var thread in threads)
            thread.Join();
        start.Dispose();
        countdown.Dispose();
    }

    void Serve()
    {
        while (true)
        {
            start.Wait();
            if (disposed)
                return;
            Drain();
        }
    }

    void EnsureReversal(int size, int log2)
    {
        if (reversalSize == size)
            return;
        if (reversal.Length < size)
            reversal = new int[size];
        var shift = 32 - log2;
        for (var index = 0; index < size; index++)
            reversal[index] = (int)(ReverseBits((uint)index) >> shift);
        reversalSize = size;
    }

    void EnsureWorkingSize(int size)
    {
        if (workingSize == size)
            return;
        foreach (var worker in workers)
            worker.Release();
        workingSize = size;
    }

    void Drain()
    {
        try
        {
            var worker = workers[Interlocked.Increment(ref claimed) - 1];
            worker.Prepare(plan.Size);
            int tile;
            while ((tile = Interlocked.Increment(ref next) - 1) < plan.TileCount)
                ConvolveTile(tile, worker);
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(ref failure, exception, null);
        }
        finally
        {
            countdown.Signal();
        }
    }

    void ConvolveTile(int tile, Worker worker)
    {
        var size = plan.Size;
        var log2 = plan.Log2Size;
        var radius = plan.Radius;
        var left = plan.OriginX(tile) - radius;
        var top = plan.OriginY(tile) - radius;
        var rowLength = size * Channels;
        var work = worker.Tile.AsSpan(0, size * rowLength);
        var line = worker.Line.AsSpan(0, rowLength);
        var spare = worker.Spare.AsSpan(0, rowLength);
        var order = reversal.AsSpan(0, size);
        var twiddles = spectrum!.Twiddles;
        var values = spectrum.Spectrum;

        var lit = false;
        var first = Math.Max(0, sourceX - left);
        var last = Math.Min(size, sourceX + sourceWidth - left);
        for (var y = 0; y < size; y++)
        {
            var row = work.Slice(y * rowLength, rowLength);
            var sourceRow = top + y - sourceY;
            if (sourceRow < 0 || sourceRow >= sourceHeight || first >= last || !LoadRow(line, order, sourceRow, left, first, last))
            {
                row.Clear();
                continue;
            }

            lit = true;
            Butterflies(line, log2, twiddles, 1f);
            line.CopyTo(row);
        }

        var regionLeft = plan.OriginX(tile) - plan.RegionX;
        var regionTop = plan.OriginY(tile) - plan.RegionY;
        var validWidth = plan.ValidWidth(tile);
        var validHeight = plan.ValidHeight(tile);
        if (!lit)
        {
            ClearValid(regionLeft, regionTop, validWidth, validHeight);
            return;
        }

        for (var column = 0; column < size; column++)
        {
            for (var y = 0; y < size; y++)
                work.Slice((y * size + column) * Channels, Channels).CopyTo(line.Slice(order[y] * Channels, Channels));

            Butterflies(line, log2, twiddles, 1f);
            var spectrumColumn = values.Slice(column * size, size);
            for (var frequency = 0; frequency < size; frequency++)
            {
                var weight = spectrumColumn[frequency];
                var from = frequency * Channels;
                var to = order[frequency] * Channels;
                var value0 = line[from];
                var value1 = line[from + 1];
                var value2 = line[from + 2];
                var value3 = line[from + 3];
                spare[to] = value0 * weight.X - value1 * weight.Y;
                spare[to + 1] = value0 * weight.Y + value1 * weight.X;
                spare[to + 2] = value2 * weight.X - value3 * weight.Y;
                spare[to + 3] = value2 * weight.Y + value3 * weight.X;
            }

            Butterflies(spare, log2, twiddles, -1f);
            for (var y = 0; y < size; y++)
                spare.Slice(y * Channels, Channels).CopyTo(work.Slice((y * size + column) * Channels, Channels));
        }

        var scale = 1f / (size * size);
        for (var y = radius; y < radius + validHeight; y++)
        {
            var row = work.Slice(y * rowLength, rowLength);
            for (var x = 0; x < size; x++)
                row.Slice(x * Channels, Channels).CopyTo(line.Slice(order[x] * Channels, Channels));

            Butterflies(line, log2, twiddles, -1f);
            var outputRow = regionTop + y - radius;
            for (var x = radius; x < radius + validWidth; x++)
            {
                var red = line[x * Channels] * scale;
                var green = line[x * Channels + 1] * scale;
                var blue = line[x * Channels + 2] * scale;
                var alpha = line[x * Channels + 3] * scale;
                var index = (outputRow * plan.RegionWidth + regionLeft + x - radius) * Channels;
                output[index] = ToUnorm(blue * gain);
                output[index + 1] = ToUnorm(green * gain);
                output[index + 2] = ToUnorm(red * gain);
                output[index + 3] = ToUnorm(alpha * gain);
                if (convolved is { } store)
                {
                    store[index] = red;
                    store[index + 1] = green;
                    store[index + 2] = blue;
                    store[index + 3] = alpha;
                }
            }
        }
    }

    bool LoadRow(Span<float> line, ReadOnlySpan<int> order, int sourceRow, int left, int first, int last)
    {
        line.Clear();
        var lit = false;
        var pixels = source.AsSpan(sourceRow * sourceWidth * Channels, sourceWidth * Channels);
        for (var x = first; x < last; x++)
        {
            var offset = (left + x - sourceX) * Channels;
            var blue = pixels[offset];
            var green = pixels[offset + 1];
            var red = pixels[offset + 2];
            var alpha = pixels[offset + 3];
            if ((blue | green | red | alpha) == 0)
                continue;

            var target = order[x] * Channels;
            line[target] = red / 255f;
            line[target + 1] = green / 255f;
            line[target + 2] = blue / 255f;
            line[target + 3] = alpha / 255f;
            lit = true;
        }

        return lit;
    }

    void ClearValid(int regionLeft, int regionTop, int validWidth, int validHeight)
    {
        for (var y = 0; y < validHeight; y++)
        {
            var index = ((regionTop + y) * plan.RegionWidth + regionLeft) * Channels;
            output.AsSpan(index, validWidth * Channels).Clear();
            convolved?.AsSpan(index, validWidth * Channels).Clear();
        }
    }

    static void Butterflies(Span<float> line, int log2, ReadOnlySpan<Float2> twiddles, float direction)
    {
        var pairs = 1 << (log2 - 1);
        for (var stage = 0; stage < log2; stage++)
        {
            var half = 1 << stage;
            var shift = log2 - 1 - stage;
            for (var pair = 0; pair < pairs; pair++)
            {
                var position = pair & (half - 1);
                var even = (((pair >> stage) << (stage + 1)) + position) * Channels;
                var odd = even + half * Channels;
                var twiddle = twiddles[position << shift];
                var real = twiddle.X;
                var imaginary = twiddle.Y * direction;
                var odd0 = line[odd];
                var odd1 = line[odd + 1];
                var odd2 = line[odd + 2];
                var odd3 = line[odd + 3];
                var product0 = odd0 * real - odd1 * imaginary;
                var product1 = odd0 * imaginary + odd1 * real;
                var product2 = odd2 * real - odd3 * imaginary;
                var product3 = odd2 * imaginary + odd3 * real;
                var even0 = line[even];
                var even1 = line[even + 1];
                var even2 = line[even + 2];
                var even3 = line[even + 3];
                line[even] = even0 + product0;
                line[even + 1] = even1 + product1;
                line[even + 2] = even2 + product2;
                line[even + 3] = even3 + product3;
                line[odd] = even0 - product0;
                line[odd + 1] = even1 - product1;
                line[odd + 2] = even2 - product2;
                line[odd + 3] = even3 - product3;
            }
        }
    }

    static uint ReverseBits(uint value)
    {
        value = ((value >> 1) & 0x55555555u) | ((value & 0x55555555u) << 1);
        value = ((value >> 2) & 0x33333333u) | ((value & 0x33333333u) << 2);
        value = ((value >> 4) & 0x0F0F0F0Fu) | ((value & 0x0F0F0F0Fu) << 4);
        value = ((value >> 8) & 0x00FF00FFu) | ((value & 0x00FF00FFu) << 8);
        return (value >> 16) | (value << 16);
    }

    sealed class Worker
    {
        public float[] Tile { get; private set; } = [];

        public float[] Line { get; private set; } = [];

        public float[] Spare { get; private set; } = [];

        public long Bytes => ((long)Tile.Length + Line.Length + Spare.Length) * sizeof(float);

        public void Prepare(int size)
        {
            var rowLength = size * Channels;
            if (Tile.Length < size * rowLength)
                Tile = new float[size * rowLength];
            if (Line.Length < rowLength)
            {
                Line = new float[rowLength];
                Spare = new float[rowLength];
            }
        }

        public void Release()
        {
            Tile = [];
            Line = [];
            Spare = [];
        }
    }
}
