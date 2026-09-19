namespace ModelQueryTool.ModelAccess.Models;

/// <summary>
/// How much of a model the application's private store holds, from nothing at all to a complete set of
/// files a runner can open.
/// </summary>
public enum ModelStagingState
{
    /// <summary>Nothing of the model is on disk; staging it is a download from the beginning.</summary>
    NotPresent = 0,

    /// <summary>
    /// Files are in the store but no manifest names the model, which is what an interrupted download
    /// leaves behind. Staging resumes it.
    /// </summary>
    Incomplete = 1,

    /// <summary>
    /// A manifest names the model, but a file it needs is missing or is not the size the manifest
    /// states. Staging repairs it.
    /// </summary>
    Damaged = 2,

    /// <summary>Every file the manifest names is present at the stated size and a runner can open it.</summary>
    Ready = 3
}
