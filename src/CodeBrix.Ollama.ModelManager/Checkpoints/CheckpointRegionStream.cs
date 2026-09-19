using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// A read-only window over part of another stream: it hands out exactly the number of bytes it was given and
/// then reports end of stream, whatever the stream underneath still holds.
/// </summary>
/// <remarks>
/// This is what a checkpoint reader returns for one tensor. It owns the stream underneath and closes it when it
/// is disposed, so one tensor's reader can be used and thrown away without touching the next one's.
/// </remarks>
internal sealed class CheckpointRegionStream : Stream
{
    private readonly Stream _inner;
    private long _remaining;

    internal CheckpointRegionStream(Stream inner, long length)
    {
        _inner = inner;
        Length = length;
        _remaining = length;
    }

    /// <summary>Always <see langword="true"/>.</summary>
    public override bool CanRead
    {
        get { return true; }
    }

    /// <summary>Always <see langword="false"/>: the window is forward-only.</summary>
    public override bool CanSeek
    {
        get { return false; }
    }

    /// <summary>Always <see langword="false"/>.</summary>
    public override bool CanWrite
    {
        get { return false; }
    }

    /// <summary>The number of bytes the window spans.</summary>
    public override long Length { get; }

    /// <summary>How many bytes have been read out of the window.</summary>
    /// <exception cref="NotSupportedException">Always, when set.</exception>
    public override long Position
    {
        get { return Length - _remaining; }
        set { throw new NotSupportedException("A checkpoint tensor window cannot be positioned."); }
    }

    /// <summary>Does nothing: the window is read-only.</summary>
    public override void Flush()
    {
    }

    /// <summary>Reads up to <paramref name="count"/> bytes, never past the end of the window.</summary>
    /// <param name="buffer">The buffer to read into.</param>
    /// <param name="offset">Where in the buffer to start.</param>
    /// <param name="count">The most bytes to read.</param>
    /// <returns>The number of bytes read, or 0 at the end of the window.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(new Span<byte>(buffer, offset, count));
    }

    /// <summary>Reads up to the span's length, never past the end of the window.</summary>
    /// <param name="buffer">The span to read into.</param>
    /// <returns>The number of bytes read, or 0 at the end of the window.</returns>
    public override int Read(Span<byte> buffer)
    {
        if (_remaining <= 0)
        {
            return 0;
        }

        if (buffer.Length > _remaining)
        {
            buffer = buffer.Slice(0, (int)_remaining);
        }

        int read = _inner.Read(buffer);
        _remaining -= read;
        return read;
    }

    /// <summary>Reads up to the memory's length, never past the end of the window.</summary>
    /// <param name="buffer">The memory to read into.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The number of bytes read, or 0 at the end of the window.</returns>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (_remaining <= 0)
        {
            return 0;
        }

        if (buffer.Length > _remaining)
        {
            buffer = buffer.Slice(0, (int)_remaining);
        }

        int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _remaining -= read;
        return read;
    }

    /// <summary>Reads up to <paramref name="count"/> bytes, never past the end of the window.</summary>
    /// <param name="buffer">The buffer to read into.</param>
    /// <param name="offset">Where in the buffer to start.</param>
    /// <param name="count">The most bytes to read.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The number of bytes read, or 0 at the end of the window.</returns>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken)
    {
        return ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();
    }

    /// <summary>Not supported.</summary>
    /// <param name="offset">Ignored.</param>
    /// <param name="origin">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException("A checkpoint tensor window cannot seek.");
    }

    /// <summary>Not supported.</summary>
    /// <param name="value">Ignored.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void SetLength(long value)
    {
        throw new NotSupportedException("A checkpoint tensor window is read-only.");
    }

    /// <summary>Not supported.</summary>
    /// <param name="buffer">Ignored.</param>
    /// <param name="offset">Ignored.</param>
    /// <param name="count">Ignored.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("A checkpoint tensor window is read-only.");
    }

    /// <summary>Closes the stream underneath.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Stream.Dispose()"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
