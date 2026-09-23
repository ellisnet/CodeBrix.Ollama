using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Restricts generation to REMIGEN2 continuations before sampling, without repairing emitted tokens.</summary>
internal sealed class Remigen2Grammar
{
    [Flags]
    private enum Kind
    {
        None = 0, Bar = 1, Signature = 2, Tempo = 4, Position = 8, Instrument = 16,
        Pitch = 32, Duration = 64, Velocity = 128, Quality = 256, End = 512
    }

    private const Kind BarStart = Kind.Bar | Kind.Signature | Kind.Tempo | Kind.Position | Kind.End;
    private readonly Kind[] _kinds;
    private Kind _allowed = BarStart | Kind.Quality;
    private bool _hasPosition;

    internal Remigen2Grammar(IReadOnlyList<string> vocabulary)
    {
        _kinds = new Kind[vocabulary.Count];
        for (int i = 0; i < vocabulary.Count; i++) _kinds[i] = Classify(vocabulary[i]);
    }

    internal bool Allows(int token) => (_allowed & _kinds[token]) != 0;

    internal void Accept(int token)
    {
        if (!Allows(token)) throw new InvalidOperationException("The token is not a valid REMIGEN2 continuation.");
        Kind kind = _kinds[token];
        if (kind == Kind.Bar) _hasPosition = false;
        else if (kind == Kind.Position) _hasPosition = true;
        _allowed = kind switch
        {
            Kind.Quality or Kind.Bar => BarStart,
            Kind.Signature => Kind.Tempo | Kind.Position | Kind.Bar | Kind.End,
            Kind.Tempo => Kind.Position | Kind.Bar | Kind.End
                | (_hasPosition ? Kind.Instrument | Kind.Pitch : Kind.None),
            Kind.Position => Kind.Tempo | Kind.Instrument | Kind.Pitch | Kind.Bar | Kind.End,
            Kind.Instrument => Kind.Pitch | Kind.Bar | Kind.End,
            Kind.Pitch => Kind.Duration | Kind.End,
            Kind.Duration => Kind.Velocity | Kind.End,
            Kind.Velocity => Kind.Pitch | Kind.Instrument | Kind.Position | Kind.Bar | Kind.End,
            _ => Kind.None
        };
        // EOS may cut an incomplete note; the existing final-position cleanup handles that case.
        // A signature is only legal at a bar start, so it cannot revise an already-started bar.
    }

    private static Kind Classify(string word)
    {
        if (word == "</s>") return Kind.End;
        if (word is "Q1" or "Q2" or "Q3" or "Q4" or "None") return Kind.Quality;
        if (word == null || word.Length < 3 || word[1] != '-'
            || !int.TryParse(word.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            return Kind.None;
        return word[0] switch
        {
            'b' when value == 1 => Kind.Bar,
            's' when value < 254 => Kind.Signature,
            't' when value <= 48 => Kind.Tempo,
            'o' => Kind.Position,
            'i' when value <= 128 => Kind.Instrument,
            'p' when value <= 255 => Kind.Pitch,
            'd' => Kind.Duration,
            'v' when value <= 31 => Kind.Velocity,
            _ => Kind.None
        };
    }
}
