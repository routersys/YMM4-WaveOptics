using WaveOptics.Abstractions;

namespace WaveOptics.Tests.Contracts;

public sealed class PsfDescriptorTests
{
    [Fact]
    public void ConstructorAcceptsValidDescriptor()
    {
        var descriptor = CreateDescriptor();

        Assert.Equal(256, descriptor.PupilGridSize);
        Assert.Equal(64, descriptor.PupilDiameterSamples);
        Assert.Equal(31, descriptor.KernelSize);
    }

    [Theory]
    [InlineData(63)]
    [InlineData(96)]
    [InlineData(257)]
    public void ConstructorRejectsNonPowerOfTwoGrid(int gridSize)
    {
        Assert.ThrowsAny<ArgumentException>(() => CreateDescriptor(gridSize: gridSize));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(30)]
    [InlineData(256)]
    public void ConstructorRejectsInvalidKernelSize(int kernelSize)
    {
        Assert.ThrowsAny<ArgumentException>(() => CreateDescriptor(kernelSize: kernelSize));
    }

    [Fact]
    public void EqualDescriptorsHaveEqualHashCodes()
    {
        var first = CreateDescriptor();
        var second = CreateDescriptor();

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    static PsfDescriptor CreateDescriptor(int gridSize = 256, int kernelSize = 31)
    {
        return new PsfDescriptor(
            gridSize,
            Math.Min(64, gridSize / 2),
            kernelSize,
            550,
            8,
            4,
            ApertureShape.Circular,
            6,
            0,
            0,
            new WavefrontAberration());
    }
}
