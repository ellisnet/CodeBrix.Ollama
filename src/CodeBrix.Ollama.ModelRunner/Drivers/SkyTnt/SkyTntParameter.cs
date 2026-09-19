namespace CodeBrix.Ollama.ModelRunner; //was previously: midi_tokenizer.py@f504d5cb58f769ab0f2909c679238f6621034573

/// <summary>
/// One parameter family in the model's own vocabulary - a pitch, a channel, a duration - which owns a
/// CONTIGUOUS block of tokens: the value <c>v</c> is the token <see cref="FirstId"/> plus <c>v</c>.
/// </summary>
/// <remarks>
/// The blocks are what the sampling masks are made of: at the position in an event's row where a pitch is
/// expected, the only tokens allowed are this family's block, so the model cannot answer "pitch" with a
/// tempo.
/// </remarks>
internal sealed class SkyTntParameter
{
    /// <summary>Creates the family.</summary>
    /// <param name="name">Its name in the model's configuration, for example <c>pitch</c>.</param>
    /// <param name="firstId">The token standing for the value nought.</param>
    /// <param name="size">How many values it has, and so how many tokens the block holds.</param>
    internal SkyTntParameter(string name, int firstId, int size)
    {
        Name = name;
        FirstId = firstId;
        Size = size;
    }

    /// <summary>Its name in the model's configuration.</summary>
    internal string Name { get; }

    /// <summary>The token standing for the value nought.</summary>
    internal int FirstId { get; }

    /// <summary>How many values it has.</summary>
    internal int Size { get; }

    /// <summary>The token standing for a value.</summary>
    /// <param name="value">The value, nought to <see cref="Size"/> minus one.</param>
    /// <returns>The token.</returns>
    internal int TokenFor(int value) => FirstId + value;

    /// <summary>Whether a value is one this family can express.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when it is in range.</returns>
    internal bool Holds(int value) => value >= 0 && value < Size;
}
