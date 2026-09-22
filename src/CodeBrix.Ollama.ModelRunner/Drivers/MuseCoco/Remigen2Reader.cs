using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner; //was previously: microsoft/muzic musecoco/2-attribute2music_model/midiprocessor (MIT)

/// <summary>The shared REMIGEN2 token decoder, before instruments are assigned MIDI channels.</summary>
internal sealed class Remigen2Reader
{
    private static readonly (int Numerator, int Denominator)[] Signatures = CreateSignatures();
    private static readonly int[] Durations = CreateDurations();
    private long _barPosition;
    private long _position;
    private int _positionsPerBar = 48;
    private int _pitch;
    private int _duration;
    private int _lastSignature = -1;
    private int _lastTempo = -1;
    private char _previous;

    internal int Instrument { get; private set; }
    internal long BarTick => _barPosition * 40;
    internal bool HasTempo => _lastTempo >= 0;
    internal bool HasSignature => _lastSignature >= 0;

    /// <summary>Returns a note or metadata event, or null. Notes still need their track and channel assigned.</summary>
    internal MidiEvent Read(string word)
    {
        if (word == null || word.Length < 3 || word[1] != '-'
            || !int.TryParse(word.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            throw new InferenceException("Unexpected REMIGEN2 music token: " + word);
        }

        MidiEvent result = null;
        switch (word[0])
        {
            case 'b':
                if (value != 1) throw new InferenceException("Invalid REMIGEN2 bar token.");
                _barPosition += _positionsPerBar;
                _position = _barPosition;
                break;
            case 's':
                if (value >= Signatures.Length) throw new InferenceException("Invalid REMIGEN2 time signature.");
                if (_lastSignature == value) break;
                _lastSignature = value;
                (int numerator, int denominator) = Signatures[value];
                _positionsPerBar = 48 * numerator / denominator;
                result = MidiEvent.TimeSignature(BarTick, 0, numerator, denominator);
                break;
            case 'o':
                _position = _barPosition + value;
                break;
            case 't':
                if (value > 48) throw new InferenceException("Invalid REMIGEN2 tempo.");
                if (_lastTempo == value) break;
                _lastTempo = value;
                result = MidiEvent.TempoFromMicroseconds(_position * 40, 0,
                    (long)Math.Round(60000000.0 / (16 * Math.Pow(2, value / 12.0))));
                break;
            case 'i':
                if (value > 128) throw new InferenceException("Invalid REMIGEN2 instrument.");
                Instrument = value;
                break;
            case 'p':
                if (value > 255) throw new InferenceException("Invalid REMIGEN2 pitch.");
                _pitch = value >= 128 ? value - 128 : value;
                break;
            case 'd':
                if (_previous != 'p') throw new InferenceException("REMIGEN2 duration without a pitch.");
                _duration = Math.Max(1, Durations[Math.Min(value, Durations.Length - 1)]);
                break;
            case 'v':
                if (_previous != 'd' || value > 31) throw new InferenceException("Invalid REMIGEN2 note velocity or ordering.");
                result = MidiEvent.Note(_position * 40, 0, 0, _pitch, value * 4 + 2, _duration * 40L);
                break;
            default:
                throw new InferenceException("Unsupported REMIGEN2 token: " + word);
        }

        _previous = word[0];
        return result;
    }

    private static (int Numerator, int Denominator)[] CreateSignatures()
    {
        var signatures = new List<(int Numerator, int Denominator)>();
        for (int power = 0; power <= 6; power++)
        {
            for (int numerator = 1; numerator <= 2 * (1 << power); numerator++)
                signatures.Add((numerator, 1 << power));
        }
        return signatures.ToArray();
    }

    private static int[] CreateDurations()
    {
        var durations = new List<int>();
        int duration = 0;
        for (int power = 0; power < 8; power++)
        {
            for (int i = 0; i < 12; i++)
            {
                durations.Add(duration);
                duration += 1 << power;
            }
        }
        return durations.ToArray();
    }
}
