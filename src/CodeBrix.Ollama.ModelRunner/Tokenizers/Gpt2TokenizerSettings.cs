using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What a bundle's tokenizer configuration files say about how its byte-level encoder is used: whether a
/// beginning-of-sequence token is added, whether a space is put in front of the text, and which tokens are
/// matched as text before anything else.
/// </summary>
internal sealed class Gpt2TokenizerSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="addBeginningOfSequence">Whether tokenizing adds the beginning-of-sequence token.</param>
    /// <param name="addPrefixSpace">Whether a space is put in front of the text before it is cut up.</param>
    /// <param name="beginningOfSequence">The beginning-of-sequence token's text, or <see langword="null"/>.</param>
    /// <param name="added">The tokens matched as text before anything else.</param>
    /// <param name="unknown">The unknown-token text, or <see langword="null"/>.</param>
    internal Gpt2TokenizerSettings(
        bool addBeginningOfSequence,
        bool addPrefixSpace,
        string beginningOfSequence,
        string unknown,
        IReadOnlyList<Gpt2AddedToken> added)
    {
        AddBeginningOfSequence = addBeginningOfSequence;
        AddPrefixSpace = addPrefixSpace;
        BeginningOfSequence = beginningOfSequence;
        Unknown = unknown;
        Added = added;
    }

    /// <summary>
    /// Whether tokenizing with special tokens adds the beginning-of-sequence token in front. Most bundles of
    /// this family say no, and the driver follows what the bundle says rather than what the model's
    /// generation settings name.
    /// </summary>
    internal bool AddBeginningOfSequence { get; }

    /// <summary>Whether a space is put in front of the text before it is cut into pieces.</summary>
    internal bool AddPrefixSpace { get; }

    /// <summary>The beginning-of-sequence token's text, or <see langword="null"/> when the bundle names none.</summary>
    internal string BeginningOfSequence { get; }

    /// <summary>
    /// The unknown-token text, or <see langword="null"/> when the bundle names none. It is what a symbol the
    /// vocabulary does not hold becomes, which a byte-level vocabulary never produces.
    /// </summary>
    internal string Unknown { get; }

    /// <summary>The tokens matched as text before anything else, longest first.</summary>
    internal IReadOnlyList<Gpt2AddedToken> Added { get; }

    /// <summary>The settings a bundle that says nothing at all gets.</summary>
    /// <returns>The defaults: nothing added, no prefix space, no added tokens.</returns>
    internal static Gpt2TokenizerSettings Default() =>
        new Gpt2TokenizerSettings(false, false, null, null, new List<Gpt2AddedToken>());
}
