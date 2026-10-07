using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ComputeWeave;
using WaveOptics.Effects;
using WaveOptics.Harness;
using WaveOptics.Rendering;
using SharpGen.Runtime;

const int CanvasWidth = 1280;
const int CanvasHeight = 720;
const int ImageWidth = 640;
const int ImageHeight = 480;
const int FullHdWidth = 1920;
const int FullHdHeight = 1080;

_ = PackUriHelper.UriSchemePack;

HarnessArguments arguments;
try
{
    arguments = HarnessArguments.Parse(args);
}
catch (HarnessException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine(HarnessArguments.Usage);
    return 2;
}

try
{
    if (arguments.Mode == HarnessMode.Compare)
        return Compare(arguments.Before!, arguments.After!);

    if (arguments.Mode == HarnessMode.Convolution)
    {
        var convolutionImage = arguments.Input is { } convolutionInput ? HarnessImage.Load(convolutionInput) : HarnessImage.Synthetic(FullHdWidth, FullHdHeight);
        return Convolution(convolutionImage);
    }

    var outputDirectory = arguments.OutputDirectory ?? Path.Combine(AppContext.BaseDirectory, "harness-output");
    if (arguments.Mode == HarnessMode.Benchmark)
    {
        if (arguments.Input is { } benchmarkInput)
        {
            Benchmark(HarnessImage.Load(benchmarkInput), arguments.Cpu);
        }
        else
        {
            Benchmark(HarnessImage.Synthetic(CanvasWidth, CanvasHeight), arguments.Cpu);
            Benchmark(HarnessImage.Synthetic(FullHdWidth, FullHdHeight), arguments.Cpu);
        }

        return 0;
    }

    var image = arguments.Input is { } input ? HarnessImage.Load(input) : HarnessImage.Synthetic(ImageWidth, ImageHeight);
    using var renderer = new HarnessRenderer(CanvasWidth, CanvasHeight, image, arguments.Cpu);
    return arguments.Mode switch
    {
        HarnessMode.Golden => WriteGolden(renderer, image),
        HarnessMode.Verify => Verify(renderer, image),
        HarnessMode.Transition => Transition(renderer, outputDirectory),
        _ => Render(renderer, outputDirectory),
    };
}
catch (Exception exception) when (exception is HarnessException or SharpGenException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

static int Render(HarnessRenderer renderer, string outputDirectory)
{
    Directory.CreateDirectory(outputDirectory);
    var failures = 0;
    var cases = Evaluate(renderer, (golden, files, frames) =>
    {
        Console.WriteLine($"{golden.Name}: {golden.Hash}");
        for (var index = 0; index < files.Length; index++)
        {
            HarnessImage.Save(Path.Combine(outputDirectory, files[index]), frames[index], renderer.CanvasWidth, renderer.CanvasHeight);
            var opaque = CountOpaque(frames[index]);
            Console.WriteLine($"  {files[index]} opaque={opaque}");
            if (opaque == 0)
            {
                Console.Error.WriteLine($"{files[index]}: 出力が完全に透明です。");
                failures++;
            }
        }
    });
    Console.WriteLine($"-> {outputDirectory}");
    return failures + CountDuplicates(cases) == 0 ? 0 : 1;
}

static int WriteGolden(HarnessRenderer renderer, HarnessImage image)
{
    var cases = Evaluate(renderer, null);
    if (CountDuplicates(cases) != 0)
        return 1;
    if (cases.Count == 0)
        Console.Error.WriteLine("ケースがありません。HarnessCases に書き足してください。");

    var golden = new Golden(renderer.Adapter, renderer.Driver, image.Identity, $"{renderer.CanvasWidth}x{renderer.CanvasHeight}", cases);
    var path = Golden.PathFor(image, renderer.Cpu);
    golden.Save(path);
    Console.WriteLine($"adapter: {golden.Adapter} (driver {golden.Driver})");
    Console.WriteLine($"input: {golden.Input}");
    foreach (var (name, _, hash) in cases)
        Console.WriteLine($"{name}: {hash}");
    Console.WriteLine($"-> {path}");
    return 0;
}

static int Verify(HarnessRenderer renderer, HarnessImage image)
{
    var golden = Golden.Load(Golden.PathFor(image, renderer.Cpu));
    var canvas = $"{renderer.CanvasWidth}x{renderer.CanvasHeight}";
    if (golden.Input != image.Identity || golden.Canvas != canvas)
    {
        Console.Error.WriteLine($"基準値は入力 {golden.Input} とキャンバス {golden.Canvas} で書かれています。今回は入力 {image.Identity} とキャンバス {canvas} です。");
        return 1;
    }

    if (golden.Adapter != renderer.Adapter || golden.Driver != renderer.Driver)
        Console.Error.WriteLine($"基準値は {golden.Adapter} (driver {golden.Driver}) で書かれています。今回は {renderer.Adapter} (driver {renderer.Driver}) です。画素の差はこの違いから来ることがあります。");

    var failures = 0;
    var current = Evaluate(renderer, null).ToDictionary(item => item.Name, StringComparer.Ordinal);
    if (golden.Cases.Count == 0 && current.Count == 0)
        Console.Error.WriteLine("ケースがありません。HarnessCases に書き足してください。");
    foreach (var (name, frames, hash) in golden.Cases)
    {
        if (!current.Remove(name, out var actual))
        {
            Console.Error.WriteLine($"{name}: ケースがありません。基準値を書き直してください。");
            failures++;
        }
        else if (!actual.Frames.SequenceEqual(frames))
        {
            Console.Error.WriteLine($"{name}: フレームが違います。基準値 [{string.Join(", ", frames)}]、今回 [{string.Join(", ", actual.Frames)}]。基準値を書き直してください。");
            failures++;
        }
        else if (actual.Hash != hash)
        {
            Console.Error.WriteLine($"{name}: 一致しません。基準値 {hash}、今回 {actual.Hash}");
            failures++;
        }
        else
        {
            Console.WriteLine($"{name}: 一致");
        }
    }

    foreach (var name in current.Keys)
    {
        Console.Error.WriteLine($"{name}: 基準値にありません。基準値を書き直してください。");
        failures++;
    }

    return failures == 0 ? 0 : 1;
}

static int Transition(HarnessRenderer renderer, string outputDirectory)
{
    var failures = 0;
    foreach (var (name, create, change, frame) in HarnessCases.Transitions())
    {
        var (direct, transitioned) = renderer.RenderTransition(create, change, frame);
        var difference = ImageComparison.Of(direct, transitioned, renderer.CanvasWidth, renderer.CanvasHeight);
        if (difference.IsEmpty)
        {
            Console.WriteLine($"{name}: 一致");
            continue;
        }

        failures++;
        Directory.CreateDirectory(outputDirectory);
        HarnessImage.Save(Path.Combine(outputDirectory, name + "-direct.png"), direct, renderer.CanvasWidth, renderer.CanvasHeight);
        HarnessImage.Save(Path.Combine(outputDirectory, name + "-transitioned.png"), transitioned, renderer.CanvasWidth, renderer.CanvasHeight);
        Console.Error.WriteLine($"{name}: {difference}。設定を変えた後の描画が作り直した場合と違います。-> {outputDirectory}");
    }

    if (!HarnessCases.Transitions().Any() && !HarnessCases.All().Any(item => item.Frames.Count > 1))
        Console.Error.WriteLine("ケースがありません。HarnessCases に書き足してください。");

    foreach (var (name, effect, frames) in HarnessCases.All())
    {
        if (frames.Count < 2)
            continue;

        var sequential = renderer.Render(effect, frames);
        for (var index = 0; index < frames.Count; index++)
        {
            var fresh = renderer.Render(effect, [frames[index]])[0];
            var difference = ImageComparison.Of(fresh, sequential[index], renderer.CanvasWidth, renderer.CanvasHeight);
            var label = $"{name}-f{frames[index]:D3}";
            if (difference.IsEmpty)
            {
                Console.WriteLine($"{label}: 一致");
                continue;
            }

            failures++;
            Directory.CreateDirectory(outputDirectory);
            HarnessImage.Save(Path.Combine(outputDirectory, label + "-direct.png"), fresh, renderer.CanvasWidth, renderer.CanvasHeight);
            HarnessImage.Save(Path.Combine(outputDirectory, label + "-transitioned.png"), sequential[index], renderer.CanvasWidth, renderer.CanvasHeight);
            Console.Error.WriteLine($"{label}: {difference}。前のフレームから進めた描画が作り直した場合と違います。-> {outputDirectory}");
        }
    }

    return failures == 0 ? 0 : 1;
}

static int Convolution(HarnessImage image)
{
    const int Rounds = 12;
    const int Seed = 17;

    var device = GraphicsDevice.GetDefault();
    using var original = device.AllocateReadWriteTexture2D<Bgra32, Float4>(image.Width, image.Height);
    using var mirrored = device.AllocateReadWriteTexture2D<Bgra32, Float4>(image.Width, image.Height);
    using var output = device.AllocateReadWriteTexture2D<Bgra32, Float4>(image.Width, image.Height);
    var originalPixels = ToPixels(image, false);
    var mirroredPixels = ToPixels(image, true);
    original.CopyFrom(originalPixels);
    mirrored.CopyFrom(mirroredPixels);
    var originalBytes = MemoryMarshal.AsBytes(originalPixels.AsSpan()).ToArray();
    var mirroredBytes = MemoryMarshal.AsBytes(mirroredPixels.AsSpan()).ToArray();

    var parameters = new WaveOpticsPipeline.Parameters(1f, new WaveOpticsPipeline.PsfParameters(
        WaveOpticsQuality.Standard, 15, 550f, 8f, 4f, WaveOpticsApertureShape.Circular, 6, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f));
    WaveOpticsPipeline.Parameters Radius(int radius) => parameters with { Psf = parameters.Psf with { KernelRadius = radius, Defocus = 2f, ComaHorizontal = 1f } };

    var subjects = new List<IDisposable>();
    GpuSubject Gpu(bool check = false)
    {
        var subject = new GpuSubject(WaveOpticsPipeline.TryCreate(device) ?? throw new HarnessException("Direct3D 12を利用できません。"), output, image.Width, image.Height, check);
        subjects.Add(subject);
        return subject;
    }
    CpuSubject Cpu()
    {
        var subject = new CpuSubject(image.Width, image.Height);
        subjects.Add(subject);
        return subject;
    }

    void MeasureSet(string title, Func<(string Name, Action<bool> Run)[]> createOwnedSubjects)
    {
        try
        {
            var variants = createOwnedSubjects();
            var samples = new List<double>[variants.Length];
            for (var index = 0; index < samples.Length; index++)
                samples[index] = new List<double>(Rounds);

            foreach (var (_, run) in variants)
            {
                run(true);
                run(false);
            }

            var stopwatch = new Stopwatch();
            var order = Enumerable.Range(0, variants.Length).ToArray();
            var random = new Random(Seed);
            for (var round = 0; round < Rounds; round++)
            {
                for (var index = order.Length - 1; index > 0; index--)
                {
                    var swap = random.Next(index + 1);
                    (order[index], order[swap]) = (order[swap], order[index]);
                }

                foreach (var index in order)
                {
                    var flip = round % 2 == 0;
                    stopwatch.Restart();
                    variants[index].Run(flip);
                    stopwatch.Stop();
                    samples[index].Add(stopwatch.Elapsed.TotalMilliseconds);
                }
            }

            Console.WriteLine($"{title}");
            for (var index = 0; index < variants.Length; index++)
            {
                var sorted = samples[index].OrderBy(static value => value).ToArray();
                var median = sorted[sorted.Length / 2];
                Console.WriteLine($"  {variants[index].Name,-20} min={sorted[0],7:F2}  median={median,7:F2}  max={sorted[^1],7:F2}");
            }
        }
        finally
        {
            foreach (var subject in subjects)
                subject.Dispose();
            subjects.Clear();
        }
    }

    Console.WriteLine($"convolution recompute at {image.Width}x{image.Height} over {Rounds} interleaved rounds (ms)");

    MeasureSet("default options", () =>
    {
        var cached = Gpu();
        var gain = Gpu();
        var source = Gpu();
        var checkedSource = Gpu(true);
        var defocus = Gpu();
        var qualityHigh = Gpu();
        var source31 = Gpu();
        var source63 = Gpu();
        var cpuSource = Cpu();
        var cpuGain = Cpu();
        var cpuSource63 = Cpu();
        return
        [
            ("cached", _ => cached.Frame(original, parameters)),
            ("gain", flip => gain.Frame(original, parameters with { Gain = flip ? 1.5f : 1f })),
            ("source", flip => source.Frame(flip ? mirrored : original, parameters)),
            ("source-checked", flip => checkedSource.Frame(flip ? mirrored : original, parameters)),
            ("defocus", flip => defocus.Frame(original, parameters with { Psf = parameters.Psf with { Defocus = flip ? 0.1f : 0f } })),
            ("quality-high", flip => qualityHigh.Frame(original, parameters with { Psf = parameters.Psf with { Quality = flip ? WaveOpticsQuality.High : WaveOpticsQuality.Standard } })),
            ("source-r31", flip => source31.Frame(flip ? mirrored : original, Radius(31))),
            ("source-r63", flip => source63.Frame(flip ? mirrored : original, Radius(63))),
            ("cpu-source", flip => cpuSource.Frame(flip ? mirroredBytes : originalBytes, parameters)),
            ("cpu-gain", flip => cpuGain.Frame(originalBytes, parameters with { Gain = flip ? 1.5f : 1f })),
            ("cpu-source-r63", flip => cpuSource63.Frame(flip ? mirroredBytes : originalBytes, Radius(63))),
        ];
    });

    var linearLight = new SpectralConvolution.LightOptions(true, false, 0f, 0f);
    var highlightLight = new SpectralConvolution.LightOptions(true, false, 0.7f, 30f);
    MeasureSet("light options", () =>
    {
        var source = Gpu();
        var checkedSource = Gpu(true);
        var linear = Gpu();
        var checkedLinear = Gpu(true);
        var linearHighlight = Gpu();
        var cpuSource = Cpu();
        var cpuLinear = Cpu();
        var cpuLinearHighlight = Cpu();
        return
        [
            ("source", flip => source.Frame(flip ? mirrored : original, parameters)),
            ("source-checked", flip => checkedSource.Frame(flip ? mirrored : original, parameters)),
            ("linear", flip => linear.Frame(flip ? mirrored : original, parameters with { Light = linearLight })),
            ("linear-checked", flip => checkedLinear.Frame(flip ? mirrored : original, parameters with { Light = linearLight })),
            ("linear-highlight", flip => linearHighlight.Frame(flip ? mirrored : original, parameters with { Light = highlightLight })),
            ("cpu-source", flip => cpuSource.Frame(flip ? mirroredBytes : originalBytes, parameters)),
            ("cpu-linear", flip => cpuLinear.Frame(flip ? mirroredBytes : originalBytes, parameters with { Light = linearLight })),
            ("cpu-linear-highlight", flip => cpuLinearHighlight.Frame(flip ? mirroredBytes : originalBytes, parameters with { Light = highlightLight })),
        ];
    });

    WaveOpticsPipeline.Parameters Colored(WaveOpticsColorMode mode, int radius = 15, WaveOpticsQuality quality = WaveOpticsQuality.Standard, float defocus = 1f)
        => parameters with { Psf = parameters.Psf with { ColorMode = mode, KernelRadius = radius, Quality = quality, Defocus = defocus } };
    WaveOpticsPipeline.Parameters Retuned(WaveOpticsPipeline.Parameters source, bool flip)
        => source with { Psf = source.Psf with { Defocus = source.Psf.Defocus + (flip ? 0.1f : 0f) } };
    var mono = Colored(WaveOpticsColorMode.Monochrome);
    var primaries = Colored(WaveOpticsColorMode.Primaries);
    var broadband = Colored(WaveOpticsColorMode.Broadband);
    var broadbandHigh = Colored(WaveOpticsColorMode.Broadband, 40, WaveOpticsQuality.High);
    var primariesHigh = Colored(WaveOpticsColorMode.Primaries, 40, WaveOpticsQuality.High);
    var monoHigh = Colored(WaveOpticsColorMode.Monochrome, 40, WaveOpticsQuality.High);
    MeasureSet("color modes, source changes", () =>
    {
        var monoGpu = Gpu();
        var primariesGpu = Gpu();
        var broadbandGpu = Gpu();
        var primariesCheckedGpu = Gpu(true);
        var monoHighGpu = Gpu();
        var primariesHighGpu = Gpu();
        var monoCpu = Cpu();
        var primariesCpu = Cpu();
        var monoHighCpu = Cpu();
        var primariesHighCpu = Cpu();
        return
        [
            ("mono", flip => monoGpu.Frame(flip ? mirrored : original, mono)),
            ("primaries", flip => primariesGpu.Frame(flip ? mirrored : original, primaries)),
            ("broadband", flip => broadbandGpu.Frame(flip ? mirrored : original, broadband)),
            ("primaries-checked", flip => primariesCheckedGpu.Frame(flip ? mirrored : original, primaries)),
            ("mono-r40", flip => monoHighGpu.Frame(flip ? mirrored : original, monoHigh)),
            ("primaries-r40", flip => primariesHighGpu.Frame(flip ? mirrored : original, primariesHigh)),
            ("cpu-mono", flip => monoCpu.Frame(flip ? mirroredBytes : originalBytes, mono)),
            ("cpu-primaries", flip => primariesCpu.Frame(flip ? mirroredBytes : originalBytes, primaries)),
            ("cpu-mono-r40", flip => monoHighCpu.Frame(flip ? mirroredBytes : originalBytes, monoHigh)),
            ("cpu-primaries-r40", flip => primariesHighCpu.Frame(flip ? mirroredBytes : originalBytes, primariesHigh)),
        ];
    });

    MeasureSet("color modes, optics change", () =>
    {
        var monoGpu = Gpu();
        var primariesGpu = Gpu();
        var broadbandGpu = Gpu();
        var monoHighGpu = Gpu();
        var primariesHighGpu = Gpu();
        var broadbandHighGpu = Gpu();
        var monoCpu = Cpu();
        var primariesCpu = Cpu();
        var broadbandCpu = Cpu();
        var broadbandHighCpu = Cpu();
        return
        [
            ("mono", flip => monoGpu.Frame(original, Retuned(mono, flip))),
            ("primaries", flip => primariesGpu.Frame(original, Retuned(primaries, flip))),
            ("broadband", flip => broadbandGpu.Frame(original, Retuned(broadband, flip))),
            ("mono-high-r40", flip => monoHighGpu.Frame(original, Retuned(monoHigh, flip))),
            ("primaries-high-r40", flip => primariesHighGpu.Frame(original, Retuned(primariesHigh, flip))),
            ("broadband-high-r40", flip => broadbandHighGpu.Frame(original, Retuned(broadbandHigh, flip))),
            ("cpu-mono", flip => monoCpu.Frame(originalBytes, Retuned(mono, flip))),
            ("cpu-primaries", flip => primariesCpu.Frame(originalBytes, Retuned(primaries, flip))),
            ("cpu-broadband", flip => broadbandCpu.Frame(originalBytes, Retuned(broadband, flip))),
            ("cpu-broadband-high-r40", flip => broadbandHighCpu.Frame(originalBytes, Retuned(broadbandHigh, flip))),
        ];
    });

    return 0;
}

static Bgra32[] ToPixels(HarnessImage image, bool mirror)
{
    var pixels = new Bgra32[image.Width * image.Height];
    for (var y = 0; y < image.Height; y++)
    {
        for (var x = 0; x < image.Width; x++)
        {
            var offset = (y * image.Width + (mirror ? image.Width - 1 - x : x)) * HarnessImage.BytesPerPixel;
            pixels[y * image.Width + x] = new Bgra32(image.Pixels[offset + 2], image.Pixels[offset + 1], image.Pixels[offset], image.Pixels[offset + 3]);
        }
    }

    return pixels;
}

static int Compare(string beforeDirectory, string afterDirectory)
{
    if (!Directory.Exists(beforeDirectory) || !Directory.Exists(afterDirectory))
        throw new HarnessException("比べる出力先がありません。");

    var failures = 0;
    var names = Directory.EnumerateFiles(beforeDirectory, "*.png")
        .Concat(Directory.EnumerateFiles(afterDirectory, "*.png"))
        .Select(path => Path.GetFileName(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (names.Length == 0)
        throw new HarnessException("比べる PNG がありません。");

    foreach (var name in names)
    {
        var before = Path.Combine(beforeDirectory, name);
        var after = Path.Combine(afterDirectory, name);
        if (!File.Exists(before) || !File.Exists(after))
        {
            Console.Error.WriteLine($"{name}: {(File.Exists(before) ? "後" : "前")}にありません。");
            failures++;
            continue;
        }

        var (beforeWidth, beforeHeight, beforePixels) = HarnessImage.LoadPremultiplied(before);
        var (afterWidth, afterHeight, afterPixels) = HarnessImage.LoadPremultiplied(after);
        if (beforeWidth != afterWidth || beforeHeight != afterHeight)
        {
            Console.Error.WriteLine($"{name}: 寸法が違います。前 {beforeWidth}x{beforeHeight}、後 {afterWidth}x{afterHeight}");
            failures++;
            continue;
        }

        var difference = ImageComparison.Of(beforePixels, afterPixels, beforeWidth, beforeHeight);
        Console.WriteLine($"{name}: {difference}");
        if (!difference.IsEmpty)
            failures++;
    }

    return failures == 0 ? 0 : 1;
}

static void Benchmark(HarnessImage image, bool cpu)
{
    const int Frames = 60;

    using var renderer = new HarnessRenderer(image.Width, image.Height, image, cpu);
    Console.WriteLine($"adapter: {renderer.Adapter} (driver {renderer.Driver})");
    foreach (var (name, effect) in HarnessCases.Benchmarks())
    {
        Report(image, name, "still", renderer.Measure(effect, Frames, moving: false));
        Report(image, name, "moving", renderer.Measure(effect, Frames, moving: true));
    }
}

static void Report(HarnessImage image, string name, string motion, HarnessRenderer.Measurement measurement)
    => Console.WriteLine(
        $"{image.Width}x{image.Height} {name,-15} {motion,-6} " +
        $"gpu={(measurement.Gpu is { } gpu ? gpu.TotalMilliseconds.ToString("F3") : "n/a"),7} " +
        $"cpu={measurement.Cpu.TotalMilliseconds,7:F3} " +
        $"update={measurement.Update.TotalMilliseconds,6:F3} " +
        $"alloc={measurement.Allocated,6} gen0={measurement.Gen0}");

static List<GoldenCase> Evaluate(HarnessRenderer renderer, Action<GoldenCase, string[], byte[][]>? report)
{
    var cases = new List<GoldenCase>();
    var names = new HashSet<string>(StringComparer.Ordinal);
    foreach (var (name, effect, frames) in HarnessCases.All())
    {
        if (!names.Add(name))
            throw new HarnessException($"ケース名が重複しています。{name}");

        var rendered = renderer.Render(effect, frames);
        var files = new string[frames.Count];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var index = 0; index < frames.Count; index++)
        {
            hash.AppendData(rendered[index]);
            files[index] = frames.Count == 1 ? name + ".png" : $"{name}-f{frames[index]:D3}.png";
        }

        var golden = new GoldenCase(name, frames, Convert.ToHexString(hash.GetHashAndReset()));
        cases.Add(golden);
        report?.Invoke(golden, files, rendered);
    }

    return cases;
}

static int CountDuplicates(IReadOnlyList<GoldenCase> cases)
{
    var duplicates = 0;
    var seen = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var (name, _, hash) in cases)
    {
        if (seen.TryGetValue(hash, out var first))
        {
            Console.Error.WriteLine($"{name}: {first} と出力が一致します。設定がシェーダーへ届いていません。");
            duplicates++;
        }
        else
        {
            seen.Add(hash, name);
        }
    }

    return duplicates;
}

static int CountOpaque(byte[] pixels)
{
    var count = 0;
    for (var offset = HarnessImage.BytesPerPixel - 1; offset < pixels.Length; offset += HarnessImage.BytesPerPixel)
    {
        if (pixels[offset] != 0)
            count++;
    }

    return count;
}

sealed class GpuSubject(WaveOpticsPipeline pipeline, ReadWriteTexture2D<Bgra32, Float4> output, int width, int height, bool check) : IDisposable
{
    readonly SpectralConvolution.ConvolutionMeasurement[] measurements = check ? new SpectralConvolution.ConvolutionMeasurement[WaveOpticsPipeline.MeasurementCount] : [];
    float renderedGain = float.NaN;

    public void Frame(ReadWriteTexture2D<Bgra32, Float4> source, in WaveOpticsPipeline.Parameters parameters)
    {
        var changed = pipeline.Simulate(source, width, height, 0, 0, width, height, in parameters);
        if ((changed || renderedGain != parameters.Gain) && pipeline.TryGetVisibleBounds(width, height, in parameters, out var rect))
        {
            pipeline.RenderVisible(source, output, rect, in parameters, measurements);
            renderedGain = parameters.Gain;
        }
        pipeline.WaitForCompletion();
    }

    public void Dispose() => pipeline.Dispose();
}

sealed class CpuSubject(int width, int height) : IDisposable
{
    readonly WaveOpticsCpuPipeline pipeline = new();
    float renderedGain = float.NaN;

    public void Frame(byte[] source, in WaveOpticsPipeline.Parameters parameters)
    {
        var changed = pipeline.Simulate(source, width, height, 0, 0, width, height, in parameters);
        if ((changed || renderedGain != parameters.Gain) && pipeline.TryGetVisibleBounds(width, height, in parameters, out var rect))
        {
            pipeline.RenderVisible(rect, in parameters);
            renderedGain = parameters.Gain;
        }
    }

    public void Dispose() => pipeline.Dispose();
}
