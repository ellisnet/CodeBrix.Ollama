namespace ModelQueryTool.ModelRunning;

/// <summary>Where the host is in the model's life: what it will accept next, and what it is busy with.</summary>
public enum ModelHostState
{
    /// <summary>No model is loaded. A start is the only thing that changes this.</summary>
    Stopped = 0,

    /// <summary>The weights are being read. Nothing can be asked of the model yet.</summary>
    Starting = 1,

    /// <summary>A model is loaded and waiting for a message.</summary>
    Ready = 2,

    /// <summary>A turn is being streamed. The next message waits until it ends.</summary>
    Generating = 3,

    /// <summary>The model is being unloaded, and any turn in flight is being cancelled.</summary>
    Stopping = 4,

    /// <summary>A reload failed and could not be undone, so no model is loaded and none was kept.</summary>
    Faulted = 5,
}
