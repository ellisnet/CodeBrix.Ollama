namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// The facts about the MuPT family that a conversion cannot read out of the files a publisher ships, and the
/// prompt form its cards document. They live on the TEST side, where a fact about one model belongs: nothing
/// model-specific ever enters either library.
/// </summary>
public static class MuPtFacts
{
    /// <summary>
    /// The four special tokens the family's files do not declare. Its tokenizer class names them as default
    /// arguments inside the publisher's own Python, which a conversion that reads files never sees and never
    /// runs, so the store is told about them from here. Every model of the family declares the same four.
    /// </summary>
    public static readonly string[] SpecialTokens = { "<pad>", "<unk>", "<bos>", "<eos>" };

    /// <summary>
    /// The prompts, in the form the family's own cards document: the MuPT-8192 models are trained with
    /// <c>&lt;n&gt;</c> where a line break would be, and a prompt with real line breaks produces a bar of
    /// rests rather than music.
    /// </summary>
    public static readonly string[] Prompts =
    {
        "X:1<n>L:1/8<n>Q:1/8=200<n>M:4/4<n>K:Gmin<n>|:\"Gm\" BGdB",
        "X:1<n>M:4/4<n>L:1/8<n>K:G<n>",
        "X:1<n>T:A Simple Tune<n>M:4/4<n>L:1/8<n>K:D<n>"
    };
}
