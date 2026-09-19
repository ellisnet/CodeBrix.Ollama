using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One array of elements, and the count of the tensors that are still reading it.
/// </summary>
/// <remarks>
/// <para>
/// A buffer is separate from the tensor that describes it because several tensors can describe the same
/// elements: <c>Reshape</c>, <c>Unsqueeze</c> and <c>Identity</c> change only a shape, and copying tens of
/// megabytes to say so would be waste. The reference count is what makes that safe - a buffer goes back to
/// the arena when the last tensor that could read it has been released, not when the first one has.
/// </para>
/// <para>
/// <see cref="Poolable"/> is false for the two kinds of buffer the arena must never hand out again: an array
/// a caller owns (a run's inputs) and an array a run is going to hand back (its outputs).
/// </para>
/// </remarks>
internal sealed class OnnxBuffer
{
    /// <summary>Creates a buffer over an array.</summary>
    /// <param name="elementType">The type of element the array holds.</param>
    /// <param name="data">The array.</param>
    /// <param name="poolable">Whether the arena may hand this array out again once nothing reads it.</param>
    /// <param name="engineOwned">
    /// Whether the engine allocated the array itself. An array a caller handed in is not the engine's to give
    /// away again, so a run that ends up naming one as an output copies it instead.
    /// </param>
    internal OnnxBuffer(OnnxElementType elementType, Array data, bool poolable, bool engineOwned)
    {
        ElementType = elementType;
        Data = data;
        Poolable = poolable;
        EngineOwned = engineOwned;
        References = 0;
    }

    /// <summary>The type of element the array holds.</summary>
    internal OnnxElementType ElementType { get; }

    /// <summary>The array itself, which may be longer than the tensor that describes it.</summary>
    internal Array Data { get; }

    /// <summary>How many elements the array holds, which is at least the tensor's element count.</summary>
    internal int Capacity => Data.Length;

    /// <summary>Whether the arena may hand this array out again once nothing reads it.</summary>
    internal bool Poolable { get; }

    /// <summary>Whether the engine allocated the array itself rather than being handed it.</summary>
    internal bool EngineOwned { get; }

    /// <summary>How many tensors are still reading it.</summary>
    internal int References { get; private set; }

    /// <summary>Records one more tensor reading the buffer.</summary>
    internal void Retain() => References++;

    /// <summary>Records one fewer tensor reading the buffer.</summary>
    /// <returns><see langword="true"/> when nothing reads it any more.</returns>
    internal bool Release()
    {
        if (References > 0) References--;
        return References == 0;
    }
}
