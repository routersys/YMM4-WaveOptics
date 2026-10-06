namespace SpectralConvolution.Tests;

internal sealed class ConvolutionScene
{
    (double RedGreen, double BlueAlpha)[]? tileNorms;
    float[]? transformed;

    ConvolutionScene(byte[] source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, KernelSpectrum spectrum, TilePlan plan, LightOptions light)
    {
        Light = light;
        Source = source;
        SourceX = sourceX;
        SourceY = sourceY;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        Spectrum = spectrum;
        Plan = plan;
    }

    public LightOptions Light { get; }

    public byte[] Source { get; }

    public int SourceX { get; }

    public int SourceY { get; }

    public int SourceWidth { get; }

    public int SourceHeight { get; }

    public KernelSpectrum Spectrum { get; }

    public TilePlan Plan { get; }

    public int RegionLength => Plan.RegionWidth * Plan.RegionHeight * 4;

    public static double[] AsymmetricKernel(int radius)
    {
        var size = radius * 2 + 1;
        var values = new double[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - radius * 0.6;
                var dy = y - radius * 1.3;
                values[y * size + x] = Math.Exp(-(dx * dx * 0.4 + dy * dy * 0.15) / (radius * radius + 1d)) + (x == 0 && y == size - 1 ? 0.5 : 0d);
            }
        }

        return values;
    }

    public static byte[] RandomSource(int width, int height, int seed, double litFraction)
    {
        var random = new Random(seed);
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < width * height; index++)
        {
            if (random.NextDouble() >= litFraction)
                continue;
            var alpha = (byte)random.Next(1, 256);
            pixels[index * 4] = (byte)random.Next(alpha + 1);
            pixels[index * 4 + 1] = (byte)random.Next(alpha + 1);
            pixels[index * 4 + 2] = (byte)random.Next(alpha + 1);
            pixels[index * 4 + 3] = alpha;
        }

        return pixels;
    }

    public static byte[] Uniform(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, value);
        return pixels;
    }

    public static ConvolutionScene Create(byte[] source, int sourceWidth, int sourceHeight, int margin, int radius, int size, LightOptions light = default)
    {
        var spectrum = new KernelSpectrum();
        spectrum.Update(AsymmetricKernel(radius), radius, size);
        var plan = TilePlan.Create(size, radius, 0, 0, sourceWidth + margin * 2, sourceHeight + margin * 2);
        return new ConvolutionScene(source, margin, margin, sourceWidth, sourceHeight, spectrum, plan, light);
    }

    public static ConvolutionScene CreateShifted(
        byte[] source, int sourceX, int sourceY, int sourceWidth, int sourceHeight,
        int regionX, int regionY, int regionWidth, int regionHeight, int radius, int size, LightOptions light = default)
    {
        var spectrum = new KernelSpectrum();
        spectrum.Update(AsymmetricKernel(radius), radius, size);
        var plan = TilePlan.Create(size, radius, regionX, regionY, regionWidth, regionHeight);
        return new ConvolutionScene(source, sourceX, sourceY, sourceWidth, sourceHeight, spectrum, plan, light);
    }

    public void Reference(int x, int y, Span<double> result)
    {
        if (!Light.Linear)
        {
            DirectCorrelation.Evaluate(Source, SourceWidth, SourceHeight, Spectrum.Kernel, Spectrum.Radius, x - SourceX, y - SourceY, result);
            return;
        }

        var values = Transformed();
        var radius = Spectrum.Radius;
        var kernelSize = radius * 2 + 1;
        var kernel = Spectrum.Kernel;
        result[..4].Clear();
        for (var offsetY = -radius; offsetY <= radius; offsetY++)
        {
            var sourceY = y - SourceY + offsetY;
            if (sourceY < 0 || sourceY >= SourceHeight)
                continue;
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
            {
                var sourceX = x - SourceX + offsetX;
                if (sourceX < 0 || sourceX >= SourceWidth)
                    continue;
                var weight = kernel[(offsetY + radius) * kernelSize + offsetX + radius];
                var offset = (sourceY * SourceWidth + sourceX) * 4;
                for (var channel = 0; channel < 4; channel++)
                    result[channel] += values[offset + channel] * weight;
            }
        }
    }

    public (double RedGreen, double BlueAlpha) Norms(int x, int y)
    {
        tileNorms ??= [.. Enumerable.Range(0, Plan.TileCount).Select(TileNorms)];
        return tileNorms[Plan.TileAt(x, y)];
    }

    public float[] Transformed()
    {
        if (transformed is null)
        {
            var values = new float[SourceWidth * SourceHeight * 4];
            for (var index = 0; index < SourceWidth * SourceHeight; index++)
            {
                var (red, green, blue, alpha) = LightTransform.FromBytes(Source[index * 4 + 2], Source[index * 4 + 1], Source[index * 4], Source[index * 4 + 3], Light);
                values[index * 4] = red;
                values[index * 4 + 1] = green;
                values[index * 4 + 2] = blue;
                values[index * 4 + 3] = alpha;
            }

            transformed = values;
        }

        return transformed;
    }

    (double RedGreen, double BlueAlpha) TileNorms(int tile)
    {
        if (!Light.Linear)
            return TileInput.Norms(Source, SourceX, SourceY, SourceWidth, SourceHeight, Plan, tile);

        var values = Transformed();
        var left = Plan.OriginX(tile) - Plan.Radius;
        var top = Plan.OriginY(tile) - Plan.Radius;
        var redGreen = 0d;
        var blueAlpha = 0d;
        var lastY = Math.Min(top + Plan.Size, SourceY + SourceHeight);
        var lastX = Math.Min(left + Plan.Size, SourceX + SourceWidth);
        for (var y = Math.Max(top, SourceY); y < lastY; y++)
        {
            for (var x = Math.Max(left, SourceX); x < lastX; x++)
            {
                var offset = ((y - SourceY) * SourceWidth + x - SourceX) * 4;
                redGreen += (double)values[offset] * values[offset] + (double)values[offset + 1] * values[offset + 1];
                blueAlpha += (double)values[offset + 2] * values[offset + 2] + (double)values[offset + 3] * values[offset + 3];
            }
        }

        return (Math.Sqrt(redGreen), Math.Sqrt(blueAlpha));
    }

    public double WorstRatio(ReadOnlySpan<float> convolved, double operationError)
    {
        var reference = new double[4];
        var worst = 0d;
        var referenceError = DirectCorrelation.ErrorBound(Spectrum.Kernel.Length);
        for (var y = 0; y < Plan.RegionHeight; y++)
        {
            for (var x = 0; x < Plan.RegionWidth; x++)
            {
                var canvasX = Plan.RegionX + x;
                var canvasY = Plan.RegionY + y;
                Reference(canvasX, canvasY, reference);
                var (redGreen, blueAlpha) = Norms(canvasX, canvasY);
                var index = (y * Plan.RegionWidth + x) * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    var norm = channel < 2 ? redGreen : blueAlpha;
                    var bound = ConvolutionBound.Absolute(Plan.Size, operationError, norm) + referenceError;
                    worst = Math.Max(worst, Math.Abs(convolved[index + channel] - reference[channel]) / bound);
                }
            }
        }

        return worst;
    }
}
