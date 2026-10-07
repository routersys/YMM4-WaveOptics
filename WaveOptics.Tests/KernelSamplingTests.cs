using WaveOptics.Abstractions;
using WaveOptics.Optics;

namespace WaveOptics.Tests.Psf;

public sealed class KernelSamplingTests
{
    static double[] Grid(int size, int seed)
    {
        var random = new Random(seed);
        var values = new double[size * size];
        for (var index = 0; index < values.Length; index++)
            values[index] = random.NextDouble() * Math.Exp(-random.NextDouble() * 12);
        return values;
    }

    static double Bilinear(double[] values, int size, double x, double y)
    {
        if (x < 0 || y < 0 || x > size - 1 || y > size - 1)
            return 0;

        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = Math.Min(x0 + 1, size - 1);
        var y1 = Math.Min(y0 + 1, size - 1);
        var tx = x - x0;
        var ty = y - y0;
        var top = values[y0 * size + x0] * (1 - tx) + values[y0 * size + x1] * tx;
        var bottom = values[y1 * size + x0] * (1 - tx) + values[y1 * size + x1] * tx;
        return top * (1 - ty) + bottom * ty;
    }

    [Theory]
    [InlineData(128, 5, 4, 8, 1)]
    [InlineData(256, 15, 4, 8, 1)]
    [InlineData(256, 30, 2, 16, 0.86)]
    [InlineData(512, 63, 4, 8, 1.19)]
    [InlineData(256, 40, 100, 0.5, 1)]
    [InlineData(256, 20, 0.25, 64, 0.9)]
    [InlineData(64, 31, 4, 8, 1.3)]
    public void TheKernelIsTheBilinearInterpolationOfTheGridAtTheScaledPositions(int gridSize, int radius, double pitch, double fNumber, double scale)
    {
        var specification = new PsfSpecification(gridSize, gridSize / 4, radius * 2 + 1, 550, fNumber, pitch, ApertureShape.Circular, 6, 0, 0, default);
        var grid = Grid(gridSize, gridSize + radius);
        var kernel = new double[specification.KernelSize * specification.KernelSize];
        var pixelPitch = specification.SensorPixelPitchMicrometers;
        var focalPitch = FraunhoferPsfGenerator.FocalPlaneSamplePitch(in specification);

        var energy = FraunhoferPsfGenerator.SampleKernel(in specification, grid, kernel, scale);

        var expectedEnergy = 0d;
        for (var y = 0; y < specification.KernelSize; y++)
        {
            var sampleY = gridSize / 2 + (y - radius) * pixelPitch / focalPitch * scale;
            for (var x = 0; x < specification.KernelSize; x++)
            {
                var sampleX = gridSize / 2 + (x - radius) * pixelPitch / focalPitch * scale;
                var expected = Bilinear(grid, gridSize, sampleX, sampleY);
                Assert.Equal(expected, kernel[y * specification.KernelSize + x]);
                expectedEnergy += expected;
            }
        }

        Assert.Equal(expectedEnergy, energy);
    }
}
