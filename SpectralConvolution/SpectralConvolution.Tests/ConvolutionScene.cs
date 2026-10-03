namespace SpectralConvolution.Tests;

internal sealed class ConvolutionScene
{
    (double RedGreen, double BlueAlpha)[]? tileNorms;

    ConvolutionScene(byte[] source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, KernelSpectrum spectrum, TilePlan plan)
    {
        Source = source;
        SourceX = sourceX;
        SourceY = sourceY;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        Spectrum = spectrum;
        Plan = plan;
    }

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

    public static ConvolutionScene Create(byte[] source, int sourceWidth, int sourceHeight, int margin, int radius, int size)
    {
        var spectrum = new KernelSpectrum();
        spectrum.Update(AsymmetricKernel(radius), radius, size);
        var plan = TilePlan.Create(size, radius, 0, 0, sourceWidth + margin * 2, sourceHeight + margin * 2);
        return new ConvolutionScene(source, margin, margin, sourceWidth, sourceHeight, spectrum, plan);
    }

    public void Reference(int x, int y, Span<double> result)
        => DirectCorrelation.Evaluate(Source, SourceWidth, SourceHeight, Spectrum.Kernel, Spectrum.Radius, x - SourceX, y - SourceY, result);

    public (double RedGreen, double BlueAlpha) Norms(int x, int y)
    {
        tileNorms ??= [.. Enumerable.Range(0, Plan.TileCount).Select(tile => TileInput.Norms(Source, SourceX, SourceY, SourceWidth, SourceHeight, Plan, tile))];
        return tileNorms[Plan.TileAt(x, y)];
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
