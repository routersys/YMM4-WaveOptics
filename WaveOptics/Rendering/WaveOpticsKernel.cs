using SpectralConvolution;
using WaveOptics.Abstractions;
using WaveOptics.Effects;
using WaveOptics.Optics;

namespace WaveOptics.Rendering;

internal sealed class WaveOpticsKernel
{
    private readonly double[] _values = new double[WaveOpticsSettings.MaximumKernelSize * WaveOpticsSettings.MaximumKernelSize];
    private readonly FraunhoferKernelSampler _sampler = new();
    private readonly KernelSpectrum _spectrum = new();
    private WaveOpticsPipeline.PsfParameters? _key;
    private bool _isValid;
    private int _version;

    public KernelSpectrum Spectrum => _spectrum;

    public bool IsValid => _isValid;

    public int Version => _version;

    public bool TryUpdate(in WaveOpticsPipeline.PsfParameters psf)
    {
        var key = KeyOf(psf);
        if (_key == key)
            return _isValid;

        _key = key;
        _isValid = false;
        _version++;
        var pupilGridSize = WaveOpticsSettings.GetPupilGridSize(psf.Quality);
        var aberration = new WavefrontAberration(
            defocusWaves: psf.Defocus,
            astigmatismVerticalWaves: psf.AstigmatismVertical,
            astigmatismObliqueWaves: psf.AstigmatismOblique,
            comaHorizontalWaves: psf.ComaHorizontal,
            comaVerticalWaves: psf.ComaVertical,
            sphericalWaves: psf.Spherical);
        var specification = new PsfSpecification(
            pupilGridSize,
            WaveOpticsSettings.GetPupilDiameterSamples(pupilGridSize),
            WaveOpticsSettings.GetKernelSize(psf.KernelRadius),
            psf.Wavelength,
            psf.FNumber,
            psf.PixelPitch,
            psf.ApertureShape == WaveOpticsApertureShape.Circular ? ApertureShape.Circular : ApertureShape.RegularPolygon,
            psf.BladeCount,
            psf.BladeRotation,
            psf.Obstruction,
            aberration);
        var size = specification.KernelSize;
        var values = _values.AsSpan(0, size * size);
        if (!_sampler.TrySample(in specification, values))
            return false;

        var sum = 0d;
        foreach (var value in values)
            sum += value;
        if (!double.IsFinite(sum) || sum <= 0d)
            return false;

        _spectrum.Update(values, psf.KernelRadius, TilePlan.SelectSize(psf.KernelRadius));
        _isValid = true;
        return true;
    }

    public static WaveOpticsPipeline.PsfParameters KeyOf(in WaveOpticsPipeline.PsfParameters psf)
        => psf.ApertureShape == WaveOpticsApertureShape.Circular ? psf with { BladeCount = 0, BladeRotation = 0f } : psf;
}
