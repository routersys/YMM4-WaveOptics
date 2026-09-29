using WaveOptics.Abstractions;
using WaveOptics.Optics;

namespace WaveOptics;

public static class WaveOpticsApi
{
    public static OpticalApiVersion Version => OpticalApiVersion.Current;

    public static IPsfGenerator CreatePsfGenerator() => new FraunhoferPsfGenerator();
}
