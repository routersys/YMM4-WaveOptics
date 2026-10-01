using WaveOptics.Abstractions;

namespace WaveOptics.Optics;

internal readonly record struct PsfSpecification(
    int PupilGridSize,
    int PupilDiameterSamples,
    int KernelSize,
    double WavelengthNanometers,
    double FNumber,
    double SensorPixelPitchMicrometers,
    ApertureShape ApertureShape,
    int BladeCount,
    double BladeRotationDegrees,
    double CentralObstructionRatio,
    WavefrontAberration Aberration)
{
    public static PsfSpecification Of(PsfDescriptor descriptor)
        => new(
            descriptor.PupilGridSize,
            descriptor.PupilDiameterSamples,
            descriptor.KernelSize,
            descriptor.WavelengthNanometers,
            descriptor.FNumber,
            descriptor.SensorPixelPitchMicrometers,
            descriptor.ApertureShape,
            descriptor.BladeCount,
            descriptor.BladeRotationDegrees,
            descriptor.CentralObstructionRatio,
            descriptor.Aberration);
}
