namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Where the value in one slot of an execution plan comes from.</summary>
internal enum OnnxSlotKind
{
    /// <summary>A weight read from the file once, when the model was loaded, and never released.</summary>
    Initializer = 0,

    /// <summary>A tensor the caller supplies on every run.</summary>
    GraphInput = 1,

    /// <summary>A tensor a node computes.</summary>
    Computed = 2,
}
