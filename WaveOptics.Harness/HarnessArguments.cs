namespace WaveOptics.Harness;

internal sealed class HarnessException(string message) : Exception(message);

internal enum HarnessMode
{
    Render,
    Golden,
    Verify,
    Transition,
    Compare,
    Benchmark,
}

internal sealed record HarnessArguments(HarnessMode Mode, string? Input, string? OutputDirectory, string? Before, string? After)
{
    public const string Usage = """
        使い方:
          [<出力先>] [--input <画像>]                ケースを描き、PNG とハッシュを出します
          --golden [--input <画像>]                  基準値を golden.json に書きます
          --verify [--input <画像>]                  基準値と照合します
          --transition [<出力先>] [--input <画像>]   設定を変えた後とフレームを進めた後の描画を、作り直した描画と照合します
          --compare <前> <後>                        2 つの出力先の PNG を画素ごとに比べます
          --benchmark [--input <画像>]               描画を計測します
        """;

    public static HarnessArguments Parse(string[] arguments)
    {
        HarnessMode? mode = null;
        string? input = null;
        var positionals = new List<string>();
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "--golden":
                    mode = Select(mode, HarnessMode.Golden);
                    break;
                case "--verify":
                    mode = Select(mode, HarnessMode.Verify);
                    break;
                case "--transition":
                    mode = Select(mode, HarnessMode.Transition);
                    break;
                case "--compare":
                    mode = Select(mode, HarnessMode.Compare);
                    break;
                case "--benchmark":
                    mode = Select(mode, HarnessMode.Benchmark);
                    break;
                case "--input":
                    if (index + 1 >= arguments.Length)
                        throw new HarnessException("--input には画像のパスを続けてください。");
                    input = arguments[++index];
                    break;
                default:
                    if (argument.StartsWith('-'))
                        throw new HarnessException($"不明なオプションです。{argument}");
                    positionals.Add(argument);
                    break;
            }
        }

        var selected = mode ?? HarnessMode.Render;
        switch (selected)
        {
            case HarnessMode.Compare:
                if (positionals.Count != 2)
                    throw new HarnessException("--compare には前と後の出力先を続けてください。");
                if (input is not null)
                    throw new HarnessException("--compare に --input は使えません。");
                return new HarnessArguments(HarnessMode.Compare, null, null, positionals[0], positionals[1]);
            case HarnessMode.Render:
            case HarnessMode.Transition:
                if (positionals.Count > 1)
                    throw new HarnessException($"余分な引数です。{positionals[1]}");
                return new HarnessArguments(selected, input, positionals.FirstOrDefault(), null, null);
            default:
                if (positionals.Count > 0)
                    throw new HarnessException($"余分な引数です。{positionals[0]}");
                return new HarnessArguments(selected, input, null, null, null);
        }
    }

    static HarnessMode Select(HarnessMode? current, HarnessMode next)
        => current is null ? next : throw new HarnessException("モードは 1 つだけ指定してください。");
}
