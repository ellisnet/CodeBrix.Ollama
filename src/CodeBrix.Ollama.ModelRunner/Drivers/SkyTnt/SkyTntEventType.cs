using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: midi_tokenizer.py@f504d5cb58f769ab0f2909c679238f6621034573

/// <summary>
/// One kind of event in the model's own vocabulary: its name, the token that says "an event of this kind
/// follows", and the parameters it is made of, in the order their tokens are generated.
/// </summary>
/// <remarks>
/// A model of this family emits an event as a FIXED ROW OF TOKENS - the kind first, then one token per
/// parameter, then padding out to the longest row any kind needs - and this is what says how long a given
/// kind's row is and what each position in it means.
/// </remarks>
internal sealed class SkyTntEventType
{
    /// <summary>Creates the kind.</summary>
    /// <param name="name">Its name in the model's configuration, for example <c>note</c>.</param>
    /// <param name="id">The token that introduces an event of this kind.</param>
    /// <param name="parameters">The parameters it is made of, in generation order.</param>
    internal SkyTntEventType(string name, int id, IReadOnlyList<SkyTntParameter> parameters)
    {
        Name = name;
        Id = id;
        Parameters = parameters;
    }

    /// <summary>Its name in the model's configuration.</summary>
    internal string Name { get; }

    /// <summary>The token that introduces an event of this kind.</summary>
    internal int Id { get; }

    /// <summary>The parameters it is made of, in generation order.</summary>
    internal IReadOnlyList<SkyTntParameter> Parameters { get; }
}
