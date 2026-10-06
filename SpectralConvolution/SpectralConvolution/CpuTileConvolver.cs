using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using ComputeWeave;

namespace SpectralConvolution;

internal sealed class CpuTileConvolver : IDisposable
{
    public const long WorkingBudgetBytes = 64L << 20;
    public const int StoredChunkPixels = 16384;
    const int Channels = 4;

    static readonly float[] Units = BuildUnits();

    readonly Worker[] workers;
    readonly Thread[] threads;
    readonly SemaphoreSlim start = new(0);
    readonly SemaphoreSlim finished = new(0);
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
    LightOptions light;
    float[]? convolved;
    float[]? stored;
    int storedPixels;
    int storedChunks;
    int storedWidth;
    int storedOriginX;
    int storedOriginY;

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
        float[]? convolvedValues,
        LightOptions lightOptions = default)
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
        lightOptions.Validate();

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
        light = lightOptions;
        convolved = convolvedValues;
        try
        {
            Dispatch(active);
        }
        finally
        {
            source = [];
            spectrum = null;
            output = [];
            convolved = null;
        }
    }

    public void RenderStored(
        float[] storedValues,
        int pixels,
        float outputGain,
        byte[] outputPixels,
        LightOptions lightOptions = default,
        int regionWidth = 0,
        int regionX = 0,
        int regionY = 0)
    {
        ArgumentNullException.ThrowIfNull(storedValues);
        ArgumentNullException.ThrowIfNull(outputPixels);
        ArgumentOutOfRangeException.ThrowIfNegative(pixels);
        if (storedValues.Length < (long)pixels * Channels)
            throw new ArgumentException(null, nameof(storedValues));
        if (outputPixels.Length < (long)pixels * Channels)
            throw new ArgumentException(null, nameof(outputPixels));
        if (!float.IsFinite(outputGain))
            throw new ArgumentOutOfRangeException(nameof(outputGain));
        lightOptions.Validate();
        if (lightOptions.Dither && pixels > 0)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(regionWidth);

        ObjectDisposedException.ThrowIf(disposed, this);
        var chunks = (pixels + StoredChunkPixels - 1) / StoredChunkPixels;
        if (chunks == 0)
            return;

        stored = storedValues;
        storedPixels = pixels;
        storedChunks = chunks;
        storedWidth = regionWidth;
        storedOriginX = regionX;
        storedOriginY = regionY;
        output = outputPixels;
        gain = outputGain;
        light = lightOptions;
        try
        {
            Dispatch(Math.Min(workers.Length, chunks));
        }
        finally
        {
            stored = null;
            output = [];
        }
    }

    void Dispatch(int active)
    {
        next = 0;
        claimed = 0;
        failure = null;
        if (active > 1)
            start.Release(active - 1);
        Drain();
        for (var index = 0; index < active; index++)
            finished.Wait();

        if (failure is { } exception)
            ExceptionDispatchInfo.Throw(exception);
    }

    static float[] BuildUnits()
    {
        var units = new float[256];
        for (var level = 0; level < units.Length; level++)
            units[level] = level / 255f;
        return units;
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
        finished.Dispose();
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
            if (stored is { } values)
            {
                int chunk;
                while ((chunk = Interlocked.Increment(ref next) - 1) < storedChunks)
                    RenderChunk(values, chunk);
            }
            else
            {
                var worker = workers[Interlocked.Increment(ref claimed) - 1];
                worker.Prepare(plan.Size);
                int tile;
                while ((tile = Interlocked.Increment(ref next) - 1) < plan.TileCount)
                    ConvolveTile(tile, worker);
            }
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(ref failure, exception, null);
        }
        finally
        {
            finished.Release();
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
            if (sourceRow < 0 || sourceRow >= sourceHeight || first >= last || !LoadRow(row, order, sourceRow, left, first, last))
            {
                row.Clear();
                continue;
            }

            lit = true;
            Butterflies(row, log2, twiddles, 1f);
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

        ref var workStart = ref MemoryMarshal.GetReference(work);
        ref var lineStart = ref MemoryMarshal.GetReference(line);
        ref var spareStart = ref MemoryMarshal.GetReference(spare);
        for (var column = 0; column < size; column++)
        {
            for (var y = 0; y < size; y++)
                Copy(ref workStart, (y * size + column) * Channels, ref lineStart, order[y] * Channels);

            Butterflies(line, log2, twiddles, 1f);
            Multiply(line, spare, order, values.Slice(column * size, size));
            Butterflies(spare, log2, twiddles, -1f);
            for (var y = 0; y < size; y++)
                Copy(ref spareStart, y * Channels, ref workStart, (y * size + column) * Channels);
        }

        var scale = 1f / (size * size);
        var light = this.light;
        var outputOrigin = plan.RegionX + regionLeft;
        for (var y = radius; y < radius + validHeight; y++)
        {
            var row = work.Slice(y * rowLength, rowLength);
            ref var rowStart = ref MemoryMarshal.GetReference(row);
            for (var x = 0; x < size; x++)
                Copy(ref rowStart, x * Channels, ref lineStart, order[x] * Channels);

            Butterflies(line, log2, twiddles, -1f);
            var outputRow = regionTop + y - radius;
            var index = (outputRow * plan.RegionWidth + regionLeft) * Channels;
            var pixels = line.Slice(radius * Channels, validWidth * Channels);
            var bytes = output.AsSpan(index, validWidth * Channels);
            var store = convolved is { } convolvedValues ? convolvedValues.AsSpan(index, validWidth * Channels) : default;
            if (light.IsDefault)
                Emit(pixels, scale, gain, bytes, store);
            else
                Emit(pixels, scale, gain, in light, outputOrigin, plan.RegionY + outputRow, bytes, store);
        }
    }

    static void Emit(ReadOnlySpan<float> pixels, float scale, float gain, Span<byte> bytes, Span<float> store)
    {
        var count = pixels.Length / Channels;
        if (bytes.Length < count * Channels || (!store.IsEmpty && store.Length < count * Channels))
            throw new ArgumentException(null, nameof(bytes));

        ref var from = ref MemoryMarshal.GetReference(pixels);
        ref var to = ref MemoryMarshal.GetReference(bytes);
        ref var kept = ref MemoryMarshal.GetReference(store);
        var scaleVector = Vector128.Create(scale);
        var gainVector = Vector128.Create(gain);
        var keep = !store.IsEmpty;
        for (var pixel = 0; pixel < count; pixel++)
        {
            var value = Vector128.LoadUnsafe(ref from, (nuint)(pixel * Channels)) * scaleVector;
            if (keep)
                value.StoreUnsafe(ref kept, (nuint)(pixel * Channels));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref to, pixel * Channels), PackBgra(value * gainVector));
        }
    }

    static void Emit(ReadOnlySpan<float> pixels, float scale, float gain, in LightOptions light, int x, int y, Span<byte> bytes, Span<float> store)
    {
        var count = pixels.Length / Channels;
        if (bytes.Length < count * Channels || (!store.IsEmpty && store.Length < count * Channels))
            throw new ArgumentException(null, nameof(bytes));

        ref var from = ref MemoryMarshal.GetReference(pixels);
        ref var to = ref MemoryMarshal.GetReference(bytes);
        ref var kept = ref MemoryMarshal.GetReference(store);
        var scaleVector = Vector128.Create(scale);
        var keep = !store.IsEmpty;
        for (var pixel = 0; pixel < count; pixel++)
        {
            var value = Vector128.LoadUnsafe(ref from, (nuint)(pixel * Channels)) * scaleVector;
            if (keep)
                value.StoreUnsafe(ref kept, (nuint)(pixel * Channels));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref to, pixel * Channels), LightTransform.ToPacked(value, gain, in light, x + pixel, y));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint PackBgra(Vector128<float> rgba)
    {
        var ordered = Vector128.Shuffle(rgba, Vector128.Create(2, 1, 0, 3));
        ordered = Vector128.ConditionalSelect(Vector128.Equals(ordered, ordered), ordered, Vector128<float>.Zero);
        var clamped = Vector128.Min(Vector128.Max(ordered, Vector128<float>.Zero), Vector128<float>.One);
        var levels = Vector128.ConvertToInt32(clamped * Vector128.Create(255f) + Vector128.Create(0.5f)).AsUInt32();
        var words = Vector128.Narrow(levels, levels);
        return Vector128.Narrow(words, words).AsUInt32().ToScalar();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Copy(ref float from, int fromIndex, ref float to, int toIndex)
        => Vector128.LoadUnsafe(ref from, (nuint)fromIndex).StoreUnsafe(ref to, (nuint)toIndex);

    static void Multiply(ReadOnlySpan<float> line, Span<float> spare, ReadOnlySpan<int> order, ReadOnlySpan<Float2> weights)
    {
        var size = weights.Length;
        if (line.Length < size * Channels || spare.Length < size * Channels || order.Length < size)
            throw new ArgumentException(null, nameof(weights));

        ref var from = ref MemoryMarshal.GetReference(line);
        ref var to = ref MemoryMarshal.GetReference(spare);
        for (var frequency = 0; frequency < size; frequency++)
        {
            var weight = weights[frequency];
            var value = Vector128.LoadUnsafe(ref from, (nuint)(frequency * Channels));
            var product = value * Vector128.Create(weight.X)
                + Vector128.Shuffle(value, Vector128.Create(1, 0, 3, 2)) * Vector128.Create(-weight.Y, weight.Y, -weight.Y, weight.Y);
            product.StoreUnsafe(ref to, (nuint)(order[frequency] * Channels));
        }
    }

    void RenderChunk(float[] values, int chunk)
    {
        var first = chunk * StoredChunkPixels;
        var count = Math.Min(first + StoredChunkPixels, storedPixels) - first;
        var light = this.light;
        var pixels = values.AsSpan(first * Channels, count * Channels);
        var bytes = output.AsSpan(first * Channels, count * Channels);
        if (light.IsDefault)
        {
            Emit(pixels, 1f, gain, bytes, default);
            return;
        }

        var column = storedWidth > 0 ? first % storedWidth : 0;
        var row = storedWidth > 0 ? first / storedWidth : 0;
        for (var done = 0; done < count;)
        {
            var run = storedWidth > 0 ? Math.Min(count - done, storedWidth - column) : count - done;
            Emit(pixels.Slice(done * Channels, run * Channels), 1f, gain, in light, storedOriginX + column, storedOriginY + row,
                bytes.Slice(done * Channels, run * Channels), default);
            done += run;
            column = 0;
            row++;
        }
    }

    bool LoadRow(Span<float> row, ReadOnlySpan<int> order, int sourceRow, int left, int first, int last)
    {
        row.Clear();
        var lit = false;
        var pixels = source.AsSpan(sourceRow * sourceWidth * Channels, sourceWidth * Channels);
        var light = this.light;
        var units = Units;
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
            if (light.Linear)
            {
                var (linearRed, linearGreen, linearBlue, linearAlpha) = LightTransform.FromBytes(red, green, blue, alpha, in light);
                row[target] = linearRed;
                row[target + 1] = linearGreen;
                row[target + 2] = linearBlue;
                row[target + 3] = linearAlpha;
            }
            else
            {
                row[target] = units[red];
                row[target + 1] = units[green];
                row[target + 2] = units[blue];
                row[target + 3] = units[alpha];
            }
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

    internal static void Butterflies(Span<float> line, int log2, ReadOnlySpan<Float2> twiddles, float direction, bool allowWide = true)
    {
        var size = 1 << log2;
        if (line.Length < size * Channels || twiddles.Length < size / 2)
            throw new ArgumentException(null, nameof(line));

        ref var data = ref MemoryMarshal.GetReference(line);
        ref var table = ref MemoryMarshal.GetReference(twiddles);
        for (var stage = 0; stage < log2; stage++)
        {
            var half = 1 << stage;
            var shift = log2 - 1 - stage;
            var distance = (nuint)(half * Channels);
            if (allowWide && Vector256.IsHardwareAccelerated && half >= 2)
            {
                StageWithPairedTwiddles(ref data, ref table, size, half, shift, direction, distance);
                continue;
            }

            for (var position = 0; position < half; position++)
            {
                var twiddle = Unsafe.Add(ref table, position << shift);
                var imaginaryPart = twiddle.Y * direction;
                var real = Vector128.Create(twiddle.X);
                var imaginary = Vector128.Create(-imaginaryPart, imaginaryPart, -imaginaryPart, imaginaryPart);
                for (var block = 0; block < size; block += half * 2)
                {
                    var even = (nuint)((block + position) * Channels);
                    var evenValue = Vector128.LoadUnsafe(ref data, even);
                    var oddValue = Vector128.LoadUnsafe(ref data, even + distance);
                    var product = oddValue * real + Vector128.Shuffle(oddValue, Vector128.Create(1, 0, 3, 2)) * imaginary;
                    (evenValue + product).StoreUnsafe(ref data, even);
                    (evenValue - product).StoreUnsafe(ref data, even + distance);
                }
            }
        }
    }

    static void StageWithPairedTwiddles(ref float data, ref Float2 table, int size, int half, int shift, float direction, nuint distance)
    {
        for (var position = 0; position < half; position += 2)
        {
            var near = Unsafe.Add(ref table, position << shift);
            var far = Unsafe.Add(ref table, (position + 1) << shift);
            var nearImaginary = near.Y * direction;
            var farImaginary = far.Y * direction;
            var real = Vector256.Create(near.X, near.X, near.X, near.X, far.X, far.X, far.X, far.X);
            var imaginary = Vector256.Create(-nearImaginary, nearImaginary, -nearImaginary, nearImaginary, -farImaginary, farImaginary, -farImaginary, farImaginary);
            for (var block = 0; block < size; block += half * 2)
            {
                var even = (nuint)((block + position) * Channels);
                var evenValue = Vector256.LoadUnsafe(ref data, even);
                var oddValue = Vector256.LoadUnsafe(ref data, even + distance);
                var product = oddValue * real + Vector256.Shuffle(oddValue, Vector256.Create(1, 0, 3, 2, 5, 4, 7, 6)) * imaginary;
                (evenValue + product).StoreUnsafe(ref data, even);
                (evenValue - product).StoreUnsafe(ref data, even + distance);
            }
        }
    }

    internal static void ButterfliesScalar(Span<float> line, int log2, ReadOnlySpan<Float2> twiddles, float direction)
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
