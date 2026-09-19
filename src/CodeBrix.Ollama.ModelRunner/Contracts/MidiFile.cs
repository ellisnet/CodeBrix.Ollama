using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Standard MIDI Files: turns a <see cref="MidiScore"/> into the bytes of one, and reads one back.
/// </summary>
/// <remarks>
/// <para>
/// IT NEEDS NOTHING INSTALLED and it is written from the published file format - a header chunk, a track chunk
/// per track, a variable-length delta time before every message, and the channel and meta messages this
/// library's events describe. No other library is involved in either direction.
/// </para>
/// <para>
/// WRITING AND READING ARE PURE and touch no I/O, so they are synchronous; the two methods that name a FILE
/// are the asynchronous ones, because they are the ones that touch a disk.
/// </para>
/// <para>
/// WHAT A ROUND TRIP KEEPS. Write a score and read it back and you get the same ticks per quarter note, the
/// same events at the same positions, with the same tracks, channels, pitches, loudnesses and lengths, and the
/// same tempi to the microsecond. What it does not keep is anything this library has no event for, because
/// nothing here ever writes such a thing: a file made elsewhere may carry pitch bends, aftertouch, lyrics,
/// track names and system-exclusive data, and reading it drops them.
/// </para>
/// </remarks>
public static class MidiFile
{
    /// <summary>Writes a score out as the bytes of a Standard MIDI File.</summary>
    /// <param name="score">The piece to write.</param>
    /// <returns>The file's bytes, ready to be saved or played.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="score"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The piece uses more tracks than a file can hold, or leaves a gap longer than a delta time can express.
    /// </exception>
    public static byte[] Write(MidiScore score) => MidiFileWriter.Write(score, runningStatus: true);

    /// <summary>
    /// Writes a score out as the bytes of a Standard MIDI File, saying whether to use the format's shorthand
    /// for a repeated channel message.
    /// </summary>
    /// <param name="score">The piece to write.</param>
    /// <param name="runningStatus">
    /// Whether a channel message may leave its status byte out when it is the same as the one before, which
    /// makes the file smaller. Every reader understands it; switch it off only to see every byte spelled out.
    /// </param>
    /// <returns>The file's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="score"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The piece uses more tracks than a file can hold, or leaves a gap longer than a delta time can express.
    /// </exception>
    public static byte[] Write(MidiScore score, bool runningStatus) =>
        MidiFileWriter.Write(score, runningStatus);

    /// <summary>Reads the bytes of a Standard MIDI File into a score.</summary>
    /// <param name="bytes">The whole file.</param>
    /// <returns>The piece it holds, with its own ticks per quarter note.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bytes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// It is not a Standard MIDI File, it is cut short, or it measures time in frames of film rather than in
    /// divisions of a quarter note.
    /// </exception>
    public static MidiScore Read(byte[] bytes) => MidiFileReader.Read(bytes);

    /// <summary>Saves a score as a Standard MIDI File.</summary>
    /// <param name="path">Where to write it. An existing file is replaced.</param>
    /// <param name="score">The piece to write.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes when the file is on disk.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not set, or the piece cannot be written.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="score"/> is <see langword="null"/>.</exception>
    public static async Task WriteAsync(
        string path, MidiScore score, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        byte[] bytes = MidiFileWriter.Write(score, runningStatus: true);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a Standard MIDI File from disk.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The piece it holds.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is not set, or the file is not a Standard MIDI File this library can read.
    /// </exception>
    /// <exception cref="FileNotFoundException">There is no file at that path.</exception>
    public static async Task<MidiScore> ReadAsync(
        string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return MidiFileReader.Read(bytes);
    }
}
