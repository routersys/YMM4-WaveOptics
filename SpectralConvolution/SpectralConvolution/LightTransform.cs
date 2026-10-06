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
    public const float EncodeExponent = 1f / 2.4f;

    static readonly float[] OpaqueDecode = BuildOpaqueDecode();

    public static float Decode(float encoded)
    {
        if (encoded <= DecodeToeLimit)
            return encoded / ToeSlope;
        return encoded >= 1f ? 1f : MathF.Pow((encoded + Offset) / Scale, DecodeExponent);
    }

    public static float Encode(float linear)
    {
        if (linear <= EncodeToeLimit)
            return ToeSlope * linear;
        return linear >= 1f ? 1f : Scale * MathF.Pow(linear, EncodeExponent) - Offset;
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

        return (
            Quantize(outputBlue, options.Dither, x, y, 2),
            Quantize(outputGreen, options.Dither, x, y, 1),
            Quantize(outputRed, options.Dither, x, y, 0),
            Quantize(outputAlpha, options.Dither, x, y, 3));
    }

    public static float DitherThreshold(int x, int y, int channel)
    {
        var hash = unchecked((uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u ^ (uint)channel * 0xC2B2AE3Du);
        hash ^= hash >> 16;
        hash = unchecked(hash * 0x7FEB352Du);
        hash ^= hash >> 15;
        hash = unchecked(hash * 0x846CA68Bu);
        hash ^= hash >> 16;
        return (hash >> 8) * (1f / 16777216f);
    }

    static byte Quantize(float value, bool dither, int x, int y, int channel)
        => dither
            ? (byte)(value * 255f + DitherThreshold(x, y, channel))
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
}
