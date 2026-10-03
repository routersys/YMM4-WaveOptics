using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WaveOptics.Harness;

internal sealed record GoldenCase(string Name, IReadOnlyList<int> Frames, string Hash);

internal sealed record Golden(string Adapter, string Driver, string Input, string Canvas, IReadOnlyList<GoldenCase> Cases)
{
    const string FileName = "golden";
    const string Extension = ".json";
    const string CpuSuffix = "-cpu";
    const string DirectoryKey = "HarnessDirectory";

    static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
    };

    public static string PathFor(HarnessImage image, bool cpu)
        => Path.Combine(ReadDirectory(), (image.Key is null ? FileName : $"{FileName}-{image.Key}") + (cpu ? CpuSuffix : string.Empty) + Extension);

    public static Golden Load(string path)
    {
        if (!File.Exists(path))
            throw new HarnessException($"基準値がありません。先に --golden で書いてください。{path}");
        Golden? golden;
        try
        {
            golden = JsonSerializer.Deserialize<Golden>(File.ReadAllBytes(path), Options);
        }
        catch (JsonException exception)
        {
            throw new HarnessException($"基準値を読めません。{path} {exception.Message}");
        }

        if (golden is not { Adapter: not null, Driver: not null, Input: not null, Canvas: not null, Cases: not null }
            || golden.Cases.Any(item => item is not { Name: not null, Frames: not null, Hash: not null }))
            throw new HarnessException($"基準値の項目が足りません。{path}");
        if (golden.Cases.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != golden.Cases.Count)
            throw new HarnessException($"基準値のケース名が重複しています。{path}");
        return golden;
    }

    public void Save(string path) => File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(this, Options));

    static string ReadDirectory()
        => typeof(Golden).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, DirectoryKey, StringComparison.Ordinal))
            ?.Value ?? throw new HarnessException($"アセンブリに {DirectoryKey} がありません。");
}
