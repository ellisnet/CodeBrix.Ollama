using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The whole of <c>masks.json</c>: the vocabulary the publisher's tokenizer allots, and every mask its
/// generation loop builds.
/// </summary>
public sealed class SkyTntMaskFixture
{
    /// <summary>How many tokens there are altogether.</summary>
    public int VocabSize { get; set; }

    /// <summary>How long an event's row of tokens is.</summary>
    public int MaxTokenSeq { get; set; }

    /// <summary>The token that fills the unused end of a row.</summary>
    public int PadId { get; set; }

    /// <summary>The token a piece starts with.</summary>
    public int BosId { get; set; }

    /// <summary>The token that ends a piece.</summary>
    public int EosId { get; set; }

    /// <summary>Each kind of event against the token that introduces it.</summary>
    public IReadOnlyDictionary<string, int> EventIds { get; set; }

    /// <summary>Each parameter family against the token standing for its value nought.</summary>
    public IReadOnlyDictionary<string, int> ParameterFirstIds { get; set; }

    /// <summary>Each parameter family against how many values it has.</summary>
    public IReadOnlyDictionary<string, int> ParameterSizes { get; set; }

    /// <summary>Every mask.</summary>
    public IReadOnlyList<SkyTntMaskCase> Cases { get; set; }
}
