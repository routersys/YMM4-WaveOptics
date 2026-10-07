using ComputeWeave;

namespace SpectralConvolution;

[ThreadGroupSize(256, 1, 1)]
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

    [GroupShared(1024)]
    private static readonly Float4[] row = null!;

    public void Execute()
    {
        var local = GroupIds.X;
        var group = ThreadIds.X >> 8;
        var size = (int)(1u << log2Size);
        var mask = size - 1;
        var shift = 32 - log2Size;
        var halfColumns = size / 2 + 1;
        var unitsPerGroup = (int)(512u >> log2Size);
        var groupsPerTile = (halfColumns + unitsPerGroup - 1) / unitsPerGroup;
        var tile = group / groupsPerTile;
        var firstColumn = group % groupsPerTile * unitsPerGroup;
        var tileBase = tile << (log2Size * 2);
        var planeLength = halfColumns * size;

        for (var step = 0; step < 2; step++)
        {
            var pair = local + step * 256;
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

            var reversed = (int)(Hlsl.ReverseBits((uint)y) >> shift);
            row[((unit * 2) << log2Size) + reversed] = redGreen;
            row[((unit * 2 + 1) << log2Size) + reversed] = blueAlpha;
        }

        Hlsl.GroupMemoryBarrierWithGroupSync();

        Butterflies(local, 1f);

        var product0 = Multiply(local, planeLength, halfColumns, firstColumn, mask, 0);
        var product1 = Multiply(local, planeLength, halfColumns, firstColumn, mask, 1);
        var product2 = Multiply(local, planeLength, halfColumns, firstColumn, mask, 2);
        var product3 = Multiply(local, planeLength, halfColumns, firstColumn, mask, 3);
        Hlsl.GroupMemoryBarrierWithGroupSync();
        row[ReversedIndex(local, shift, mask, 0)] = product0;
        row[ReversedIndex(local, shift, mask, 1)] = product1;
        row[ReversedIndex(local, shift, mask, 2)] = product2;
        row[ReversedIndex(local, shift, mask, 3)] = product3;
        Hlsl.GroupMemoryBarrierWithGroupSync();

        Butterflies(local, -1f);

        for (var step = 0; step < 2; step++)
        {
            var pair = local + step * 256;
            var unit = pair % unitsPerGroup;
            var y = pair / unitsPerGroup;
            var column = firstColumn + unit;
            if (column >= halfColumns)
                continue;

            var redGreen = row[((unit * 2) << log2Size) + y];
            var blueAlpha = row[((unit * 2 + 1) << log2Size) + y];
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

    private int ReversedIndex(int local, int shift, int mask, int part)
    {
        var element = local + part * 256;
        return (element & ~mask) + (int)(Hlsl.ReverseBits((uint)(element & mask)) >> shift);
    }

    private Float4 Multiply(int local, int planeLength, int halfColumns, int firstColumn, int mask, int part)
    {
        var element = local + part * 256;
        var line = element >> log2Size;
        var frequency = element & mask;
        var column = firstColumn + (line >> 1);
        if (column >= halfColumns)
            return new Float4(0f, 0f, 0f, 0f);

        var value = row[element];
        var index = (column << log2Size) + frequency;
        var first = spectrum[(line & 1) * 2 * planeLength + index];
        var second = spectrum[planeLength + index];
        return new Float4(
            value.X * first.X - value.Y * first.Y,
            value.X * first.Y + value.Y * first.X,
            value.Z * second.X - value.W * second.Y,
            value.Z * second.Y + value.W * second.X);
    }

    private static Float4 Rotate(Float4 value, float real, float imaginary)
        => new(
            value.X * real - value.Y * imaginary,
            value.X * imaginary + value.Y * real,
            value.Z * real - value.W * imaginary,
            value.Z * imaginary + value.W * real);

    private void Butterflies(int local, float direction)
    {
        var halfSize = (int)(1u << (log2Size - 1));
        var unitRowBase = (local >> (log2Size - 2)) << log2Size;
        var unit = local & ((int)(1u << (log2Size - 2)) - 1);
        var stage = 0;
        for (; stage + 1 < log2Size; stage += 2)
        {
            var step = (int)(1u << stage);
            var position = unit & (step - 1);
            var first = unitRowBase + ((unit >> stage) << (stage + 2)) + position;
            var second = first + step;
            var third = second + step;
            var fourth = third + step;
            var firstTwiddle = twiddles[position << (log2Size - 1 - stage)];
            var firstReal = firstTwiddle.X;
            var firstImaginary = firstTwiddle.Y * direction;
            var secondEvenTwiddle = twiddles[position << (log2Size - 2 - stage)];
            var secondOddTwiddle = twiddles[(position + step) << (log2Size - 2 - stage)];
            var value0 = row[first];
            var value1 = row[second];
            var value2 = row[third];
            var value3 = row[fourth];
            var product1 = Rotate(value1, firstReal, firstImaginary);
            var product3 = Rotate(value3, firstReal, firstImaginary);
            var sum0 = value0 + product1;
            var sum1 = value0 - product1;
            var sum2 = value2 + product3;
            var sum3 = value2 - product3;
            var evenProduct = Rotate(sum2, secondEvenTwiddle.X, secondEvenTwiddle.Y * direction);
            var oddProduct = Rotate(sum3, secondOddTwiddle.X, secondOddTwiddle.Y * direction);
            row[first] = sum0 + evenProduct;
            row[third] = sum0 - evenProduct;
            row[second] = sum1 + oddProduct;
            row[fourth] = sum1 - oddProduct;
            Hlsl.GroupMemoryBarrierWithGroupSync();
        }

        for (; stage < log2Size; stage++)
        {
            var half = (int)(1u << stage);
            for (var step = 0; step < 2; step++)
            {
                var index = local + step * 256;
                var rowBase = (index >> (log2Size - 1)) << log2Size;
                var pair = index & (halfSize - 1);
                var position = pair & (half - 1);
                var even = rowBase + ((pair >> stage) << (stage + 1)) + position;
                var odd = even + half;
                var twiddle = twiddles[position << (log2Size - 1 - stage)];
                var product = Rotate(row[odd], twiddle.X, twiddle.Y * direction);
                var evenValue = row[even];
                row[even] = evenValue + product;
                row[odd] = evenValue - product;
            }

            Hlsl.GroupMemoryBarrierWithGroupSync();
        }
    }
}
