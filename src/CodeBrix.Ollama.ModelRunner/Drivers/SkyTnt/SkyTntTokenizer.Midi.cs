using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: midi_tokenizer.py@f504d5cb58f769ab0f2909c679238f6621034573

/// <content>
/// The direction from a piece of music to rows of tokens - the half of the tokenizer a PROMPT goes through -
/// and the direction back out again.
/// </content>
/// <remarks>
/// It is a second file of the same class for the reason the platform-call surface is: this is one type split
/// by subject, not two types. The vocabulary and the two token-row conversions are in
/// <c>SkyTntTokenizer.cs</c>; reading and writing whole pieces are here.
/// </remarks>
internal sealed partial class SkyTntTokenizer
{
    /// <summary>How many tracks of a piece the model reads; the rest are left out.</summary>
    internal const int MaximumTracks = 128;

    private static readonly string[] SortOrderByName =
    {
        "time_signature", "key_signature", "set_tempo", "patch_change", "control_change", "note",
    };

    /// <summary>
    /// Reads a piece of music into the rows of tokens the model continues from.
    /// </summary>
    /// <param name="score">The piece.</param>
    /// <param name="addBeginningAndEnd">Whether to wrap it in the beginning and ending tokens.</param>
    /// <param name="controlEpsilon">
    /// How far a controller has to move before the change is worth keeping; nought keeps every one.
    /// </param>
    /// <param name="tempoEpsilon">How far the speed has to move before the change is worth keeping.</param>
    /// <returns>One row of tokens per event, in the order the model expects to see them.</returns>
    internal List<int[]> Tokenize(
        MidiScore score, bool addBeginningAndEnd, int controlEpsilon, int tempoEpsilon) =>
        Tokenize(score, addBeginningAndEnd, controlEpsilon, tempoEpsilon, OptimiseMidi, OptimiseMidi,
            OptimiseMidi);

    /// <summary>
    /// Reads a piece of music into the rows of tokens the model continues from, saying which parts of the
    /// tidying up to do.
    /// </summary>
    /// <param name="score">The piece.</param>
    /// <param name="addBeginningAndEnd">Whether to wrap it in the beginning and ending tokens.</param>
    /// <param name="controlEpsilon">How far a controller has to move before the change is worth keeping.</param>
    /// <param name="tempoEpsilon">How far the speed has to move before the change is worth keeping.</param>
    /// <param name="remapTrackChannel">Whether to re-number channels and tracks into the order they are used.</param>
    /// <param name="addDefaultInstrument">Whether to give an instrument to a channel that never chose one.</param>
    /// <param name="removeEmptyChannels">Whether to drop channels that carry no notes.</param>
    /// <returns>One row of tokens per event.</returns>
    internal List<int[]> Tokenize(
        MidiScore score,
        bool addBeginningAndEnd,
        int controlEpsilon,
        int tempoEpsilon,
        bool remapTrackChannel,
        bool addDefaultInstrument,
        bool removeEmptyChannels)
    {
        if (score == null) throw new ArgumentNullException(nameof(score));

        int ticksPerBeat = score.TicksPerQuarterNote;
        List<List<MidiEvent>> tracks = Tracks(score);

        SkyTntRowMap eventList = new SkyTntRowMap();
        SkyTntOrderedIntMap[] trackIdxMap = new SkyTntOrderedIntMap[16];
        List<int>[] channelNoteTracks = new List<int>[16];
        bool[] channelIsEmpty = new bool[16];
        for (int c = 0; c < 16; c++)
        {
            trackIdxMap[c] = new SkyTntOrderedIntMap();
            channelNoteTracks[c] = new List<int>();
            channelIsEmpty[c] = true;
        }

        Dictionary<int, int> trackIdxDict = new Dictionary<int, int>();
        List<int> channels = new List<int>();
        List<int> patchChannels = new List<int>();
        int[] noteKeyHistogram = new int[12];
        List<SkyTntEventRow> keySignatures = new List<SkyTntEventRow>();
        List<int> trackToChannelsOrder = new List<int>();
        Dictionary<int, List<int>> trackToChannels = new Dictionary<int, List<int>>();

        for (int trackIdx = 0; trackIdx < tracks.Count; trackIdx++)
        {
            Dictionary<int, SkyTntLastNote> lastNotes = new Dictionary<int, SkyTntLastNote>();
            Dictionary<int, int> patchDictionary = new Dictionary<int, int>();
            Dictionary<int, int> controlDictionary = new Dictionary<int, int>();
            int lastBeatsPerMinute = 0;

            List<int> trackChannels = new List<int>();
            if (!trackToChannels.ContainsKey(trackIdx))
            {
                trackToChannels[trackIdx] = trackChannels;
                trackToChannelsOrder.Add(trackIdx);
            }
            else
            {
                trackChannels = trackToChannels[trackIdx];
            }

            foreach (MidiEvent item in tracks[trackIdx])
            {
                int channel = -1;
                int t = Quantize(item.Tick, ticksPerBeat);
                SkyTntEventType type;
                int[] values;

                switch (item.Kind)
                {
                    case MidiEventKind.Note:
                    {
                        channel = item.Channel;
                        if (channel < 0 || channel > 15) continue;
                        int duration = Quantize(item.DurationTicks, ticksPerBeat);
                        if (duration < 1) duration = 1;

                        type = _typesByName["note"];
                        values = new[]
                        {
                            t / StepsPerBeat, t % StepsPerBeat, trackIdx, channel, item.NoteNumber,
                            item.Velocity, duration,
                        };

                        channelIsEmpty[channel] = false;
                        if (!trackIdxDict.ContainsKey(channel)) trackIdxDict[channel] = trackIdx;
                        if (!channelNoteTracks[channel].Contains(trackIdx))
                        {
                            channelNoteTracks[channel].Add(trackIdx);
                        }

                        if (channel != 9) noteKeyHistogram[item.NoteNumber % 12]++;
                        if (!trackChannels.Contains(channel)) trackChannels.Add(channel);
                        break;
                    }

                    case MidiEventKind.ProgramChange:
                    {
                        channel = item.Channel;
                        if (channel < 0 || channel > 15) continue;

                        type = _typesByName["patch_change"];
                        values = new[] { t / StepsPerBeat, t % StepsPerBeat, trackIdx, channel, item.Program };

                        if (patchDictionary.TryGetValue(channel, out int lastPatch) && lastPatch == item.Program)
                        {
                            continue;
                        }

                        patchDictionary[channel] = item.Program;
                        if (!patchChannels.Contains(channel)) patchChannels.Add(channel);
                        break;
                    }

                    case MidiEventKind.ControlChange:
                    {
                        channel = item.Channel;
                        if (channel < 0 || channel > 15) continue;

                        type = _typesByName["control_change"];
                        values = new[]
                        {
                            t / StepsPerBeat, t % StepsPerBeat, trackIdx, channel, item.Controller, item.Value,
                        };

                        int key = (channel << 8) | item.Controller;
                        if (!controlDictionary.TryGetValue(key, out int lastValue))
                        {
                            //The upstream default is stored as well as returned, so a controller whose first
                            //move is too small to keep is compared against nought again next time.
                            lastValue = 0;
                            controlDictionary[key] = 0;
                        }

                        if (Math.Abs(lastValue - item.Value) < controlEpsilon) continue;
                        controlDictionary[key] = item.Value;
                        break;
                    }

                    case MidiEventKind.Tempo:
                    {
                        long tempo = item.MicrosecondsPerQuarterNote;
                        if (tempo == 0) continue;

                        int beatsPerMinute = (int)(60.0 / (tempo / 1000000.0));
                        if (beatsPerMinute > 383) beatsPerMinute = 383;

                        type = _typesByName["set_tempo"];
                        values = new[] { t / StepsPerBeat, t % StepsPerBeat, trackIdx, beatsPerMinute };

                        if (Math.Abs(lastBeatsPerMinute - beatsPerMinute) < tempoEpsilon) continue;
                        lastBeatsPerMinute = beatsPerMinute;
                        break;
                    }

                    case MidiEventKind.TimeSignature:
                    {
                        int numerator = item.Numerator;
                        int power = Power(item.Denominator);
                        if (numerator < 1 || numerator > 16 || power < 1 || power > 4) continue;

                        type = _typesByName["time_signature"];
                        values = new[]
                        {
                            t / StepsPerBeat, t % StepsPerBeat, trackIdx, numerator - 1, power - 1,
                        };
                        break;
                    }

                    case MidiEventKind.KeySignature:
                    {
                        int sharpsOrFlats = item.SharpsOrFlats;
                        if (sharpsOrFlats < -7 || sharpsOrFlats > 7) continue;

                        type = _typesByName["key_signature"];
                        values = new[]
                        {
                            t / StepsPerBeat, t % StepsPerBeat, trackIdx, sharpsOrFlats + 7,
                            item.IsMinor ? 1 : 0,
                        };
                        break;
                    }

                    default:
                        continue;
                }

                SkyTntEventRow row = new SkyTntEventRow(type, values);
                if (type.Name == "key_signature") keySignatures.Add(row);

                //A note, a time signature and a key signature are identified by everything but their last two
                //parameters; everything else by all but its last one.
                int drop = IsPositionKeyed(type.Name) ? 2 : 1;
                string rowKey = row.KeyWithout(drop);

                if (channel != -1)
                {
                    if (!channels.Contains(channel)) channels.Add(channel);
                    if (!trackIdxMap[channel].Contains(trackIdx)) trackIdxMap[channel].Set(trackIdx, 0);
                }

                if (type.Name == "note")
                {
                    //Two notes of the same pitch on the same channel cannot overlap once the positions have
                    //been rounded to sixteenths, so the earlier one is cut back to where the later one starts -
                    //and dropped if that leaves nothing of it.
                    int pitchKey = (values[3] << 8) | values[4];
                    if (lastNotes.TryGetValue(pitchKey, out SkyTntLastNote previous))
                    {
                        SkyTntEventRow last = previous.Row;
                        int lastT = (last.Values[0] * StepsPerBeat) + last.Values[1];
                        int shortened = Math.Min(last.Values[last.Values.Length - 1], t - lastT);
                        if (shortened < 0) shortened = 0;
                        last.Values[last.Values.Length - 1] = shortened;
                        if (shortened == 0) eventList.Remove(previous.Key);
                    }

                    lastNotes[pitchKey] = new SkyTntLastNote(rowKey, row);
                }

                eventList.Set(rowKey, row);
            }
        }

        List<SkyTntEventRow> rows = eventList.Rows();
        List<int> emptyChannels = new List<int>();
        foreach (int c in channels)
        {
            if (channelIsEmpty[c]) emptyChannels.Add(c);
        }

        if (remapTrackChannel)
        {
            patchChannels = new List<int>();
            SkyTntOrderedIntMap channelsMap = new SkyTntOrderedIntMap();
            if (channels.Contains(9)) channelsMap.Set(9, 9);

            if (removeEmptyChannels)
            {
                //A stable ordering that puts the channels carrying notes first.
                List<int> reordered = new List<int>();
                foreach (int c in channels)
                {
                    if (!emptyChannels.Contains(c)) reordered.Add(c);
                }

                foreach (int c in channels)
                {
                    if (emptyChannels.Contains(c)) reordered.Add(c);
                }

                channels = reordered;
            }

            int channelsCount = 0;
            foreach (int c in channels)
            {
                if (c == 9) continue;
                channelsMap.Set(c, channelsCount);
                channelsCount++;

                //Channel nine is the percussion channel wherever it appears, so it is stepped over.
                if (channelsCount == 9) channelsCount = 10;
            }

            channels = new List<int>();
            foreach (int c in channelsMap.Keys) channels.Add(channelsMap[c]);

            List<int> mapOrder = new List<int>(channelsMap.Keys);
            mapOrder.Sort((left, right) => channelsMap[left].CompareTo(channelsMap[right]));

            int trackCount = 0;
            foreach (int c in mapOrder)
            {
                if (removeEmptyChannels && emptyChannels.Contains(c)) continue;
                SkyTntOrderedIntMap map = trackIdxMap[c];
                foreach (int trackIdx in map.Keys)
                {
                    List<int> noteTracks = channelNoteTracks[c];
                    if (noteTracks.Count != 0 && !noteTracks.Contains(trackIdx)) continue;
                    trackCount++;
                    map.Set(trackIdx, trackCount);
                }
            }

            foreach (int c in mapOrder)
            {
                if (!(removeEmptyChannels && emptyChannels.Contains(c))) continue;
                SkyTntOrderedIntMap map = trackIdxMap[c];
                foreach (int trackIdx in map.Keys)
                {
                    List<int> noteTracks = channelNoteTracks[c];
                    if (!(noteTracks.Count != 0 && !noteTracks.Contains(trackIdx))) continue;
                    trackCount++;
                    map.Set(trackIdx, trackCount);
                }
            }

            List<int> remappedEmptyChannels = new List<int>();
            foreach (int c in emptyChannels) remappedEmptyChannels.Add(channelsMap[c]);
            emptyChannels = remappedEmptyChannels;

            trackIdxDict = new Dictionary<int, int>();
            keySignatures = new List<SkyTntEventRow>();
            List<SkyTntEventRow> keySignaturesToAdd = new List<SkyTntEventRow>();
            List<SkyTntEventRow> keySignaturesToRemove = new List<SkyTntEventRow>();

            foreach (SkyTntEventRow row in rows)
            {
                int trackIdx = row.Track;
                switch (row.Type.Name)
                {
                    case "note":
                    {
                        int c = row.Values[3];
                        row.Values[3] = channelsMap[c];
                        row.Track = trackIdxMap[c][trackIdx];
                        if (!trackIdxDict.ContainsKey(row.Values[3]))
                        {
                            trackIdxDict[row.Values[3]] = row.Track;
                        }

                        break;
                    }

                    case "set_tempo":
                    case "time_signature":
                        row.Track = 0;
                        break;

                    case "key_signature":
                    {
                        List<int> newTracks = new List<int>();
                        List<int> newChannels = new List<int>();
                        for (int c = 0; c < 16; c++)
                        {
                            SkyTntOrderedIntMap map = trackIdxMap[c];
                            if (!map.Contains(trackIdx)) continue;

                            int newTrackIdx = map[trackIdx];
                            int mapped = channelsMap[c];
                            if (newTrackIdx == 0) continue;

                            bool seen = false;
                            for (int i = 0; i < newTracks.Count; i++)
                            {
                                if (newTracks[i] == newTrackIdx && newChannels[i] == mapped) seen = true;
                            }

                            if (seen) continue;
                            newTracks.Add(newTrackIdx);
                            newChannels.Add(mapped);
                        }

                        if (newTracks.Count == 0)
                        {
                            if (row.Track == 0)
                            {
                                keySignatures.Add(row);
                                continue;
                            }

                            //Marked so that removing it cannot match another row that happens to be equal.
                            row.Track = -1;
                            keySignaturesToRemove.Add(row);
                            continue;
                        }

                        row.Track = newTracks[0];
                        keySignatures.Add(row);

                        //A key signature on the percussion channel means nothing, so it is flattened to none.
                        if (newChannels[0] == 9) row.Values[3] = 7;

                        for (int i = 1; i < newTracks.Count; i++)
                        {
                            SkyTntEventRow copy = row.Copy();
                            copy.Track = newTracks[i];
                            if (newChannels[i] == 9) copy.Values[3] = 7;
                            keySignatures.Add(copy);
                            keySignaturesToAdd.Add(copy);
                        }

                        break;
                    }

                    case "control_change":
                    case "patch_change":
                    {
                        int c = row.Values[3];
                        row.Values[3] = channelsMap[c];
                        SkyTntOrderedIntMap map = trackIdxMap[c];

                        //An event on a track that carries no notes of its channel is moved to the first track
                        //that does, so it is not left behind on a track nothing else reaches.
                        List<int> noteTracks = channelNoteTracks[c];
                        int from = trackIdx;
                        if (noteTracks.Count != 0 && !noteTracks.Contains(trackIdx)) from = noteTracks[0];

                        row.Track = map[from];
                        if (row.Type.Name == "patch_change" && !patchChannels.Contains(row.Values[3]))
                        {
                            patchChannels.Add(row.Values[3]);
                        }

                        break;
                    }
                }
            }

            foreach (SkyTntEventRow row in keySignaturesToRemove) rows.Remove(row);
            rows.AddRange(keySignaturesToAdd);

            trackToChannelsOrder = new List<int>();
            trackToChannels = new Dictionary<int, List<int>>();
            for (int c = 0; c < 16; c++)
            {
                if (!channelsMap.Contains(c)) continue;
                int mapped = channelsMap[c];
                SkyTntOrderedIntMap map = trackIdxMap[c];
                foreach (int key in map.Keys)
                {
                    int trackIdx = map[key];
                    if (!trackToChannels.TryGetValue(trackIdx, out List<int> list))
                    {
                        list = new List<int>();
                        trackToChannels[trackIdx] = list;
                        trackToChannelsOrder.Add(trackIdx);
                    }

                    if (!list.Contains(mapped)) list.Add(mapped);
                }
            }
        }

        if (addDefaultInstrument)
        {
            foreach (int c in channels)
            {
                if (patchChannels.Contains(c) || !trackIdxDict.ContainsKey(c)) continue;
                rows.Add(new SkyTntEventRow(
                    _typesByName["patch_change"], new[] { 0, 0, trackIdxDict[c], c, 0 }));
            }
        }

        ApplyKeySignature(
            rows, keySignatures, noteKeyHistogram, trackToChannelsOrder, trackToChannels, remapTrackChannel);

        rows = SortRows(rows);
        rows = OptimiseSetup(rows);

        List<int[]> sequence = new List<int[]>();
        if (addBeginningAndEnd) sequence.Add(BeginningRow());

        int lastBeat = 0;
        foreach (SkyTntEventRow row in rows)
        {
            if (removeEmptyChannels
                && (row.Type.Name == "control_change" || row.Type.Name == "patch_change")
                && emptyChannels.Contains(row.Values[3]))
            {
                continue;
            }

            int beat = row.Beat;
            row.Beat = beat - lastBeat;
            int[] tokens = EventToTokens(row);
            if (tokens == null) continue;

            sequence.Add(tokens);
            lastBeat = beat;
        }

        if (addBeginningAndEnd) sequence.Add(EndRow());
        return sequence;
    }

    /// <summary>
    /// Turns the rows of tokens a generation produced back into a piece of music, tidying a note that the
    /// next one of the same pitch cuts off.
    /// </summary>
    /// <param name="sequence">The rows, beginning and ending tokens and all.</param>
    /// <returns>The piece, at this tokenizer's own resolution.</returns>
    internal MidiScore Detokenize(IEnumerable<int[]> sequence)
    {
        if (sequence == null) throw new ArgumentNullException(nameof(sequence));

        List<MidiEvent> events = new List<MidiEvent>();
        int beat = 0;
        foreach (int[] tokens in sequence)
        {
            SkyTntEventRow row = TokensToEvent(tokens);
            if (row == null) continue;

            beat += row.Values[0];
            MidiEvent item = ToMidiEvent(row, beat);
            if (item != null) events.Add(item);
        }

        return Tidy(events);
    }

    /// <summary>
    /// The same tidying up <see cref="Detokenize"/> ends with, applied to events that were streamed out one
    /// at a time: a note is cut back to where the next one of the same pitch on the same track begins, and one
    /// left with nothing is dropped.
    /// </summary>
    /// <param name="events">The events of the piece.</param>
    /// <returns>The piece, at this tokenizer's own resolution.</returns>
    internal MidiScore Tidy(IEnumerable<MidiEvent> events)
    {
        if (events == null) throw new ArgumentNullException(nameof(events));

        Dictionary<int, List<MidiEvent>> byTrack = new Dictionary<int, List<MidiEvent>>();
        List<int> order = new List<int>();
        foreach (MidiEvent item in events)
        {
            if (!byTrack.TryGetValue(item.Track, out List<MidiEvent> list))
            {
                list = new List<MidiEvent>();
                byTrack[item.Track] = list;
                order.Add(item.Track);
            }

            list.Add(item);
        }

        order.Sort();
        List<MidiEvent> tidied = new List<MidiEvent>();
        foreach (int track in order)
        {
            List<MidiEvent> list = Stable(byTrack[track], (left, right) => left.Tick.CompareTo(right.Tick));
            Dictionary<int, long> lastNoteTick = new Dictionary<int, long>();

            //Walked backwards, so that each note sees where the NEXT one of its pitch begins.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                MidiEvent item = list[i];
                if (item.Kind != MidiEventKind.Note) continue;

                long duration = item.DurationTicks;
                int key = (item.Channel << 8) | item.NoteNumber;
                if (lastNoteTick.TryGetValue(key, out long next))
                {
                    long room = next - item.Tick;
                    if (room < 0) room = 0;
                    if (room < duration) duration = room;
                }

                lastNoteTick[key] = item.Tick;
                list[i] = duration == 0 ? null : item.WithDuration(duration);
            }

            foreach (MidiEvent item in list)
            {
                if (item != null) tidied.Add(item);
            }
        }

        return new MidiScore(TicksPerQuarterNote, tidied);
    }

    /// <summary>
    /// The position in ticks an event's beat and offset inside it stand for, at this tokenizer's resolution.
    /// </summary>
    /// <param name="beat">The beat, counted from the start of the piece.</param>
    /// <param name="withinBeat">Where inside the beat, in sixteenths.</param>
    /// <returns>The position in ticks.</returns>
    internal static long TickOf(int beat, int withinBeat) =>
        (long)((double)((beat * StepsPerBeat) + withinBeat) * TicksPerQuarterNote / StepsPerBeat);

    /// <summary>
    /// Turns one generated event into a playable one at an absolute position, or <see langword="null"/> when
    /// it is a note so short that it can never be heard.
    /// </summary>
    /// <param name="row">The event.</param>
    /// <param name="beat">The beat it falls on, counted from the start of the piece.</param>
    /// <returns>The event, or <see langword="null"/>.</returns>
    internal MidiEvent ToMidiEvent(SkyTntEventRow row, int beat)
    {
        long tick = TickOf(beat, row.Values[1]);
        int track = row.Values[2];

        switch (row.Type.Name)
        {
            case "note":
            {
                long duration = (long)((double)row.Values[6] * TicksPerQuarterNote / StepsPerBeat);
                if (duration <= 0) return null;
                return MidiEvent.Note(tick, track, row.Values[3], row.Values[4], row.Values[5], duration);
            }

            case "patch_change":
                return MidiEvent.ProgramChange(tick, track, row.Values[3], row.Values[4]);

            case "control_change":
                return MidiEvent.ControlChange(tick, track, row.Values[3], row.Values[4], row.Values[5]);

            case "set_tempo":
                return MidiEvent.TempoFromMicroseconds(tick, track, MicrosecondsFor(row.Values[3]));

            case "time_signature":
                return MidiEvent.TimeSignature(tick, track, row.Values[3] + 1, 1 << (row.Values[4] + 1));

            case "key_signature":
                return MidiEvent.KeySignature(tick, track, row.Values[3] - 7, row.Values[4] != 0);

            default:
                return null;
        }
    }

    /// <summary>The microseconds a quarter note lasts at a whole number of beats per minute.</summary>
    /// <param name="beatsPerMinute">The speed; nought is read as one, as upstream does.</param>
    /// <returns>The microseconds.</returns>
    internal static long MicrosecondsFor(int beatsPerMinute)
    {
        int bpm = beatsPerMinute == 0 ? 1 : beatsPerMinute;
        return (long)((60.0 / bpm) * 1000000.0);
    }

    private static bool IsPositionKeyed(string name) =>
        name == "note" || name == "time_signature" || name == "key_signature";

    private static int Quantize(long tick, int ticksPerBeat) =>
        (int)Math.Round((double)(StepsPerBeat * tick) / ticksPerBeat, MidpointRounding.ToEven);

    private static int Power(int denominator)
    {
        int power = 0;
        int value = denominator;
        while (value > 1)
        {
            value >>= 1;
            power++;
        }

        return power;
    }

    private static List<List<MidiEvent>> Tracks(MidiScore score)
    {
        int count = Math.Min(score.TrackCount, MaximumTracks);
        List<List<MidiEvent>> tracks = new List<List<MidiEvent>>(count);
        List<List<long>> keys = new List<List<long>>(count);
        for (int i = 0; i < count; i++)
        {
            tracks.Add(new List<MidiEvent>());
            keys.Add(new List<long>());
        }

        //A track is read in the order a FILE settles its events: a note takes its place where it ENDS,
        //because that is where the file says so, and everything else where it happens. It decides which of
        //two events sharing a position comes first, and the model was trained on what a file gives.
        foreach (MidiEvent item in score.Events)
        {
            if (item.Track >= count) continue;
            tracks[item.Track].Add(item);
            keys[item.Track].Add(item.Kind == MidiEventKind.Note ? item.Tick + item.DurationTicks : item.Tick);
        }

        for (int i = 0; i < count; i++)
        {
            List<MidiEvent> events = tracks[i];
            List<long> order = keys[i];
            int[] indices = new int[events.Count];
            for (int j = 0; j < indices.Length; j++) indices[j] = j;
            Array.Sort(indices, (left, right) =>
            {
                int byKey = order[left].CompareTo(order[right]);
                return byKey != 0 ? byKey : left.CompareTo(right);
            });

            List<MidiEvent> sorted = new List<MidiEvent>(events.Count);
            foreach (int index in indices) sorted.Add(events[index]);
            tracks[i] = sorted;
        }

        return tracks;
    }

    private static List<MidiEvent> Stable(List<MidiEvent> events, Comparison<MidiEvent> comparison)
    {
        int[] indices = new int[events.Count];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        Array.Sort(indices, (left, right) =>
        {
            int result = comparison(events[left], events[right]);
            return result != 0 ? result : left.CompareTo(right);
        });

        List<MidiEvent> sorted = new List<MidiEvent>(events.Count);
        foreach (int index in indices) sorted.Add(events[index]);
        return sorted;
    }

    private static List<SkyTntEventRow> SortRows(List<SkyTntEventRow> rows)
    {
        int[] indices = new int[rows.Count];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        Array.Sort(indices, (left, right) =>
        {
            SkyTntEventRow a = rows[left];
            SkyTntEventRow b = rows[right];
            for (int i = 0; i < 3; i++)
            {
                int byValue = a.Values[i].CompareTo(b.Values[i]);
                if (byValue != 0) return byValue;
            }

            int byName = NameOrder(a.Type.Name).CompareTo(NameOrder(b.Type.Name));
            return byName != 0 ? byName : left.CompareTo(right);
        });

        List<SkyTntEventRow> sorted = new List<SkyTntEventRow>(rows.Count);
        foreach (int index in indices) sorted.Add(rows[index]);
        return sorted;
    }

    private static int NameOrder(string name)
    {
        for (int i = 0; i < SortOrderByName.Length; i++)
        {
            if (SortOrderByName[i] == name) return i;
        }

        return SortOrderByName.Length;
    }

    //Everything that happens before the first note is gathered into a SETUP at the front of the piece: one
    //event of each kind per track and channel, all at position nought, in place of however they were spread
    //out. It is what the model was trained to see, and it is why a generated piece begins with its
    //instruments, its tempo and its signatures rather than discovering them a beat at a time.
    private static List<SkyTntEventRow> OptimiseSetup(List<SkyTntEventRow> rows)
    {
        SkyTntRowMap setup = new SkyTntRowMap();
        bool notesInSetup = false;

        for (int i = 0; i < rows.Count; i++)
        {
            SkyTntEventRow row = rows[i];
            SkyTntEventRow copy = row.Copy();
            if (row.Type.Name != "note" && row.Type.Name != "time_signature")
            {
                copy.Values[0] = 0;
                copy.Values[1] = 0;
            }

            bool hasNext = false;
            bool hasPrevious = false;
            int position = row.Values[0] + row.Values[1];
            if (i < rows.Count - 1)
            {
                SkyTntEventRow next = rows[i + 1];
                hasNext = position == next.Values[0] + next.Values[1];
            }

            if (notesInSetup && i > 0)
            {
                SkyTntEventRow previous = rows[i - 1];
                hasPrevious = position == previous.Values[0] + previous.Values[1];
            }

            if ((row.Type.Name == "note" && !hasNext) || (notesInSetup && !hasPrevious))
            {
                List<SkyTntEventRow> result = SortRows(setup.Rows());
                for (int j = i; j < rows.Count; j++) result.Add(rows[j]);
                return result;
            }

            if (row.Type.Name == "note") notesInSetup = true;
            int drop = IsPositionKeyed(row.Type.Name) ? 2 : 1;
            setup.Set(row.KeyFromTrackWithout(drop), copy);
        }

        return rows;
    }

    //The key signature a piece is written in, worked out from the notes in it when the file said nothing
    //useful - which is most files, because the default key signature of no sharps and no flats is what a
    //writer puts there when it does not know.
    private void ApplyKeySignature(
        List<SkyTntEventRow> rows,
        List<SkyTntEventRow> keySignatures,
        int[] noteKeyHistogram,
        List<int> trackToChannelsOrder,
        Dictionary<int, List<int>> trackToChannels,
        bool remapTrackChannel)
    {
        bool allDefault = true;
        foreach (SkyTntEventRow row in keySignatures)
        {
            if (row.Values[3] != 7) allDefault = false;
        }

        if (keySignatures.Count != 0 && !allDefault) return;

        int rootKey = DetectKey(noteKeyHistogram);
        if (rootKey < 0)
        {
            foreach (SkyTntEventRow row in keySignatures) rows.Remove(row);
            return;
        }

        int sharpsOrFlats = KeyToSharpsOrFlats(rootKey, 0);
        if (keySignatures.Count == 0)
        {
            SkyTntEventType type = _typesByName["key_signature"];
            foreach (int track in trackToChannelsOrder)
            {
                if (remapTrackChannel && track == 0) continue;
                List<int> list = trackToChannels[track];
                bool percussionOnly = list.Count == 1 && list[0] == 9;
                rows.Add(new SkyTntEventRow(
                    type, new[] { 0, 0, track, (percussionOnly ? 0 : sharpsOrFlats) + 7, 0 }));
            }

            return;
        }

        foreach (SkyTntEventRow row in keySignatures)
        {
            if (trackToChannels.TryGetValue(row.Track, out List<int> list)
                && list.Count == 1 && list[0] == 9)
            {
                continue;
            }

            row.Values[3] = sharpsOrFlats + 7;
            row.Values[4] = 0;
        }
    }

    private static int KeyToSharpsOrFlats(int key, int minor)
    {
        int sharpsOrFlats = (key * 7) % 12;
        if (sharpsOrFlats > 6 || (minor == 1 && sharpsOrFlats >= 5)) sharpsOrFlats -= 12;
        return sharpsOrFlats;
    }

    private static int DetectKey(int[] histogram)
    {
        const double Threshold = 0.7;

        if (histogram.Length != 12) return -1;

        int total = 0;
        foreach (int count in histogram) total += count;
        if (total == 0) return -1;

        int[] byCount = new int[12];
        for (int i = 0; i < 12; i++) byCount[i] = i;

        //Sorted the way the upstream code does: by how often the note is used, highest first, with two notes
        //used equally often left in the order they already stood in - which is what a stable descending sort
        //on the count alone gives.
        Array.Sort(byCount, (left, right) =>
        {
            int byValue = histogram[right].CompareTo(histogram[left]);
            return byValue != 0 ? byValue : left.CompareTo(right);
        });

        int top = 0;
        for (int i = 0; i < 7; i++) top += histogram[byCount[i]];
        if ((double)top / total < Threshold) return -1;

        List<int> keys = new List<int>();
        for (int i = 0; i < 7; i++) keys.Add(byCount[i]);
        keys.Sort();

        List<int> semitones = new List<int>();
        for (int i = 0; i < keys.Count; i++)
        {
            int previous = i == 0 ? keys[keys.Count - 1] : keys[i - 1];
            int distance = keys[i] - previous;
            if (distance == 1 || distance == -11) semitones.Add(keys[i]);
        }

        if (semitones.Count != 2) return -1;

        int between = semitones[1] - semitones[0];
        if (between == 5) return semitones[0];
        if (between == 7) return semitones[1];
        return -1;
    }
}
