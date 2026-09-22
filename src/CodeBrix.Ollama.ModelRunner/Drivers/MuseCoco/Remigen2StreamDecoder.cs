using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Buffers one unfinished bar, then releases events whose positions no later bar can precede.
/// The final bar receives the same incomplete-position cleanup as a completed score.
/// </summary>
internal sealed class Remigen2StreamDecoder
{
    private readonly Remigen2Reader _reader = new Remigen2Reader();
    private readonly List<string> _words = new List<string>();
    private readonly List<(MidiEvent Event, int Instrument)> _pending = new List<(MidiEvent, int)>();
    private readonly Dictionary<int, (int Track, int Channel)> _instruments = new Dictionary<int, (int, int)>();
    private int _nextChannel;
    private bool _started;
    private bool _initialized;
    private bool _completed;

    internal IReadOnlyList<MidiEvent> Add(string word)
    {
        if (_completed) throw new InvalidOperationException("The MIDI stream has already ended.");
        if (word == "</s>") return Complete();
        if (!_started)
        {
            _started = true;
            if (word is "Q1" or "Q2" or "Q3" or "Q4" or "None") return Array.Empty<MidiEvent>();
        }

        _words.Add(word);
        if (word != "b-1") return Array.Empty<MidiEvent>();
        ReadBar();
        return Drain(_reader.BarTick);
    }

    internal IReadOnlyList<MidiEvent> Complete()
    {
        if (_completed) throw new InvalidOperationException("The MIDI stream has already ended.");
        _completed = true;
        Remigen2Decoder.CleanEnding(_words);
        ReadBar();
        return Drain(long.MaxValue);
    }

    private void ReadBar()
    {
        foreach (string word in _words)
        {
            MidiEvent item = _reader.Read(word);
            if (item != null) _pending.Add((item, _reader.Instrument));
        }
        _words.Clear();
    }

    private IReadOnlyList<MidiEvent> Drain(long beforeTick)
    {
        if (!_initialized)
        {
            // A stream must establish playback defaults now, even if its first explicit change is later.
            if (!_pending.Any(p => p.Event.Kind == MidiEventKind.Tempo && p.Event.Tick == 0))
                _pending.Add((MidiEvent.Tempo(0, 0, 120), 0));
            if (!_pending.Any(p => p.Event.Kind == MidiEventKind.TimeSignature && p.Event.Tick == 0))
                _pending.Add((MidiEvent.TimeSignature(0, 0, 4, 4), 0));
            _initialized = true;
        }

        var result = new List<MidiEvent>();
        // OrderBy is stable. At one tick establish tempo/signature before any note is made playable.
        foreach (var entry in _pending.Where(p => p.Event.Tick < beforeTick)
            .OrderBy(p => p.Event.Tick).ThenBy(p => p.Event.Kind == MidiEventKind.Note ? 1 : 0))
        {
            MidiEvent item = entry.Event;
            if (item.Kind != MidiEventKind.Note)
            {
                result.Add(item);
                continue;
            }

            if (!_instruments.TryGetValue(entry.Instrument, out var assignment))
            {
                if (_nextChannel == 9) _nextChannel++;
                int channel = entry.Instrument == 128 ? 9 : _nextChannel++;
                if (channel > 15)
                    throw new InferenceException("This piece needs more than 15 melodic MIDI channels. Use fewer instrument attributes.");
                assignment = (_instruments.Count + 1, channel);
                _instruments.Add(entry.Instrument, assignment);
                result.Add(MidiEvent.ProgramChange(item.Tick, assignment.Track, assignment.Channel,
                    entry.Instrument == 128 ? 0 : entry.Instrument));
            }

            result.Add(MidiEvent.Note(item.Tick, assignment.Track, assignment.Channel,
                item.NoteNumber, item.Velocity, item.DurationTicks));
        }
        // A malformed position can lie beyond its bar. Keep it until the advancing bar boundary makes
        // that tick safe too, so even such a position cannot make the stream's horizon move backwards.
        _pending.RemoveAll(p => p.Event.Tick < beforeTick);
        return result;
    }
}
