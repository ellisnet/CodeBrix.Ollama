using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The entry point for generating TEXT from an ONNX bundle in this process: loads a bundle holding a
/// single-graph causal language model into an <see cref="IOnnxCausalLmModel"/>.
/// </summary>
/// <remarks>
/// <para>
/// IT NEEDS NOTHING INSTALLED. The graph runs on the managed interpreter inside this package, and the
/// tokenizer, the sampler and the generation loop are all here too - no runtime to install, no Python, no
/// native library, and no other package. A bundle and .NET are the whole of it.
/// </para>
/// <para>
/// LOADING TAKES PATHS. A bundle directory, or a set of (logical file name to path) pairs for the case where
/// the files are held under names of their own - a content-addressed store keeps them under digests, and the
/// pairs are how a caller says which is which, without copying anything into a directory first. Working those
/// paths out is the CALLER's business: this library knows nothing about model stores, and a model store knows
/// nothing about this library.
/// </para>
/// <para>
/// THE BUNDLE CHOOSES THE DRIVER. What is loaded here is decided by the bundle's own
/// <c>genai_config.json</c>: its decoder block names the graph, says what its tensors are called, and states
/// the layer, head and cache shapes the loop lays out. A bundle describing an encoder-decoder, a vision or
/// speech model, or a pipeline of several graphs is refused by name. The interpreter underneath knows nothing
/// about any of it and will run any graph it is given.
/// </para>
/// <para>
/// WHAT IT EXPECTS TO FIND: a <c>genai_config.json</c>, the graph file it names (with any side file holding
/// the weights beside it), and a byte-level byte-pair tokenizer - <c>vocab.json</c> and <c>merges.txt</c>,
/// with <c>tokenizer_config.json</c> and <c>special_tokens_map.json</c> when the publisher wrote them. A
/// tokenizer of another kind is refused by the name of what was found.
/// </para>
/// <para>
/// WHAT COMES BACK IS AN <see cref="IRunningModel"/>, so the same calls that complete a prompt against a
/// checkpoint work here; <see cref="IOnnxCausalLmModel"/> lists what a bundle cannot honour and how each of
/// those is refused.
/// </para>
/// </remarks>
public static class OnnxCausalLmModel
{
    /// <summary>Loads a text-generation model out of a bundle directory.</summary>
    /// <param name="bundleDirectory">The directory the bundle's files are laid out in.</param>
    /// <param name="options">
    /// How the graph is loaded and run - the thread count above all - or <see langword="null"/> for the
    /// defaults.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the load.</param>
    /// <returns>The loaded model. Dispose it to release the weights.</returns>
    /// <exception cref="ArgumentException"><paramref name="bundleDirectory"/> is not set.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="OnnxRunnerOptions.Threads"/> or <see cref="OnnxRunnerOptions.MaxThreads"/> is below one.
    /// </exception>
    /// <exception cref="ModelLoadException">
    /// The directory is not there, holds no <c>genai_config.json</c>, describes a model this driver does not
    /// generate for, is missing its graph or its tokenizer, or holds a graph this engine does not run.
    /// </exception>
    /// <exception cref="NotSupportedException">The bundle's tokenizer asks for a matching rule this library does not implement.</exception>
    public static Task<IOnnxCausalLmModel> LoadFromDirectoryAsync(
        string bundleDirectory,
        OnnxRunnerOptions options = null,
        CancellationToken cancellationToken = default)
    {
        RequireCoreContract();
        if (string.IsNullOrWhiteSpace(bundleDirectory))
        {
            throw new ArgumentException("A bundle directory is required.", nameof(bundleDirectory));
        }

        CausalLmBundle bundle = CausalLmBundle.FromDirectory(bundleDirectory);
        return LoadAsync(
            bundle,
            name => OnnxModel.LoadFromDirectoryAsync(bundleDirectory, name, options, cancellationToken));
    }

    /// <summary>
    /// Loads a text-generation model out of a set of (logical file name to path) pairs, for files that are
    /// not laid out as a directory - the blobs of a content-addressed store, for instance, which are held
    /// under digests.
    /// </summary>
    /// <param name="files">
    /// The bundle's files: the publisher's name for each one against the path it is really at. It must
    /// include <c>genai_config.json</c>, the graph it names, any side file the graph names, and the
    /// tokenizer's files.
    /// </param>
    /// <param name="options">How the graph is loaded and run, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the load.</param>
    /// <returns>The loaded model. Dispose it to release the weights.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="OnnxRunnerOptions.Threads"/> or <see cref="OnnxRunnerOptions.MaxThreads"/> is below one.
    /// </exception>
    /// <exception cref="ModelLoadException">
    /// There is no <c>genai_config.json</c> among the files, it describes a model this driver does not
    /// generate for, the graph or the tokenizer is missing, or the graph is one this engine does not run.
    /// </exception>
    /// <exception cref="NotSupportedException">The bundle's tokenizer asks for a matching rule this library does not implement.</exception>
    public static Task<IOnnxCausalLmModel> LoadFromFilesAsync(
        IReadOnlyDictionary<string, string> files,
        OnnxRunnerOptions options = null,
        CancellationToken cancellationToken = default)
    {
        RequireCoreContract();
        if (files == null) throw new ArgumentNullException(nameof(files));

        CausalLmBundle bundle = CausalLmBundle.FromFiles(files);
        return LoadAsync(
            bundle, name => OnnxModel.LoadFromFilesAsync(files, name, options, cancellationToken));
    }

    private static async Task<IOnnxCausalLmModel> LoadAsync(
        CausalLmBundle bundle, Func<string, Task<IOnnxModel>> load)
    {
        //The tokenizer is built FIRST, because a bundle whose tokenizer this library cannot read is refused
        //in milliseconds rather than after the weights have been read off disk.
        Gpt2ByteLevelTokenizer tokenizer = Gpt2TokenizerFiles.Load(bundle.Files);

        IOnnxModel model = await load(bundle.Decoder.FileName).ConfigureAwait(false);
        try
        {
            return new CausalLmGenerationModel(bundle, tokenizer, model);
        }
        catch
        {
            //A load that fails after the graph is read would otherwise leave its weights held for as long as
            //nothing collected them, and they run to most of a gigabyte.
            model.Dispose();
            throw;
        }
    }

    //The door a TEXT MODEL comes in through, which is where this library reaches CodeBrix.Ollama.Core: the
    //graph is read by the codec that lives there and the merge table by its tokenizer primitive. An
    //application that installed mismatched CodeBrix.Ollama packages is told so here, in a sentence of its
    //own, rather than through a missing member somewhere further in.
    private static void RequireCoreContract() =>
        CoreContract.Require(CoreContract.Revision, "CodeBrix.Ollama.ModelRunner");
}
