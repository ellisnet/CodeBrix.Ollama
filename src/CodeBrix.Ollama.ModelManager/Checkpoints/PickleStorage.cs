using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// A PyTorch storage as a checkpoint's pickle names it: which file inside the zip holds the bytes, what type
/// they are, and how many values there are.
/// </summary>
/// <remarks>
/// The restricted interpreter builds one of these from a persistent id of the shape
/// <c>('storage', &lt;storage class&gt;, &lt;key&gt;, &lt;device&gt;, &lt;numel&gt;)</c> and from nothing else.
/// </remarks>
internal sealed class PickleStorage
{
    internal PickleStorage(string key, CheckpointDataType dataType, string location, long elementCount)
    {
        Key = key;
        DataType = dataType;
        Location = location;
        ElementCount = elementCount;
    }

    /// <summary>The storage key, which is the file name under <c>&lt;archive&gt;/data/</c>.</summary>
    internal string Key { get; }

    /// <summary>The element type of the values.</summary>
    internal CheckpointDataType DataType { get; }

    /// <summary>The device the publisher saved from, for example <c>cpu</c>.</summary>
    internal string Location { get; }

    /// <summary>The number of values in the storage.</summary>
    internal long ElementCount { get; }
}
