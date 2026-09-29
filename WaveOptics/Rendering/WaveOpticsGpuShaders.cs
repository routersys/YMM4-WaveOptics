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
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= sourceWidth || y >= sourceHeight)
            return;

        var color = source[ThreadIds.XY];
        var quantized = ((uint)(color.X * 255f + 0.5f) << 24)
            | ((uint)(color.Y * 255f + 0.5f) << 16)
            | ((uint)(color.Z * 255f + 0.5f) << 8)
            | (uint)(color.W * 255f + 0.5f);
        if (quantized == 0u)
            return;

        var mixed = ((uint)(y * sourceWidth + x) * 0x9E3779B9u) ^ (quantized * 0x85EBCA6Bu);
        mixed ^= mixed >> 16;
        mixed *= 0x85EBCA6Bu;
        mixed ^= mixed >> 13;
        Hlsl.InterlockedAdd(ref scratch[WaveOpticsSettings.ScratchHashSum], (int)mixed);
        Hlsl.InterlockedXor(ref scratch[WaveOpticsSettings.ScratchHashMix], (int)(mixed * 0xC2B2AE35u));
        Hlsl.InterlockedAdd(ref scratch[WaveOpticsSettings.ScratchLitCount], 1);
        Hlsl.InterlockedMin(ref scratch[WaveOpticsSettings.ScratchBoundsMinX], sourceX + x);
        Hlsl.InterlockedMin(ref scratch[WaveOpticsSettings.ScratchBoundsMinY], sourceY + y);
        Hlsl.InterlockedMax(ref scratch[WaveOpticsSettings.ScratchBoundsMaxX], sourceX + x);
        Hlsl.InterlockedMax(ref scratch[WaveOpticsSettings.ScratchBoundsMaxY], sourceY + y);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct HorizontalPassShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadOnlyBuffer<float> weights,
    ReadWriteBuffer<Float4> horizontal,
    int weightOffset,
    int radius,
    int regionX,
    int regionY,
    int regionWidth,
    int regionHeight,
    int canvasWidth,
    int sourceX,
    int sourceY,
    int sourceWidth) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadOnlyBuffer<float> weights = weights;
    private readonly ReadWriteBuffer<Float4> horizontal = horizontal;
    private readonly int weightOffset = weightOffset;
    private readonly int radius = radius;
    private readonly int regionX = regionX;
    private readonly int regionY = regionY;
    private readonly int regionWidth = regionWidth;
    private readonly int regionHeight = regionHeight;
    private readonly int canvasWidth = canvasWidth;
    private readonly int sourceX = sourceX;
    private readonly int sourceY = sourceY;
    private readonly int sourceWidth = sourceWidth;

    public void Execute()
    {
        if (ThreadIds.X >= regionWidth || ThreadIds.Y >= regionHeight)
            return;

        var x = regionX + ThreadIds.X;
        var y = regionY + ThreadIds.Y;
        var column = x - sourceX;
        var row = y - sourceY;
        var first = Hlsl.Max(-radius, -column);
        var last = Hlsl.Min(radius, sourceWidth - 1 - column);
        var sum = new Float4(0f, 0f, 0f, 0f);
        for (var offset = first; offset <= last; offset++)
            sum += source[new Int2(column + offset, row)] * weights[weightOffset + radius + offset];
        horizontal[y * canvasWidth + x] = sum;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct VerticalPassShader(
    ReadWriteBuffer<Float4> horizontal,
    ReadOnlyBuffer<float> weights,
    ReadWriteBuffer<Float4> convolved,
    int weightOffset,
    int radius,
    int rectX,
    int rectY,
    int rectWidth,
    int rectHeight,
    int canvasWidth,
    int sourceY,
    int sourceHeight,
    bool accumulate) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> horizontal = horizontal;
    private readonly ReadOnlyBuffer<float> weights = weights;
    private readonly ReadWriteBuffer<Float4> convolved = convolved;
    private readonly int weightOffset = weightOffset;
    private readonly int radius = radius;
    private readonly int rectX = rectX;
    private readonly int rectY = rectY;
    private readonly int rectWidth = rectWidth;
    private readonly int rectHeight = rectHeight;
    private readonly int canvasWidth = canvasWidth;
    private readonly int sourceY = sourceY;
    private readonly int sourceHeight = sourceHeight;
    private readonly bool accumulate = accumulate;

    public void Execute()
    {
        if (ThreadIds.X >= rectWidth || ThreadIds.Y >= rectHeight)
            return;

        var x = rectX + ThreadIds.X;
        var y = rectY + ThreadIds.Y;
        var row = y - sourceY;
        var first = Hlsl.Max(-radius, -row);
        var last = Hlsl.Min(radius, sourceHeight - 1 - row);
        var sum = new Float4(0f, 0f, 0f, 0f);
        for (var offset = first; offset <= last; offset++)
            sum += horizontal[(y + offset) * canvasWidth + x] * weights[weightOffset + radius + offset];
        var index = y * canvasWidth + x;
        convolved[index] = accumulate ? convolved[index] + sum : sum;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RenderShader(
    ReadWriteBuffer<Float4> convolved,
    ReadWriteTexture2D<Bgra32, Float4> output,
    int rectX,
    int rectY,
    int rectWidth,
    int rectHeight,
    int canvasWidth,
    float gain) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> convolved = convolved;
    private readonly ReadWriteTexture2D<Bgra32, Float4> output = output;
    private readonly int rectX = rectX;
    private readonly int rectY = rectY;
    private readonly int rectWidth = rectWidth;
    private readonly int rectHeight = rectHeight;
    private readonly int canvasWidth = canvasWidth;
    private readonly float gain = gain;

    public void Execute()
    {
        if (ThreadIds.X >= rectWidth || ThreadIds.Y >= rectHeight)
            return;

        output[ThreadIds.XY] = convolved[(rectY + ThreadIds.Y) * canvasWidth + rectX + ThreadIds.X] * gain;
    }
}
