using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Player.Video;

namespace WaveOptics.Tests;

internal static class EffectDescriptions
{
    public const int Fps = 30;

    static readonly Size ScreenSize = new(1920, 1080);

    public static EffectDescription At(int frame, int length)
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
        var draw = new DrawDescription(Vector3.Zero, Vector2.Zero, Vector2.One, Vector3.Zero, Matrix4x4.Identity, InterpolationMode.Linear, 1d, false, []);
        return new EffectDescription(item, draw, 0, 1, 0, 1);
    }
}
