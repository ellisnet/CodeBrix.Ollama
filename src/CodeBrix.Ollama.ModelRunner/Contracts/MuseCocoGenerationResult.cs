using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Generated MIDI events, raw music tokens and the settings needed to identify this run.</summary>
public sealed class MuseCocoGenerationResult
{
    internal MuseCocoGenerationResult(MidiScore score, int[] tokens, long seed, MusicAttributes attributes,
        bool endedWithEos, TimeSpan promptTime, TimeSpan generationTime)
    {
        Score = score;
        TokenIds = Array.AsReadOnly(tokens);
        Seed = seed;
        Attributes = attributes;
        EndedWithEos = endedWithEos;
        PromptTime = promptTime;
        GenerationTime = generationTime;
    }

    /// <summary>The decoded score. Save it with <see cref="MidiFile.WriteAsync"/> or consume its MIDI events.</summary>
    public MidiScore Score { get; }

    /// <summary>Generated token IDs, excluding the attribute prefix and EOS, before incomplete-ending cleanup.</summary>
    public IReadOnlyList<int> TokenIds { get; }

    /// <summary>The effective random seed, including when a fresh seed was requested.</summary>
    public long Seed { get; }

    /// <summary>The immutable attributes used by this generation.</summary>
    public MusicAttributes Attributes { get; }

    /// <summary>True for a model-selected EOS; false when the token budget stopped generation.</summary>
    public bool EndedWithEos { get; }

    /// <summary>Time spent processing the attribute prefix.</summary>
    public TimeSpan PromptTime { get; }

    /// <summary>Time spent selecting and generating music tokens, excluding MIDI decoding.</summary>
    public TimeSpan GenerationTime { get; }
}
