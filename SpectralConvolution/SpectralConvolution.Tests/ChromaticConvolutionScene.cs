namespace SpectralConvolution.Tests;

internal sealed class ChromaticConvolutionScene
{
    (double RedGreen, double BlueAlpha)[]? tileNorms;

    ChromaticConvolutionScene(byte[] source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, ChromaticKernelSpectrum spectrum, TilePlan plan, LightOptions light)
    {
        Source = source;
        SourceX = sourceX;
        SourceY = sourceY;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        Spectrum = spectrum;
        Plan = plan;
        Light = light;
    }

    public byte[] Source { get; }

    public int SourceX { get; }

    public int SourceY { get; }

    public int SourceWidth { get; }

    public int SourceHeight { get; }

    public ChromaticKernelSpectrum Spectrum { get; }

    public TilePlan Plan { get; }

    public LightOptions Light { get; }

    public int RegionLength => Plan.RegionWidth * Plan.RegionHeight * 4;

    public static double[] Kernel(int radius, double spread, double lean)
    {
        var size = radius * 2 + 1;
        var values = new double[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - radius * lean;
                var dy = y - radius * (2d - lean);
                values[y * size + x] = Math.Exp(-(dx * dx * 0.4 + dy * dy * 0.15) / (spread * (radius * radius + 1d))) + (x == 0 && y == size - 1 ? 0.5 : 0d);
            }
        }

        return values;
    }

    public static ChromaticConvolutionScene Create(byte[] source, int sourceWidth, int sourceHeight, int margin, int radius, int size, LightOptions light = default)
        => CreateShifted(source, margin, margin, sourceWidth, sourceHeight, 0, 0, sourceWidth + margin * 2, sourceHeight + margin * 2, radius, size, light);

    public static ChromaticConvolutionScene CreateShifted(
        byte[] source, int sourceX, int sourceY, int sourceWidth, int sourceHeight,
        int regionX, int regionY, int regionWidth, int regionHeight, int radius, int size, LightOptions light = default)
    {
        var spectrum = new ChromaticKernelSpectrum();
        spectrum.Update(Kernel(radius, 1.3, 0.6), Kernel(radius, 1d, 0.9), Kernel(radius, 0.7, 1.2), radius, size);
        var plan = TilePlan.Create(size, radius, regionX, regionY, regionWidth, regionHeight);
        return new ChromaticConvolutionScene(source, sourceX, sourceY, sourceWidth, sourceHeight, spectrum, plan, light);
    }

    public void Reference(int x, int y, Span<double> result)
    {
        var radius = Spectrum.Radius;
        if (!Light.Linear)
        {
            DirectCorrelation.Evaluate(Source, SourceWidth, SourceHeight, Spectrum.Red.Kernel, Spectrum.Green.Kernel, Spectrum.Blue.Kernel, radius, x - SourceX, y - SourceY, result);
            return;
        }

        var kernelSize = radius * 2 + 1;
        var values = Transformed();
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
                var index = (offsetY + radius) * kernelSize + offsetX + radius;
                var offset = (sourceY * SourceWidth + sourceX) * 4;
                result[0] += values[offset] * Spectrum.Red.Kernel[index];
                result[1] += values[offset + 1] * Spectrum.Green.Kernel[index];
                result[2] += values[offset + 2] * Spectrum.Blue.Kernel[index];
                result[3] += values[offset + 3] * Spectrum.Green.Kernel[index];
            }
        }
    }

    public (double RedGreen, double BlueAlpha) Norms(int x, int y)
    {
        tileNorms ??= [.. Enumerable.Range(0, Plan.TileCount).Select(TileNorms)];
        return tileNorms[Plan.TileAt(x, y)];
    }

    public double WorstRatio(ReadOnlySpan<float> convolved, double operationError)
    {
        var reference = new double[4];
        var worst = 0d;
        var referenceError = DirectCorrelation.ErrorBound(Spectrum.Green.Kernel.Length);
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
                    var bound = ConvolutionBound.ChromaticAbsolute(Plan.Size, operationError, norm) + referenceError;
                    worst = Math.Max(worst, Math.Abs(convolved[index + channel] - reference[channel]) / bound);
                }
            }
        }

        return worst;
    }

    float[] Transformed()
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

        return values;
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
}
