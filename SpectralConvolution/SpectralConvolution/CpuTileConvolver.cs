using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using ComputeWeave;

namespace SpectralConvolution;

internal sealed class CpuTileConvolver : IDisposable
{
    public const long WorkingBudgetBytes = 64L << 20;
    public const int StoredChunkPixels = 16384;
    const int Channels = ByteColor.Channels;

    static readonly Lazy<Engine> SharedEngine = new(static () => new Engine(Environment.ProcessorCount));

    readonly Worker[] workers;
    readonly WorkerPool pool;
    readonly bool ownsEngine;
    readonly ConvolveJob convolveJob;
    readonly RenderJob renderJob;
    bool disposed;
    int[] reversal = [];
    int reversalSize;
    byte[] source = [];
    int sourceX;
    int sourceY;
    int sourceWidth;
    int sourceHeight;
    KernelSpectrum? spectrum;
    ChromaticKernelSpectrum? chromatic;
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
        : this(new Engine(threadCount), true)
    {
    }

    CpuTileConvolver(Engine engine, bool ownsEngine)
    {
        workers = engine.Workers;
        pool = engine.Pool;
        this.ownsEngine = ownsEngine;
        convolveJob = new ConvolveJob(this);
        renderJob = new RenderJob(this);
    }

    public static CpuTileConvolver CreateShared() => new(SharedEngine.Value, false);

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
        ArgumentNullException.ThrowIfNull(kernelSpectrum);
        if (kernelSpectrum.Size != tilePlan.Size || kernelSpectrum.Radius != tilePlan.Radius)
            throw new ArgumentException(null, nameof(kernelSpectrum));
        Convolve(sourcePixels, sourceLeft, sourceTop, sourceColumns, sourceRows, kernelSpectrum, null, in tilePlan, outputPixels, outputGain, convolvedValues, lightOptions);
    }

    public void Convolve(
        byte[] sourcePixels,
        int sourceLeft,
        int sourceTop,
        int sourceColumns,
        int sourceRows,
        ChromaticKernelSpectrum chromaticSpectrum,
        in TilePlan tilePlan,
        byte[] outputPixels,
        float outputGain,
        float[]? convolvedValues,
        LightOptions lightOptions = default)
    {
        ArgumentNullException.ThrowIfNull(chromaticSpectrum);
        if (chromaticSpectrum.Size != tilePlan.Size || chromaticSpectrum.Radius != tilePlan.Radius)
            throw new ArgumentException(null, nameof(chromaticSpectrum));
        Convolve(sourcePixels, sourceLeft, sourceTop, sourceColumns, sourceRows, null, chromaticSpectrum, in tilePlan, outputPixels, outputGain, convolvedValues, lightOptions);
    }

    void Convolve(
        byte[] sourcePixels,
        int sourceLeft,
        int sourceTop,
        int sourceColumns,
        int sourceRows,
        KernelSpectrum? kernelSpectrum,
        ChromaticKernelSpectrum? chromaticSpectrum,
        in TilePlan tilePlan,
        byte[] outputPixels,
        float outputGain,
        float[]? convolvedValues,
        LightOptions lightOptions)
    {
        ArgumentNullException.ThrowIfNull(sourcePixels);
        ArgumentNullException.ThrowIfNull(outputPixels);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceColumns);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceRows);
        if (sourcePixels.Length < (long)sourceColumns * sourceRows * Channels)
            throw new ArgumentException(null, nameof(sourcePixels));
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

        source = sourcePixels;
        sourceX = sourceLeft;
        sourceY = sourceTop;
        sourceWidth = sourceColumns;
        sourceHeight = sourceRows;
        spectrum = kernelSpectrum;
        chromatic = chromaticSpectrum;
        plan = tilePlan;
        output = outputPixels;
        gain = outputGain;
        light = lightOptions;
        convolved = convolvedValues;
        try
        {
            pool.Run(convolveJob, plan.TileCount, ActiveThreads(workers.Length, plan.Size));
        }
        finally
        {
            source = [];
            spectrum = null;
            chromatic = null;
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
            pool.Run(renderJob, chunks);
        }
        finally
        {
            stored = null;
            output = [];
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (ownsEngine)
            pool.Dispose();
    }

    void EnsureReversal(int size, int log2)
    {
        if (reversalSize == size)
            return;
        if (reversal.Length < size)
            reversal = new int[size];
        CpuFft.FillReversal(reversal.AsSpan(0, size), log2);
        reversalSize = size;
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
        var twiddles = chromatic is { } multi ? multi.Twiddles : spectrum!.Twiddles;
        var values = spectrum is { } single ? single.Spectrum : default;
        var light = this.light;

        var lit = false;
        var first = Math.Max(0, sourceX - left);
        var last = Math.Min(size, sourceX + sourceWidth - left);
        for (var y = 0; y < size; y++)
        {
            var row = work.Slice(y * rowLength, rowLength);
            var sourceRow = top + y - sourceY;
            if (sourceRow < 0 || sourceRow >= sourceHeight || first >= last || !CpuPixels.LoadRow(row, source.AsSpan(sourceRow * sourceWidth * Channels, sourceWidth * Channels), order, sourceX, left, first, last, in light))
            {
                row.Clear();
                continue;
            }

            lit = true;
            CpuFft.Butterflies(row, log2, twiddles, 1f);
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
        if (chromatic is not null)
            ConvolveColumnsChromatic(work, worker, size, log2, order, twiddles);
        else
        {
            for (var column = 0; column < size; column++)
            {
                for (var y = 0; y < size; y++)
                    Copy(ref workStart, (y * size + column) * Channels, ref lineStart, order[y] * Channels);

                CpuFft.Butterflies(line, log2, twiddles, 1f);
                Multiply(line, spare, order, values.Slice(column * size, size));
                CpuFft.Butterflies(spare, log2, twiddles, -1f);
                for (var y = 0; y < size; y++)
                    Copy(ref spareStart, y * Channels, ref workStart, (y * size + column) * Channels);
            }
        }

        var scale = 1f / (size * size);
        var outputOrigin = plan.RegionX + regionLeft;
        for (var y = radius; y < radius + validHeight; y++)
        {
            var row = work.Slice(y * rowLength, rowLength);
            ref var rowStart = ref MemoryMarshal.GetReference(row);
            for (var x = 0; x < size; x++)
                Copy(ref rowStart, x * Channels, ref lineStart, order[x] * Channels);

            CpuFft.Butterflies(line, log2, twiddles, -1f);
            var outputRow = regionTop + y - radius;
            var index = (outputRow * plan.RegionWidth + regionLeft) * Channels;
            var pixels = line.Slice(radius * Channels, validWidth * Channels);
            var bytes = output.AsSpan(index, validWidth * Channels);
            var store = convolved is { } convolvedValues ? convolvedValues.AsSpan(index, validWidth * Channels) : default;
            if (light.IsDefault)
                CpuPixels.Emit(pixels, scale, gain, bytes, store);
            else
                CpuPixels.Emit(pixels, scale, gain, in light, outputOrigin, plan.RegionY + outputRow, bytes, store);
        }
    }

    void ConvolveColumnsChromatic(Span<float> work, Worker worker, int size, int log2, ReadOnlySpan<int> order, ReadOnlySpan<Float2> twiddles)
    {
        var spectra = chromatic!;
        var rowLength = size * Channels;
        var redGreen = worker.Line.AsSpan(0, rowLength);
        var redGreenSpare = worker.Spare.AsSpan(0, rowLength);
        var blueAlpha = worker.BlueAlphaLine.AsSpan(0, rowLength);
        var blueAlphaSpare = worker.BlueAlphaSpare.AsSpan(0, rowLength);
        var mask = size - 1;
        ref var workStart = ref MemoryMarshal.GetReference(work);
        ref var redGreenStart = ref MemoryMarshal.GetReference(redGreen);
        ref var redGreenSpareStart = ref MemoryMarshal.GetReference(redGreenSpare);
        ref var blueAlphaStart = ref MemoryMarshal.GetReference(blueAlpha);
        ref var blueAlphaSpareStart = ref MemoryMarshal.GetReference(blueAlphaSpare);
        for (var column = 0; column < spectra.HalfColumns; column++)
        {
            var mirror = (size - column) & mask;
            for (var y = 0; y < size; y++)
            {
                var near = Vector128.LoadUnsafe(ref workStart, (nuint)((y * size + column) * Channels));
                var far = Vector128.LoadUnsafe(ref workStart, (nuint)((y * size + mirror) * Channels));
                SplitRedGreen(near, far).StoreUnsafe(ref redGreenStart, (nuint)(order[y] * Channels));
                SplitBlueAlpha(near, far).StoreUnsafe(ref blueAlphaStart, (nuint)(order[y] * Channels));
            }

            CpuFft.Butterflies(redGreen, log2, twiddles, 1f);
            CpuFft.Butterflies(blueAlpha, log2, twiddles, 1f);
            var green = spectra.Green.Spectrum.Slice(column * size, size);
            MultiplyPair(redGreen, redGreenSpare, order, spectra.Red.Spectrum.Slice(column * size, size), green);
            MultiplyPair(blueAlpha, blueAlphaSpare, order, spectra.Blue.Spectrum.Slice(column * size, size), green);
            CpuFft.Butterflies(redGreenSpare, log2, twiddles, -1f);
            CpuFft.Butterflies(blueAlphaSpare, log2, twiddles, -1f);
            for (var y = 0; y < size; y++)
            {
                var redGreenValue = Vector128.LoadUnsafe(ref redGreenSpareStart, (nuint)(y * Channels));
                var blueAlphaValue = Vector128.LoadUnsafe(ref blueAlphaSpareStart, (nuint)(y * Channels));
                MergeChannels(redGreenValue, blueAlphaValue, out var near, out var far);
                near.StoreUnsafe(ref workStart, (nuint)((y * size + column) * Channels));
                if (mirror != column)
                    far.StoreUnsafe(ref workStart, (nuint)((y * size + mirror) * Channels));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Vector128<float> SplitRedGreen(Vector128<float> near, Vector128<float> far)
        => Vector128.Create(near[0] + far[0], near[1] - far[1], near[1] + far[1], far[0] - near[0]) * Vector128.Create(0.5f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Vector128<float> SplitBlueAlpha(Vector128<float> near, Vector128<float> far)
        => Vector128.Create(near[2] + far[2], near[3] - far[3], near[3] + far[3], far[2] - near[2]) * Vector128.Create(0.5f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void MergeChannels(Vector128<float> redGreen, Vector128<float> blueAlpha, out Vector128<float> near, out Vector128<float> far)
    {
        near = Vector128.Create(redGreen[0] - redGreen[3], redGreen[1] + redGreen[2], blueAlpha[0] - blueAlpha[3], blueAlpha[1] + blueAlpha[2]);
        far = Vector128.Create(redGreen[0] + redGreen[3], redGreen[2] - redGreen[1], blueAlpha[0] + blueAlpha[3], blueAlpha[2] - blueAlpha[1]);
    }

    static void MultiplyPair(ReadOnlySpan<float> line, Span<float> spare, ReadOnlySpan<int> order, ReadOnlySpan<Float2> first, ReadOnlySpan<Float2> second)
    {
        var size = first.Length;
        if (line.Length < size * Channels || spare.Length < size * Channels || order.Length < size || second.Length != size)
            throw new ArgumentException(null, nameof(first));

        ref var from = ref MemoryMarshal.GetReference(line);
        ref var to = ref MemoryMarshal.GetReference(spare);
        for (var frequency = 0; frequency < size; frequency++)
        {
            var near = first[frequency];
            var far = second[frequency];
            var value = Vector128.LoadUnsafe(ref from, (nuint)(frequency * Channels));
            var product = CpuFft.Rotate(value, Vector128.Create(near.X, near.X, far.X, far.X), Vector128.Create(-near.Y, near.Y, -far.Y, far.Y));
            product.StoreUnsafe(ref to, (nuint)(order[frequency] * Channels));
        }
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
            var product = CpuFft.Rotate(value, Vector128.Create(weight.X), CpuFft.Imaginary(weight.Y));
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
            CpuPixels.Emit(pixels, 1f, gain, bytes, default);
            return;
        }

        var column = storedWidth > 0 ? first % storedWidth : 0;
        var row = storedWidth > 0 ? first / storedWidth : 0;
        for (var done = 0; done < count;)
        {
            var run = storedWidth > 0 ? Math.Min(count - done, storedWidth - column) : count - done;
            CpuPixels.Emit(pixels.Slice(done * Channels, run * Channels), 1f, gain, in light, storedOriginX + column, storedOriginY + row,
                bytes.Slice(done * Channels, run * Channels), default);
            done += run;
            column = 0;
            row++;
        }
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

    sealed class ConvolveJob(CpuTileConvolver owner) : IParallelJob
    {
        public void Begin(int worker) => owner.workers[worker].Prepare(owner.plan.Size, owner.chromatic is not null);

        public void Execute(int index, int worker) => owner.ConvolveTile(index, owner.workers[worker]);
    }

    sealed class RenderJob(CpuTileConvolver owner) : IParallelJob
    {
        public void Execute(int index, int worker) => owner.RenderChunk(owner.stored!, index);
    }

    sealed class Engine
    {
        public Engine(int threadCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threadCount);
            Workers = new Worker[threadCount];
            for (var index = 0; index < threadCount; index++)
                Workers[index] = new Worker();
            Pool = new WorkerPool(threadCount - 1);
        }

        public Worker[] Workers { get; }

        public WorkerPool Pool { get; }
    }

    sealed class Worker
    {
        public float[] Tile { get; private set; } = [];

        public float[] Line { get; private set; } = [];

        public float[] Spare { get; private set; } = [];

        public float[] BlueAlphaLine { get; private set; } = [];

        public float[] BlueAlphaSpare { get; private set; } = [];

        public long Bytes => ((long)Tile.Length + Line.Length + Spare.Length + BlueAlphaLine.Length + BlueAlphaSpare.Length) * sizeof(float);

        public void Prepare(int size, bool chromatic)
        {
            var rowLength = size * Channels;
            if (Tile.Length < size * rowLength)
                Tile = new float[size * rowLength];
            if (Line.Length < rowLength)
            {
                Line = new float[rowLength];
                Spare = new float[rowLength];
            }
            if (chromatic && BlueAlphaLine.Length < rowLength)
            {
                BlueAlphaLine = new float[rowLength];
                BlueAlphaSpare = new float[rowLength];
            }
        }
    }
}
