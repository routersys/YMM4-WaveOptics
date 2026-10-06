using ComputeWeave;

namespace SpectralConvolution;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ReportResetShader(
    ReadWriteBuffer<uint> report) : IComputeShader
{
    private readonly ReadWriteBuffer<uint> report = report;

    public void Execute()
    {
        if (ThreadIds.X == 0)
            report[0] = 0u;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
[CompileOptions(CompileOptions.Default | CompileOptions.IeeeStrictness)]
internal readonly partial struct StoredRenderShader(
    ReadWriteBuffer<Float4> store,
    ReadWriteTexture2D<Bgra32, Float4> output,
    int width,
    int height,
    float gain) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> store = store;
    private readonly ReadWriteTexture2D<Bgra32, Float4> output = output;
    private readonly int width = width;
    private readonly int height = height;
    private readonly float gain = gain;

    public void Execute()
    {
        if (ThreadIds.X >= width || ThreadIds.Y >= height)
            return;
        output[ThreadIds.XY] = store[ThreadIds.Y * width + ThreadIds.X] * gain;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
[CompileOptions(CompileOptions.Default | CompileOptions.IeeeStrictness)]
internal readonly partial struct GatherShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadOnlyBuffer<Int2> samples,
    ReadWriteBuffer<uint> report,
    int gatheredOffset,
    int radius,
    int sampleCount,
    int regionX,
    int regionY,
    int sourceX,
    int sourceY,
    int sourceWidth,
    int sourceHeight) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadOnlyBuffer<Int2> samples = samples;
    private readonly ReadWriteBuffer<uint> report = report;
    private readonly int gatheredOffset = gatheredOffset;
    private readonly int radius = radius;
    private readonly int sampleCount = sampleCount;
    private readonly int regionX = regionX;
    private readonly int regionY = regionY;
    private readonly int sourceX = sourceX;
    private readonly int sourceY = sourceY;
    private readonly int sourceWidth = sourceWidth;
    private readonly int sourceHeight = sourceHeight;

    public void Execute()
    {
        var kernelSize = radius * 2 + 1;
        var area = kernelSize * kernelSize;
        var index = ThreadIds.X;
        var sample = ThreadIds.Y;
        if (index >= area || sample >= sampleCount)
            return;

        var position = samples[sample];
        var x = regionX + position.X + index % kernelSize - radius - sourceX;
        var y = regionY + position.Y + index / kernelSize - radius - sourceY;
        var packed = 0u;
        if (x >= 0 && x < sourceWidth && y >= 0 && y < sourceHeight)
        {
            var value = source[new Int2(x, y)];
            packed = (uint)(value.X * 255f + 0.5f)
                | ((uint)(value.Y * 255f + 0.5f) << 8)
                | ((uint)(value.Z * 255f + 0.5f) << 16)
                | ((uint)(value.W * 255f + 0.5f) << 24);
        }

        report[gatheredOffset + sample * area + index] = packed;
    }
}

[ThreadGroupSize(256, 1, 1)]
[GeneratedComputeShaderDescriptor]
[CompileOptions(CompileOptions.Default | CompileOptions.IeeeStrictness)]
internal readonly partial struct ForwardRowShader(
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
    int groupStart) : IComputeShader
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

    [GroupShared(512)]
    private static readonly Float4[] row = null!;

    [GroupShared(6)]
    private static readonly uint[] statistics = null!;

    public void Execute()
    {
        var local = GroupIds.X;
        var groupBase = (ThreadIds.X - local) * 2;
        var size = (int)(1u << log2Size);
        var mask = size - 1;
        var shift = 32 - log2Size;
        if (local < 6)
            statistics[local] = 0u;
        Hlsl.GroupMemoryBarrierWithGroupSync();

        // A group of 512 elements never lies in two tiles, so one division serves the whole group.
        var tileIndex = batchStart + (groupBase >> (log2Size * 2));
        var tileRow = tileIndex / tilesX;
        var originX = regionX + (tileIndex - tileRow * tilesX) * validSize - radius;
        var originY = regionY + tileRow * validSize - radius;

        var sumRed = 0u;
        var sumGreen = 0u;
        var sumBlue = 0u;
        var sumAlpha = 0u;
        var squareRedGreen = 0u;
        var squareBlueAlpha = 0u;
        for (var pair = 0; pair < 2; pair++)
        {
            var element = local * 2 + pair;
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
                value = source[new Int2(column, line)];
                var red = (uint)(value.X * 255f + 0.5f);
                var green = (uint)(value.Y * 255f + 0.5f);
                var blue = (uint)(value.Z * 255f + 0.5f);
                var alpha = (uint)(value.W * 255f + 0.5f);
                squareRedGreen += red * red + green * green;
                squareBlueAlpha += blue * blue + alpha * alpha;
                if (x >= radius && x < size - radius && y >= radius && y < size - radius
                    && canvasX < regionX + regionWidth && canvasY < regionY + regionHeight)
                {
                    sumRed += red;
                    sumGreen += green;
                    sumBlue += blue;
                    sumAlpha += alpha;
                }
            }

            var reversed = (int)(Hlsl.ReverseBits((uint)x) >> shift);
            row[((element >> log2Size) << log2Size) + reversed] = value;
        }

        Hlsl.InterlockedAdd(ref statistics[0], sumRed);
        Hlsl.InterlockedAdd(ref statistics[1], sumGreen);
        Hlsl.InterlockedAdd(ref statistics[2], sumBlue);
        Hlsl.InterlockedAdd(ref statistics[3], sumAlpha);
        Hlsl.InterlockedAdd(ref statistics[4], squareRedGreen);
        Hlsl.InterlockedAdd(ref statistics[5], squareBlueAlpha);
        Hlsl.GroupMemoryBarrierWithGroupSync();
        if (local < 6)
            report[statisticsOffset + (groupStart + (ThreadIds.X >> 8)) * 6 + local] = statistics[local];

        Butterflies(local, 1f);

        tiles[groupBase + local * 2] = row[local * 2];
        tiles[groupBase + local * 2 + 1] = row[local * 2 + 1];
    }

    private void Butterflies(int local, float direction)
    {
        var halfSize = (int)(1u << (log2Size - 1));
        var rowBase = (local >> (log2Size - 1)) << log2Size;
        var pair = local & (halfSize - 1);
        for (var stage = 0; stage < log2Size; stage++)
        {
            var half = (int)(1u << stage);
            var position = pair & (half - 1);
            var even = rowBase + ((pair >> stage) << (stage + 1)) + position;
            var odd = even + half;
            var twiddle = twiddles[position << (log2Size - 1 - stage)];
            var real = twiddle.X;
            var imaginary = twiddle.Y * direction;
            var evenValue = row[even];
            var oddValue = row[odd];
            var product = new Float4(
                oddValue.X * real - oddValue.Y * imaginary,
                oddValue.X * imaginary + oddValue.Y * real,
                oddValue.Z * real - oddValue.W * imaginary,
                oddValue.Z * imaginary + oddValue.W * real);
            row[even] = evenValue + product;
            row[odd] = evenValue - product;
            Hlsl.GroupMemoryBarrierWithGroupSync();
        }
    }
}

[ThreadGroupSize(256, 1, 1)]
[GeneratedComputeShaderDescriptor]
[CompileOptions(CompileOptions.Default | CompileOptions.IeeeStrictness)]
internal readonly partial struct ColumnShader(
    ReadWriteBuffer<Float4> tiles,
    ReadOnlyBuffer<Float2> twiddles,
    ReadOnlyBuffer<Float2> spectrum,
    int log2Size) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> tiles = tiles;
    private readonly ReadOnlyBuffer<Float2> twiddles = twiddles;
    private readonly ReadOnlyBuffer<Float2> spectrum = spectrum;
    private readonly int log2Size = log2Size;

    [GroupShared(512)]
    private static readonly Float4[] row = null!;

    public void Execute()
    {
        var local = GroupIds.X;
        var group = ThreadIds.X >> 8;
        var mask = (int)(1u << log2Size) - 1;
        var shift = 32 - log2Size;
        var log2Columns = 9 - log2Size;
        var columnMask = (int)(1u << log2Columns) - 1;
        var groupsPerTile = (int)(1u << (log2Size - log2Columns));
        var tile = group / groupsPerTile;
        var firstColumn = (group % groupsPerTile) << log2Columns;
        var tileBase = tile << (log2Size * 2);

        var element0 = local * 2;
        var element1 = local * 2 + 1;
        var y0 = element0 >> log2Columns;
        var column0 = element0 & columnMask;
        var y1 = element1 >> log2Columns;
        var column1 = element1 & columnMask;
        var global0 = tileBase + (y0 << log2Size) + firstColumn + column0;
        var global1 = tileBase + (y1 << log2Size) + firstColumn + column1;
        row[(column0 << log2Size) + (int)(Hlsl.ReverseBits((uint)y0) >> shift)] = tiles[global0];
        row[(column1 << log2Size) + (int)(Hlsl.ReverseBits((uint)y1) >> shift)] = tiles[global1];
        Hlsl.GroupMemoryBarrierWithGroupSync();

        Butterflies(local, 1f);

        var frequency0 = element0 & mask;
        var frequency1 = element1 & mask;
        var product0 = Multiply(row[element0], spectrum[((firstColumn + (element0 >> log2Size)) << log2Size) + frequency0]);
        var product1 = Multiply(row[element1], spectrum[((firstColumn + (element1 >> log2Size)) << log2Size) + frequency1]);
        Hlsl.GroupMemoryBarrierWithGroupSync();
        row[((element0 >> log2Size) << log2Size) + (int)(Hlsl.ReverseBits((uint)frequency0) >> shift)] = product0;
        row[((element1 >> log2Size) << log2Size) + (int)(Hlsl.ReverseBits((uint)frequency1) >> shift)] = product1;
        Hlsl.GroupMemoryBarrierWithGroupSync();

        Butterflies(local, -1f);

        tiles[global0] = row[(column0 << log2Size) + y0];
        tiles[global1] = row[(column1 << log2Size) + y1];
    }

    private static Float4 Multiply(Float4 value, Float2 weight)
        => new(
            value.X * weight.X - value.Y * weight.Y,
            value.X * weight.Y + value.Y * weight.X,
            value.Z * weight.X - value.W * weight.Y,
            value.Z * weight.Y + value.W * weight.X);

    private void Butterflies(int local, float direction)
    {
        var halfSize = (int)(1u << (log2Size - 1));
        var rowBase = (local >> (log2Size - 1)) << log2Size;
        var pair = local & (halfSize - 1);
        for (var stage = 0; stage < log2Size; stage++)
        {
            var half = (int)(1u << stage);
            var position = pair & (half - 1);
            var even = rowBase + ((pair >> stage) << (stage + 1)) + position;
            var odd = even + half;
            var twiddle = twiddles[position << (log2Size - 1 - stage)];
            var real = twiddle.X;
            var imaginary = twiddle.Y * direction;
            var evenValue = row[even];
            var oddValue = row[odd];
            var product = new Float4(
                oddValue.X * real - oddValue.Y * imaginary,
                oddValue.X * imaginary + oddValue.Y * real,
                oddValue.Z * real - oddValue.W * imaginary,
                oddValue.Z * imaginary + oddValue.W * real);
            row[even] = evenValue + product;
            row[odd] = evenValue - product;
            Hlsl.GroupMemoryBarrierWithGroupSync();
        }
    }
}

[ThreadGroupSize(256, 1, 1)]
[GeneratedComputeShaderDescriptor]
[CompileOptions(CompileOptions.Default | CompileOptions.IeeeStrictness)]
internal readonly partial struct InverseRowShader(
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
    int regionWidth,
    int regionHeight,
    float scale,
    float gain,
    int sampleCount,
    int sumsOffset,
    int spotsOffset,
    int groupStart,
    int storeValues,
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
    private readonly int regionWidth = regionWidth;
    private readonly int regionHeight = regionHeight;
    private readonly float scale = scale;
    private readonly float gain = gain;
    private readonly int sampleCount = sampleCount;
    private readonly int sumsOffset = sumsOffset;
    private readonly int spotsOffset = spotsOffset;
    private readonly int groupStart = groupStart;
    private readonly int storeValues = storeValues;
    private readonly int sampleTilesOffset = sampleTilesOffset;

    [GroupShared(512)]
    private static readonly Float4[] row = null!;

    [GroupShared(256)]
    private static readonly Float4[] partial = null!;

    public void Execute()
    {
        var local = GroupIds.X;
        var groupBase = (ThreadIds.X - local) * 2;
        var size = (int)(1u << log2Size);
        var mask = size - 1;
        var shift = 32 - log2Size;
        var element0 = local * 2;
        var element1 = local * 2 + 1;
        row[((element0 >> log2Size) << log2Size) + (int)(Hlsl.ReverseBits((uint)(element0 & mask)) >> shift)] = tiles[groupBase + element0];
        row[((element1 >> log2Size) << log2Size) + (int)(Hlsl.ReverseBits((uint)(element1 & mask)) >> shift)] = tiles[groupBase + element1];
        Hlsl.GroupMemoryBarrierWithGroupSync();

        Butterflies(local, -1f);

        // A group of 512 elements never lies in two tiles, so one division serves the whole group. The
        // samples buffer lists the sample positions first and then, one bit per tile, the tiles that hold a
        // sample, so that only the threads of those tiles look for samples.
        var tileIndex = batchStart + (groupBase >> (log2Size * 2));
        var tileRow = tileIndex / tilesX;
        var originX = (tileIndex - tileRow * tilesX) * validSize - radius;
        var originY = tileRow * validSize - radius;
        var sampled = sampleCount > 0 && IsSampled(tileIndex);
        partial[local] = Emit(groupBase + element0, row[element0] * scale, size, mask, originX, originY, sampled)
            + Emit(groupBase + element1, row[element1] * scale, size, mask, originX, originY, sampled);
        Hlsl.GroupMemoryBarrierWithGroupSync();
        for (var stride = 128; stride > 0; stride >>= 1)
        {
            if (local < stride)
                partial[local] = partial[local] + partial[local + stride];
            Hlsl.GroupMemoryBarrierWithGroupSync();
        }

        if (local == 0)
        {
            var sum = partial[0];
            var offset = sumsOffset + (groupStart + (ThreadIds.X >> 8)) * 4;
            report[offset] = Hlsl.AsUInt(sum.X);
            report[offset + 1] = Hlsl.AsUInt(sum.Y);
            report[offset + 2] = Hlsl.AsUInt(sum.Z);
            report[offset + 3] = Hlsl.AsUInt(sum.W);
        }
    }

    private bool IsSampled(int tileIndex)
    {
        var entry = samples[sampleTilesOffset + (tileIndex >> 6)];
        var bits = (tileIndex & 32) == 0 ? (uint)entry.X : (uint)entry.Y;
        return ((bits >> (tileIndex & 31)) & 1u) != 0u;
    }

    private Float4 Emit(int flat, Float4 value, int size, int mask, int originX, int originY, bool sampled)
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

        output[new Int2(outputX, outputY)] = value * gain;
        if (storeValues != 0)
            store[outputY * regionWidth + outputX] = value;
        if (sampled)
        {
            for (var sample = 0; sample < sampleCount; sample++)
            {
                var position = samples[sample];
                if (position.X == outputX && position.Y == outputY)
                {
                    report[spotsOffset + sample * 4] = Hlsl.AsUInt(value.X);
                    report[spotsOffset + sample * 4 + 1] = Hlsl.AsUInt(value.Y);
                    report[spotsOffset + sample * 4 + 2] = Hlsl.AsUInt(value.Z);
                    report[spotsOffset + sample * 4 + 3] = Hlsl.AsUInt(value.W);
                }
            }
        }

        return value;
    }

    private static bool IsNonFinite(float value)
        => (Hlsl.AsUInt(value) & 0x7F800000u) == 0x7F800000u;

    private void Butterflies(int local, float direction)
    {
        var halfSize = (int)(1u << (log2Size - 1));
        var rowBase = (local >> (log2Size - 1)) << log2Size;
        var pair = local & (halfSize - 1);
        for (var stage = 0; stage < log2Size; stage++)
        {
            var half = (int)(1u << stage);
            var position = pair & (half - 1);
            var even = rowBase + ((pair >> stage) << (stage + 1)) + position;
            var odd = even + half;
            var twiddle = twiddles[position << (log2Size - 1 - stage)];
            var real = twiddle.X;
            var imaginary = twiddle.Y * direction;
            var evenValue = row[even];
            var oddValue = row[odd];
            var product = new Float4(
                oddValue.X * real - oddValue.Y * imaginary,
                oddValue.X * imaginary + oddValue.Y * real,
                oddValue.Z * real - oddValue.W * imaginary,
                oddValue.Z * imaginary + oddValue.W * real);
            row[even] = evenValue + product;
            row[odd] = evenValue - product;
            Hlsl.GroupMemoryBarrierWithGroupSync();
        }
    }
}
