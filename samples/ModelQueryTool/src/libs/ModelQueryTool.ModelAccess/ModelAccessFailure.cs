namespace ModelQueryTool.ModelAccess;

/// <summary>
/// What went wrong, in the few kinds an application acts differently on. Anything finer is in the
/// message and in the inner exception.
/// </summary>
public enum ModelAccessFailure
{
    /// <summary>Something else went wrong; read the message.</summary>
    Unknown = 0,

    /// <summary>The registry has no such model, so waiting and trying again will not help.</summary>
    ModelNotFound = 1,

    /// <summary>The registry could not be reached or refused the request; trying again may help.</summary>
    Registry = 2,

    /// <summary>
    /// What arrived was not what the registry said it would be. The bad bytes are already gone and
    /// staging again downloads them afresh.
    /// </summary>
    CorruptDownload = 3,

    /// <summary>The disk refused the work: no room, no permission, or a path that cannot be used.</summary>
    Storage = 4
}
