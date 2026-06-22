using WaveOptics.Abstractions;

namespace WaveOptics.Tests.Contracts;

public sealed class PsfKernelTests
{
    [Fact]
    public void ConstructorNormalizesKernel()
    {
        var values = new double[9];
        values[4] = 2;

        var kernel = new PsfKernel(3, values);

        Assert.Equal(1, kernel.Sum(), 12);
        Assert.Equal(1, kernel[1, 1], 12);
    }

    [Fact]
    public void ConstructorCopiesInput()
    {
        var values = new double[9];
        values[4] = 1;
        var kernel = new PsfKernel(3, values);

        values[4] = 0;

        Assert.Equal(1, kernel[1, 1], 12);
    }

    [Fact]
    public void ConstructorRejectsNegativeValues()
    {
        var values = new double[9];
        values[4] = 1;
        values[0] = -0.1;

        Assert.Throws<ArgumentOutOfRangeException>(() => new PsfKernel(3, values));
    }
}
