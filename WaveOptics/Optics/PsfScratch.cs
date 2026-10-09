namespace WaveOptics.Optics;

internal sealed class PsfScratch
{
    public FraunhoferKernelSampler Sampler { get; } = new();

    public ChromaticKernelSampler ChromaticSampler { get; } = new();

    public double[] Green { get; private set; } = [];

    public double[] Red { get; private set; } = [];

    public double[] Blue { get; private set; } = [];

    public void Ensure(int area, bool chromatic)
    {
        if (Green.Length < area)
            Green = new double[area];
        if (!chromatic)
            return;

        if (Red.Length < area)
            Red = new double[area];
        if (Blue.Length < area)
            Blue = new double[area];
    }
}
