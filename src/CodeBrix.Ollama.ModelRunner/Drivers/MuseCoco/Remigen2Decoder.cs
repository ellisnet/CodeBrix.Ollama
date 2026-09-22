using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Ollama.ModelRunner; //was previously: microsoft/muzic musecoco/2-attribute2music_model/midiprocessor (MIT)

/// <summary>MuseCoco REMIGEN2 decoding rules, translated from the Microsoft Muzic implementation.</summary>
internal static class Remigen2Decoder
{
    internal static MidiScore Decode(IReadOnlyList<int> ids, IReadOnlyList<string> vocabulary)
    {
        var words = new List<string>(ids.Count);
        foreach (int id in ids)
        {
            if (id < 0 || id >= vocabulary.Count) throw new ArgumentOutOfRangeException(nameof(ids));
            string word = vocabulary[id];
            if (word == "</s>") break;
            words.Add(word);
        }
        if (words.Count != 0 && words[0] is "Q1" or "Q2" or "Q3" or "Q4" or "None") words.RemoveAt(0);
        CleanEnding(words);
        var reader = new Remigen2Reader();
        var notes = new List<(int Instrument, MidiEvent Note)>();
        var events = new List<MidiEvent>();
        foreach (string word in words)
        {
            MidiEvent item = reader.Read(word);
            if (item == null) continue;
            if (item.Kind == MidiEventKind.Note) notes.Add((reader.Instrument, item));
            else events.Add(item);
        }
        if (!reader.HasTempo) events.Add(MidiEvent.Tempo(0, 0, 120));
        if (!reader.HasSignature) events.Add(MidiEvent.TimeSignature(0, 0, 4, 4));
        int channel = 0, track = 1;
        foreach (var group in notes.GroupBy(n => n.Instrument).OrderBy(g => g.Key))
        {
            if (channel == 9) channel++;
            int assigned = group.Key == 128 ? 9 : channel++;
            if (assigned > 15)
            {
                throw new InferenceException("This piece needs more than 15 melodic MIDI channels. Use fewer instrument attributes.");
            }
            events.Add(MidiEvent.ProgramChange(0, track, assigned, group.Key == 128 ? 0 : group.Key));
            foreach (var entry in group)
            {
                MidiEvent note = entry.Note;
                events.Add(MidiEvent.Note(note.Tick, track, assigned, note.NoteNumber, note.Velocity, note.DurationTicks));
            }
            track++;
        }
        return new MidiScore(480, events);
    }

    internal static void CleanEnding(List<string> words)
    {
        // Match the publisher's cleanup of an excerpt cut at the token limit.
        if (words.Count != 0)
        {
            char last = words[words.Count - 1][0];
            if (last == 'o') words[words.Count - 1] = "b-1";
            else if (last is 'p' or 'd')
            {
                int position = words.FindLastIndex(w => w.StartsWith("o-", StringComparison.Ordinal));
                if (position < 0) throw new InferenceException("An incomplete REMIGEN2 note has no position token.");
                words.RemoveRange(position, words.Count - position);
                words.Add("b-1");
            }
            else if (last == 'v') words.Add("b-1");
        }
    }
}
