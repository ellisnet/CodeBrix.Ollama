using System;
using System.Collections.Generic;

namespace ModelQueryTool.ModelAccess.Models;

/// <summary>
/// Everything a check of the private store found out about one model: whether a runner can open it,
/// where its files are, what the manifest says about them and one sentence a person can read.
/// </summary>
/// <remarks>
/// Nothing here is hashed. The state is decided from what the manifest names and from the size of each
/// file on disk, which is what makes a check cheap enough to run every time the application starts.
/// </remarks>
public sealed class ModelStagingStatus
{
    private static readonly IReadOnlyList<string> NoPaths = Array.Empty<string>();

    /// <summary>
    /// Records what a check found.
    /// </summary>
    /// <param name="model">The model that was checked.</param>
    /// <param name="state">How much of it is on disk.</param>
    /// <param name="modelPath">The weights file, or <see langword="null"/> unless the model is ready.</param>
    /// <param name="projectorPaths">The projector files, empty unless the model is ready.</param>
    /// <param name="weightsDigest">
    /// The weights digest as the manifest states it, including the <c>sha256:</c> prefix, or
    /// <see langword="null"/> when no manifest could be read.
    /// </param>
    /// <param name="matchesPinnedWeights">
    /// Whether the weights digest is the one the descriptor expects, or <see langword="null"/> when the
    /// descriptor expects none or no manifest could be read.
    /// </param>
    /// <param name="bytesOnDisk">How many bytes lie under the store folder, whatever they belong to.</param>
    /// <param name="expectedTotalBytes">
    /// The manifest's own total when there is a manifest, and the descriptor's expected total otherwise.
    /// </param>
    /// <param name="storeDirectory">The store folder the check looked in.</param>
    /// <param name="detail">One sentence saying what was found.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public ModelStagingStatus(
        ModelDescriptor model,
        ModelStagingState state,
        string modelPath,
        IReadOnlyList<string> projectorPaths,
        string weightsDigest,
        bool? matchesPinnedWeights,
        long bytesOnDisk,
        long expectedTotalBytes,
        string storeDirectory,
        string detail)
    {
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        Model = model;
        State = state;
        ModelPath = modelPath;
        ProjectorPaths = projectorPaths ?? NoPaths;
        WeightsDigest = weightsDigest;
        MatchesPinnedWeights = matchesPinnedWeights;
        BytesOnDisk = bytesOnDisk;
        ExpectedTotalBytes = expectedTotalBytes;
        StoreDirectory = storeDirectory;
        Detail = detail ?? string.Empty;
    }

    /// <summary>Gets the model this status is about.</summary>
    public ModelDescriptor Model { get; }

    /// <summary>Gets how much of the model the store holds.</summary>
    public ModelStagingState State { get; }

    /// <summary>Gets whether a runner can be pointed at the model right now.</summary>
    public bool IsReady
    {
        get { return State == ModelStagingState.Ready; }
    }

    /// <summary>
    /// Gets the absolute path of the weights file a runner opens, or <see langword="null"/> unless the
    /// model is ready.
    /// </summary>
    public string ModelPath { get; }

    /// <summary>
    /// Gets the absolute paths of the projector files, in manifest order. Never <see langword="null"/>,
    /// and empty unless the model is ready. Nothing reads them yet; they are counted as part of the
    /// model all the same.
    /// </summary>
    public IReadOnlyList<string> ProjectorPaths { get; }

    /// <summary>
    /// Gets the digest of the weights layer as the manifest states it, <c>sha256:</c> prefix and all, or
    /// <see langword="null"/> when no manifest could be read.
    /// </summary>
    public string WeightsDigest { get; }

    /// <summary>
    /// Gets whether the weights digest is the one the descriptor expects: <see langword="null"/> when
    /// the descriptor expects none, or when no manifest could be read.
    /// </summary>
    public bool? MatchesPinnedWeights { get; }

    /// <summary>Gets how many bytes lie under the store folder, whatever they belong to.</summary>
    public long BytesOnDisk { get; }

    /// <summary>
    /// Gets the size the whole model comes to: the manifest's own total when there is a manifest, and
    /// the descriptor's expected total otherwise.
    /// </summary>
    public long ExpectedTotalBytes { get; }

    /// <summary>Gets the store folder the check looked in.</summary>
    public string StoreDirectory { get; }

    /// <summary>Gets one sentence saying what was found, fit to show a person.</summary>
    public string Detail { get; }

    /// <summary>The state and the sentence behind it, for a log line or a test failure.</summary>
    /// <returns>The state followed by the detail.</returns>
    public override string ToString()
    {
        return State + ": " + Detail;
    }
}
