using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;
using ModelQueryTool.ModelAccess.Models;

namespace ModelQueryTool.ModelAccess;

/// <summary>
/// The one implementation of <see cref="IModelStager"/>: a private store under a folder the application
/// owns, filled from the model's own registry and emptied again down to the folder itself.
/// </summary>
/// <remarks>
/// <para>
/// It knows nothing about any other model store on the machine and reads no environment variable of
/// anyone else's. The only thing it ever writes is the store, and the only thing that can tell it what
/// is staged is the files in that store - nothing is remembered between runs.
/// </para>
/// <para>
/// One stager is meant to be created and kept: the download client and its pooled connections are built
/// on the first download and live until the stager is disposed.
/// </para>
/// </remarks>
public sealed class ModelStager : IModelStager
{
    /// <summary>The status reported when a check found the model already staged and nothing was fetched.</summary>
    private const string AlreadyStagedStatus = "already staged";

    /// <summary>How a digest is spelled inside a manifest.</summary>
    private const string DigestPrefix = "sha256:";

    /// <summary>How many hexadecimal characters follow the prefix.</summary>
    private const int Sha256HexLength = 64;

    private static readonly IReadOnlyList<string> NoPaths = Array.Empty<string>();

    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private readonly ModelStore _store;
    private bool _disposed;

    /// <summary>
    /// Creates a stager.
    /// </summary>
    /// <param name="options">
    /// What to stage and where, or <see langword="null"/> for the model and the folder the application
    /// uses.
    /// </param>
    public ModelStager(ModelStagerOptions options = null)
        : this(options, null)
    {
    }

    /// <summary>
    /// Creates a stager whose downloads go through a message handler of the caller's, which is how the
    /// offline tests reach a registry that exists only in memory.
    /// </summary>
    /// <param name="options">What to stage and where, or <see langword="null"/> for the defaults.</param>
    /// <param name="messageHandler">
    /// The handler every request goes through, or <see langword="null"/> for the ordinary one. A handler
    /// supplied here is NOT disposed with the stager, because the caller who made it owns it.
    /// </param>
    internal ModelStager(ModelStagerOptions options, HttpMessageHandler messageHandler)
    {
        ModelStagerOptions effective = options ?? new ModelStagerOptions();
        Model = effective.Model ?? KnownModels.Qwen35;

        string root = string.IsNullOrWhiteSpace(effective.RootDirectory)
            ? ModelStagerOptions.ResolveDefaultRootDirectory()
            : Path.GetFullPath(effective.RootDirectory);

        RootDirectory = Path.TrimEndingDirectorySeparator(root);
        StoreDirectory = Path.Combine(RootDirectory, ModelStagerOptions.StoreFolderName);

        _store = new ModelStore(new ModelStoreOptions
        {
            StoreDirectory = StoreDirectory,
            HttpMessageHandler = messageHandler
        });
    }

    /// <inheritdoc />
    public ModelDescriptor Model { get; }

    /// <inheritdoc />
    public string RootDirectory { get; }

    /// <inheritdoc />
    public string StoreDirectory { get; }

    /// <inheritdoc />
    public async Task<ModelStagingStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        StoreInspection inspection = await InspectAsync(cancellationToken).ConfigureAwait(false);
        return inspection.Status;
    }

    /// <inheritdoc />
    public async Task<ModelStagingStatus> StageAsync(
        IProgress<ModelStagingProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StoreInspection before = await InspectAsync(cancellationToken).ConfigureAwait(false);
            if (before.Status.IsReady)
            {
                ReportAlreadyStaged(progress, before.Status);
                return before.Status;
            }

            // A download takes any file already in the blobs folder for one it does not have to fetch,
            // whatever size it is, so a file of the wrong size has to go before it can be replaced.
            foreach (string wrongSized in before.WrongSizedBlobPaths)
            {
                StoreFolders.TryDeleteFile(wrongSized);
            }

            var tracker = new StagingProgressTracker(Model.ExpectedTotalBytes, progress);
            try
            {
                await foreach (PullProgress pullProgress in _store
                    .PullAsync(Model.Name, cancellationToken)
                    .ConfigureAwait(false))
                {
                    tracker.Report(pullProgress);
                }
            }
            catch (OperationCanceledException)
            {
                // What has arrived stays where it is, so that a later call carries on from there.
                throw;
            }
            catch (Exception exception) when (IsStoreOrDiskFailure(exception))
            {
                StoreFolders.SweepEmptyFolders(StoreDirectory, RootDirectory);
                throw Translate(
                    Model.DisplayName + " could not be obtained.",
                    exception);
            }

            StoreInspection after = await InspectAsync(cancellationToken).ConfigureAwait(false);
            if (!after.Status.IsReady)
            {
                StoreFolders.SweepEmptyFolders(StoreDirectory, RootDirectory);
                throw new ModelAccessException(
                    Model.DisplayName + " was downloaded but is still not usable: " + after.Status.Detail,
                    ModelAccessFailure.Unknown);
            }

            tracker.ReportComplete(after.Status.ExpectedTotalBytes);
            return after.Status;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ModelStagingStatus> RemoveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(StoreDirectory))
            {
                await DeleteModelAsync(cancellationToken).ConfigureAwait(false);
                await SweepPartialDownloadsAsync(cancellationToken).ConfigureAwait(false);
            }

            StoreFolders.SweepEmptyFolders(StoreDirectory, RootDirectory);

            StoreInspection after = await InspectAsync(cancellationToken).ConfigureAwait(false);
            return DescribeRemoval(after.Status);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Releases the download client and its pooled connections, and leaves every file exactly where it
    /// is.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.Dispose();
        _gate.Dispose();
    }

    /// <summary>
    /// Removes the manifest and the files nothing else names, and copes with a manifest that can no
    /// longer be read by taking the manifest file away itself.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the removal.</param>
    /// <returns>A task that completes when the model is no longer named by anything.</returns>
    /// <exception cref="ModelAccessException">The manifest could neither be deleted nor read.</exception>
    private async Task DeleteModelAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _store.DeleteAsync(Model.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelNotFoundException)
        {
            // There was nothing of this model to remove, which is a quiet success.
        }
        catch (Exception exception) when (IsStoreOrDiskFailure(exception))
        {
            // A manifest that cannot be read must not make a model impossible to get rid of.
            if (!StoreFolders.TryDeleteFile(GetManifestPath()))
            {
                throw Translate(Model.DisplayName + " could not be removed.", exception);
            }
        }
    }

    /// <summary>
    /// Sweeps away what an interrupted download left behind, and any file nothing names any more.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the sweep.</param>
    /// <returns>A task that completes when the sweep is done.</returns>
    /// <exception cref="ModelAccessException">The sweep could not be carried out.</exception>
    private async Task SweepPartialDownloadsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _store.PruneAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsStoreOrDiskFailure(exception))
        {
            throw Translate(
                "What was left of " + Model.DisplayName + " could not be swept away.",
                exception);
        }
    }

    /// <summary>
    /// Looks at the store and works out how much of the model is there. It creates nothing.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the look.</param>
    /// <returns>The status, and the files a repair would have to delete.</returns>
    private async Task<StoreInspection> InspectAsync(CancellationToken cancellationToken)
    {
        StoreContents contents = StoreFolders.Measure(StoreDirectory);

        bool manifestExists = await _store.ExistsAsync(Model.Name, cancellationToken).ConfigureAwait(false);
        if (!manifestExists)
        {
            return DescribeAbsent(contents);
        }

        ModelInfo info = null;
        string failureMessage = null;
        try
        {
            info = await _store.ShowAsync(Model.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelNotFoundException)
        {
            return DescribeAbsent(StoreFolders.Measure(StoreDirectory));
        }
        catch (Exception exception) when (IsStoreOrDiskFailure(exception))
        {
            failureMessage = exception.Message;
        }

        ResolvedModel resolved = null;
        try
        {
            resolved = await _store.ResolveAsync(Model.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsStoreOrDiskFailure(exception))
        {
            failureMessage ??= exception.Message;
        }

        if (info == null)
        {
            string missing = DescribeMissingFile(resolved);
            return DescribeDamaged(
                contents,
                null,
                null,
                missing ?? "the store could not be read (" + failureMessage + ")",
                NoPaths);
        }

        return DescribeManifest(contents, info, resolved);
    }

    /// <summary>
    /// Works out the status of a model whose manifest was read, from the size of every file the manifest
    /// names. Nothing is hashed.
    /// </summary>
    /// <param name="contents">What lies under the store folder.</param>
    /// <param name="info">Everything the store says about the model.</param>
    /// <param name="resolved">The paths the store resolved, which may be <see langword="null"/>.</param>
    /// <returns>The status, and the files a repair would have to delete.</returns>
    private StoreInspection DescribeManifest(StoreContents contents, ModelInfo info, ResolvedModel resolved)
    {
        string weightsDigest = FindWeightsDigest(info.Manifest);
        bool? matchesPin = MatchesPin(weightsDigest);
        long expectedTotalBytes = info.Manifest.GetTotalSize();
        var wrongSized = new List<string>();
        string trouble = null;

        foreach (ModelLayer layer in EnumerateLayers(info.Manifest))
        {
            string blobPath = GetBlobPath(layer.Digest);
            if (blobPath == null)
            {
                trouble ??= "the manifest names a file as '" + layer.Digest + "', which is not a digest";
                continue;
            }

            var file = new FileInfo(blobPath);
            if (!file.Exists)
            {
                trouble ??= "the file " + ShortDigest(layer.Digest) + " the manifest names is missing";
                continue;
            }

            if (file.Length != layer.Size)
            {
                wrongSized.Add(blobPath);
                trouble ??= "the file " + ShortDigest(layer.Digest) + " is "
                    + file.Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes on disk where the manifest states "
                    + layer.Size.ToString("N0", CultureInfo.InvariantCulture);
            }
        }

        if (trouble == null && (resolved == null || string.IsNullOrEmpty(resolved.ModelPath)))
        {
            trouble = "the manifest names no model-weights file";
        }

        if (trouble != null)
        {
            return DescribeDamaged(contents, weightsDigest, matchesPin, trouble, wrongSized, expectedTotalBytes);
        }

        var status = new ModelStagingStatus(
            Model,
            ModelStagingState.Ready,
            resolved.ModelPath,
            resolved.ProjectorPaths,
            weightsDigest,
            matchesPin,
            contents.Bytes,
            expectedTotalBytes,
            StoreDirectory,
            Model.DisplayName + " is staged and ready; its weights are at " + resolved.ModelPath + ".");

        return new StoreInspection(status, NoPaths);
    }

    /// <summary>
    /// Builds the status of a store that holds no manifest for this model: nothing at all, or the files
    /// an interrupted download left.
    /// </summary>
    /// <param name="contents">What lies under the store folder.</param>
    /// <returns>The status, with nothing to repair.</returns>
    private StoreInspection DescribeAbsent(StoreContents contents)
    {
        bool interrupted = contents.HoldsAnyFile;
        var status = new ModelStagingStatus(
            Model,
            interrupted ? ModelStagingState.Incomplete : ModelStagingState.NotPresent,
            null,
            NoPaths,
            null,
            null,
            contents.Bytes,
            Model.ExpectedTotalBytes,
            StoreDirectory,
            interrupted
                ? Model.DisplayName + " is not staged, but an interrupted download left "
                    + contents.Bytes.ToString("N0", CultureInfo.InvariantCulture)
                    + " bytes in " + StoreDirectory + "; staging carries on from there."
                : Model.DisplayName + " is not staged; there is nothing of it in " + StoreDirectory + ".");

        return new StoreInspection(status, NoPaths);
    }

    /// <summary>
    /// Builds the status of a store whose manifest is there but whose files are not what it says.
    /// </summary>
    /// <param name="contents">What lies under the store folder.</param>
    /// <param name="weightsDigest">The weights digest, or <see langword="null"/> when it is unknown.</param>
    /// <param name="matchesPin">Whether the digest is the expected one, or <see langword="null"/>.</param>
    /// <param name="trouble">What is wrong, as a phrase that finishes the sentence.</param>
    /// <param name="wrongSizedBlobPaths">The files a repair would have to delete.</param>
    /// <param name="expectedTotalBytes">
    /// The manifest's own total, or zero to fall back on the descriptor's expected total.
    /// </param>
    /// <returns>The status, and the files a repair would have to delete.</returns>
    private StoreInspection DescribeDamaged(
        StoreContents contents,
        string weightsDigest,
        bool? matchesPin,
        string trouble,
        IReadOnlyList<string> wrongSizedBlobPaths,
        long expectedTotalBytes = 0L)
    {
        var status = new ModelStagingStatus(
            Model,
            ModelStagingState.Damaged,
            null,
            NoPaths,
            weightsDigest,
            matchesPin,
            contents.Bytes,
            expectedTotalBytes > 0L ? expectedTotalBytes : Model.ExpectedTotalBytes,
            StoreDirectory,
            Model.DisplayName + " is staged but damaged: " + trouble + "; staging again repairs it.");

        return new StoreInspection(status, wrongSizedBlobPaths);
    }

    /// <summary>
    /// Adds to a status, after a removal, what is still standing in the application's own folder.
    /// </summary>
    /// <param name="status">The status a check produced once the removal was done.</param>
    /// <returns>The status to hand back.</returns>
    private ModelStagingStatus DescribeRemoval(ModelStagingStatus status)
    {
        string detail;
        if (Directory.Exists(RootDirectory))
        {
            detail = Model.DisplayName + " has been removed, and " + RootDirectory
                + " is still there because something that is not part of the model is in it.";
        }
        else
        {
            detail = Model.DisplayName + " has been removed, and so has " + RootDirectory + ".";
        }

        return new ModelStagingStatus(
            status.Model,
            status.State,
            status.ModelPath,
            status.ProjectorPaths,
            status.WeightsDigest,
            status.MatchesPinnedWeights,
            status.BytesOnDisk,
            status.ExpectedTotalBytes,
            status.StoreDirectory,
            detail);
    }

    /// <summary>
    /// Reports one hundred percent for a model that was already staged, so that a caller driving a bar
    /// sees the same ending whether anything was fetched or not.
    /// </summary>
    /// <param name="progress">Where to report, which may be <see langword="null"/>.</param>
    /// <param name="status">What the check found.</param>
    private static void ReportAlreadyStaged(IProgress<ModelStagingProgress> progress, ModelStagingStatus status)
    {
        progress?.Report(new ModelStagingProgress(
            AlreadyStagedStatus,
            null,
            0L,
            0L,
            status.ExpectedTotalBytes,
            status.ExpectedTotalBytes,
            100d));
    }

    /// <summary>
    /// Names the first file a resolve promised that is not on disk.
    /// </summary>
    /// <param name="resolved">The paths the store resolved, which may be <see langword="null"/>.</param>
    /// <returns>A phrase naming the missing file, or <see langword="null"/> when they are all there.</returns>
    private static string DescribeMissingFile(ResolvedModel resolved)
    {
        if (resolved == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(resolved.ModelPath) && !File.Exists(resolved.ModelPath))
        {
            return "the model-weights file " + resolved.ModelPath + " is missing";
        }

        foreach (string projectorPath in resolved.ProjectorPaths)
        {
            if (!File.Exists(projectorPath))
            {
                return "the projector file " + projectorPath + " is missing";
            }
        }

        foreach (string shardPath in resolved.ModelShardPaths)
        {
            if (!File.Exists(shardPath))
            {
                return "the model-weights file " + shardPath + " is missing";
            }
        }

        return null;
    }

    /// <summary>
    /// Every layer of a manifest that names a file, the config layer among them.
    /// </summary>
    /// <param name="manifest">The manifest to walk.</param>
    /// <returns>The layers, in manifest order, with the config last.</returns>
    private static IEnumerable<ModelLayer> EnumerateLayers(ModelManifest manifest)
    {
        if (manifest.Layers != null)
        {
            foreach (ModelLayer layer in manifest.Layers)
            {
                if (layer != null && !string.IsNullOrEmpty(layer.Digest))
                {
                    yield return layer;
                }
            }
        }

        if (manifest.Config != null && !string.IsNullOrEmpty(manifest.Config.Digest))
        {
            yield return manifest.Config;
        }
    }

    /// <summary>
    /// The digest of the first model-weights layer of a manifest.
    /// </summary>
    /// <param name="manifest">The manifest to read.</param>
    /// <returns>The digest as the manifest spells it, or <see langword="null"/> when there is none.</returns>
    private static string FindWeightsDigest(ModelManifest manifest)
    {
        if (manifest.Layers == null)
        {
            return null;
        }

        foreach (ModelLayer layer in manifest.Layers)
        {
            if (layer != null
                && string.Equals(layer.MediaType, MediaTypes.Model, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(layer.Digest))
            {
                return layer.Digest;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the staged weights carry the digest the descriptor expects. It is reported and never
    /// acted on.
    /// </summary>
    /// <param name="weightsDigest">The digest the manifest states, which may be <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> or <see langword="false"/> when both are known, and
    /// <see langword="null"/> when either is not.
    /// </returns>
    private bool? MatchesPin(string weightsDigest)
    {
        if (Model.WeightsSha256 == null || string.IsNullOrEmpty(weightsDigest))
        {
            return null;
        }

        return string.Equals(
            weightsDigest,
            DigestPrefix + Model.WeightsSha256,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The path a digest's file lies at, which is <c>&lt;store&gt;/blobs/sha256-&lt;hex&gt;</c>.
    /// </summary>
    /// <param name="digest">The digest as a manifest spells it.</param>
    /// <returns>The absolute path, or <see langword="null"/> when the digest is not well formed.</returns>
    private string GetBlobPath(string digest)
    {
        if (digest == null
            || digest.Length != DigestPrefix.Length + Sha256HexLength
            || !digest.StartsWith(DigestPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        for (int index = DigestPrefix.Length; index < digest.Length; index++)
        {
            if (!Uri.IsHexDigit(digest[index]))
            {
                return null;
            }
        }

        return Path.Combine(
            StoreDirectory,
            StoreFolders.BlobsFolderName,
            "sha256-" + digest.Substring(DigestPrefix.Length));
    }

    /// <summary>
    /// The path of this model's manifest file, which is where the store keeps it.
    /// </summary>
    /// <returns>The absolute path of the manifest file.</returns>
    private string GetManifestPath()
    {
        return Path.Combine(
            StoreDirectory,
            StoreFolders.ManifestsFolderName,
            ModelName.Parse(Model.Name).ToRelativePath());
    }

    /// <summary>
    /// The first twelve hexadecimal characters of a digest, which is how a download names a layer.
    /// </summary>
    /// <param name="digest">The digest as a manifest spells it.</param>
    /// <returns>The short form.</returns>
    private static string ShortDigest(string digest)
    {
        string hex = digest.StartsWith(DigestPrefix, StringComparison.Ordinal)
            ? digest.Substring(DigestPrefix.Length)
            : digest;

        return hex.Length <= 12 ? hex : hex.Substring(0, 12);
    }

    /// <summary>
    /// Whether an exception is one of the store's own or one the disk raised, which are the two this
    /// library translates and the only two it ever catches.
    /// </summary>
    /// <param name="exception">The exception to judge.</param>
    /// <returns><see langword="true"/> when it is one to translate.</returns>
    private static bool IsStoreOrDiskFailure(Exception exception)
    {
        return exception is ModelManagerException
            || exception is IOException
            || exception is UnauthorizedAccessException;
    }

    /// <summary>
    /// Turns what the store or the disk raised into the one exception this library has.
    /// </summary>
    /// <param name="what">A sentence saying what was being attempted.</param>
    /// <param name="exception">The exception to translate.</param>
    /// <returns>The exception to raise.</returns>
    private ModelAccessException Translate(string what, Exception exception)
    {
        if (exception is ModelNotFoundException)
        {
            return new ModelAccessException(
                what + " No model called " + Model.Name + " could be found.",
                ModelAccessFailure.ModelNotFound,
                null,
                exception);
        }

        if (exception is RegistryException registryFailure)
        {
            return new ModelAccessException(
                what + " The registry answered: " + registryFailure.Message,
                ModelAccessFailure.Registry,
                (int?)registryFailure.StatusCode,
                exception);
        }

        if (exception is DigestMismatchException)
        {
            return new ModelAccessException(
                what + " What arrived was not what the registry promised: " + exception.Message,
                ModelAccessFailure.CorruptDownload,
                null,
                exception);
        }

        if (exception is ModelManagerException)
        {
            return new ModelAccessException(
                what + " " + exception.Message,
                ModelAccessFailure.Unknown,
                null,
                exception);
        }

        return new ModelAccessException(
            what + " The disk refused the work: " + exception.Message,
            ModelAccessFailure.Storage,
            null,
            exception);
    }

    /// <summary>
    /// Refuses any work after the stager has been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The stager has been disposed.</exception>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
