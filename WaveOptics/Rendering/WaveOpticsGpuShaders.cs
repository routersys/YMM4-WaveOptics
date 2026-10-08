using ComputeWeave;

namespace WaveOptics.Rendering;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FillIntShader(
    ReadWriteBuffer<int> values,
    int length,
    int value) : IComputeShader
{
    private readonly ReadWriteBuffer<int> values = values;
    private readonly int length = length;
    private readonly int value = value;

    public void Execute()
    {
        var index = ThreadIds.X;
        if (index >= length)
            return;
        values[index] = value;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SourceHashResetShader(
    ReadWriteBuffer<int> scratch) : IComputeShader
{
    private readonly ReadWriteBuffer<int> scratch = scratch;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        scratch[WaveOpticsSettings.ScratchLitCount] = 0;
        scratch[WaveOpticsSettings.ScratchBoundsMinX] = 2147483647;
        scratch[WaveOpticsSettings.ScratchBoundsMinY] = 2147483647;
        scratch[WaveOpticsSettings.ScratchBoundsMaxX] = -2147483648;
        scratch[WaveOpticsSettings.ScratchBoundsMaxY] = -2147483648;
        scratch[WaveOpticsSettings.ScratchHashSum] = 0;
        scratch[WaveOpticsSettings.ScratchHashMix] = 0;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SourceHashShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteBuffer<int> scratch,
    int sourceX,
    int sourceY,
    int sourceWidth,
    int sourceHeight) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int sourceX = sourceX;
    private readonly int sourceY = sourceY;
    private readonly int sourceWidth = sourceWidth;
    private readonly int sourceHeight = sourceHeight;

    public void Execute()
    {
        var first = ThreadIds.X * WaveOpticsSettings.SourceHashSpan;
        var y = ThreadIds.Y;
        if (first >= sourceWidth || y >= sourceHeight)
            return;

        var count = 0;
        var sum = 0;
        var mix = 0;
        var minimumX = 2147483647;
        var maximumX = -2147483648;
        var last = Hlsl.Min(first + WaveOpticsSettings.SourceHashSpan, sourceWidth);
        for (var x = first; x < last; x++)
        {
            var color = source[new Int2(x, y)];
            var quantized = SourceHashMath.Quantize(color);
            if (quantized == 0u)
                continue;

            var mixed = SourceHashMath.Mixed(y * sourceWidth + x, quantized);
            sum += (int)mixed;
            mix ^= (int)(mixed * SourceHashMath.AccumulateMultiplier);
            count++;
            minimumX = Hlsl.Min(minimumX, sourceX + x);
            maximumX = Hlsl.Max(maximumX, sourceX + x);
        }

        if (count == 0)
            return;
        Hlsl.InterlockedAdd(ref scratch[WaveOpticsSettings.ScratchHashSum], sum);
        Hlsl.InterlockedXor(ref scratch[WaveOpticsSettings.ScratchHashMix], mix);
        Hlsl.InterlockedAdd(ref scratch[WaveOpticsSettings.ScratchLitCount], count);
        Hlsl.InterlockedMin(ref scratch[WaveOpticsSettings.ScratchBoundsMinX], minimumX);
        Hlsl.InterlockedMin(ref scratch[WaveOpticsSettings.ScratchBoundsMinY], sourceY + y);
        Hlsl.InterlockedMax(ref scratch[WaveOpticsSettings.ScratchBoundsMaxX], maximumX);
        Hlsl.InterlockedMax(ref scratch[WaveOpticsSettings.ScratchBoundsMaxY], sourceY + y);
    }
}
