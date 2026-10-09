using ComputeWeave;

namespace SpectralConvolution;

internal readonly record struct GpuTileLayout(int GroupCount, int SampleCount, int KernelArea)
{
    public const int CountOffset = 0;
    public const int StatisticsOffset = 1;
    public const int StatisticsPerGroup = 6;
    public const int SumsPerGroup = 4;
    public const int ValuesPerSpot = 4;

    public int SumsOffset => StatisticsOffset + GroupCount * StatisticsPerGroup;

    public int SpotsOffset => SumsOffset + GroupCount * SumsPerGroup;

    public int GatheredOffset => SpotsOffset + SampleCount * ValuesPerSpot;

    public int Length => GatheredOffset + SampleCount * KernelArea;
}

internal readonly record struct GpuTileJob(
    TilePlan Plan,
    int SourceX,
    int SourceY,
    int SourceWidth,
    int SourceHeight,
    float Gain,
    int SampleCount,
    bool Store,
    LightOptions Light = default,
    bool Chromatic = false)
{
    public int KernelArea => (Plan.Radius * 2 + 1) * (Plan.Radius * 2 + 1);

    public GpuTileLayout Layout => new(Plan.GroupCount, SampleCount, KernelArea);
}

internal sealed class SpectrumStaging
{
    public Float2[] Values = [];
}

internal sealed class GpuTileConvolver : IDisposable
{
    public const int MaximumSamples = 16;
    public const int SampleTilesPerWord = 32;
    public const int WordsPerSampleEntry = 2;

    static readonly ScratchPool<SpectrumStaging> Staging = new(Environment.ProcessorCount);

    readonly GraphicsDevice device;
    readonly ReadWriteBuffer<Float4> spare;
    ReadOnlyBuffer<Int2> samples;
    Int2[] sampleStaging = [];
    ReadWriteBuffer<Float4>? tiles;
    ReadOnlyBuffer<Float2>? twiddles;
    ReadOnlyBuffer<Float2>? spectrum;
    ReadWriteBuffer<uint>? report;
    ReadBackBuffer<uint>? readBack;
    ReadWriteBuffer<Float4>? store;
    int spectrumSize;
    int spectrumLength;
    int spectrumRadius = -1;
    bool spectrumChromatic;

    public GpuTileConvolver(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        this.device = device;
        samples = device.AllocateReadOnlyBuffer<Int2>(MaximumSamples);
        spare = device.AllocateReadWriteBuffer<Float4>(1);
    }

    public ReadWriteBuffer<Float4> Tiles => tiles ?? throw new InvalidOperationException();

    public ReadOnlyBuffer<Float2> Twiddles => twiddles ?? throw new InvalidOperationException();

    public ReadOnlyBuffer<Float2> Spectrum => spectrum ?? throw new InvalidOperationException();

    public ReadWriteBuffer<uint> Report => report ?? throw new InvalidOperationException();

    public ReadOnlyBuffer<Int2> Samples => samples;

    public bool HasStore => store is not null;

    public void Upload(KernelSpectrum kernelSpectrum)
    {
        ArgumentNullException.ThrowIfNull(kernelSpectrum);
        if (kernelSpectrum.Size == 0)
            throw new ArgumentException(null, nameof(kernelSpectrum));

        EnsureSpectrumBuffers(kernelSpectrum.Size, kernelSpectrum.Size * kernelSpectrum.Size, false);
        twiddles!.CopyFrom(kernelSpectrum.Twiddles);
        spectrum!.CopyFrom(kernelSpectrum.Spectrum);
        spectrumRadius = kernelSpectrum.Radius;
    }

    public void Upload(ChromaticKernelSpectrum kernelSpectrum)
    {
        ArgumentNullException.ThrowIfNull(kernelSpectrum);
        if (kernelSpectrum.Size == 0)
            throw new ArgumentException(null, nameof(kernelSpectrum));

        var length = ChromaticKernelSpectrum.ChannelCount * kernelSpectrum.HalfColumns * kernelSpectrum.Size;
        EnsureSpectrumBuffers(kernelSpectrum.Size, length, true);
        using var lease = Staging.Rent();
        var staging = lease.Value;
        if (staging.Values.Length < length)
            staging.Values = new Float2[length];
        kernelSpectrum.CopyHalfSpectra(staging.Values);
        twiddles!.CopyFrom(kernelSpectrum.Twiddles);
        spectrum!.CopyFrom(staging.Values.AsSpan(0, length));
        spectrumRadius = kernelSpectrum.Radius;
    }

    void EnsureSpectrumBuffers(int size, int length, bool chromatic)
    {
        spectrumRadius = -1;
        if (spectrumSize == size && spectrumLength == length && spectrumChromatic == chromatic)
            return;

        twiddles?.Dispose();
        spectrum?.Dispose();
        twiddles = null;
        spectrum = null;
        spectrumSize = 0;
        twiddles = device.AllocateReadOnlyBuffer<Float2>(size / 2);
        spectrum = device.AllocateReadOnlyBuffer<Float2>(length);
        spectrumSize = size;
        spectrumLength = length;
        spectrumChromatic = chromatic;
    }

    public GpuTileJob Prepare(
        in TilePlan plan,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        float gain,
        ReadOnlySpan<Int2> sampleValues,
        bool storeValues,
        LightOptions light = default,
        bool chromatic = false)
    {
        if (plan.Size != spectrumSize || plan.Radius != spectrumRadius || chromatic != spectrumChromatic)
            throw new InvalidOperationException();
        light.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        if (!float.IsFinite(gain))
            throw new ArgumentOutOfRangeException(nameof(gain));
        if (sampleValues.Length > MaximumSamples)
            throw new ArgumentOutOfRangeException(nameof(sampleValues));
        foreach (var sample in sampleValues)
        {
            if (sample.X < 0 || sample.X >= plan.RegionWidth || sample.Y < 0 || sample.Y >= plan.RegionHeight)
                throw new ArgumentOutOfRangeException(nameof(sampleValues));
        }

        var job = new GpuTileJob(plan, sourceX, sourceY, sourceWidth, sourceHeight, gain, sampleValues.Length, storeValues, light, chromatic);
        var tileLength = (long)plan.BatchTiles * plan.TileElements;
        if (tiles is null || tiles.Length < tileLength)
        {
            tiles?.Dispose();
            tiles = null;
            tiles = device.AllocateReadWriteBuffer<Float4>(checked((int)tileLength));
        }

        var reportLength = job.Layout.Length;
        if (report is null || report.Length < reportLength)
        {
            report?.Dispose();
            readBack?.Dispose();
            report = null;
            readBack = null;
            report = device.AllocateReadWriteBuffer<uint>(reportLength);
            readBack = device.AllocateReadBackBuffer<uint>(reportLength);
        }

        if (storeValues)
        {
            var storeLength = checked(plan.RegionWidth * plan.RegionHeight);
            if (store is null || store.Length < storeLength)
            {
                store?.Dispose();
                store = null;
                store = device.AllocateReadWriteBuffer<Float4>(storeLength);
            }
        }

        if (sampleValues.Length > 0)
            UploadSamples(sampleValues, plan);
        return job;
    }

    void UploadSamples(ReadOnlySpan<Int2> sampleValues, in TilePlan plan)
    {
        var length = MaximumSamples + (plan.TileCount + SampleTilesPerWord * WordsPerSampleEntry - 1) / (SampleTilesPerWord * WordsPerSampleEntry);
        if (samples.Length < length)
        {
            samples.Dispose();
            samples = device.AllocateReadOnlyBuffer<Int2>(length);
        }

        if (sampleStaging.Length < length)
            sampleStaging = new Int2[length];
        var staging = sampleStaging.AsSpan(0, length);
        staging.Clear();
        sampleValues.CopyTo(staging);
        foreach (var sample in sampleValues)
        {
            var tile = plan.TileAt(plan.RegionX + sample.X, plan.RegionY + sample.Y);
            var word = tile / SampleTilesPerWord;
            ref var entry = ref staging[MaximumSamples + word / WordsPerSampleEntry];
            if (word % WordsPerSampleEntry == 0)
                entry.X |= 1 << (tile % SampleTilesPerWord);
            else
                entry.Y |= 1 << (tile % SampleTilesPerWord);
        }

        samples.CopyFrom(staging);
    }

    public ReadWriteBuffer<Float4> StoreFor(in GpuTileJob job)
        => job.Store ? store ?? throw new InvalidOperationException() : spare;

    public ReadOnlySpan<uint> ReadReport(in GpuTileJob job)
    {
        var length = job.Layout.Length;
        if (report is null || readBack is null || report.Length < length)
            throw new InvalidOperationException();
        readBack.CopyFrom(report, 0, 0, length);
        return readBack.Span[..length];
    }

    public void ReadStore(in GpuTileJob job, Span<Float4> destination)
    {
        var length = job.Plan.RegionWidth * job.Plan.RegionHeight;
        if (!job.Store || store is null || destination.Length < length)
            throw new InvalidOperationException();
        store.CopyTo(destination[..length], 0);
    }

    public void ReleaseStore()
    {
        store?.Dispose();
        store = null;
    }

    public static void Record(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        ReadWriteBuffer<Float4> tiles,
        ReadOnlyBuffer<Float2> twiddles,
        ReadOnlyBuffer<Float2> spectrum,
        ReadWriteBuffer<uint> report,
        ReadOnlyBuffer<Int2> samples,
        ReadWriteBuffer<Float4> store,
        in GpuTileJob job)
    {
        var plan = job.Plan;
        var layout = job.Layout;
        context.For(1, new ReportResetShader(report));
        context.Barrier(report);
        if (job.SampleCount > 0)
        {
            context.For(layout.KernelArea, job.SampleCount, new GatherShader(
                source, samples, report, layout.GatheredOffset, plan.Radius, job.SampleCount,
                plan.RegionX, plan.RegionY, job.SourceX, job.SourceY, job.SourceWidth, job.SourceHeight));
        }

        var scale = 1f / plan.TileElements;
        for (var start = 0; start < plan.TileCount; start += plan.BatchTiles)
        {
            var count = Math.Min(plan.BatchTiles, plan.TileCount - start);
            var threads = count * plan.TileElements / 2;
            var columnThreads = job.Chromatic ? count * plan.ChromaticGroupsPerTile * TilePlan.GroupThreads : threads;
            var groupStart = start * plan.GroupsPerTile;
            if (job.Light.IsDefault)
            {
                context.For(threads, new ForwardRowShader(
                    source, tiles, twiddles, report, plan.Log2Size, plan.Radius, plan.ValidSize, plan.TilesX, start,
                    plan.RegionX, plan.RegionY, plan.RegionWidth, plan.RegionHeight,
                    job.SourceX, job.SourceY, job.SourceWidth, job.SourceHeight, GpuTileLayout.StatisticsOffset, groupStart));
                context.Barrier(tiles);
                RecordColumns(in context, tiles, twiddles, spectrum, plan.Log2Size, columnThreads, job.Chromatic);
                context.Barrier(tiles);
                context.For(threads, new InverseRowShader(
                    tiles, twiddles, output, report, samples, store, plan.Log2Size, plan.Radius, plan.ValidSize, plan.TilesX, start,
                    plan.RegionWidth, plan.RegionHeight, scale, job.Gain, job.SampleCount, layout.SumsOffset, layout.SpotsOffset,
                    groupStart, job.Store ? 1 : 0, MaximumSamples));
                context.Barrier(tiles);
            }
            else
            {
                var light = job.Light;
                context.For(threads, new ForwardRowLightShader(
                    source, tiles, twiddles, report, plan.Log2Size, plan.Radius, plan.ValidSize, plan.TilesX, start,
                    plan.RegionX, plan.RegionY, plan.RegionWidth, plan.RegionHeight,
                    job.SourceX, job.SourceY, job.SourceWidth, job.SourceHeight, GpuTileLayout.StatisticsOffset, groupStart,
                    light.Linear ? 1 : 0, light.Highlights ? 1 : 0, light.Threshold, light.Boost));
                context.Barrier(tiles);
                RecordColumns(in context, tiles, twiddles, spectrum, plan.Log2Size, columnThreads, job.Chromatic);
                context.Barrier(tiles);
                context.For(threads, new InverseRowLightShader(
                    tiles, twiddles, output, report, samples, store, plan.Log2Size, plan.Radius, plan.ValidSize, plan.TilesX, start,
                    plan.RegionX, plan.RegionY, plan.RegionWidth, plan.RegionHeight, scale, job.Gain, job.SampleCount, layout.SumsOffset, layout.SpotsOffset,
                    groupStart, job.Store ? 1 : 0, light.Linear ? 1 : 0, light.Dither ? 1 : 0, MaximumSamples));
                context.Barrier(tiles);
            }
        }

        context.Barrier(report);
    }

    static void RecordColumns(
        in ComputeContext context,
        ReadWriteBuffer<Float4> tiles,
        ReadOnlyBuffer<Float2> twiddles,
        ReadOnlyBuffer<Float2> spectrum,
        int log2Size,
        int threads,
        bool chromatic)
    {
        if (chromatic)
            context.For(threads, new ColumnChromaticShader(tiles, twiddles, spectrum, log2Size));
        else
            context.For(threads, new ColumnShader(tiles, twiddles, spectrum, log2Size));
    }

    public static void RecordStored(
        in ComputeContext context,
        ReadWriteBuffer<Float4> store,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        float gain)
        => context.For(width, height, new StoredRenderShader(store, output, width, height, gain));

    public static void RecordStored(
        in ComputeContext context,
        ReadWriteBuffer<Float4> store,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        float gain,
        in LightOptions light,
        int originX,
        int originY)
    {
        if (light.IsDefault)
            context.For(width, height, new StoredRenderShader(store, output, width, height, gain));
        else
            context.For(width, height, new StoredRenderLightShader(store, output, width, height, gain, light.Linear ? 1 : 0, light.Dither ? 1 : 0, originX, originY));
    }

    public void Dispose()
    {
        tiles?.Dispose();
        twiddles?.Dispose();
        spectrum?.Dispose();
        report?.Dispose();
        readBack?.Dispose();
        store?.Dispose();
        samples.Dispose();
        spare.Dispose();
        tiles = null;
        twiddles = null;
        spectrum = null;
        report = null;
        readBack = null;
        store = null;
        spectrumSize = 0;
        spectrumLength = 0;
        spectrumRadius = -1;
        spectrumChromatic = false;
    }
}
