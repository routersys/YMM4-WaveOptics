using System.Globalization;
using WaveOptics.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Json;

namespace WaveOptics.Harness;

internal static class HarnessCases
{
    public static IEnumerable<(string Name, WaveOpticsEffect Effect, IReadOnlyList<int> Frames)> All()
    {
        yield return ("default", Create(), [0]);
        yield return ("default-frames-0-8", Create(), Enumerable.Range(0, 9).ToArray());
        yield return ("amount-0", Create(effect => effect.Amount.Values[0].Value = 0), [0]);
        yield return ("amount-50", Create(effect => effect.Amount.Values[0].Value = 50), [0]);
        yield return ("gain-50", Create(effect => effect.Gain.Values[0].Value = 50), [0]);
        yield return ("gain-400", Create(effect => effect.Gain.Values[0].Value = 400), [0]);
        yield return ("gain-400-amount-50", Create(effect =>
        {
            effect.Gain.Values[0].Value = 400;
            effect.Amount.Values[0].Value = 50;
        }), [0]);
        yield return ("wavelength-380", Create(effect => effect.Wavelength.Values[0].Value = 380), [0]);
        yield return ("wavelength-780", Create(effect => effect.Wavelength.Values[0].Value = 780), [0]);
        yield return ("f-number-4", Create(effect => effect.FNumber.Values[0].Value = 4), [0]);
        yield return ("f-number-64", Create(effect => effect.FNumber.Values[0].Value = 64), [0]);
        yield return ("pixel-pitch-0.25", Create(effect => effect.PixelPitch.Values[0].Value = 0.25), [0]);
        yield return ("pixel-pitch-2", Create(effect => effect.PixelPitch.Values[0].Value = 2), [0]);
        yield return ("kernel-radius-1", Create(effect => effect.KernelRadius = 1), [0]);
        yield return ("kernel-radius-5", Create(effect => effect.KernelRadius = 5), [0]);
        yield return ("kernel-radius-40-defocus-2", Create(effect =>
        {
            effect.KernelRadius = 40;
            effect.Defocus.Values[0].Value = 2;
        }), [0]);
        yield return ("kernel-radius-63-polygon-defocus-3", Create(effect =>
        {
            effect.KernelRadius = WaveOpticsSettings.MaximumKernelRadius;
            effect.Quality = WaveOpticsQuality.High;
            effect.ApertureShape = WaveOpticsApertureShape.RegularPolygon;
            effect.Defocus.Values[0].Value = 3;
        }), [0]);
        yield return ("quality-draft", Create(effect => effect.Quality = WaveOpticsQuality.Draft), [0]);
        yield return ("quality-high", Create(effect => effect.Quality = WaveOpticsQuality.High), [0]);
        yield return ("aperture-polygon", Create(effect => effect.ApertureShape = WaveOpticsApertureShape.RegularPolygon), [0]);
        yield return ("blade-count-3", Create(effect =>
        {
            effect.ApertureShape = WaveOpticsApertureShape.RegularPolygon;
            effect.BladeCount = 3;
        }), [0]);
        yield return ("blade-count-32", Create(effect =>
        {
            effect.ApertureShape = WaveOpticsApertureShape.RegularPolygon;
            effect.BladeCount = 32;
        }), [0]);
        yield return ("blade-rotation-30", Create(effect =>
        {
            effect.ApertureShape = WaveOpticsApertureShape.RegularPolygon;
            effect.BladeRotation.Values[0].Value = 30;
        }), [0]);
        yield return ("obstruction-50", Create(effect => effect.Obstruction.Values[0].Value = 50), [0]);
        yield return ("defocus-1", Create(effect => effect.Defocus.Values[0].Value = 1), [0]);
        yield return ("astigmatism-vertical-1", Create(effect => effect.AstigmatismVertical.Values[0].Value = 1), [0]);
        yield return ("astigmatism-oblique-1", Create(effect => effect.AstigmatismOblique.Values[0].Value = 1), [0]);
        yield return ("coma-horizontal-1", Create(effect => effect.ComaHorizontal.Values[0].Value = 1), [0]);
        yield return ("coma-vertical-1", Create(effect => effect.ComaVertical.Values[0].Value = 1), [0]);
        yield return ("spherical-1", Create(effect => effect.Spherical.Values[0].Value = 1), [0]);
        yield return ("defocus-animated-frames-0-8", Create(effect => effect.Defocus.CopyFrom(Linear(0d, 3d))), Enumerable.Range(0, 9).ToArray());
        yield return ("linear", Create(effect => effect.Linear = true), [0]);
        yield return ("linear-gain-150", Create(effect =>
        {
            effect.Linear = true;
            effect.Gain.Values[0].Value = 150;
        }), [0]);
        yield return ("linear-highlight-boost-30", Create(effect =>
        {
            effect.Linear = true;
            effect.HighlightThreshold.Values[0].Value = 70;
            effect.HighlightBoost.Values[0].Value = 30;
        }), [0]);
        yield return ("linear-highlight-threshold-50-boost-8", Create(effect =>
        {
            effect.Linear = true;
            effect.HighlightThreshold.Values[0].Value = 50;
            effect.HighlightBoost.Values[0].Value = 8;
        }), [0]);
        yield return ("dither", Create(effect => effect.Dither = true), [0]);
        yield return ("linear-dither-defocus-1", Create(effect =>
        {
            effect.Linear = true;
            effect.Dither = true;
            effect.Defocus.Values[0].Value = 1;
        }), [0]);
        yield return ("linear-highlight-polygon-defocus-2-radius-40", Create(effect =>
        {
            effect.Linear = true;
            effect.HighlightThreshold.Values[0].Value = 70;
            effect.HighlightBoost.Values[0].Value = 20;
            effect.ApertureShape = WaveOpticsApertureShape.RegularPolygon;
            effect.KernelRadius = 40;
            effect.Defocus.Values[0].Value = 2;
        }), [0]);
        yield return ("linear-highlight-animated-frames-0-8", Create(effect =>
        {
            effect.Linear = true;
            effect.HighlightThreshold.Values[0].Value = 70;
            effect.HighlightBoost.CopyFrom(Linear(1d, 40d));
        }), Enumerable.Range(0, 9).ToArray());
        yield return ("color-primaries-defocus-1", Create(effect =>
        {
            effect.ColorMode = WaveOpticsColorMode.Primaries;
            effect.Defocus.Values[0].Value = 1;
        }), [0]);
        yield return ("color-broadband-defocus-1", Create(effect =>
        {
            effect.ColorMode = WaveOpticsColorMode.Broadband;
            effect.Defocus.Values[0].Value = 1;
        }), [0]);
        yield return ("color-primaries-polygon-radius-40-clear", Create(effect =>
        {
            effect.ColorMode = WaveOpticsColorMode.Primaries;
            effect.ApertureShape = WaveOpticsApertureShape.RegularPolygon;
            effect.KernelRadius = 40;
            effect.FNumber.Values[0].Value = 16;
            effect.PixelPitch.Values[0].Value = 2;
        }), [0]);
        yield return ("color-broadband-high-coma-radius-63", Create(effect =>
        {
            effect.ColorMode = WaveOpticsColorMode.Broadband;
            effect.Quality = WaveOpticsQuality.High;
            effect.KernelRadius = WaveOpticsSettings.MaximumKernelRadius;
            effect.ComaHorizontal.Values[0].Value = 1;
        }), [0]);
        yield return ("color-primaries-linear-highlight-defocus-1", Create(effect =>
        {
            effect.ColorMode = WaveOpticsColorMode.Primaries;
            effect.Linear = true;
            effect.HighlightThreshold.Values[0].Value = 70;
            effect.HighlightBoost.Values[0].Value = 20;
            effect.Defocus.Values[0].Value = 1;
        }), [0]);
        yield return ("color-broadband-defocus-animated-frames-0-8", Create(effect =>
        {
            effect.ColorMode = WaveOpticsColorMode.Broadband;
            effect.Quality = WaveOpticsQuality.Draft;
            effect.Defocus.CopyFrom(Linear(0d, 3d));
        }), Enumerable.Range(0, 9).ToArray());
    }

    public static IEnumerable<(string Name, Func<WaveOpticsEffect> Create, Action<WaveOpticsEffect> Change, int Frame)> Transitions()
    {
        yield return ("amount-100-to-50", () => Create(), effect => effect.Amount.Values[0].Value = 50, 0);
        yield return ("gain-100-to-400", () => Create(), effect => effect.Gain.Values[0].Value = 400, 0);
        yield return ("wavelength-550-to-780", () => Create(), effect => effect.Wavelength.Values[0].Value = 780, 0);
        yield return ("f-number-8-to-32", () => Create(), effect => effect.FNumber.Values[0].Value = 32, 0);
        yield return ("pixel-pitch-4-to-2", () => Create(), effect => effect.PixelPitch.Values[0].Value = 2, 0);
        yield return ("kernel-radius-15-to-5", () => Create(), effect => effect.KernelRadius = 5, 0);
        yield return ("kernel-radius-15-to-40", () => Create(effect => effect.Defocus.Values[0].Value = 2), effect => effect.KernelRadius = 40, 0);
        yield return ("kernel-radius-40-to-15", () => Create(effect =>
        {
            effect.KernelRadius = 40;
            effect.Defocus.Values[0].Value = 2;
        }), effect => effect.KernelRadius = 15, 0);
        yield return ("quality-standard-to-high", () => Create(), effect => effect.Quality = WaveOpticsQuality.High, 0);
        yield return ("aperture-circular-to-polygon", () => Create(), effect => effect.ApertureShape = WaveOpticsApertureShape.RegularPolygon, 0);
        yield return ("blade-count-6-to-5", () => Create(effect => effect.ApertureShape = WaveOpticsApertureShape.RegularPolygon), effect => effect.BladeCount = 5, 0);
        yield return ("blade-rotation-0-to-30", () => Create(effect => effect.ApertureShape = WaveOpticsApertureShape.RegularPolygon), effect => effect.BladeRotation.Values[0].Value = 30, 0);
        yield return ("obstruction-0-to-50", () => Create(), effect => effect.Obstruction.Values[0].Value = 50, 0);
        yield return ("defocus-0-to-1", () => Create(), effect => effect.Defocus.Values[0].Value = 1, 0);
        yield return ("astigmatism-vertical-0-to-1", () => Create(), effect => effect.AstigmatismVertical.Values[0].Value = 1, 0);
        yield return ("astigmatism-oblique-0-to-1", () => Create(), effect => effect.AstigmatismOblique.Values[0].Value = 1, 0);
        yield return ("coma-horizontal-0-to-1", () => Create(), effect => effect.ComaHorizontal.Values[0].Value = 1, 0);
        yield return ("coma-vertical-0-to-1", () => Create(), effect => effect.ComaVertical.Values[0].Value = 1, 0);
        yield return ("spherical-0-to-1", () => Create(), effect => effect.Spherical.Values[0].Value = 1, 0);
        yield return ("amount-100-to-0", () => Create(), effect => effect.Amount.Values[0].Value = 0, 0);
        yield return ("linear-off-to-on", () => Create(), effect => effect.Linear = true, 0);
        yield return ("linear-on-to-off", () => Create(effect => effect.Linear = true), effect => effect.Linear = false, 0);
        yield return ("dither-off-to-on", () => Create(), effect => effect.Dither = true, 0);
        yield return ("dither-on-to-off", () => Create(effect => effect.Dither = true), effect => effect.Dither = false, 0);
        yield return ("highlight-boost-1-to-30", () => Create(effect =>
        {
            effect.Linear = true;
            effect.HighlightThreshold.Values[0].Value = 70;
        }), effect => effect.HighlightBoost.Values[0].Value = 30, 0);
        yield return ("highlight-threshold-95-to-50", () => Create(effect =>
        {
            effect.Linear = true;
            effect.HighlightBoost.Values[0].Value = 8;
        }), effect => effect.HighlightThreshold.Values[0].Value = 50, 0);
        yield return ("linear-gain-100-to-150", () => Create(effect => effect.Linear = true), effect => effect.Gain.Values[0].Value = 150, 0);
        yield return ("color-monochrome-to-primaries", () => Create(effect => effect.Defocus.Values[0].Value = 1), effect => effect.ColorMode = WaveOpticsColorMode.Primaries, 0);
        yield return ("color-primaries-to-broadband", () => Create(effect =>
        {
            effect.ColorMode = WaveOpticsColorMode.Primaries;
            effect.Defocus.Values[0].Value = 1;
        }), effect => effect.ColorMode = WaveOpticsColorMode.Broadband, 0);
        yield return ("color-broadband-to-monochrome", () => Create(effect =>
        {
            effect.ColorMode = WaveOpticsColorMode.Broadband;
            effect.Defocus.Values[0].Value = 1;
        }), effect => effect.ColorMode = WaveOpticsColorMode.Monochrome, 0);
        yield return ("color-primaries-defocus-1-to-2", () => Create(effect =>
        {
            effect.ColorMode = WaveOpticsColorMode.Primaries;
            effect.Defocus.Values[0].Value = 1;
        }), effect => effect.Defocus.Values[0].Value = 2, 0);
    }

    public static IEnumerable<(string Name, WaveOpticsEffect Effect)> Benchmarks()
    {
        yield return ("quality-draft", Create(effect => effect.Quality = WaveOpticsQuality.Draft));
        yield return ("default", Create());
        yield return ("quality-high", Create(effect => effect.Quality = WaveOpticsQuality.High));
        yield return ("kernel-radius-5", Create(effect => effect.KernelRadius = 5));
        yield return ("kernel-radius-40", Create(effect =>
        {
            effect.KernelRadius = 40;
            effect.Defocus.Values[0].Value = 2;
        }));
        yield return ("defocus-animated", Create(effect => effect.Defocus.CopyFrom(Linear(0d, 3d))));
        yield return ("amount-0", Create(effect => effect.Amount.Values[0].Value = 0));
        yield return ("color-primaries", Create(effect => effect.ColorMode = WaveOpticsColorMode.Primaries));
        yield return ("color-broadband", Create(effect => effect.ColorMode = WaveOpticsColorMode.Broadband));
        yield return ("color-primaries-defocus-animated", Create(effect =>
        {
            effect.ColorMode = WaveOpticsColorMode.Primaries;
            effect.Defocus.CopyFrom(Linear(0d, 3d));
        }));
        yield return ("color-broadband-defocus-animated", Create(effect =>
        {
            effect.ColorMode = WaveOpticsColorMode.Broadband;
            effect.Defocus.CopyFrom(Linear(0d, 3d));
        }));
        yield return ("color-broadband-high-defocus-animated", Create(effect =>
        {
            effect.ColorMode = WaveOpticsColorMode.Broadband;
            effect.Quality = WaveOpticsQuality.High;
            effect.KernelRadius = 40;
            effect.Defocus.CopyFrom(Linear(0d, 3d));
        }));
    }

    static Animation Linear(double from, double to)
        => Json.LoadFromText<Animation>(string.Create(CultureInfo.InvariantCulture, $$"""{"AnimationType":"直線移動","Values":[{"Value":{{from}}},{"Value":{{to}}}]}""")) ?? throw new HarnessException("アニメーションを読み込めません。");

    public static WaveOpticsEffect Create(Action<WaveOpticsEffect>? configure = null)
    {
        var effect = new WaveOpticsEffect();
        configure?.Invoke(effect);
        return effect;
    }
}
