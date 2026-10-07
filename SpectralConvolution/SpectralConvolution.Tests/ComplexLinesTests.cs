using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SpectralConvolution.Tests;

public sealed class ComplexLinesTests
{
    static long Distance(ComplexLines lines)
        => Unsafe.ByteOffset(ref MemoryMarshal.GetReference(lines.Real), ref MemoryMarshal.GetReference(lines.Imaginary));

    [Theory]
    [InlineData(128)]
    [InlineData(512)]
    [InlineData(31 * 128)]
    [InlineData(81 * 256)]
    [InlineData(81 * 512)]
    [InlineData(256 * 256)]
    [InlineData(512 * 512)]
    public void TheImaginaryPartStartsAtAFixedOffsetWithinTheFourKilobytePeriod(int count)
    {
        var lines = new ComplexLines();

        lines.Ensure(count);

        Assert.Equal(1536L, Distance(lines) % 4096);
        Assert.True(Distance(lines) >= (count + 64L) * sizeof(double), $"{Distance(lines)}");
    }

    [Fact]
    public void TheTwoPartsDoNotOverlap()
    {
        var lines = new ComplexLines();
        lines.Ensure(1000);

        lines.Real.Fill(1d);
        lines.Imaginary.Fill(2d);

        Assert.All(lines.Real.ToArray(), value => Assert.Equal(1d, value));
        Assert.All(lines.Imaginary.ToArray(), value => Assert.Equal(2d, value));
    }

    [Fact]
    public void ABufferThatIsLargeEnoughIsKeptAndAGrownOneKeepsTheOffset()
    {
        var lines = new ComplexLines();
        lines.Ensure(100);

        lines.Ensure(50);

        Assert.Equal(100, lines.Real.Length);
        Assert.Equal(100, lines.Imaginary.Length);

        lines.Ensure(4096);

        Assert.Equal(4096, lines.Real.Length);
        Assert.Equal(4096, lines.Imaginary.Length);
        Assert.Equal(1536L, Distance(lines) % 4096);
    }

    [Fact]
    public void AnUnusedBufferIsEmpty()
    {
        var lines = new ComplexLines();

        Assert.True(lines.Real.IsEmpty);
        Assert.True(lines.Imaginary.IsEmpty);
    }
}
