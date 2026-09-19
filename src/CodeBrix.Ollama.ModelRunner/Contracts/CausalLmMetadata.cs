using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What an ONNX text-generation bundle says about itself: the shape of its decoder, the length of its
/// context, and the token numbers that begin and end a sequence.
/// </summary>
/// <remarks>
/// Every value here is READ OUT OF THE BUNDLE rather than assumed. Nothing about any particular publisher's
/// model is built into this library: a bundle from another publisher that fills the same generation
/// configuration in differently is driven by exactly the same code and reports itself here.
/// </remarks>
public sealed class CausalLmMetadata
{
    /// <summary>Builds the description of one loaded bundle.</summary>
    /// <param name="architecture">The model type the bundle names.</param>
    /// <param name="modelFileName">The graph file's name inside the bundle.</param>
    /// <param name="tokenizerKind">The kind of tokenizer the bundle carries.</param>
    /// <param name="layerCount">How many layers.</param>
    /// <param name="headCount">How many attention heads.</param>
    /// <param name="keyValueHeadCount">How many key and value heads.</param>
    /// <param name="headSize">How wide one head is.</param>
    /// <param name="hiddenSize">The embedding width, or -1 when the bundle states none.</param>
    /// <param name="contextLength">The longest sequence the model was trained for.</param>
    /// <param name="vocabularySize">How many token numbers the model answers over.</param>
    /// <param name="beginningOfSequenceTokenId">The beginning-of-sequence token, or -1.</param>
    /// <param name="endOfSequenceTokenIds">The end-of-sequence tokens.</param>
    /// <param name="paddingTokenId">The padding token, or -1.</param>
    /// <param name="mergeCount">How many merges the tokenizer's table holds.</param>
    internal CausalLmMetadata(
        string architecture,
        string modelFileName,
        string tokenizerKind,
        int layerCount,
        int headCount,
        int keyValueHeadCount,
        int headSize,
        int hiddenSize,
        int contextLength,
        int vocabularySize,
        int beginningOfSequenceTokenId,
        IReadOnlyList<int> endOfSequenceTokenIds,
        int paddingTokenId,
        int mergeCount)
    {
        Architecture = architecture;
        ModelFileName = modelFileName;
        TokenizerKind = tokenizerKind;
        LayerCount = layerCount;
        HeadCount = headCount;
        KeyValueHeadCount = keyValueHeadCount;
        HeadSize = headSize;
        HiddenSize = hiddenSize;
        ContextLength = contextLength;
        VocabularySize = vocabularySize;
        BeginningOfSequenceTokenId = beginningOfSequenceTokenId;
        EndOfSequenceTokenIds = endOfSequenceTokenIds;
        PaddingTokenId = paddingTokenId;
        MergeCount = mergeCount;
    }

    /// <summary>The model type the bundle names, for example <c>llama</c>.</summary>
    public string Architecture { get; }

    /// <summary>The graph file's name inside the bundle.</summary>
    public string ModelFileName { get; }

    /// <summary>The kind of tokenizer the bundle carries, for example <c>GPT-2 byte-level BPE</c>.</summary>
    public string TokenizerKind { get; }

    /// <summary>How many layers the decoder has, which is how many key-and-value pairs its cache holds.</summary>
    public int LayerCount { get; }

    /// <summary>How many attention heads.</summary>
    public int HeadCount { get; }

    /// <summary>
    /// How many key and value heads. Fewer than <see cref="HeadCount"/> means several query heads share one
    /// cached key and value, which is what makes the cache smaller than the head count alone suggests.
    /// </summary>
    public int KeyValueHeadCount { get; }

    /// <summary>How wide one head is.</summary>
    public int HeadSize { get; }

    /// <summary>The embedding width, or -1 when the bundle states none.</summary>
    public int HiddenSize { get; }

    /// <summary>The longest sequence the model was trained for, prompt and generated tokens together.</summary>
    public int ContextLength { get; }

    /// <summary>How many token numbers the model answers over.</summary>
    public int VocabularySize { get; }

    /// <summary>The beginning-of-sequence token number, or -1 when the bundle names none.</summary>
    public int BeginningOfSequenceTokenId { get; }

    /// <summary>
    /// The end-of-sequence token numbers. A generation ends when the model produces any of them, and the
    /// token itself is not part of what the generation returns.
    /// </summary>
    public IReadOnlyList<int> EndOfSequenceTokenIds { get; }

    /// <summary>The padding token number, or -1 when the bundle names none.</summary>
    public int PaddingTokenId { get; }

    /// <summary>How many merges the tokenizer's table holds.</summary>
    public int MergeCount { get; }
}
