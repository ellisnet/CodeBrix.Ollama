namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The stream of random numbers a generation draws its choices from: given a seed it is the same stream every
/// time, on every machine and every platform this library runs on.
/// </summary>
/// <remarks>
/// <para>
/// IT IS OURS AND IT IS FIXED. The framework's own generator is explicitly allowed to change between releases,
/// so a piece of music generated with it would not come back the same next year; this one is written out here
/// so that a seed means one thing for ever. It is the well-known xoshiro256** generator, started from a seed
/// spread out by SplitMix64 - a few lines of shifts and multiplications, chosen because it is small enough to
/// read and its behaviour is completely settled by the seed.
/// </para>
/// <para>
/// IT IS NOT THE PUBLISHER'S. Generating the same music as the publisher's own Python needs the publisher's
/// own generator, which is a different algorithm again; what is required to match, and what is proved to
/// match, is GREEDY generation - where the model's most likely answer is always taken and no random number is
/// drawn at all.
/// </para>
/// <para>
/// It is not for cryptography, and nothing here pretends otherwise.
/// </para>
/// </remarks>
internal sealed class GenerationRandom
{
    private ulong _a;
    private ulong _b;
    private ulong _c;
    private ulong _d;

    /// <summary>Starts a stream from a seed.</summary>
    /// <param name="seed">The seed. Every seed gives a different stream, and the same seed the same one.</param>
    internal GenerationRandom(long seed)
    {
        ulong state = (ulong)seed;
        _a = SplitMix64(ref state);
        _b = SplitMix64(ref state);
        _c = SplitMix64(ref state);
        _d = SplitMix64(ref state);

        //A state of all noughts would keep the generator at nought for ever, and only one seed can produce it.
        if ((_a | _b | _c | _d) == 0) _d = 0x9E3779B97F4A7C15UL;
    }

    /// <summary>The next number, at least nought and below one.</summary>
    /// <returns>The number.</returns>
    internal double NextDouble()
    {
        //The top 53 bits are exactly the number of bits a double can hold without rounding.
        return (Next() >> 11) * (1.0 / 9007199254740992.0);
    }

    private ulong Next()
    {
        ulong result = Rotate(_b * 5, 7) * 9;
        ulong t = _b << 17;

        _c ^= _a;
        _d ^= _b;
        _b ^= _c;
        _a ^= _d;
        _c ^= t;
        _d = Rotate(_d, 45);

        return result;
    }

    private static ulong Rotate(ulong value, int count) => (value << count) | (value >> (64 - count));

    private static ulong SplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
