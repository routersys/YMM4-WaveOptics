using System.Windows;
using Telemetry;
using WaveOptics.Effects;

namespace WaveOptics.Tests;

public sealed class HostIntegrationTests
{
    [Fact]
    public void OutsideAWpfApplicationNoTelemetryIsStartedOrSent()
    {
        Assert.Null(Application.Current);

        WaveOpticsTelemetry.EnsureStartedOnce();
        WaveOpticsTelemetry.Report(new InvalidOperationException());

        Assert.Null(ProcessState.Read("DrainClaimed"));
        Assert.Null(ProcessState.Read("SentCount"));
    }

    [Fact]
    public void OutsideAWpfApplicationTheEffectCanStillBeCreated()
    {
        Assert.Null(Application.Current);

        var effect = new WaveOpticsEffect();

        Assert.Equal(Texts.WaveOptics, effect.Label);
        Assert.Null(ProcessState.Read("DrainClaimed"));
    }
}
