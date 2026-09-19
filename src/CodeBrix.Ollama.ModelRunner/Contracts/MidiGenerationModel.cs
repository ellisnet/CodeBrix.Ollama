using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The entry point for generating MIDI music in this process: loads a bundle holding a two-graph MIDI model
/// into an <see cref="IMidiGenerationModel"/>.
/// </summary>
/// <remarks>
/// <para>
/// IT NEEDS NOTHING INSTALLED. The graphs run on the managed interpreter inside this package and the model's
/// own tokenizer, sampling and MIDI file writing are all here too - no runtime to install, no Python, no
/// native library, and no other package. A bundle and .NET are the whole of it.
/// </para>
/// <para>
/// LOADING TAKES PATHS. A bundle directory, or a set of (logical file name to path) pairs for the case where
/// the files are held under names of their own - a content-addressed store keeps them under digests, and the
/// pairs are how a caller says which is which, without copying anything into a directory first. Working those
/// paths out is the CALLER's business: this library knows nothing about model stores, and a model store
/// knows nothing about this library.
/// </para>
/// <para>
/// THE BUNDLE CHOOSES THE DRIVER. What is loaded here is decided by what the bundle's <c>config.json</c> says
/// it is; a bundle that is not a MIDI model of this family is refused by name. The interpreter underneath
/// knows nothing about any of it and will run any graph it is given.
/// </para>
/// <para>
/// WHAT IT EXPECTS TO FIND: a <c>config.json</c> describing the model and its tokenizer, and two graphs - one
/// that turns the events so far into hidden states and one that turns a hidden state into an event's tokens.
/// The publisher's names for those two are <c>model_base.onnx</c> and <c>model_token.onnx</c>, in a folder or
/// not.
/// </para>
/// </remarks>
public static class MidiGenerationModel
{
    /// <summary>
    /// Loads a MIDI model out of a bundle directory.
    /// </summary>
    /// <param name="bundleDirectory">The directory the bundle's files are laid out in.</param>
    /// <param name="options">
    /// How the two graphs are loaded and run - the thread count above all - or <see langword="null"/> for the
    /// defaults.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the load.</param>
    /// <returns>The loaded model. Dispose it to release both graphs' weights.</returns>
    /// <exception cref="ArgumentException"><paramref name="bundleDirectory"/> is not set.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="OnnxRunnerOptions.Threads"/> or <see cref="OnnxRunnerOptions.MaxThreads"/> is below one.
    /// </exception>
    /// <exception cref="ModelLoadException">
    /// The directory is not there, holds no <c>config.json</c>, describes a model this driver does not
    /// generate for, is missing one of the two graphs, or holds a graph this engine does not run.
    /// </exception>
    public static Task<IMidiGenerationModel> LoadFromDirectoryAsync(
        string bundleDirectory,
        OnnxRunnerOptions options = null,
        CancellationToken cancellationToken = default)
    {
        RequireCoreContract();
        if (string.IsNullOrWhiteSpace(bundleDirectory))
        {
            throw new ArgumentException("A bundle directory is required.", nameof(bundleDirectory));
        }

        SkyTntBundle bundle = SkyTntBundle.FromDirectory(bundleDirectory);
        return LoadAsync(
            bundle,
            name => OnnxModel.LoadFromDirectoryAsync(bundleDirectory, name, options, cancellationToken));
    }

    /// <summary>
    /// Loads a MIDI model out of a set of (logical file name to path) pairs, for files that are not laid out
    /// as a directory - the blobs of a content-addressed store, for instance, which are held under digests.
    /// </summary>
    /// <param name="files">
    /// The bundle's files: the publisher's name for each one against the path it is really at. It must
    /// include <c>config.json</c> and both graphs.
    /// </param>
    /// <param name="options">How the two graphs are loaded and run, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the load.</param>
    /// <returns>The loaded model. Dispose it to release both graphs' weights.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="OnnxRunnerOptions.Threads"/> or <see cref="OnnxRunnerOptions.MaxThreads"/> is below one.
    /// </exception>
    /// <exception cref="ModelLoadException">
    /// There is no <c>config.json</c> among the files, it describes a model this driver does not generate for,
    /// one of the two graphs is missing, or a graph is one this engine does not run.
    /// </exception>
    public static Task<IMidiGenerationModel> LoadFromFilesAsync(
        IReadOnlyDictionary<string, string> files,
        OnnxRunnerOptions options = null,
        CancellationToken cancellationToken = default)
    {
        RequireCoreContract();
        if (files == null) throw new ArgumentNullException(nameof(files));

        SkyTntBundle bundle = SkyTntBundle.FromFiles(files);
        return LoadAsync(
            bundle, name => OnnxModel.LoadFromFilesAsync(files, name, options, cancellationToken));
    }

    private static async Task<IMidiGenerationModel> LoadAsync(
        SkyTntBundle bundle, Func<string, Task<IOnnxModel>> load)
    {
        SkyTntTokenizer tokenizer = SkyTntTokenizer.FromConfiguration(bundle.Tokenizer);

        IOnnxModel baseModel = await load(bundle.BaseGraph).ConfigureAwait(false);
        IOnnxModel tokenModel = null;
        try
        {
            tokenModel = await load(bundle.TokenGraph).ConfigureAwait(false);
            return new SkyTntGenerationModel(bundle, tokenizer, baseModel, tokenModel);
        }
        catch
        {
            //A load that fails halfway would otherwise leave the first graph's weights held for as long as
            //nothing collected them, and the larger of the two is most of a gigabyte.
            tokenModel?.Dispose();
            baseModel.Dispose();
            throw;
        }
    }

    //The door a MIDI MODEL comes in through, which is where this library reaches CodeBrix.Ollama.Core: the
    //graphs are read by the codec that lives there. An application that installed mismatched CodeBrix.Ollama
    //packages is told so here, in a sentence of its own, rather than through a missing member somewhere
    //further in.
    private static void RequireCoreContract() =>
        CoreContract.Require(CoreContract.Revision, "CodeBrix.Ollama.ModelRunner");
}
