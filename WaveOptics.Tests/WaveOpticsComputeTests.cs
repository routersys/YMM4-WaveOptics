using SpectralConvolution;
using WaveOptics.Rendering;

namespace WaveOptics.Tests;

public sealed class WaveOpticsComputeTests
{
    [Theory]
    [InlineData(15, 128)]
    [InlineData(63, 512)]
    public void TheChromaticModeNeedsThreeHalfSpectraInsteadOfOneFullSpectrum(int radius, int size)
    {
        var single = WaveOpticsCompute.GpuMemoryBytes(1920, 1080, radius);
        var chromatic = WaveOpticsCompute.GpuMemoryBytes(1920, 1080, radius, true);

        var expected = (3L * (size / 2 + 1) * size - (long)size * size) * sizeof(float) * 2;
        Assert.Equal(expected, chromatic - single);
        Assert.Equal(single, WaveOpticsCompute.GpuMemoryBytes(1920, 1080, radius, false));
    }

    [Fact]
    public void TheWorkloadCarriesTheMemoryOfTheChosenMode()
    {
        var single = WaveOpticsCompute.Workload(1280, 720, 40);
        var chromatic = WaveOpticsCompute.Workload(1280, 720, 40, true);

        Assert.Equal(WaveOpticsCompute.GpuMemoryBytes(1280, 720, 40), single.GpuMemoryBytes);
        Assert.Equal(WaveOpticsCompute.GpuMemoryBytes(1280, 720, 40, true), chromatic.GpuMemoryBytes);
        Assert.True(chromatic.GpuMemoryBytes > single.GpuMemoryBytes);
    }
}
