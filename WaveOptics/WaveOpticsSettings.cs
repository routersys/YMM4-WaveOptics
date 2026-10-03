using WaveOptics.Effects;

namespace WaveOptics;

internal static class WaveOpticsSettings
{
    public const int MinimumKernelRadius = 1;
    public const int DefaultKernelRadius = 15;
    public const int MaximumKernelRadius = 63;
    public const int MaximumKernelSize = MaximumKernelRadius * 2 + 1;
    public const int SourceHashSpan = 16;
    public const int DefaultCanvasMargin = (DefaultKernelRadius + 3) & ~3;
    public const int MaximumCanvasSize = 8192;
    public const int ScratchLength = 7;
    public const int ScratchLitCount = 0;
    public const int ScratchBoundsMinX = 1;
    public const int ScratchBoundsMinY = 2;
    public const int ScratchBoundsMaxX = 3;
    public const int ScratchBoundsMaxY = 4;
    public const int ScratchHashSum = 5;
    public const int ScratchHashMix = 6;
    public const int MaximumPendingSubmissions = 32;

    public static int GetPupilGridSize(WaveOpticsQuality quality)
        => quality switch
        {
            WaveOpticsQuality.Draft => 128,
            WaveOpticsQuality.High => 512,
            _ => 256,
        };

    public static int GetPupilDiameterSamples(int pupilGridSize) => pupilGridSize / 4;

    public static int GetKernelSize(int kernelRadius) => kernelRadius * 2 + 1;

    public static int GetCanvasMargin(int kernelRadius) => Math.Max(DefaultCanvasMargin, (kernelRadius + 3) & ~3);
}
