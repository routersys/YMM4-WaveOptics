using WaveOptics.Abstractions;
using WaveOptics.Optics;

namespace WaveOptics.Tests.Psf;

public sealed class FraunhoferPupilTests
{
    static PsfSpecification Specification(int gridSize, ApertureShape shape, int blades, double rotation, double obstruction, WavefrontAberration aberration)
        => new(gridSize, gridSize / 4, 31, 550d, 8d, 4d, shape, blades, rotation, obstruction, aberration);

    static int Reference(in PsfSpecification specification, double pupilDiameter, double phaseScale, double[] real, double[] imaginary, int firstRow, int lastRow)
    {
        var gridSize = specification.PupilGridSize;
        var center = gridSize / 2;
        var radius = pupilDiameter / 2d;
        var rotation = specification.BladeRotationDegrees * Math.PI / 180d;
        var count = 0;
        for (var y = firstRow; y <= lastRow; y++)
        {
            for (var x = 0; x < gridSize; x++)
            {
                var normalizedX = (x - center) / radius;
                var normalizedY = (y - center) / radius;
                var index = y * gridSize + x;
                if (FraunhoferPsfGenerator.IsInsideAperture(normalizedX, normalizedY, in specification, rotation))
                {
                    var waves = ZernikeWavefront.Evaluate(normalizedX, normalizedY, specification.Aberration) * phaseScale;
                    var angle = 2 * Math.PI * waves;
                    real[index] = Math.Cos(angle);
                    imaginary[index] = Math.Sin(angle);
                    count++;
                }
                else
                {
                    real[index] = 0;
                    imaginary[index] = 0;
                }
            }
        }

        return count;
    }

    static IEnumerable<(string Name, PsfSpecification Specification, double Diameter, double PhaseScale)> Cases()
    {
        var aberration = new WavefrontAberration(1.5, -0.8, 0.6, 2.2, -1.1, 0.7, -0.4, -0.9);
        var shapes = new[]
        {
            ("circle", ApertureShape.Circular, 6, 0d, 0d),
            ("obstructed", ApertureShape.Circular, 6, 0d, 0.4d),
            ("triangle", ApertureShape.RegularPolygon, 3, 17d, 0d),
            ("pentagon", ApertureShape.RegularPolygon, 5, -33d, 0.3d),
            ("octagon", ApertureShape.RegularPolygon, 8, 22.5d, 0d),
        };
        foreach (var gridSize in new[] { 64, 128, 256, 512 })
        {
            var diameters = new[]
            {
                (gridSize / 4d, 1d),
                (gridSize / 4d * 550d / 610d, 610d / 550d),
                (gridSize / 4d * 550d / 465d, 465d / 550d),
                (gridSize / 4d + 0.5d, 1d),
                (gridSize / 2d - 1d, 1d),
                (gridSize / 4d * 1.0000000000000002d, 1d),
                (gridSize / 4d - 1e-13d, 1d),
                (Math.BitDecrement(gridSize / 4d), 1d),
                (Math.BitIncrement(gridSize / 4d), 1d),
            };
            foreach (var (shapeName, shape, blades, rotation, obstruction) in shapes)
            {
                foreach (var (diameter, phaseScale) in diameters)
                    yield return ($"{shapeName} {gridSize} {diameter}", Specification(gridSize, shape, blades, rotation, obstruction, aberration), diameter, phaseScale);
            }
        }
    }

    [Fact]
    public void ThePupilIsTheApertureAndWavefrontEvaluatedAtEverySampleOfTheRows()
    {
        var mismatches = new List<string>();
        foreach (var (name, specification, diameter, phaseScale) in Cases())
        {
            var gridSize = specification.PupilGridSize;
            var center = gridSize / 2;
            var reach = (int)(diameter / 2d);
            foreach (var (firstRow, lastRow) in new[] { (Math.Max(center - reach, 0), Math.Min(center + reach, gridSize - 1)), (0, gridSize - 1) })
            {
                var expectedReal = new double[gridSize * gridSize];
                var expectedImaginary = new double[gridSize * gridSize];
                var actualReal = new double[gridSize * gridSize];
                var actualImaginary = new double[gridSize * gridSize];
                expectedReal.AsSpan().Fill(double.NaN);
                expectedImaginary.AsSpan().Fill(double.NaN);
                actualReal.AsSpan().Fill(double.NaN);
                actualImaginary.AsSpan().Fill(double.NaN);

                var expectedCount = Reference(in specification, diameter, phaseScale, expectedReal, expectedImaginary, firstRow, lastRow);
                var actualCount = FraunhoferPsfGenerator.BuildPupil(in specification, diameter, phaseScale, actualReal, actualImaginary, firstRow, lastRow);

                if (expectedCount != actualCount || !expectedReal.AsSpan().SequenceEqual(actualReal) || !expectedImaginary.AsSpan().SequenceEqual(actualImaginary))
                    mismatches.Add($"{name} rows {firstRow}-{lastRow}");
            }
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public void RowsOutsideTheRequestedRangeAreNotTouched()
    {
        var specification = Specification(128, ApertureShape.Circular, 6, 0d, 0d, new WavefrontAberration(1d, 0d, 0d, 0d, 0d, 0d));
        var real = new double[128 * 128];
        var imaginary = new double[128 * 128];
        real.AsSpan().Fill(7d);
        imaginary.AsSpan().Fill(7d);

        FraunhoferPsfGenerator.BuildPupil(in specification, 32d, 1d, real, imaginary, 48, 80);

        Assert.All(real.AsSpan(0, 48 * 128).ToArray(), value => Assert.Equal(7d, value));
        Assert.All(real.AsSpan(81 * 128).ToArray(), value => Assert.Equal(7d, value));
        Assert.All(imaginary.AsSpan(0, 48 * 128).ToArray(), value => Assert.Equal(7d, value));
        Assert.All(imaginary.AsSpan(81 * 128).ToArray(), value => Assert.Equal(7d, value));
    }

    [Fact]
    public void ASampleOnTheEdgeOfTheCircleIsOpen()
    {
        var specification = Specification(128, ApertureShape.Circular, 6, 0d, 0d, default);
        var real = new double[128 * 128];
        var imaginary = new double[128 * 128];

        var count = FraunhoferPsfGenerator.BuildPupil(in specification, 32d, 1d, real, imaginary, 0, 127);

        Assert.Equal(1d, real[64 * 128 + 64 + 16]);
        Assert.Equal(1d, real[64 * 128 + 64 - 16]);
        Assert.Equal(0d, real[64 * 128 + 64 + 17]);
        Assert.Equal(0d, real[64 * 128 + 64 - 17]);
        Assert.True(count > 700);
    }
}
