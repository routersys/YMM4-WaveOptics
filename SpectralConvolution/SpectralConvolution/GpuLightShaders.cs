using ComputeWeave;

namespace SpectralConvolution;

internal static class LightShaderMath
{
    public static float Decode(float encoded)
    {
        if (encoded <= 0.04045f)
            return encoded / 12.92f;
        if (encoded >= 1f)
            return 1f;
        return Hlsl.Pow((encoded + 0.055f) / 1.055f, 2.4f);
    }

    public static float Encode(float linear)
    {
        if (linear <= 0.0031308f)
            return 12.92f * linear;
        if (linear >= 1f)
            return 1f;
        return 1.055f * Hlsl.Pow(linear, 0.41666666f) - 0.055f;
    }
}

internal static class LightShaderOutput
{
    public static float DitherThreshold(int x, int y)
    {
        var hash = (uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u;
        hash ^= hash >> 16;
        hash *= 0x7FEB352Du;
        hash ^= hash >> 15;
        hash *= 0x846CA68Bu;
        hash ^= hash >> 16;
        return (hash >> 8) * (1f / 16777216f);
    }

    public static float Quantize(float value, int dither, float threshold)
    {
        if (dither == 0)
            return Hlsl.Floor(value * 255f + 0.5f);
        return Hlsl.Min(Hlsl.Floor(value * 255f + threshold), 255f);
    }

    public static Float4 Convert(Float4 value, float gain, int linear, int dither, int x, int y)
    {
        var red = 0f;
        var green = 0f;
        var blue = 0f;
        var alpha = 0f;
        if (linear != 0)
        {
            alpha = Hlsl.Saturate(value.W * gain);
            if (alpha > 0f)
            {
                red = LightShaderMath.Encode(Hlsl.Min(Hlsl.Saturate(value.X * gain), alpha) / alpha) * alpha;
                green = LightShaderMath.Encode(Hlsl.Min(Hlsl.Saturate(value.Y * gain), alpha) / alpha) * alpha;
                blue = LightShaderMath.Encode(Hlsl.Min(Hlsl.Saturate(value.Z * gain), alpha) / alpha) * alpha;
            }
        }
        else
        {
            red = Hlsl.Saturate(value.X * gain);
            green = Hlsl.Saturate(value.Y * gain);
            blue = Hlsl.Saturate(value.Z * gain);
            alpha = Hlsl.Saturate(value.W * gain);
        }

        var threshold = 0f;
        if (dither != 0)
            threshold = DitherThreshold(x, y);
        return new Float4(
            Quantize(red, dither, threshold) / 255f,
            Quantize(green, dither, threshold) / 255f,
            Quantize(blue, dither, threshold) / 255f,
            Quantize(alpha, dither, threshold) / 255f);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
[CompileOptions(CompileOptions.Default | CompileOptions.IeeeStrictness)]
internal readonly partial struct StoredRenderLightShader(
    ReadWriteBuffer<Float4> store,
    ReadWriteTexture2D<Bgra32, Float4> output,
    int width,
    int height,
    float gain,
    int linear,
    int dither,
    int originX,
    int originY) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> store = store;
    private readonly ReadWriteTexture2D<Bgra32, Float4> output = output;
    private readonly int width = width;
    private readonly int height = height;
    private readonly float gain = gain;
    private readonly int linear = linear;
    private readonly int dither = dither;
    private readonly int originX = originX;
    private readonly int originY = originY;

    public void Execute()
    {
        if (ThreadIds.X >= width || ThreadIds.Y >= height)
            return;
        output[ThreadIds.XY] = LightShaderOutput.Convert(
            store[ThreadIds.Y * width + ThreadIds.X], gain, linear, dither, originX + ThreadIds.X, originY + ThreadIds.Y);
    }
}

[ThreadGroupSize(TilePlan.GroupThreads, 1, 1)]
[GeneratedComputeShaderDescriptor]
[CompileOptions(CompileOptions.Default | CompileOptions.IeeeStrictness)]
internal readonly partial struct ForwardRowLightShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteBuffer<Float4> tiles,
    ReadOnlyBuffer<Float2> twiddles,
    ReadWriteBuffer<uint> report,
    int log2Size,
    int radius,
    int validSize,
    int tilesX,
    int batchStart,
    int regionX,
    int regionY,
    int regionWidth,
    int regionHeight,
    int sourceX,
    int sourceY,
    int sourceWidth,
    int sourceHeight,
    int statisticsOffset,
    int groupStart,
    int linear,
    int highlights,
    float threshold,
    float boost) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteBuffer<Float4> tiles = tiles;
    private readonly ReadOnlyBuffer<Float2> twiddles = twiddles;
    private readonly ReadWriteBuffer<uint> report = report;
    private readonly int log2Size = log2Size;
    private readonly int radius = radius;
    private readonly int validSize = validSize;
    private readonly int tilesX = tilesX;
    private readonly int batchStart = batchStart;
    private readonly int regionX = regionX;
    private readonly int regionY = regionY;
    private readonly int regionWidth = regionWidth;
    private readonly int regionHeight = regionHeight;
    private readonly int sourceX = sourceX;
    private readonly int sourceY = sourceY;
    private readonly int sourceWidth = sourceWidth;
    private readonly int sourceHeight = sourceHeight;
    private readonly int statisticsOffset = statisticsOffset;
    private readonly int groupStart = groupStart;
    private readonly int linear = linear;
    private readonly int highlights = highlights;
    private readonly float threshold = threshold;
    private readonly float boost = boost;

    [GroupShared(TilePlan.GroupElements)]
    private static readonly Float4[] row = null!;

    [GroupShared(TilePlan.GroupThreads)]
    private static readonly Float4[] partialSums = null!;

    [GroupShared(TilePlan.GroupThreads)]
    private static readonly Float2[] partialSquares = null!;

    public void Execute()
    {
        var local = GroupIds.X;
        var groupBase = GridIds.X * TilePlan.GroupElements;
        var size = (int)(1u << log2Size);
        var mask = size - 1;

        var tileIndex = TileIndexOfGroup(groupBase);
        var tileRow = tileIndex / tilesX;
        var originX = regionX + (tileIndex - tileRow * tilesX) * validSize - radius;
        var originY = regionY + tileRow * validSize - radius;

        var sums = new Float4(0f, 0f, 0f, 0f);
        var squares = new Float2(0f, 0f);
        for (var pair = 0; pair < TilePlan.ValuesPerThread; pair++)
        {
            var element = local * TilePlan.ValuesPerThread + pair;
            var flat = groupBase + element;
            var rest = flat & (size * size - 1);
            var y = rest >> log2Size;
            var x = rest & mask;
            var canvasX = originX + x;
            var canvasY = originY + y;
            var value = new Float4(0f, 0f, 0f, 0f);
            var column = canvasX - sourceX;
            var line = canvasY - sourceY;
            if (column >= 0 && column < sourceWidth && line >= 0 && line < sourceHeight)
            {
                value = Transform(source[new Int2(column, line)]);
                squares = new Float2(
                    squares.X + (value.X * value.X + value.Y * value.Y),
                    squares.Y + (value.Z * value.Z + value.W * value.W));
                if (x >= radius && x < size - radius && y >= radius && y < size - radius
                    && canvasX < regionX + regionWidth && canvasY < regionY + regionHeight)
                    sums = sums + value;
            }

            var reversed = FftShaderMath.BitReverse(x, log2Size);
            row[((element >> log2Size) << log2Size) + reversed] = value;
        }

        partialSums[local] = sums;
        partialSquares[local] = squares;
        Hlsl.GroupMemoryBarrierWithGroupSync();
        for (var stride = TilePlan.GroupThreads / 2; stride > 0; stride >>= 1)
        {
            if (local < stride)
            {
                partialSums[local] = partialSums[local] + partialSums[local + stride];
                partialSquares[local] = partialSquares[local] + partialSquares[local + stride];
            }

            Hlsl.GroupMemoryBarrierWithGroupSync();
        }

        if (local == 0)
        {
            var offset = statisticsOffset + (groupStart + GridIds.X) * GpuTileLayout.StatisticsPerGroup;
            report[offset] = Hlsl.AsUInt(partialSums[0].X);
            report[offset + 1] = Hlsl.AsUInt(partialSums[0].Y);
            report[offset + 2] = Hlsl.AsUInt(partialSums[0].Z);
            report[offset + 3] = Hlsl.AsUInt(partialSums[0].W);
            report[offset + 4] = Hlsl.AsUInt(partialSquares[0].X);
            report[offset + 5] = Hlsl.AsUInt(partialSquares[0].Y);
        }

        Butterflies(local, FftShaderMath.Forward);

        var element0 = local * TilePlan.ValuesPerThread;
        var element1 = element0 + 1;
        tiles[groupBase + element0] = row[element0];
        tiles[groupBase + element1] = row[element1];
    }

    private Float4 Transform(Float4 value)
    {
        if (linear == 0)
            return value;

        var red = (float)(uint)(value.X * 255f + 0.5f);
        var green = (float)(uint)(value.Y * 255f + 0.5f);
        var blue = (float)(uint)(value.Z * 255f + 0.5f);
        var alpha = (float)(uint)(value.W * 255f + 0.5f);
        if (alpha == 0f)
            return new Float4(0f, 0f, 0f, 0f);

        var opacity = alpha / 255f;
        var straightRed = red / 255f;
        var straightGreen = green / 255f;
        var straightBlue = blue / 255f;
        if (alpha != 255f)
        {
            straightRed = Hlsl.Min(red / alpha, 1f);
            straightGreen = Hlsl.Min(green / alpha, 1f);
            straightBlue = Hlsl.Min(blue / alpha, 1f);
        }

        var linearRed = LightShaderMath.Decode(straightRed);
        var linearGreen = LightShaderMath.Decode(straightGreen);
        var linearBlue = LightShaderMath.Decode(straightBlue);
        var weight = 1f;
        if (highlights != 0)
        {
            var peak = Hlsl.Max(Hlsl.Max(straightRed, straightGreen), straightBlue);
            var excess = Hlsl.Min(Hlsl.Max((peak - threshold) / (1f - threshold), 0f), 1f);
            weight = 1f + (boost - 1f) * excess * excess;
        }

        var scale = opacity * weight;
        return new Float4(linearRed * scale, linearGreen * scale, linearBlue * scale, scale);
    }

    private int TileIndexOfGroup(int groupBase)
    {
        return batchStart + (groupBase >> (log2Size * 2));
    }

    private void Butterflies(int local, float direction)
    {
        var stage = 0;
        for (; stage + 1 < log2Size; stage += 2)
        {
            if (local < FftShaderMath.StageUnitsPerGroup)
            {
                var step = FftShaderMath.Pow2(stage);
                var position = FftShaderMath.Position(local, stage);
                var first = FftShaderMath.Radix4First(local, log2Size, stage);
                var second = first + step;
                var third = second + step;
                var fourth = third + step;
                var value0 = row[first];
                var value1 = row[second];
                var value2 = row[third];
                var value3 = row[fourth];
                FftShaderMath.Radix4(
                    ref value0,
                    ref value1,
                    ref value2,
                    ref value3,
                    twiddles[FftShaderMath.TwiddleIndex(position, log2Size, stage)],
                    twiddles[FftShaderMath.TwiddleIndex(position, log2Size, stage + 1)],
                    twiddles[FftShaderMath.TwiddleIndex(position + step, log2Size, stage + 1)],
                    direction);
                row[first] = value0;
                row[second] = value1;
                row[third] = value2;
                row[fourth] = value3;
            }

            Hlsl.GroupMemoryBarrierWithGroupSync();
        }

        for (; stage < log2Size; stage++)
        {
            var even = FftShaderMath.Radix2Even(local, log2Size, stage);
            var odd = even + FftShaderMath.Pow2(stage);
            var evenValue = row[even];
            var oddValue = row[odd];
            FftShaderMath.Radix2(
                ref evenValue,
                ref oddValue,
                twiddles[FftShaderMath.TwiddleIndex(FftShaderMath.Position(local, stage), log2Size, stage)],
                direction);
            row[even] = evenValue;
            row[odd] = oddValue;
            Hlsl.GroupMemoryBarrierWithGroupSync();
        }
    }
}

[ThreadGroupSize(TilePlan.GroupThreads, 1, 1)]
[GeneratedComputeShaderDescriptor]
[CompileOptions(CompileOptions.Default | CompileOptions.IeeeStrictness)]
internal readonly partial struct InverseRowLightShader(
    ReadWriteBuffer<Float4> tiles,
    ReadOnlyBuffer<Float2> twiddles,
    ReadWriteTexture2D<Bgra32, Float4> output,
    ReadWriteBuffer<uint> report,
    ReadOnlyBuffer<Int2> samples,
    ReadWriteBuffer<Float4> store,
    int log2Size,
    int radius,
    int validSize,
    int tilesX,
    int batchStart,
    int regionX,
    int regionY,
    int regionWidth,
    int regionHeight,
    float scale,
    float gain,
    int sampleCount,
    int sumsOffset,
    int spotsOffset,
    int groupStart,
    int storeValues,
    int linear,
    int dither,
    int sampleTilesOffset) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> tiles = tiles;
    private readonly ReadOnlyBuffer<Float2> twiddles = twiddles;
    private readonly ReadWriteTexture2D<Bgra32, Float4> output = output;
    private readonly ReadWriteBuffer<uint> report = report;
    private readonly ReadOnlyBuffer<Int2> samples = samples;
    private readonly ReadWriteBuffer<Float4> store = store;
    private readonly int log2Size = log2Size;
    private readonly int radius = radius;
    private readonly int validSize = validSize;
    private readonly int tilesX = tilesX;
    private readonly int batchStart = batchStart;
    private readonly int regionX = regionX;
    private readonly int regionY = regionY;
    private readonly int regionWidth = regionWidth;
    private readonly int regionHeight = regionHeight;
    private readonly float scale = scale;
    private readonly float gain = gain;
    private readonly int sampleCount = sampleCount;
    private readonly int sumsOffset = sumsOffset;
    private readonly int spotsOffset = spotsOffset;
    private readonly int groupStart = groupStart;
    private readonly int storeValues = storeValues;
    private readonly int linear = linear;
    private readonly int dither = dither;
    private readonly int sampleTilesOffset = sampleTilesOffset;

    [GroupShared(TilePlan.GroupElements)]
    private static readonly Float4[] row = null!;

    [GroupShared(TilePlan.GroupThreads)]
    private static readonly Float4[] partial = null!;

    public void Execute()
    {
        var local = GroupIds.X;
        var groupBase = GridIds.X * TilePlan.GroupElements;
        var size = (int)(1u << log2Size);
        var mask = size - 1;
        var element0 = local * TilePlan.ValuesPerThread;
        var element1 = element0 + 1;
        row[((element0 >> log2Size) << log2Size) + FftShaderMath.BitReverse(element0 & mask, log2Size)] = tiles[groupBase + element0];
        row[((element1 >> log2Size) << log2Size) + FftShaderMath.BitReverse(element1 & mask, log2Size)] = tiles[groupBase + element1];
        Hlsl.GroupMemoryBarrierWithGroupSync();

        Butterflies(local, FftShaderMath.Inverse);

        var tileIndex = TileIndexOfGroup(groupBase);
        var tileRow = tileIndex / tilesX;
        var originX = (tileIndex - tileRow * tilesX) * validSize - radius;
        var originY = tileRow * validSize - radius;
        var tileHoldsSample = sampleCount > 0 && TileHoldsSample(tileIndex);
        partial[local] = Emit(groupBase + element0, row[element0] * scale, size, mask, originX, originY, tileHoldsSample)
            + Emit(groupBase + element1, row[element1] * scale, size, mask, originX, originY, tileHoldsSample);
        Hlsl.GroupMemoryBarrierWithGroupSync();
        for (var stride = TilePlan.GroupThreads / 2; stride > 0; stride >>= 1)
        {
            if (local < stride)
                partial[local] = partial[local] + partial[local + stride];
            Hlsl.GroupMemoryBarrierWithGroupSync();
        }

        if (local == 0)
        {
            var sum = partial[0];
            var offset = sumsOffset + (groupStart + GridIds.X) * GpuTileLayout.SumsPerGroup;
            report[offset] = Hlsl.AsUInt(sum.X);
            report[offset + 1] = Hlsl.AsUInt(sum.Y);
            report[offset + 2] = Hlsl.AsUInt(sum.Z);
            report[offset + 3] = Hlsl.AsUInt(sum.W);
        }
    }

    private int TileIndexOfGroup(int groupBase)
    {
        return batchStart + (groupBase >> (log2Size * 2));
    }

    private bool TileHoldsSample(int tileIndex)
    {
        var word = tileIndex / GpuTileConvolver.SampleTilesPerWord;
        var entry = samples[sampleTilesOffset + word / GpuTileConvolver.WordsPerSampleEntry];
        var bits = word % GpuTileConvolver.WordsPerSampleEntry == 0 ? (uint)entry.X : (uint)entry.Y;
        return ((bits >> (tileIndex % GpuTileConvolver.SampleTilesPerWord)) & 1u) != 0u;
    }

    private Float4 Emit(int flat, Float4 value, int size, int mask, int originX, int originY, bool tileHoldsSample)
    {
        if (IsNonFinite(value.X) || IsNonFinite(value.Y) || IsNonFinite(value.Z) || IsNonFinite(value.W))
            Hlsl.InterlockedAdd(ref report[0], 1u);

        var rest = flat & (size * size - 1);
        var y = rest >> log2Size;
        var x = rest & mask;
        if (x < radius || x >= size - radius || y < radius || y >= size - radius)
            return new Float4(0f, 0f, 0f, 0f);

        var outputX = originX + x;
        var outputY = originY + y;
        if (outputX >= regionWidth || outputY >= regionHeight)
            return new Float4(0f, 0f, 0f, 0f);

        output[new Int2(outputX, outputY)] = LightShaderOutput.Convert(value, gain, linear, dither, regionX + outputX, regionY + outputY);
        if (storeValues != 0)
            store[outputY * regionWidth + outputX] = value;
        if (tileHoldsSample)
        {
            for (var sample = 0; sample < sampleCount; sample++)
            {
                var position = samples[sample];
                if (position.X == outputX && position.Y == outputY)
                {
                    var spot = spotsOffset + sample * GpuTileLayout.ValuesPerSpot;
                    report[spot] = Hlsl.AsUInt(value.X);
                    report[spot + 1] = Hlsl.AsUInt(value.Y);
                    report[spot + 2] = Hlsl.AsUInt(value.Z);
                    report[spot + 3] = Hlsl.AsUInt(value.W);
                }
            }
        }

        return value;
    }

    private static bool IsNonFinite(float value)
        => (Hlsl.AsUInt(value) & 0x7F800000u) == 0x7F800000u;

    private void Butterflies(int local, float direction)
    {
        var stage = 0;
        for (; stage + 1 < log2Size; stage += 2)
        {
            if (local < FftShaderMath.StageUnitsPerGroup)
            {
                var step = FftShaderMath.Pow2(stage);
                var position = FftShaderMath.Position(local, stage);
                var first = FftShaderMath.Radix4First(local, log2Size, stage);
                var second = first + step;
                var third = second + step;
                var fourth = third + step;
                var value0 = row[first];
                var value1 = row[second];
                var value2 = row[third];
                var value3 = row[fourth];
                FftShaderMath.Radix4(
                    ref value0,
                    ref value1,
                    ref value2,
                    ref value3,
                    twiddles[FftShaderMath.TwiddleIndex(position, log2Size, stage)],
                    twiddles[FftShaderMath.TwiddleIndex(position, log2Size, stage + 1)],
                    twiddles[FftShaderMath.TwiddleIndex(position + step, log2Size, stage + 1)],
                    direction);
                row[first] = value0;
                row[second] = value1;
                row[third] = value2;
                row[fourth] = value3;
            }

            Hlsl.GroupMemoryBarrierWithGroupSync();
        }

        for (; stage < log2Size; stage++)
        {
            var even = FftShaderMath.Radix2Even(local, log2Size, stage);
            var odd = even + FftShaderMath.Pow2(stage);
            var evenValue = row[even];
            var oddValue = row[odd];
            FftShaderMath.Radix2(
                ref evenValue,
                ref oddValue,
                twiddles[FftShaderMath.TwiddleIndex(FftShaderMath.Position(local, stage), log2Size, stage)],
                direction);
            row[even] = evenValue;
            row[odd] = oddValue;
            Hlsl.GroupMemoryBarrierWithGroupSync();
        }
    }
}
