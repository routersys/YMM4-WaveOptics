namespace WaveOptics.Abstractions;

public sealed record PsfGenerationResult(PsfKernel Kernel, PsfDiagnostics Diagnostics);
