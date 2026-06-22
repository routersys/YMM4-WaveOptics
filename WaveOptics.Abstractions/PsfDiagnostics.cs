namespace WaveOptics.Abstractions;

public readonly record struct PsfDiagnostics(
    int OpenPupilSampleCount,
    double FocalPlaneSamplePitchMicrometers,
    double UnnormalizedKernelEnergy,
    double PeakIntensity);
