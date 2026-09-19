using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One allowed global, as it sits on the restricted interpreter's stack: which of the four it is, and - for a
/// storage class - what element type that class carries.
/// </summary>
internal sealed class PickleGlobalReference
{
    internal PickleGlobalReference(PickleGlobal global, CheckpointDataType dataType, string qualifiedName)
    {
        Global = global;
        DataType = dataType;
        QualifiedName = qualifiedName;
    }

    /// <summary>Which allowed global this is.</summary>
    internal PickleGlobal Global { get; }

    /// <summary>The element type, meaningful only when <see cref="Global"/> is <see cref="PickleGlobal.Storage"/>.</summary>
    internal CheckpointDataType DataType { get; }

    /// <summary>The module and name the stream used, for messages.</summary>
    internal string QualifiedName { get; }
}
