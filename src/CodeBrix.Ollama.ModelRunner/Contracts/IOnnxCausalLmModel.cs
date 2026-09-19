namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A text-generation model loaded from an ONNX bundle and ready to query. It is an
/// <see cref="IRunningModel"/>, so the same calls that complete a prompt against a checkpoint run against a
/// bundle - and the parts of that contract a bundle cannot honour are refused by name rather than answered
/// wrongly.
/// </summary>
/// <remarks>
/// <para>
/// WHAT IT IMPLEMENTS, and what a caller can rely on: <see cref="IRunningModel.TokenizeAsync"/>,
/// <see cref="IRunningModel.DetokenizeAsync"/>, <see cref="IRunningModel.GenerateAsync"/>,
/// <see cref="IRunningModel.GenerateToEndAsync"/> and <see cref="IRunningModel.ClearCacheAsync"/>, with the
/// same <see cref="GenerationOptions"/>, <see cref="SamplingOptions"/>, <see cref="GenerationUpdate"/> and
/// <see cref="GenerationResult"/> types the rest of this library uses.
/// </para>
/// <para>
/// WHAT IT REFUSES, each with a <see cref="System.NotSupportedException"/> naming it:
/// <see cref="IRunningModel.ChatAsync"/>, <see cref="IRunningModel.ChatToEndAsync"/> and
/// <see cref="IRunningModel.RenderChatPromptAsync"/> - a bundle carries no chat template and this driver
/// applies none, so a conversation is the CALLER's to render into a prompt;
/// <see cref="IRunningModel.EmbedAsync"/> - a decoder graph answers with scores over the vocabulary and
/// hands back no hidden state to pool; and <see cref="IRunningModel.SetLoraAdaptersAsync"/> - an adapter is
/// applied to a checkpoint's weights and there is nothing in a graph to apply one to. On
/// <see cref="GenerationOptions"/> the three constraint settings - <c>Grammar</c>, <c>JsonSchema</c> and
/// <c>JsonMode</c> - are refused the same way, because constraining output needs a grammar engine this
/// driver does not carry.
/// </para>
/// <para>
/// EVERY REQUEST EVALUATES ITS WHOLE PROMPT. There is no prefix cache between requests: the cache belongs to
/// the generation and is emptied when it ends, so a growing conversation re-reads its history every turn.
/// <see cref="IRunningModel.ClearCacheAsync"/> therefore has nothing to clear and completes at once.
/// </para>
/// <para>
/// ONE GENERATION AT A TIME. A second request that arrives while one is in flight is refused with
/// <see cref="InferenceException"/> rather than left to wait; load the bundle again to run two at once.
/// </para>
/// </remarks>
public interface IOnnxCausalLmModel : IRunningModel
{
    /// <summary>What the bundle says about itself: its decoder's shape, its context and its special tokens.</summary>
    CausalLmMetadata Metadata { get; }

    /// <summary>
    /// The options the graph was loaded with, with every default resolved to the value in use. This is the
    /// ONNX engine's own settings; <see cref="IRunningModel.Options"/> carries the few of them that the
    /// shared contract has a place for.
    /// </summary>
    OnnxRunnerOptions RunnerOptions { get; }
}
