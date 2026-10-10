using SpectralConvolution;
using WaveOptics.Abstractions;
using WaveOptics.Effects;
using WaveOptics.Optics;

namespace WaveOptics.Rendering;

internal sealed class WaveOpticsKernel
{
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
        var area = specification.KernelSize * specification.KernelSize;
        using var lease = ScratchPool<PsfScratch>.Shared.Rent();
        var scratch = lease.Value;
        scratch.Ensure(area, chromatic);
        var values = scratch.Green.AsSpan(0, area);
        if (chromatic)
            return TryUpdateChromatic(scratch, in specification, psf, values);

        if (!scratch.Sampler.TrySample(in specification, values))
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

    private bool TryUpdateChromatic(PsfScratch scratch, in PsfSpecification specification, in WaveOpticsPipeline.PsfParameters psf, Span<double> green)
    {
        var size = green.Length;
        var red = scratch.Red.AsSpan(0, size);
        var blue = scratch.Blue.AsSpan(0, size);
        if (!scratch.ChromaticSampler.TrySample(in specification, psf.ColorMode, psf.Quality, red, green, blue))
            return false;
        if (!HasEnergy(red) || !HasEnergy(green) || !HasEnergy(blue))
            return false;

        _chromaticSpectrum.Update(scratch.Red.AsMemory(0, size), scratch.Green.AsMemory(0, size), scratch.Blue.AsMemory(0, size), psf.KernelRadius, TilePlan.SelectSize(psf.KernelRadius));
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
