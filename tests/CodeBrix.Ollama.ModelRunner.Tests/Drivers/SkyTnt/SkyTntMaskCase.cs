using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// One sampling mask as the publisher's own generation loop builds it: where in an event's row it applies,
/// what is being written, what is refused, and which tokens are left.
/// </summary>
public sealed class SkyTntMaskCase
{
    /// <summary>What the case is called.</summary>
    public string Name { get; set; }

    /// <summary>Which token of the row it applies to, counting the kind of event as nought.</summary>
    public int Position { get; set; }

    /// <summary>Whether the model has already said the piece is finished.</summary>
    public bool Ended { get; set; }

    /// <summary>The kind of event being written, or <see langword="null"/> at the first token.</summary>
    public string Event { get; set; }

    /// <summary>Whether the model is refused an instrument change.</summary>
    public bool DisablePatchChange { get; set; }

    /// <summary>Whether the model is refused a controller change.</summary>
    public bool DisableControlChange { get; set; }

    /// <summary>Which channels the model is refused.</summary>
    public IReadOnlyList<int> DisabledChannels { get; set; }

    /// <summary>The tokens it may answer with, sorted.</summary>
    public IReadOnlyList<int> Allowed { get; set; }
}
