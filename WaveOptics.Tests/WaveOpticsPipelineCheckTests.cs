using System.Runtime.InteropServices;
using ComputeWeave;
using SpectralConvolution;
using WaveOptics.Effects;
using WaveOptics.Rendering;

namespace WaveOptics.Tests;

[Collection("Direct3D12")]
public sealed class WaveOpticsPipelineCheckTests
{
    const int Width = 160;
    const int Height = 144;
    const int Opaque = unchecked((int)0xFFC0A080);

    static WaveOpticsPipeline.Parameters Parameters(float gain = 1f, int kernelRadius = 15, float defocus = 0.5f)
        => new(gain, new WaveOpticsPipeline.PsfParameters(
            WaveOpticsQuality.Standard, kernelRadius, 550f, 8f, 4f, WaveOpticsApertureShape.Circular, 6, 0f, 0f, defocus, 0f, 0f, 0.3f, 0f, 0f));

    sealed class Scene : IDisposable
    {
        public Scene()
        {
            var pixels = new int[Width * Height];
            for (var y = 50; y < 90; y++)
            {
                for (var x = 40; x < 110; x++)
                    pixels[y * Width + x] = (x + y) % 7 == 0 ? 0 : Opaque;
            }

            var device = GraphicsDevice.GetDefault();
            Source = device.AllocateReadWriteTexture2D<Bgra32, Float4>(Width, Height);
            Source.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(pixels.AsSpan()));
            Output = device.AllocateReadWriteTexture2D<Bgra32, Float4>(Width, Height);
        }

        public ReadWriteTexture2D<Bgra32, Float4> Source { get; }

        public ReadWriteTexture2D<Bgra32, Float4> Output { get; }

        public void Dispose()
        {
            Source.Dispose();
            Output.Dispose();
        }
    }

    static ConvolutionMeasurement[] Render(WaveOpticsPipeline pipeline, Scene scene, in WaveOpticsPipeline.Parameters parameters, out int count)
    {
        var measurements = new ConvolutionMeasurement[WaveOpticsPipeline.MeasurementCount];
        pipeline.Simulate(scene.Source, Width, Height, 0, 0, Width, Height, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(Width, Height, in parameters, out var rect));
        count = pipeline.RenderVisible(scene.Source, scene.Output, rect, in parameters, measurements);
        return measurements;
    }

    static WaveOpticsPipeline Create()
    {
        var pipeline = WaveOpticsPipeline.TryCreate();
        Assert.NotNull(pipeline);
        return pipeline;
    }

    [Theory]
    [InlineData(15)]
    [InlineData(40)]
    public void AHealthyConvolutionPassesEveryCheck(int kernelRadius)
    {
        using var pipeline = Create();
        using var scene = new Scene();

        var measurements = Render(pipeline, scene, Parameters(kernelRadius: kernelRadius), out var count);

        Assert.Equal(WaveOpticsPipeline.MeasurementCount, count);
        Assert.All(measurements, measurement => Assert.True(measurement.Passes, $"{measurement}"));
        Assert.False(pipeline.IsDeviceLost);
    }

    [Fact]
    public void DrawingFromTheStoreReturnsNoMeasurements()
    {
        using var pipeline = Create();
        using var scene = new Scene();
        Render(pipeline, scene, Parameters(), out _);
        Render(pipeline, scene, Parameters(gain: 2f), out var storing);

        Render(pipeline, scene, Parameters(gain: 3f), out var stored);

        Assert.Equal(WaveOpticsPipeline.MeasurementCount, storing);
        Assert.Equal(0, stored);
    }

    [Fact]
    public void TheSamePictureIsCheckedAtTheSamePlaces()
    {
        using var first = Create();
        using var second = Create();
        using var scene = new Scene();

        var a = Render(first, scene, Parameters(), out _);
        var b = Render(second, scene, Parameters(), out _);

        Assert.Equal(a.Select(measurement => (measurement.Name, measurement.Reference, measurement.Tolerance)), b.Select(measurement => (measurement.Name, measurement.Reference, measurement.Tolerance)));
    }

    [Fact]
    public void AScaledSpectrumFailsTheChecks()
    {
        using var pipeline = Create();
        using var scene = new Scene();
        pipeline.SpectrumTamper = spectrum => [.. spectrum.Select(value => new Float2(value.X * 1.001f, value.Y * 1.001f))];

        var measurements = Render(pipeline, scene, Parameters(), out _);

        Assert.Contains(measurements, measurement => !measurement.Passes);
    }

    [Fact]
    public void ANaNInTheSpectrumIsCounted()
    {
        using var pipeline = Create();
        using var scene = new Scene();
        pipeline.SpectrumTamper = spectrum =>
        {
            var broken = (Float2[])spectrum.Clone();
            broken[3] = new Float2(float.NaN, 0f);
            return broken;
        };

        var measurements = Render(pipeline, scene, Parameters(), out _);

        Assert.Equal(GpuTileCheck.NonFiniteName, measurements[0].Name);
        Assert.False(measurements[0].Passes);
    }
}
