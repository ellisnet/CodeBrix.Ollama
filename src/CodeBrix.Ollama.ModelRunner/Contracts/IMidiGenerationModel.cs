using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A loaded MIDI-generating model, ready to write music: it yields events AS IT MAKES THEM, and turns what it
/// has made into a piece that can be saved as a Standard MIDI File.
/// </summary>
/// <remarks>
/// <para>
/// EVENTS COME OUT WHILE THE MUSIC IS STILL BEING WRITTEN. <see cref="GenerateAsync"/> hands over each event
/// the moment its last token is chosen, so a streaming player can be fed from an <c>await foreach</c> and
/// start sounding the beginning of a piece whose end does not exist yet. A model may well write music more
/// slowly than it is played - the consumer buffers ahead, which is what
/// <see cref="MidiEvent.HorizonTicks"/> is for.
/// </para>
/// <para>
/// EVERY EVENT IS PLACED IN ABSOLUTE TIME, in ticks from the start of the piece, at the resolution
/// <see cref="Metadata"/> states - not in the model's own encoding, which a consumer could only use by
/// re-implementing the tokenizer. A note carries its own length, so there are no endings to pair up and no
/// note can be left hanging.
/// </para>
/// <para>
/// THE ORDER EVENTS ARRIVE IN. An event's position is a whole beat plus an offset inside that beat, and the
/// BEAT only ever moves forward - so the ticks are non-decreasing BEAT BY BEAT but not event by event: two
/// events on the same beat can arrive with the later offset first. What can be relied on is
/// <see cref="MidiEvent.HorizonTicks"/>: no event yielded after a given one will be earlier than the horizon
/// that one carried. A player can sound everything up to the horizon and hold the rest.
/// </para>
/// <para>
/// CHANNELS ARE 0 TO 15 and channel 9 is percussion; a player that numbers them 1 to 16 adds one. Tempo is in
/// quarter notes per minute.
/// </para>
/// <para>
/// ONE GENERATION AT A TIME. The graphs underneath run one step at a time, so a second generation started
/// while one is still going is refused with <see cref="InferenceException"/> rather than queued. Cancelling
/// one stops it between token steps and leaves the model ready for the next.
/// </para>
/// </remarks>
public interface IMidiGenerationModel : IDisposable, IAsyncDisposable
{
    /// <summary>What the model says about itself, including the size of a tick in everything it produces.</summary>
    MidiGenerationMetadata Metadata { get; }

    /// <summary>The options the two graphs were loaded with, with every default resolved to the value in use.</summary>
    OnnxRunnerOptions Options { get; }

    /// <summary>
    /// Writes music, handing over each event as it is made.
    /// </summary>
    /// <param name="options">What to generate and how, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">
    /// A token that stops the generation. It is honoured between token steps, so a generation stops within
    /// one step of the smaller graph; everything already handed over stays handed over.
    /// </param>
    /// <returns>
    /// The events, in the order described above. The enumeration ends when the model decides the piece is
    /// finished or when <see cref="MidiGenerationOptions.MaximumEvents"/> have been made.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// An option is outside the range stated for it, or a prompt of music is given together with settings
    /// describing a piece to start.
    /// </exception>
    /// <exception cref="InferenceException">A generation is already in flight, or a graph could not be run.</exception>
    /// <exception cref="ObjectDisposedException">The model has been disposed.</exception>
    IAsyncEnumerable<MidiEvent> GenerateAsync(
        MidiGenerationOptions options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gathers events into a piece, tidied the way the model's own tokenizer tidies one: a note is cut back to
    /// where the next note of the same pitch on the same track begins, and one left with no length is dropped.
    /// </summary>
    /// <param name="events">
    /// The events, which may be everything a generation produced or only what it had produced when it was
    /// cancelled.
    /// </param>
    /// <returns>The piece, at this model's resolution, ready for <see cref="MidiFile.Write(MidiScore)"/>.</returns>
    /// <remarks>
    /// IT IS NOT THE SAME AS THE EVENTS THEMSELVES, and the difference is one that streaming cannot avoid: a
    /// note's length can only be trimmed once the note that cuts it short is known, which is in the future when
    /// the note is handed over. A player sounding the stream will hold such a note a little longer than the
    /// saved file does.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is <see langword="null"/>.</exception>
    MidiScore ToScore(IEnumerable<MidiEvent> events);

    /// <summary>
    /// Saves events as a Standard MIDI File, tidied as <see cref="ToScore"/> describes.
    /// </summary>
    /// <param name="path">Where to write it. An existing file is replaced.</param>
    /// <param name="events">The events to save.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes when the file is on disk.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not set.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is <see langword="null"/>.</exception>
    Task SaveAsync(
        string path, IEnumerable<MidiEvent> events, CancellationToken cancellationToken = default);
}
