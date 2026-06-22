namespace WaveOptics.Abstractions;

public interface IPsfGenerator
{
    OpticalApiVersion ApiVersion { get; }
    OpticalCapabilities Capabilities { get; }
    PsfGenerationResult Generate(PsfDescriptor descriptor);
}
