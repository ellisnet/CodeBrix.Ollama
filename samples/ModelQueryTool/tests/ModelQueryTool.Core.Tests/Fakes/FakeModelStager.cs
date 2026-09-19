using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ModelQueryTool.ModelAccess;
using ModelQueryTool.ModelAccess.Models;

namespace ModelQueryTool.Core.Tests.Fakes;

/// <summary>
/// A model store that exists only in this test: it answers with whatever status the test set,
/// counts what was asked of it, and never touches a folder or the network.
/// </summary>
internal sealed class FakeModelStager : IModelStager
{
    /// <summary>The model the fake describes - deliberately not the one the application really uses.</summary>
    internal static readonly ModelDescriptor TestModel = new(
        "hf.co/codebrix-testing/tiny-test-model:test", "Test Model", 4_000_000_000L);

    private readonly List<ModelStagingProgress> _stageReports = [];

    /// <summary>Creates the fake with nothing on disk.</summary>
    internal FakeModelStager()
    {
        Model = TestModel;
        RootDirectory = "/tmp/fake-model-query-tool";
        StoreDirectory = RootDirectory + "/models";
        CheckStatus = CreateStatus(ModelStagingState.NotPresent, detail: "Nothing of this model is on disk.");
    }

    /// <inheritdoc />
    public ModelDescriptor Model { get; set; }

    /// <inheritdoc />
    public string RootDirectory { get; set; }

    /// <inheritdoc />
    public string StoreDirectory { get; set; }

    /// <summary>Gets or sets what a check answers with.</summary>
    internal ModelStagingStatus CheckStatus { get; set; }

    /// <summary>Gets or sets what staging answers with; null answers with a ready status.</summary>
    internal ModelStagingStatus StageStatus { get; set; }

    /// <summary>Gets or sets what removal answers with; null answers with nothing present.</summary>
    internal ModelStagingStatus RemoveStatus { get; set; }

    /// <summary>Gets or sets what a check throws instead of answering.</summary>
    internal Exception CheckFailure { get; set; }

    /// <summary>Gets or sets what staging throws instead of answering.</summary>
    internal Exception StageFailure { get; set; }

    /// <summary>Gets the reports staging hands to the progress before it answers.</summary>
    internal IList<ModelStagingProgress> StageReports => _stageReports;

    /// <summary>
    /// Gets or sets whether staging waits to be cancelled instead of finishing, which is how a
    /// test stops a download that is in flight.
    /// </summary>
    internal bool StageWaitsToBeCancelled { get; set; }

    /// <summary>Gets how many times the store was asked what it holds.</summary>
    internal int CheckCalls { get; private set; }

    /// <summary>Gets how many times a download was asked for.</summary>
    internal int StageCalls { get; private set; }

    /// <summary>Gets how many times a removal was asked for.</summary>
    internal int RemoveCalls { get; private set; }

    /// <summary>Gets whether the fake has been disposed.</summary>
    internal bool IsDisposed { get; private set; }

    /// <summary>
    /// Sets every "how many times" count back to zero, so that a test can arrange whatever it
    /// likes and then assert about the one call it is actually looking at.
    /// </summary>
    internal void ResetCounts()
    {
        CheckCalls = 0;
        StageCalls = 0;
        RemoveCalls = 0;
    }

    /// <summary>Builds a status of the fake's own model, for a test to hand back from a check.</summary>
    /// <param name="state">How much of the model is on disk.</param>
    /// <param name="modelPath">The weights file, when there is one.</param>
    /// <param name="bytesOnDisk">How many bytes lie under the store folder.</param>
    /// <param name="detail">The sentence a person reads.</param>
    /// <returns>The status.</returns>
    internal ModelStagingStatus CreateStatus(
        ModelStagingState state,
        string modelPath = null,
        long bytesOnDisk = 0L,
        string detail = "")
    {
        return new ModelStagingStatus(
            Model,
            state,
            modelPath,
            [],
            null,
            null,
            bytesOnDisk,
            Model.ExpectedTotalBytes,
            StoreDirectory,
            detail);
    }

    /// <inheritdoc />
    public Task<ModelStagingStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        CheckCalls++;

        if (CheckFailure != null) { throw CheckFailure; }

        return Task.FromResult(CheckStatus);
    }

    /// <inheritdoc />
    public async Task<ModelStagingStatus> StageAsync(
        IProgress<ModelStagingProgress> progress = null, CancellationToken cancellationToken = default)
    {
        StageCalls++;

        if (StageFailure != null) { throw StageFailure; }

        foreach (var report in _stageReports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(report);
        }

        if (StageWaitsToBeCancelled)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        return StageStatus ?? CreateStatus(
            ModelStagingState.Ready,
            StoreDirectory + "/blobs/sha256-weights",
            Model.ExpectedTotalBytes,
            "Every file the manifest names is present.");
    }

    /// <inheritdoc />
    public Task<ModelStagingStatus> RemoveAsync(CancellationToken cancellationToken = default)
    {
        RemoveCalls++;

        return Task.FromResult(RemoveStatus ?? CreateStatus(
            ModelStagingState.NotPresent, detail: "Nothing of this model is on disk."));
    }

    /// <inheritdoc />
    public void Dispose() => IsDisposed = true;
}
