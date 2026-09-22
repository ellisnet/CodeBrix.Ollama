using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Request statistics filled by the shared MuseCoco token generation loop.</summary>
internal sealed class MuseCocoGenerationState
{
    internal long Seed { get; set; }
    internal bool EndedWithEos { get; set; }
    internal TimeSpan PromptTime { get; set; }
    internal TimeSpan GenerationTime { get; set; }
}
