using System;

namespace CodeBrix.Ollama.ModelRunner; //was previously: LLamaSharp LLama/Native/LLamaBatch.cs (reference only);

/// <summary>
/// An owned <c>llama_batch</c>: allocated by <c>llama_batch_init</c>, filled a token at a time, and released
/// by <c>llama_batch_free</c>.
/// </summary>
/// <remarks>
/// <c>llama_batch_init</c> leaves every member uninitialized, so a caller that forgets one field gets
/// whatever was in that memory - which is why adding a token here always writes all five. The buffer is not
/// thread-safe; one belongs to one decode loop.
/// </remarks>
internal sealed unsafe class LlamaBatchBuffer : IDisposable
{
    private LlamaBatch batch;
    private bool disposed;

    /// <summary>Allocates a batch.</summary>
    /// <param name="capacity">How many tokens the batch can hold.</param>
    /// <param name="maxSequencesPerToken">How many sequences one token may belong to; usually 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count is not positive.</exception>
    public LlamaBatchBuffer(int capacity, int maxSequencesPerToken = 1)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "A batch holds at least one token.");
        if (maxSequencesPerToken <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSequencesPerToken), maxSequencesPerToken, "A token belongs to at least one sequence.");
        }

        NativeLibraryLoader.EnsureLoaded();

        Capacity = capacity;
        MaxSequencesPerToken = maxSequencesPerToken;
        batch = NativeMethods.llama_batch_init(capacity, 0, maxSequencesPerToken);
        batch.NTokens = 0;
    }

    /// <summary>How many tokens the batch can hold.</summary>
    public int Capacity { get; }

    /// <summary>How many sequences one token of this batch may belong to.</summary>
    public int MaxSequencesPerToken { get; }

    /// <summary>How many tokens the batch currently holds.</summary>
    public int Count => batch.NTokens;

    /// <summary>The batch, ready to be passed to <c>llama_decode</c> or <c>llama_encode</c>.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    /// <remarks>
    /// <c>llama_batch</c> is a structure of pointers, and this returns a copy of it. The copy's pointers are
    /// the buffer's own arrays, which <c>llama_batch_free</c> releases: once this buffer has been disposed -
    /// or finalized, if it was dropped without being disposed - every copy taken from here holds pointers
    /// into freed memory, and passing one to the engine reads memory that is no longer ours. A copy is also
    /// a snapshot of the token count at the moment it was read, so a copy taken before <see cref="Add"/> or
    /// <see cref="Clear"/> still carries the old <c>NTokens</c>. Read this immediately before the decode call
    /// that uses it, and keep the buffer alive until that call has returned.
    /// </remarks>
    public LlamaBatch Batch
    {
        get
        {
            ThrowIfDisposed();
            return batch;
        }
    }

    /// <summary>Appends one token.</summary>
    /// <param name="token">The token id.</param>
    /// <param name="position">The token's position in its sequence.</param>
    /// <param name="sequenceId">The sequence the token belongs to.</param>
    /// <param name="wantLogits">Whether the output for this position is wanted.</param>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The batch is full.</exception>
    public void Add(int token, int position, int sequenceId, bool wantLogits)
    {
        ThrowIfDisposed();

        int index = batch.NTokens;
        if (index >= Capacity)
        {
            throw new InvalidOperationException(
                $"This batch holds {Capacity} tokens and is full. Decode it and clear it, or allocate a "
                + "larger one.");
        }

        batch.Token[index] = token;
        batch.Pos[index] = position;
        batch.NSeqId[index] = 1;
        batch.SeqId[index][0] = sequenceId;
        batch.Logits[index] = wantLogits ? (sbyte)1 : (sbyte)0;
        batch.NTokens = index + 1;
    }

    /// <summary>Empties the batch without releasing it.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public void Clear()
    {
        ThrowIfDisposed();
        batch.NTokens = 0;
    }

    /// <summary>Releases the batch.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        NativeMethods.llama_batch_free(batch);
        batch = default;
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the batch when it was not disposed.</summary>
    ~LlamaBatchBuffer()
    {
        if (disposed) return;
        disposed = true;
        NativeMethods.llama_batch_free(batch);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
