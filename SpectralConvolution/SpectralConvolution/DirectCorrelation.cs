namespace SpectralConvolution;

internal static class DirectCorrelation
{
    public const int Channels = 4;

    public static uint Pack(byte red, byte green, byte blue, byte alpha)
        => red | (uint)green << 8 | (uint)blue << 16 | (uint)alpha << 24;

    public static void Evaluate(ReadOnlySpan<uint> neighborhood, ReadOnlySpan<double> kernel, Span<double> result)
    {
        if (neighborhood.Length != kernel.Length)
            throw new ArgumentException(null, nameof(neighborhood));
        if (result.Length < Channels)
            throw new ArgumentException(null, nameof(result));

        var red = 0d;
        var green = 0d;
        var blue = 0d;
        var alpha = 0d;
        for (var index = 0; index < kernel.Length; index++)
        {
            var packed = neighborhood[index];
            var weight = kernel[index];
            red += (packed & 255u) / 255d * weight;
            green += (packed >> 8 & 255u) / 255d * weight;
            blue += (packed >> 16 & 255u) / 255d * weight;
            alpha += (packed >> 24) / 255d * weight;
        }

        result[0] = red;
        result[1] = green;
        result[2] = blue;
        result[3] = alpha;
    }

    public static void Evaluate(ReadOnlySpan<uint> neighborhood, ReadOnlySpan<double> kernel, in LightOptions light, Span<double> result)
    {
        if (!light.Linear)
        {
            Evaluate(neighborhood, kernel, result);
            return;
        }

        if (neighborhood.Length != kernel.Length)
            throw new ArgumentException(null, nameof(neighborhood));
        if (result.Length < Channels)
            throw new ArgumentException(null, nameof(result));

        var red = 0d;
        var green = 0d;
        var blue = 0d;
        var alpha = 0d;
        for (var index = 0; index < kernel.Length; index++)
        {
            var packed = neighborhood[index];
            var weight = kernel[index];
            var (linearRed, linearGreen, linearBlue, linearAlpha) = LightTransform.FromBytes(
                (byte)(packed & 255u), (byte)(packed >> 8 & 255u), (byte)(packed >> 16 & 255u), (byte)(packed >> 24), in light);
            red += linearRed * weight;
            green += linearGreen * weight;
            blue += linearBlue * weight;
            alpha += linearAlpha * weight;
        }

        result[0] = red;
        result[1] = green;
        result[2] = blue;
        result[3] = alpha;
    }

    public static void Evaluate(ReadOnlySpan<byte> bgra, int width, int height, ReadOnlySpan<double> kernel, int radius, int x, int y, Span<double> result)
    {
        var kernelSize = radius * 2 + 1;
        if (kernel.Length != kernelSize * kernelSize)
            throw new ArgumentException(null, nameof(kernel));
        if (bgra.Length < width * height * Channels)
            throw new ArgumentException(null, nameof(bgra));
        if (result.Length < Channels)
            throw new ArgumentException(null, nameof(result));

        var red = 0d;
        var green = 0d;
        var blue = 0d;
        var alpha = 0d;
        for (var offsetY = -radius; offsetY <= radius; offsetY++)
        {
            var sourceY = y + offsetY;
            if (sourceY < 0 || sourceY >= height)
                continue;
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
            {
                var sourceX = x + offsetX;
                if (sourceX < 0 || sourceX >= width)
                    continue;
                var weight = kernel[(offsetY + radius) * kernelSize + offsetX + radius];
                var offset = (sourceY * width + sourceX) * Channels;
                red += bgra[offset + 2] / 255d * weight;
                green += bgra[offset + 1] / 255d * weight;
                blue += bgra[offset] / 255d * weight;
                alpha += bgra[offset + 3] / 255d * weight;
            }
        }

        result[0] = red;
        result[1] = green;
        result[2] = blue;
        result[3] = alpha;
    }

    public static void Evaluate(
        ReadOnlySpan<uint> neighborhood,
        ReadOnlySpan<double> redKernel,
        ReadOnlySpan<double> greenKernel,
        ReadOnlySpan<double> blueKernel,
        in LightOptions light,
        Span<double> result)
    {
        if (neighborhood.Length != redKernel.Length || neighborhood.Length != greenKernel.Length || neighborhood.Length != blueKernel.Length)
            throw new ArgumentException(null, nameof(neighborhood));
        if (result.Length < Channels)
            throw new ArgumentException(null, nameof(result));

        var red = 0d;
        var green = 0d;
        var blue = 0d;
        var alpha = 0d;
        for (var index = 0; index < greenKernel.Length; index++)
        {
            var packed = neighborhood[index];
            var (linearRed, linearGreen, linearBlue, linearAlpha) = light.Linear
                ? LightTransform.FromBytes((byte)(packed & 255u), (byte)(packed >> 8 & 255u), (byte)(packed >> 16 & 255u), (byte)(packed >> 24), in light)
                : ((packed & 255u) / 255d, (packed >> 8 & 255u) / 255d, (packed >> 16 & 255u) / 255d, (packed >> 24) / 255d);
            red += linearRed * redKernel[index];
            green += linearGreen * greenKernel[index];
            blue += linearBlue * blueKernel[index];
            alpha += linearAlpha * greenKernel[index];
        }

        result[0] = red;
        result[1] = green;
        result[2] = blue;
        result[3] = alpha;
    }

    public static void Evaluate(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        ReadOnlySpan<double> redKernel,
        ReadOnlySpan<double> greenKernel,
        ReadOnlySpan<double> blueKernel,
        int radius,
        int x,
        int y,
        Span<double> result)
    {
        var kernelSize = radius * 2 + 1;
        if (redKernel.Length != kernelSize * kernelSize || greenKernel.Length != redKernel.Length || blueKernel.Length != redKernel.Length)
            throw new ArgumentException(null, nameof(redKernel));
        if (bgra.Length < width * height * Channels)
            throw new ArgumentException(null, nameof(bgra));
        if (result.Length < Channels)
            throw new ArgumentException(null, nameof(result));

        var red = 0d;
        var green = 0d;
        var blue = 0d;
        var alpha = 0d;
        for (var offsetY = -radius; offsetY <= radius; offsetY++)
        {
            var sourceY = y + offsetY;
            if (sourceY < 0 || sourceY >= height)
                continue;
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
            {
                var sourceX = x + offsetX;
                if (sourceX < 0 || sourceX >= width)
                    continue;
                var index = (offsetY + radius) * kernelSize + offsetX + radius;
                var offset = (sourceY * width + sourceX) * Channels;
                red += bgra[offset + 2] / 255d * redKernel[index];
                green += bgra[offset + 1] / 255d * greenKernel[index];
                blue += bgra[offset] / 255d * blueKernel[index];
                alpha += bgra[offset + 3] / 255d * greenKernel[index];
            }
        }

        result[0] = red;
        result[1] = green;
        result[2] = blue;
        result[3] = alpha;
    }

    public static double ErrorBound(int kernelLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kernelLength);
        return ConvolutionBound.Gamma(kernelLength + 2, ConvolutionBound.DoubleRounding);
    }
}
