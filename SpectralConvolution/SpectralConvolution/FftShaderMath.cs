using ComputeWeave;

namespace SpectralConvolution;

internal static class FftShaderMath
{
    public const float Forward = 1f;
    public const float Inverse = -1f;
    public const int Radix4Elements = 1 << Radix4Bits;
    public const int StageUnitsPerGroup = TilePlan.GroupElements / Radix4Elements;

    const int UIntBits = 32;
    const int Radix2Bits = 1;
    const int Radix4Bits = 2;

    public static int Pow2(int exponent) => (int)(1u << exponent);

    public static int BitReverse(int index, int log2Size) => (int)(Hlsl.ReverseBits((uint)index) >> (UIntBits - log2Size));

    public static int Position(int index, int stage) => index & (Pow2(stage) - 1);

    public static int TwiddleIndex(int position, int log2Size, int stage) => position << (log2Size - 1 - stage);

    public static int Radix2Even(int index, int log2Size, int stage) => Locate(index, log2Size, stage, Radix2Bits);

    public static int Radix4First(int index, int log2Size, int stage) => Locate(index, log2Size, stage, Radix4Bits);

    public static Float4 Rotate(Float4 value, float real, float imaginary)
        => new(
            value.X * real - value.Y * imaginary,
            value.X * imaginary + value.Y * real,
            value.Z * real - value.W * imaginary,
            value.Z * imaginary + value.W * real);

    public static Float4 Multiply(Float4 value, Float2 weight) => Rotate(value, weight.X, weight.Y);

    public static Float4 Multiply(Float4 value, Float2 firstWeight, Float2 secondWeight)
        => new(
            value.X * firstWeight.X - value.Y * firstWeight.Y,
            value.X * firstWeight.Y + value.Y * firstWeight.X,
            value.Z * secondWeight.X - value.W * secondWeight.Y,
            value.Z * secondWeight.Y + value.W * secondWeight.X);

    public static void Radix2(ref Float4 even, ref Float4 odd, Float2 twiddle, float direction)
    {
        var product = Rotate(odd, twiddle.X, twiddle.Y * direction);
        var evenValue = even;
        even = evenValue + product;
        odd = evenValue - product;
    }

    public static void Radix4(
        ref Float4 first,
        ref Float4 second,
        ref Float4 third,
        ref Float4 fourth,
        Float2 stageTwiddle,
        Float2 nextEvenTwiddle,
        Float2 nextOddTwiddle,
        float direction)
    {
        var secondProduct = Rotate(second, stageTwiddle.X, stageTwiddle.Y * direction);
        var fourthProduct = Rotate(fourth, stageTwiddle.X, stageTwiddle.Y * direction);
        var firstSum = first + secondProduct;
        var secondSum = first - secondProduct;
        var thirdSum = third + fourthProduct;
        var fourthSum = third - fourthProduct;
        var evenProduct = Rotate(thirdSum, nextEvenTwiddle.X, nextEvenTwiddle.Y * direction);
        var oddProduct = Rotate(fourthSum, nextOddTwiddle.X, nextOddTwiddle.Y * direction);
        first = firstSum + evenProduct;
        third = firstSum - evenProduct;
        second = secondSum + oddProduct;
        fourth = secondSum - oddProduct;
    }

    static int Locate(int index, int log2Size, int stage, int radixBits)
    {
        var laneBits = log2Size - radixBits;
        var row = (index >> laneBits) << log2Size;
        var lane = index & (Pow2(laneBits) - 1);
        return row + ((lane >> stage) << (stage + radixBits)) + Position(lane, stage);
    }
}
