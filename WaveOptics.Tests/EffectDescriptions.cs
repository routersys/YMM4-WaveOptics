using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Player.Video;

namespace WaveOptics.Tests;

internal static class EffectDescriptions
{
    public const int Fps = 30;

    static readonly Size ScreenSize = new(1920, 1080);

    public static DrawDescription Draw(Vector3 position, Matrix4x4? camera = null)
        => new(position, Vector2.Zero, Vector2.One, Vector3.Zero, camera ?? Matrix4x4.Identity, InterpolationMode.Linear, 1d, false, []);

    public static EffectDescription At(int frame, int length) => At(frame, length, Draw(Vector3.Zero));

    public static EffectDescription At(int frame, int length, DrawDescription draw)
    {
        var timeline = new TimelineSourceDescription(
            ScreenSize,
            new FrameTime(frame, Fps),
            new FrameTime(length, Fps),
            Fps,
            TimelineSourceUsage.Playing,
            Guid.Empty,
            []);
        var item = new TimelineItemSourceDescription(timeline, frame, length, 0);
        return new EffectDescription(item, draw, 0, 1, 0, 1);
    }
}
