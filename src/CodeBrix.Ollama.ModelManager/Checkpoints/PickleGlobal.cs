namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The only globals a checkpoint's pickle is allowed to name. Anything else the stream asks to import is
/// refused with the module and the name in the message.
/// </summary>
internal enum PickleGlobal
{
    /// <summary><c>torch._utils._rebuild_tensor_v2</c>.</summary>
    RebuildTensorV2,

    /// <summary><c>torch._utils._rebuild_parameter</c>.</summary>
    RebuildParameter,

    /// <summary><c>collections.OrderedDict</c>.</summary>
    OrderedDict,

    /// <summary>One of the <c>torch.*Storage</c> classes; the element type is carried beside it.</summary>
    Storage
}
