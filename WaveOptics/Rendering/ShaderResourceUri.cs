namespace WaveOptics.Rendering;

internal static class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new($"pack://application:,,,/WaveOptics;component/Resources/Shader/{shaderName}.cso", UriKind.Absolute);
}
