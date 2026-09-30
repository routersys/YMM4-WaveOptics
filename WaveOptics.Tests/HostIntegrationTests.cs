using System.Windows;
using WaveOptics.Effects;

namespace WaveOptics.Tests;

public sealed class HostIntegrationTests
{
    [Fact]
    public void OutsideAWpfApplicationTheEffectCanStillBeCreated()
    {
        Assert.Null(Application.Current);

        var effect = new WaveOpticsEffect();

        Assert.Equal(Texts.WaveOptics, effect.Label);
    }
}
