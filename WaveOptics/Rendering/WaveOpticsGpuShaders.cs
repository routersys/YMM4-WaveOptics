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
            var quantized = ((uint)(color.X * 255f + 0.5f) << 24)
                | ((uint)(color.Y * 255f + 0.5f) << 16)
                | ((uint)(color.Z * 255f + 0.5f) << 8)
                | (uint)(color.W * 255f + 0.5f);
            if (quantized == 0u)
                continue;

            var mixed = ((uint)(y * sourceWidth + x) * 0x9E3779B9u) ^ (quantized * 0x85EBCA6Bu);
            mixed ^= mixed >> 16;
            mixed *= 0x85EBCA6Bu;
            mixed ^= mixed >> 13;
            sum += (int)mixed;
            mix ^= (int)(mixed * 0xC2B2AE35u);
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
        var offsetX = ThreadIds.X * WaveOpticsSettings.ConvolutionBlock;
        if (offsetX >= regionWidth || ThreadIds.Y >= regionHeight)
            return;

        var x = regionX + offsetX;
        var y = regionY + ThreadIds.Y;
        var column = x - sourceX;
        var row = y - sourceY;
        var first = Hlsl.Max(-radius, -column);
        var last = Hlsl.Min(radius + WaveOpticsSettings.ConvolutionBlock - 1, sourceWidth - 1 - column);
        var weight0 = Weight(first - 1);
        var weight1 = Weight(first - 2);
        var weight2 = Weight(first - 3);
        var weight3 = Weight(first - 4);
        var weight4 = Weight(first - 5);
        var weight5 = Weight(first - 6);
        var weight6 = Weight(first - 7);
        var sum0 = new Float4(0f, 0f, 0f, 0f);
        var sum1 = sum0;
        var sum2 = sum0;
        var sum3 = sum0;
        var sum4 = sum0;
        var sum5 = sum0;
        var sum6 = sum0;
        var sum7 = sum0;
        for (var offset = first; offset <= last; offset++)
        {
            var weight7 = weight6;
            weight6 = weight5;
            weight5 = weight4;
            weight4 = weight3;
            weight3 = weight2;
            weight2 = weight1;
            weight1 = weight0;
            weight0 = Weight(offset);
            var texel = source[new Int2(column + offset, row)];
            sum0 += texel * weight0;
            sum1 += texel * weight1;
            sum2 += texel * weight2;
            sum3 += texel * weight3;
            sum4 += texel * weight4;
            sum5 += texel * weight5;
            sum6 += texel * weight6;
            sum7 += texel * weight7;
        }

        var index = y * canvasWidth + x;
        horizontal[index] = sum0;
        if (offsetX + 1 < regionWidth)
            horizontal[index + 1] = sum1;
        if (offsetX + 2 < regionWidth)
            horizontal[index + 2] = sum2;
        if (offsetX + 3 < regionWidth)
            horizontal[index + 3] = sum3;
        if (offsetX + 4 < regionWidth)
            horizontal[index + 4] = sum4;
        if (offsetX + 5 < regionWidth)
            horizontal[index + 5] = sum5;
        if (offsetX + 6 < regionWidth)
            horizontal[index + 6] = sum6;
        if (offsetX + 7 < regionWidth)
            horizontal[index + 7] = sum7;
    }

    private float Weight(int offset)
        => offset >= -radius && offset <= radius ? weights[weightOffset + radius + offset] : 0f;
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
        var offsetY = ThreadIds.Y * WaveOpticsSettings.ConvolutionBlock;
        if (ThreadIds.X >= rectWidth || offsetY >= rectHeight)
            return;

        var x = rectX + ThreadIds.X;
        var y = rectY + offsetY;
        var row = y - sourceY;
        var first = Hlsl.Max(-radius, -row);
        var last = Hlsl.Min(radius + WaveOpticsSettings.ConvolutionBlock - 1, sourceHeight - 1 - row);
        var weight0 = Weight(first - 1);
        var weight1 = Weight(first - 2);
        var weight2 = Weight(first - 3);
        var weight3 = Weight(first - 4);
        var weight4 = Weight(first - 5);
        var weight5 = Weight(first - 6);
        var weight6 = Weight(first - 7);
        var sum0 = new Float4(0f, 0f, 0f, 0f);
        var sum1 = sum0;
        var sum2 = sum0;
        var sum3 = sum0;
        var sum4 = sum0;
        var sum5 = sum0;
        var sum6 = sum0;
        var sum7 = sum0;
        for (var offset = first; offset <= last; offset++)
        {
            var weight7 = weight6;
            weight6 = weight5;
            weight5 = weight4;
            weight4 = weight3;
            weight3 = weight2;
            weight2 = weight1;
            weight1 = weight0;
            weight0 = Weight(offset);
            var value = horizontal[(y + offset) * canvasWidth + x];
            sum0 += value * weight0;
            sum1 += value * weight1;
            sum2 += value * weight2;
            sum3 += value * weight3;
            sum4 += value * weight4;
            sum5 += value * weight5;
            sum6 += value * weight6;
            sum7 += value * weight7;
        }

        var index = y * canvasWidth + x;
        Store(index, sum0);
        if (offsetY + 1 < rectHeight)
            Store(index + canvasWidth, sum1);
        if (offsetY + 2 < rectHeight)
            Store(index + canvasWidth * 2, sum2);
        if (offsetY + 3 < rectHeight)
            Store(index + canvasWidth * 3, sum3);
        if (offsetY + 4 < rectHeight)
            Store(index + canvasWidth * 4, sum4);
        if (offsetY + 5 < rectHeight)
            Store(index + canvasWidth * 5, sum5);
        if (offsetY + 6 < rectHeight)
            Store(index + canvasWidth * 6, sum6);
        if (offsetY + 7 < rectHeight)
            Store(index + canvasWidth * 7, sum7);
    }

    private float Weight(int offset)
        => offset >= -radius && offset <= radius ? weights[weightOffset + radius + offset] : 0f;

    private void Store(int index, Float4 sum)
    {
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
