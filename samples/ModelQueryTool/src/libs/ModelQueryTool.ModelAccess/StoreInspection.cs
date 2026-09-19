using System;
using System.Collections.Generic;
using ModelQueryTool.ModelAccess.Models;

namespace ModelQueryTool.ModelAccess;

/// <summary>
/// What one look at the store produced: the status a caller sees, and the files a repair would have to
/// delete before a download could put them right.
/// </summary>
/// <remarks>
/// The second list is the reason this exists. A download treats a file that is already in the blobs
/// folder as a file it does not have to fetch, whatever size it is, so a file of the wrong size has to
/// go before a download can replace it.
/// </remarks>
internal sealed class StoreInspection
{
    private static readonly IReadOnlyList<string> NoPaths = Array.Empty<string>();

    /// <summary>
    /// Records one look at the store.
    /// </summary>
    /// <param name="status">What a caller is told.</param>
    /// <param name="wrongSizedBlobPaths">The files that are not the size the manifest states.</param>
    public StoreInspection(ModelStagingStatus status, IReadOnlyList<string> wrongSizedBlobPaths)
    {
        Status = status;
        WrongSizedBlobPaths = wrongSizedBlobPaths ?? NoPaths;
    }

    /// <summary>Gets what a caller is told.</summary>
    public ModelStagingStatus Status { get; }

    /// <summary>Gets the files that are on disk at a size the manifest does not state.</summary>
    public IReadOnlyList<string> WrongSizedBlobPaths { get; }
}
