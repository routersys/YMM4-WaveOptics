using System.Numerics;

namespace WaveOptics.Abstractions;

public sealed class PsfDescriptor : IEquatable<PsfDescriptor>
{
    public int PupilGridSize { get; }
    public int PupilDiameterSamples { get; }
    public int KernelSize { get; }
    public double WavelengthNanometers { get; }
    public double FNumber { get; }
    public double SensorPixelPitchMicrometers { get; }
    public ApertureShape ApertureShape { get; }
    public int BladeCount { get; }
    public double BladeRotationDegrees { get; }
    public double CentralObstructionRatio { get; }
    public WavefrontAberration Aberration { get; }

    public PsfDescriptor(
        int pupilGridSize,
        int pupilDiameterSamples,
        int kernelSize,
        double wavelengthNanometers,
        double fNumber,
        double sensorPixelPitchMicrometers,
        ApertureShape apertureShape,
        int bladeCount,
        double bladeRotationDegrees,
        double centralObstructionRatio,
        WavefrontAberration aberration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pupilGridSize, 64);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pupilGridSize, 2048);
        if (!BitOperations.IsPow2((uint)pupilGridSize))
            throw new ArgumentException(nameof(pupilGridSize));
        ArgumentOutOfRangeException.ThrowIfLessThan(pupilDiameterSamples, 16);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pupilDiameterSamples, pupilGridSize / 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(kernelSize, 3);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(kernelSize, 255);
        if ((kernelSize & 1) == 0)
            throw new ArgumentException(nameof(kernelSize));
        ValidateFiniteRange(wavelengthNanometers, 380, 780, nameof(wavelengthNanometers));
        ValidateFiniteRange(fNumber, 0.5, 64, nameof(fNumber));
        ValidateFiniteRange(sensorPixelPitchMicrometers, 0.25, 100, nameof(sensorPixelPitchMicrometers));
        if (!Enum.IsDefined(apertureShape))
            throw new ArgumentOutOfRangeException(nameof(apertureShape));
        ArgumentOutOfRangeException.ThrowIfLessThan(bladeCount, 3);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bladeCount, 32);
        ValidateFiniteRange(bladeRotationDegrees, -360, 360, nameof(bladeRotationDegrees));
        ValidateFiniteRange(centralObstructionRatio, 0, 0.95, nameof(centralObstructionRatio));

        PupilGridSize = pupilGridSize;
        PupilDiameterSamples = pupilDiameterSamples;
        KernelSize = kernelSize;
        WavelengthNanometers = wavelengthNanometers;
        FNumber = fNumber;
        SensorPixelPitchMicrometers = sensorPixelPitchMicrometers;
        ApertureShape = apertureShape;
        BladeCount = bladeCount;
        BladeRotationDegrees = bladeRotationDegrees;
        CentralObstructionRatio = centralObstructionRatio;
        Aberration = aberration;
    }

    static void ValidateFiniteRange(double value, double minimum, double maximum, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(value), true, name);
        ArgumentOutOfRangeException.ThrowIfLessThan(value, minimum, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, maximum, name);
    }

    public bool Equals(PsfDescriptor? other)
    {
        return other is not null
            && PupilGridSize == other.PupilGridSize
            && PupilDiameterSamples == other.PupilDiameterSamples
            && KernelSize == other.KernelSize
            && WavelengthNanometers.Equals(other.WavelengthNanometers)
            && FNumber.Equals(other.FNumber)
            && SensorPixelPitchMicrometers.Equals(other.SensorPixelPitchMicrometers)
            && ApertureShape == other.ApertureShape
            && BladeCount == other.BladeCount
            && BladeRotationDegrees.Equals(other.BladeRotationDegrees)
            && CentralObstructionRatio.Equals(other.CentralObstructionRatio)
            && Aberration.Equals(other.Aberration);
    }

    public override bool Equals(object? obj) => Equals(obj as PsfDescriptor);

    public override int GetHashCode()
    {
        var first = HashCode.Combine(PupilGridSize, PupilDiameterSamples, KernelSize, WavelengthNanometers, FNumber, SensorPixelPitchMicrometers);
        var second = HashCode.Combine(ApertureShape, BladeCount, BladeRotationDegrees, CentralObstructionRatio, Aberration);
        return HashCode.Combine(first, second);
    }
}
