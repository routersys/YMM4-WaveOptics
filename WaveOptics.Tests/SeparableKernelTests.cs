using WaveOptics.Optics;

namespace WaveOptics.Tests.Optics;

public sealed class SeparableKernelTests
{
    [Fact]
    public void FullRankReconstructionMatchesOriginal()
    {
        const int size = 15;
        var random = new Random(2024);
        var kernel = new double[size * size];
        for (var index = 0; index < kernel.Length; index++)
            kernel[index] = random.NextDouble();

        var separable = SeparableKernel.Decompose(kernel, size, 0d, size);
        var maximumError = ReconstructionError(separable, kernel);

        Assert.Equal(size, separable.Rank);
        Assert.True(maximumError < 1e-5, $"{maximumError:R}");
    }

    [Fact]
    public void OuterProductKernelIsRankOne()
    {
        const int size = 11;
        var vertical = new double[size];
        var horizontal = new double[size];
        for (var i = 0; i < size; i++)
        {
            vertical[i] = Math.Exp(-0.2 * (i - 5) * (i - 5));
            horizontal[i] = Math.Exp(-0.35 * (i - 5) * (i - 5));
        }
        var kernel = new double[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
                kernel[y * size + x] = vertical[y] * horizontal[x];
        }

        var separable = SeparableKernel.Decompose(kernel, size, 1e-6, size);
        var maximumError = ReconstructionError(separable, kernel);

        Assert.Equal(1, separable.Rank);
        Assert.True(maximumError < 1e-5, $"{maximumError:R}");
    }

    [Fact]
    public void LowRankApproximationHonorsResidualBudget()
    {
        const int size = 21;
        var center = size / 2;
        var kernel = new double[size * size];
        var sum = 0d;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var radiusSquared = (x - center) * (x - center) + (y - center) * (y - center);
                var value = Math.Exp(-0.12 * radiusSquared);
                kernel[y * size + x] = value;
                sum += value;
            }
        }
        for (var index = 0; index < kernel.Length; index++)
            kernel[index] /= sum;

        var separable = SeparableKernel.Decompose(kernel, size, 1e-4, size);
        var energy = 0d;
        var residual = 0d;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var original = kernel[y * size + x];
                var approximation = Reconstruct(separable, x, y);
                energy += original * original;
                residual += (original - approximation) * (original - approximation);
            }
        }

        Assert.True(separable.Rank < size);
        Assert.True(residual <= 1e-4 * energy, $"{residual / energy:R}");
    }

    static double Reconstruct(SeparableKernel separable, int x, int y)
    {
        var size = separable.Size;
        var value = 0d;
        for (var term = 0; term < separable.Rank; term++)
            value += separable.Vertical[term * size + y] * separable.Horizontal[term * size + x];
        return value;
    }

    static double ReconstructionError(SeparableKernel separable, double[] kernel)
    {
        var size = separable.Size;
        var maximumError = 0d;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var error = Math.Abs(kernel[y * size + x] - Reconstruct(separable, x, y));
                maximumError = Math.Max(maximumError, error);
            }
        }
        return maximumError;
    }
}
