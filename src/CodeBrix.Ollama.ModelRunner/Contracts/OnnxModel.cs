using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The entry point for running an ONNX graph in this process: loads one into an <see cref="IOnnxModel"/>.
/// </summary>
/// <remarks>
/// <para>
/// IT NEEDS NOTHING INSTALLED. The interpreter that runs the graph is written in managed code inside this
/// package - no runtime to install, no Python, no native library of its own - so a graph runs wherever .NET
/// runs, on every platform this package supports.
/// </para>
/// <para>
/// LOADING TAKES PATHS. A graph file on its own, a bundle directory with a file name inside it, or a set of
/// (logical file name to path) pairs for the case where the files are held under names of their own - a
/// content-addressed store keeps them under digests, and the pairs are how a caller says which is which,
/// without copying anything into a directory first. Working those paths out is the CALLER's business: this
/// library knows nothing about model stores, and a model store knows nothing about this library.
/// </para>
/// <para>
/// WHAT IT IS NOT. This is the raw surface - tensors in by name, tensors out by name. It does not tokenize,
/// sample, or keep a key/value cache; those belong to whatever drives a particular family of model.
/// </para>
/// </remarks>
public static class OnnxModel
{
    /// <summary>
    /// Loads a graph from one <c>.onnx</c> file, with any side file it names beside it.
    /// </summary>
    /// <param name="modelPath">The graph file's path.</param>
    /// <param name="options">How to load and run it, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the load.</param>
    /// <returns>The loaded graph. Dispose it to release the weights.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is not set.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="OnnxRunnerOptions.Threads"/> or <see cref="OnnxRunnerOptions.MaxThreads"/> is below one.
    /// </exception>
    /// <exception cref="ModelLoadException">
    /// The file is not there, is not an ONNX graph, or asks for an operator, an operator set, an element type
    /// or an attribute this engine does not implement. The node and the operator are named.
    /// </exception>
    public static Task<IOnnxModel> LoadAsync(
        string modelPath, OnnxRunnerOptions options = null, CancellationToken cancellationToken = default)
    {
        RequireCoreContract();
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("A graph file path is required.", nameof(modelPath));
        }

        return LoadAsync(OnnxModelLocation.ForFile(modelPath), options, cancellationToken);
    }

    /// <summary>
    /// Loads a graph out of a bundle directory, with its side files in the same directory.
    /// </summary>
    /// <param name="bundleDirectory">The directory the bundle's files are laid out in.</param>
    /// <param name="modelFileName">
    /// The graph file's name inside it, as the publisher writes it, which may name a sub-folder - for example
    /// <c>onnx/model_base.onnx</c>.
    /// </param>
    /// <param name="options">How to load and run it, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the load.</param>
    /// <returns>The loaded graph. Dispose it to release the weights.</returns>
    /// <exception cref="ArgumentException"><paramref name="bundleDirectory"/> or <paramref name="modelFileName"/> is not set.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="OnnxRunnerOptions.Threads"/> or <see cref="OnnxRunnerOptions.MaxThreads"/> is below one.
    /// </exception>
    /// <exception cref="ModelLoadException">The file is not there, is not a graph, or asks for something this engine does not implement.</exception>
    public static Task<IOnnxModel> LoadFromDirectoryAsync(
        string bundleDirectory,
        string modelFileName,
        OnnxRunnerOptions options = null,
        CancellationToken cancellationToken = default)
    {
        RequireCoreContract();
        if (string.IsNullOrWhiteSpace(bundleDirectory))
        {
            throw new ArgumentException("A bundle directory is required.", nameof(bundleDirectory));
        }

        if (string.IsNullOrWhiteSpace(modelFileName))
        {
            throw new ArgumentException("A graph file name is required.", nameof(modelFileName));
        }

        return LoadAsync(
            OnnxModelLocation.ForDirectory(bundleDirectory, modelFileName), options, cancellationToken);
    }

    /// <summary>
    /// Loads a graph out of a set of (logical file name to path) pairs, for files that are not laid out as a
    /// directory - the blobs of a content-addressed store, for instance, which are held under digests.
    /// </summary>
    /// <param name="files">
    /// The bundle's files: the publisher's name for each one against the path it is really at. It must
    /// include the graph file and every side file the graph names.
    /// </param>
    /// <param name="modelFileName">The logical name of the graph file among them.</param>
    /// <param name="options">How to load and run it, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the load.</param>
    /// <returns>The loaded graph. Dispose it to release the weights.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="modelFileName"/> is not set or is not among the files, or a pair has an empty name or path.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="OnnxRunnerOptions.Threads"/> or <see cref="OnnxRunnerOptions.MaxThreads"/> is below one.
    /// </exception>
    /// <exception cref="ModelLoadException">The file is not there, is not a graph, or asks for something this engine does not implement.</exception>
    public static Task<IOnnxModel> LoadFromFilesAsync(
        IReadOnlyDictionary<string, string> files,
        string modelFileName,
        OnnxRunnerOptions options = null,
        CancellationToken cancellationToken = default)
    {
        RequireCoreContract();
        if (files == null) throw new ArgumentNullException(nameof(files));
        if (string.IsNullOrWhiteSpace(modelFileName))
        {
            throw new ArgumentException("A graph file name is required.", nameof(modelFileName));
        }

        return LoadAsync(OnnxModelLocation.ForFiles(files, modelFileName), options, cancellationToken);
    }

    private static async Task<IOnnxModel> LoadAsync(
        OnnxModelLocation location, OnnxRunnerOptions options, CancellationToken cancellationToken)
    {
        OnnxRunnerOptions copy = (options ?? new OnnxRunnerOptions()).Copy();
        if (copy.Threads.HasValue && copy.Threads.Value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), copy.Threads.Value, "A thread count must be at least one.");
        }

        if (copy.MaxThreads.HasValue && copy.MaxThreads.Value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), copy.MaxThreads.Value,
                $"{nameof(OnnxRunnerOptions)}.{nameof(OnnxRunnerOptions.MaxThreads)} must be at least one; "
                + "leave it null for no cap.");
        }

        OnnxExecutionSettings settings = OnnxExecutionSettings.Resolve(copy);
        copy.Threads = settings.Threads;

        OnnxExecutionPlan plan = await OnnxGraphLoader
            .LoadAsync(location, cancellationToken)
            .ConfigureAwait(false);

        return new OnnxSession(plan, copy, settings);
    }

    //The door an ONNX MODEL comes in through, which is where this library reaches CodeBrix.Ollama.Core: the
    //graph is read by the codec that lives there. An application that installed mismatched CodeBrix.Ollama
    //packages is told so here, in a sentence of its own, rather than through a missing member somewhere
    //further in. The check is an integer comparison, and CoreContract.Revision is a constant this assembly's
    //compiler baked in.
    private static void RequireCoreContract() =>
        CoreContract.Require(CoreContract.Revision, "CodeBrix.Ollama.ModelRunner");
}
