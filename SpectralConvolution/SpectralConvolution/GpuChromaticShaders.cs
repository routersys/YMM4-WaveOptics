using ComputeWeave;

namespace SpectralConvolution;

[ThreadGroupSize(TilePlan.GroupThreads, 1, 1)]
[GeneratedComputeShaderDescriptor]
[CompileOptions(CompileOptions.Default | CompileOptions.IeeeStrictness)]
internal readonly partial struct ColumnChromaticShader(
    ReadWriteBuffer<Float4> tiles,
    ReadOnlyBuffer<Float2> twiddles,
    ReadOnlyBuffer<Float2> spectrum,
    int log2Size) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> tiles = tiles;
    private readonly ReadOnlyBuffer<Float2> twiddles = twiddles;
    private readonly ReadOnlyBuffer<Float2> spectrum = spectrum;
    private readonly int log2Size = log2Size;

    [GroupShared(TilePlan.ChromaticGroupElements)]
    private static readonly Float4[] row = null!;

    public void Execute()
    {
        var local = GroupIds.X;
        var group = GridIds.X;
        var size = (int)(1u << log2Size);
        var mask = size - 1;
        var halfColumns = ChromaticKernelSpectrum.HalfColumnsOf(size);
        var unitsPerGroup = TilePlan.GroupElements / size;
        var groupsPerTile = (halfColumns + unitsPerGroup - 1) / unitsPerGroup;
        var tile = group / groupsPerTile;
        var firstColumn = group % groupsPerTile * unitsPerGroup;
        var tileBase = tile << (log2Size * 2);
        var planeLength = halfColumns * size;

        for (var step = 0; step < TilePlan.ValuesPerThread; step++)
        {
            var pair = local + step * TilePlan.GroupThreads;
            var unit = pair % unitsPerGroup;
            var y = pair / unitsPerGroup;
            var column = firstColumn + unit;
            var redGreen = new Float4(0f, 0f, 0f, 0f);
            var blueAlpha = new Float4(0f, 0f, 0f, 0f);
            if (column < halfColumns)
            {
                var near = tiles[tileBase + (y << log2Size) + column];
                var far = tiles[tileBase + (y << log2Size) + ((size - column) & mask)];
                redGreen = new Float4(near.X + far.X, near.Y - far.Y, near.Y + far.Y, far.X - near.X) * 0.5f;
                blueAlpha = new Float4(near.Z + far.Z, near.W - far.W, near.W + far.W, far.Z - near.Z) * 0.5f;
            }

            var reversed = FftShaderMath.BitReverse(y, log2Size);
            var redGreenRow = (unit * TilePlan.ChromaticPlanes) << log2Size;
            row[redGreenRow + reversed] = redGreen;
            row[redGreenRow + size + reversed] = blueAlpha;
        }

        Hlsl.GroupMemoryBarrierWithGroupSync();

        Butterflies(local, FftShaderMath.Forward);

        var product0 = Multiply(local, planeLength, halfColumns, firstColumn, mask, 0);
        var product1 = Multiply(local, planeLength, halfColumns, firstColumn, mask, 1);
        var product2 = Multiply(local, planeLength, halfColumns, firstColumn, mask, 2);
        var product3 = Multiply(local, planeLength, halfColumns, firstColumn, mask, 3);
        Hlsl.GroupMemoryBarrierWithGroupSync();
        row[ReversedIndex(local, mask, 0)] = product0;
        row[ReversedIndex(local, mask, 1)] = product1;
        row[ReversedIndex(local, mask, 2)] = product2;
        row[ReversedIndex(local, mask, 3)] = product3;
        Hlsl.GroupMemoryBarrierWithGroupSync();

        Butterflies(local, FftShaderMath.Inverse);

        for (var step = 0; step < TilePlan.ValuesPerThread; step++)
        {
            var pair = local + step * TilePlan.GroupThreads;
            var unit = pair % unitsPerGroup;
            var y = pair / unitsPerGroup;
            var column = firstColumn + unit;
            if (column >= halfColumns)
                continue;

            var redGreenRow = (unit * TilePlan.ChromaticPlanes) << log2Size;
            var redGreen = row[redGreenRow + y];
            var blueAlpha = row[redGreenRow + size + y];
            var address = tileBase + (y << log2Size);
            tiles[address + column] = new Float4(
                redGreen.X - redGreen.W,
                redGreen.Y + redGreen.Z,
                blueAlpha.X - blueAlpha.W,
                blueAlpha.Y + blueAlpha.Z);
            var mirror = (size - column) & mask;
            if (mirror != column)
            {
                tiles[address + mirror] = new Float4(
                    redGreen.X + redGreen.W,
                    redGreen.Z - redGreen.Y,
                    blueAlpha.X + blueAlpha.W,
                    blueAlpha.Z - blueAlpha.Y);
            }
        }
    }

    private int ReversedIndex(int local, int mask, int part)
    {
        var element = local + part * TilePlan.GroupThreads;
        return (element & ~mask) + FftShaderMath.BitReverse(element & mask, log2Size);
    }

    private Float4 Multiply(int local, int planeLength, int halfColumns, int firstColumn, int mask, int part)
    {
        var element = local + part * TilePlan.GroupThreads;
        var line = element >> log2Size;
        var frequency = element & mask;
        var column = firstColumn + (line / TilePlan.ChromaticPlanes);
        if (column >= halfColumns)
            return new Float4(0f, 0f, 0f, 0f);

        var value = row[element];
        var index = (column << log2Size) + frequency;
        var firstChannel = line % TilePlan.ChromaticPlanes == 0 ? ChromaticChannels.Red : ChromaticChannels.Blue;
        var first = spectrum[firstChannel * planeLength + index];
        var second = spectrum[ChromaticChannels.Green * planeLength + index];
        return FftShaderMath.Multiply(value, first, second);
    }

    private void Butterflies(int local, float direction)
    {
        var stage = 0;
        for (; stage + 1 < log2Size; stage += 2)
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
            Hlsl.GroupMemoryBarrierWithGroupSync();
        }

        for (; stage < log2Size; stage++)
        {
            for (var step = 0; step < TilePlan.ValuesPerThread; step++)
            {
                var index = local + step * TilePlan.GroupThreads;
                var even = FftShaderMath.Radix2Even(index, log2Size, stage);
                var odd = even + FftShaderMath.Pow2(stage);
                var evenValue = row[even];
                var oddValue = row[odd];
                FftShaderMath.Radix2(
                    ref evenValue,
                    ref oddValue,
                    twiddles[FftShaderMath.TwiddleIndex(FftShaderMath.Position(index, stage), log2Size, stage)],
                    direction);
                row[even] = evenValue;
                row[odd] = oddValue;
            }

            Hlsl.GroupMemoryBarrierWithGroupSync();
        }
    }
}
