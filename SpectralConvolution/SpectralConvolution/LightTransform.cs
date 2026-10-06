using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace SpectralConvolution;

internal static class LightTransform
{
    public const int Channels = 4;
    public const float DecodeToeLimit = 0.04045f;
    public const float EncodeToeLimit = 0.0031308f;
    public const float ToeSlope = 12.92f;
    public const float Offset = 0.055f;
    public const float Scale = 1.055f;
    public const float DecodeExponent = 2.4f;
    public const float EncodeExponent = 0.41666666f;

    const int MantissaBits = 23;
    const int ExponentBias = 127;
    const int OneBits = ExponentBias << MantissaBits;
    const int EncodeStepsPerOctaveLog2 = 8;
    const int EncodeFirstOctave = -9;
    const int EncodeFractionBits = MantissaBits - EncodeStepsPerOctaveLog2;
    const int EncodeFirstStep = ((ExponentBias + EncodeFirstOctave) << MantissaBits) >> EncodeFractionBits;
    const int EncodeSteps = (OneBits >> EncodeFractionBits) - EncodeFirstStep;
    const int DecodeStepsPerOctaveLog2 = 10;
    const int DecodeFirstOctave = -4;
    const int DecodeFractionBits = MantissaBits - DecodeStepsPerOctaveLog2;
    const int DecodeFirstStep = ((ExponentBias + DecodeFirstOctave) << MantissaBits) >> DecodeFractionBits;
    const int DecodeSteps = (OneBits >> DecodeFractionBits) - DecodeFirstStep;
    const float EncodeFractionScale = 1f / (1 << EncodeFractionBits);
    const float DecodeFractionScale = 1f / (1 << DecodeFractionBits);

    static readonly float[] EncodePairs = BuildPairs(EncodeFractionBits, EncodeFirstStep, EncodeSteps, linear => Scale * Math.Pow(linear, EncodeExponent) - Offset);
    static readonly float[] DecodePairs = BuildPairs(DecodeFractionBits, DecodeFirstStep, DecodeSteps, power => Math.Pow(power, DecodeExponent));
    static readonly float[] OpaqueDecode = BuildOpaqueDecode();
    static readonly Vector128<float> AlphaLane = Vector128.Create(0, 0, 0, -1).AsSingle();

    public static float Decode(float encoded)
    {
        if (!(encoded > DecodeToeLimit))
            return encoded / ToeSlope;
        if (encoded >= 1f)
            return 1f;

        var bits = BitConverter.SingleToInt32Bits((encoded + Offset) / Scale);
        var step = (bits >> DecodeFractionBits) - DecodeFirstStep;
        var fraction = (bits & ((1 << DecodeFractionBits) - 1)) * DecodeFractionScale;
        ref var pair = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(DecodePairs), step * 2);
        return pair + Unsafe.Add(ref pair, 1) * fraction;
    }

    public static float Encode(float linear)
    {
        if (!(linear > EncodeToeLimit))
            return ToeSlope * linear;
        if (linear >= 1f)
            return 1f;

        var bits = BitConverter.SingleToInt32Bits(linear);
        var step = (bits >> EncodeFractionBits) - EncodeFirstStep;
        var fraction = (bits & ((1 << EncodeFractionBits) - 1)) * EncodeFractionScale;
        ref var pair = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(EncodePairs), step * 2);
        return pair + Unsafe.Add(ref pair, 1) * fraction;
    }

    public static (float Red, float Green, float Blue, float Alpha) FromBytes(byte red, byte green, byte blue, byte alpha, in LightOptions options)
    {
        if (!options.Linear)
            return (red / 255f, green / 255f, blue / 255f, alpha / 255f);
        if (alpha == 0)
            return default;

        var opacity = alpha / 255f;
        float straightRed;
        float straightGreen;
        float straightBlue;
        float linearRed;
        float linearGreen;
        float linearBlue;
        if (alpha == 255)
        {
            straightRed = red / 255f;
            straightGreen = green / 255f;
            straightBlue = blue / 255f;
            linearRed = OpaqueDecode[red];
            linearGreen = OpaqueDecode[green];
            linearBlue = OpaqueDecode[blue];
        }
        else
        {
            straightRed = MathF.Min(red / (float)alpha, 1f);
            straightGreen = MathF.Min(green / (float)alpha, 1f);
            straightBlue = MathF.Min(blue / (float)alpha, 1f);
            linearRed = Decode(straightRed);
            linearGreen = Decode(straightGreen);
            linearBlue = Decode(straightBlue);
        }

        var weight = 1f;
        if (options.Highlights)
        {
            var peak = MathF.Max(MathF.Max(straightRed, straightGreen), straightBlue);
            var excess = MathF.Min(MathF.Max((peak - options.Threshold) / (1f - options.Threshold), 0f), 1f);
            weight = 1f + (options.Boost - 1f) * excess * excess;
        }

        var scale = opacity * weight;
        return (linearRed * scale, linearGreen * scale, linearBlue * scale, scale);
    }

    public static (byte Blue, byte Green, byte Red, byte Alpha) ToBytes(
        float red,
        float green,
        float blue,
        float alpha,
        float gain,
        in LightOptions options,
        int x,
        int y)
    {
        float outputRed;
        float outputGreen;
        float outputBlue;
        float outputAlpha;
        if (options.Linear)
        {
            outputAlpha = Saturate(alpha * gain);
            if (outputAlpha > 0f)
            {
                outputRed = Encode(MathF.Min(Saturate(red * gain), outputAlpha) / outputAlpha) * outputAlpha;
                outputGreen = Encode(MathF.Min(Saturate(green * gain), outputAlpha) / outputAlpha) * outputAlpha;
                outputBlue = Encode(MathF.Min(Saturate(blue * gain), outputAlpha) / outputAlpha) * outputAlpha;
            }
            else
            {
                outputRed = 0f;
                outputGreen = 0f;
                outputBlue = 0f;
            }
        }
        else
        {
            outputRed = Saturate(red * gain);
            outputGreen = Saturate(green * gain);
            outputBlue = Saturate(blue * gain);
            outputAlpha = Saturate(alpha * gain);
        }

        var thresholdSharedByChannels = options.Dither ? DitherThreshold(x, y) : 0f;
        return (
            Quantize(outputBlue, options.Dither, thresholdSharedByChannels),
            Quantize(outputGreen, options.Dither, thresholdSharedByChannels),
            Quantize(outputRed, options.Dither, thresholdSharedByChannels),
            Quantize(outputAlpha, options.Dither, thresholdSharedByChannels));
    }

    public static uint ToPacked(Vector128<float> value, float gain, in LightOptions options, int x, int y)
    {
        var saturated = Saturate(value * Vector128.Create(gain));
        var color = saturated;
        if (options.Linear)
        {
            var alpha = Vector128.Shuffle(saturated, Vector128.Create(3, 3, 3, 3));
            var visible = Vector128.GreaterThan(alpha, Vector128<float>.Zero);
            var encoded = Encode(Vector128.Min(saturated, alpha) / alpha) * alpha;
            color = Vector128.ConditionalSelect(AlphaLane, saturated, Vector128.ConditionalSelect(visible, encoded, Vector128<float>.Zero));
        }

        var scaled = color * Vector128.Create(255f);
        var levels = options.Dither
            ? Vector128.ConvertToInt32(Vector128.Min(scaled + Vector128.Create(DitherThreshold(x, y)), Vector128.Create(255f)))
            : Vector128.ConvertToInt32(scaled + Vector128.Create(0.5f));
        var ordered = Vector128.Shuffle(levels, Vector128.Create(2, 1, 0, 3)).AsUInt32();
        var words = Vector128.Narrow(ordered, ordered);
        return Vector128.Narrow(words, words).AsUInt32().ToScalar();
    }

    static Vector128<float> Encode(Vector128<float> linear)
        => Vector128.Create(Encode(linear.GetElement(0)), Encode(linear.GetElement(1)), Encode(linear.GetElement(2)), 0f);

    static Vector128<float> Saturate(Vector128<float> value)
    {
        value = Vector128.ConditionalSelect(Vector128.Equals(value, value), value, Vector128<float>.Zero);
        return Vector128.Min(Vector128.Max(value, Vector128<float>.Zero), Vector128<float>.One);
    }

    public static float DitherThreshold(int x, int y)
    {
        var hash = unchecked((uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u);
        hash ^= hash >> 16;
        hash = unchecked(hash * 0x7FEB352Du);
        hash ^= hash >> 15;
        hash = unchecked(hash * 0x846CA68Bu);
        hash ^= hash >> 16;
        return (hash >> 8) * (1f / 16777216f);
    }

    static byte Quantize(float value, bool dither, float threshold)
        => dither
            ? (byte)MathF.Min(value * 255f + threshold, 255f)
            : (byte)(value * 255f + 0.5f);

    static float Saturate(float value)
        => float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 1f);

    static float[] BuildOpaqueDecode()
    {
        var table = new float[256];
        for (var index = 0; index < table.Length; index++)
            table[index] = Decode(index / 255f);
        return table;
    }

    static float[] BuildPairs(int fractionBits, int firstStep, int steps, Func<double, double> curve)
    {
        var values = new float[steps + 1];
        for (var index = 0; index < values.Length; index++)
            values[index] = (float)curve(BitConverter.Int32BitsToSingle((firstStep + index) << fractionBits));

        var pairs = new float[(steps + 1) * 2];
        for (var index = 0; index <= steps; index++)
        {
            pairs[index * 2] = values[index];
            pairs[index * 2 + 1] = index < steps ? values[index + 1] - values[index] : 0f;
        }

        return pairs;
    }
}
