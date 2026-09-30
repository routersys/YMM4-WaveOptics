using WaveOptics.Effects;

namespace WaveOptics;

internal static class WaveOpticsSettings
{
    public const int MinimumKernelRadius = 1;
    public const int MaximumKernelRadius = 15;
    public const int MaximumKernelSize = MaximumKernelRadius * 2 + 1;
    public const int MaximumRank = MaximumKernelSize;
    public const double SeparableResidualRatio = 1e-4;
    public const int WeightsLength = MaximumRank * 2 * MaximumKernelSize;
    public const int ConvolutionBlock = 8;
    public const int CanvasMargin = (MaximumKernelRadius + 3) & ~3;
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

    public static int GetWeightOffset(int term, int axis) => (term * 2 + axis) * MaximumKernelSize;
}
