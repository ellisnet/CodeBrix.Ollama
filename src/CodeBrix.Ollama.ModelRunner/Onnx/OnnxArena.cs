using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The pool of element arrays one loaded model runs out of.
/// </summary>
/// <remarks>
/// <para>
/// A decoder graph is a thousand nodes and almost every one of them produces a tensor, but only a handful are
/// alive at the same time: the execution plan knows which node last reads each tensor, so a buffer can go
/// back into the pool the moment that node has run. That is what keeps a step's allocation proportional to
/// the widest point of the graph rather than to its length, and it is why a second step of the same model
/// allocates almost nothing at all.
/// </para>
/// <para>
/// The pool belongs to the model, not to the run, so the arrays a first run allocated are what a second run
/// uses. It never holds a caller's array and never holds an array a run handed back.
/// </para>
/// <para>
/// With reuse switched off, every request allocates and nothing is ever kept. The two settings must produce
/// identical numbers; the suite runs every fixture both ways to say so.
/// </para>
/// </remarks>
internal sealed class OnnxArena
{
    private const string Unknown =
        "The engine computes in float, int64, int32, bool and the two quantized 8-bit types only.";

    private readonly List<OnnxBuffer> _floats = new List<OnnxBuffer>();
    private readonly List<OnnxBuffer> _int64s = new List<OnnxBuffer>();
    private readonly List<OnnxBuffer> _int32s = new List<OnnxBuffer>();
    private readonly List<OnnxBuffer> _booleans = new List<OnnxBuffer>();
    private readonly List<OnnxBuffer> _bytes = new List<OnnxBuffer>();
    private readonly List<OnnxBuffer> _signedBytes = new List<OnnxBuffer>();

    /// <summary>Creates an arena.</summary>
    /// <param name="reuse">Whether finished buffers are kept and handed out again.</param>
    internal OnnxArena(bool reuse)
    {
        Reuse = reuse;
    }

    /// <summary>Whether finished buffers are kept and handed out again.</summary>
    internal bool Reuse { get; }

    /// <summary>How many arrays the pool is holding, for diagnostics and tests.</summary>
    internal int PooledCount =>
        _floats.Count + _int64s.Count + _int32s.Count + _booleans.Count + _bytes.Count + _signedBytes.Count;

    /// <summary>
    /// Takes a buffer of at least the requested length, from the pool when one fits and by allocating when
    /// none does.
    /// </summary>
    /// <param name="elementType">The element type wanted.</param>
    /// <param name="count">How many elements are wanted.</param>
    /// <param name="poolable">Whether the buffer may go back into the pool when nothing reads it.</param>
    /// <returns>The buffer, with no references yet.</returns>
    internal OnnxBuffer Rent(OnnxElementType elementType, int count, bool poolable)
    {
        if (Reuse && poolable)
        {
            List<OnnxBuffer> pool = PoolFor(elementType);

            // The smallest array that fits, so a one-element request does not walk off with a buffer of ten
            // million floats and keep it out of circulation for the rest of the run.
            int best = -1;
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i].Capacity < count) continue;
                if (best < 0 || pool[i].Capacity < pool[best].Capacity) best = i;
            }

            if (best >= 0)
            {
                OnnxBuffer found = pool[best];
                pool.RemoveAt(best);
                return found;
            }
        }

        return new OnnxBuffer(elementType, Allocate(elementType, count), poolable, true);
    }

    /// <summary>Takes a buffer back once nothing reads it.</summary>
    /// <param name="buffer">The buffer.</param>
    internal void Return(OnnxBuffer buffer)
    {
        if (!Reuse || buffer == null || !buffer.Poolable) return;

        PoolFor(buffer.ElementType).Add(buffer);
    }

    /// <summary>Lets go of every array the pool is holding.</summary>
    internal void Clear()
    {
        _floats.Clear();
        _int64s.Clear();
        _int32s.Clear();
        _booleans.Clear();
        _bytes.Clear();
        _signedBytes.Clear();
    }

    /// <summary>Allocates an array of the given element type.</summary>
    /// <param name="elementType">The element type.</param>
    /// <param name="count">How many elements.</param>
    /// <returns>The array.</returns>
    internal static Array Allocate(OnnxElementType elementType, int count) => elementType switch
    {
        OnnxElementType.Float => new float[count],
        OnnxElementType.Int64 => new long[count],
        OnnxElementType.Int32 => new int[count],
        OnnxElementType.Bool => new bool[count],
        OnnxElementType.UInt8 => new byte[count],
        OnnxElementType.Int8 => new sbyte[count],
        _ => throw new ArgumentOutOfRangeException(
            nameof(elementType), elementType, Unknown),
    };

    private List<OnnxBuffer> PoolFor(OnnxElementType elementType) => elementType switch
    {
        OnnxElementType.Float => _floats,
        OnnxElementType.Int64 => _int64s,
        OnnxElementType.Int32 => _int32s,
        OnnxElementType.Bool => _booleans,
        OnnxElementType.UInt8 => _bytes,
        OnnxElementType.Int8 => _signedBytes,
        _ => throw new ArgumentOutOfRangeException(
            nameof(elementType), elementType, Unknown),
    };
}
