namespace CodeBrix.Ollama.ModelRunner; //was previously: onnxruntime/core/mlas/lib/rotary_embedding.cpp@v1.30.0

/// <summary>
/// The rotary position embedding, one row of one head at a time, in the form ONNX Runtime's own fallback
/// kernel writes it.
/// </summary>
/// <remarks>
/// <para>
/// A rotary embedding turns a head's values in pairs, by an angle that depends on the position in the
/// sequence: the cosine and sine of that angle are looked up from caches the graph carries, one row per
/// position and half a head wide. Which values are paired is the whole of the difference between the two
/// layouts. NOT INTERLEAVED - the usual one, and what the builders emit - pairs the first half of the head
/// with the second half, so value <c>i</c> turns with value <c>i + half</c>. INTERLEAVED pairs neighbours, so
/// value <c>2i</c> turns with value <c>2i + 1</c>. Getting that round the wrong way gives a model that still
/// runs and writes nonsense, which is why it is ported rather than re-derived.
/// </para>
/// <para>
/// When the rotary width is narrower than the head, the values past it are copied through unturned.
/// </para>
/// </remarks>
internal static class OnnxRotaryEmbedding
{
    /// <summary>Turns one row of one head into another buffer.</summary>
    /// <remarks>
    /// The two buffers must be DIFFERENT arrays, or the two runs must not overlap: each output value is built
    /// from two input values, so writing one of them back over its input would spoil the pair that has not
    /// been reached yet. Upstream writes into a buffer of its own for the same reason.
    /// </remarks>
    /// <param name="input">The values to read.</param>
    /// <param name="inputOffset">Where the row starts in <paramref name="input"/>.</param>
    /// <param name="output">The values to write.</param>
    /// <param name="outputOffset">Where the row starts in <paramref name="output"/>.</param>
    /// <param name="cosine">The cosine cache.</param>
    /// <param name="sine">The sine cache.</param>
    /// <param name="cacheOffset">Where this position's row starts in both caches.</param>
    /// <param name="rotaryWidth">How many of the head's values are turned; it is even.</param>
    /// <param name="interleaved">Whether the pairs are neighbours rather than halves.</param>
    internal static void Rotate(
        float[] input,
        int inputOffset,
        float[] output,
        int outputOffset,
        float[] cosine,
        float[] sine,
        int cacheOffset,
        int rotaryWidth,
        bool interleaved)
    {
        int half = rotaryWidth / 2;
        for (int i = 0; i < rotaryWidth; i++)
        {
            int cacheIndex;
            bool second;
            int partner;
            if (interleaved)
            {
                cacheIndex = (i / 2) % half;
                second = (i & 1) == 1;
                partner = second ? i - 1 : i + 1;
            }
            else
            {
                cacheIndex = i % half;
                second = i >= half;
                partner = (i + half) % rotaryWidth;
            }

            float turned = input[inputOffset + i] * cosine[cacheOffset + cacheIndex];
            float other = input[inputOffset + partner] * sine[cacheOffset + cacheIndex];
            output[outputOffset + i] = second ? turned + other : turned - other;
        }
    }
}
