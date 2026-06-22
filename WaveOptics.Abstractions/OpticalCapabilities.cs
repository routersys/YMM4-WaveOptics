namespace WaveOptics.Abstractions;

[Flags]
public enum OpticalCapabilities
{
    None = 0,
    MonochromaticPsf = 1,
    CircularAperture = 2,
    RegularPolygonAperture = 4,
    CentralObstruction = 8,
    ZernikeAberration = 16,
    DirectConvolution = 32,
}
