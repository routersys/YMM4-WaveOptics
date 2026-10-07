using SpectralConvolution;
using WaveOptics.Abstractions;
using WaveOptics.Effects;
using WaveOptics.Optics;

namespace WaveOptics.Rendering;

internal sealed class WaveOpticsKernel
{
    private readonly double[] _values = new double[WaveOpticsSettings.MaximumKernelSize * WaveOpticsSettings.MaximumKernelSize];
    private readonly double[] _redValues = new double[WaveOpticsSettings.MaximumKernelSize * WaveOpticsSettings.MaximumKernelSize];
    private readonly double[] _blueValues = new double[WaveOpticsSettings.MaximumKernelSize * WaveOpticsSettings.MaximumKernelSize];
    private readonly FraunhoferKernelSampler _sampler = new();
    private readonly ChromaticKernelSampler _chromaticSampler = new();
    private readonly KernelSpectrum _spectrum = new();
    private readonly ChromaticKernelSpectrum _chromaticSpectrum = new();
    private WaveOpticsPipeline.PsfParameters? _key;
    private bool _isValid;
    private int _version;

    public KernelSpectrum Spectrum => _spectrum;

    public ChromaticKernelSpectrum ChromaticSpectrum => _chromaticSpectrum;

    public bool IsChromatic => _key is { ColorMode: not WaveOpticsColorMode.Monochrome };

    public int Size => IsChromatic ? _chromaticSpectrum.Size : _spectrum.Size;

    public int Radius => IsChromatic ? _chromaticSpectrum.Radius : _spectrum.Radius;

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
        var chromatic = psf.ColorMode != WaveOpticsColorMode.Monochrome;
        var specification = new PsfSpecification(
            pupilGridSize,
            WaveOpticsSettings.GetPupilDiameterSamples(pupilGridSize),
            WaveOpticsSettings.GetKernelSize(psf.KernelRadius),
            chromatic ? SpectralPlan.ReferenceWavelength : psf.Wavelength,
            psf.FNumber,
            psf.PixelPitch,
            psf.ApertureShape == WaveOpticsApertureShape.Circular ? ApertureShape.Circular : ApertureShape.RegularPolygon,
            psf.BladeCount,
            psf.BladeRotation,
            psf.Obstruction,
            aberration);
        var size = specification.KernelSize;
        var values = _values.AsSpan(0, size * size);
        if (chromatic)
            return TryUpdateChromatic(in specification, psf, values);

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

    private bool TryUpdateChromatic(in PsfSpecification specification, in WaveOpticsPipeline.PsfParameters psf, Span<double> green)
    {
        var size = green.Length;
        var red = _redValues.AsSpan(0, size);
        var blue = _blueValues.AsSpan(0, size);
        if (!_chromaticSampler.TrySample(in specification, psf.ColorMode, psf.Quality, red, green, blue))
            return false;
        if (!HasEnergy(red) || !HasEnergy(green) || !HasEnergy(blue))
            return false;

        _chromaticSpectrum.Update(_redValues.AsMemory(0, size), _values.AsMemory(0, size), _blueValues.AsMemory(0, size), psf.KernelRadius, TilePlan.SelectSize(psf.KernelRadius));
        _isValid = true;
        return true;
    }

    private static bool HasEnergy(ReadOnlySpan<double> values)
    {
        var sum = 0d;
        foreach (var value in values)
            sum += value;
        return double.IsFinite(sum) && sum > 0d;
    }

    public static WaveOpticsPipeline.PsfParameters KeyOf(in WaveOpticsPipeline.PsfParameters psf)
    {
        var key = psf.ApertureShape == WaveOpticsApertureShape.Circular ? psf with { BladeCount = 0, BladeRotation = 0f } : psf;
        return key.ColorMode == WaveOpticsColorMode.Monochrome ? key : key with { Wavelength = 0f };
    }
}
