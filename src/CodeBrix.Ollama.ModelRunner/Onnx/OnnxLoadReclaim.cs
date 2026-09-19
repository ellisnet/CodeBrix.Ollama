using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Keeps a load's abandoned memory from piling up: it counts the bytes the load has finished with and asks
/// the collector for them once that count is worth a collection.
/// </summary>
/// <remarks>
/// <para>
/// A LOAD IS THE ONE PLACE IN THIS ENGINE WHERE ASKING FOR A COLLECTION IS THE RIGHT ANSWER, and the numbers
/// say why. Reading an 822 MB graph abandons a model's worth of memory three times over in a few hundred
/// milliseconds - the file's own bytes once the message is parsed, the codec's copy of every weight once the
/// engine has its own, and the stored layout of every matrix once a kernel has turned it round - while the
/// live set never exceeds one copy. The collector cannot know that, so its budget grows instead, and the
/// process peaks at four times what the model needs and settles back afterwards. On a machine with room to
/// spare that is merely untidy; on a 16 GiB machine it is the difference between loading a model and not.
/// </para>
/// <para>
/// IT IS PROPORTIONATE, NOT UNCONDITIONAL. Nothing is collected until the load has abandoned more than
/// <see cref="ThresholdBytes"/>, so the small graphs a test suite loads by the hundred never pay for a
/// collection at all, and a large model pays for a handful. The collections are non-compacting: the large
/// object heap is what the arrays live on, the load is about to ask for buffers of the same shape again, and
/// compacting them would cost far more than it saves.
/// </para>
/// </remarks>
internal sealed class OnnxLoadReclaim
{
    /// <summary>
    /// How many bytes a load may abandon before it asks the collector for them. A model smaller than this
    /// never asks for a collection at all.
    /// </summary>
    internal const long ThresholdBytes = 128L * 1024 * 1024;

    private long _abandoned;

    /// <summary>How many collections this load has asked for. Read by the tests.</summary>
    internal int Collections { get; private set; }

    /// <summary>The bytes one tensor of a given element type and length occupies.</summary>
    /// <param name="elementType">The element type.</param>
    /// <param name="count">The number of elements.</param>
    /// <returns>The byte count.</returns>
    internal static long Bytes(OnnxElementType elementType, int count) => elementType switch
    {
        OnnxElementType.Float or OnnxElementType.Int32 => count * 4L,
        OnnxElementType.Int64 => count * 8L,
        _ => count,
    };

    /// <summary>
    /// Records that the load has finished with so many bytes, and collects when enough have piled up.
    /// </summary>
    /// <param name="bytes">How many bytes are now unreachable.</param>
    internal void Abandoned(long bytes)
    {
        if (bytes <= 0) return;

        _abandoned += bytes;
        if (_abandoned >= ThresholdBytes) Now();
    }

    /// <summary>
    /// Collects what has been abandoned, if it is worth a collection, and starts counting again.
    /// </summary>
    internal void Now()
    {
        if (_abandoned < ThresholdBytes) return;

        _abandoned = 0;
        Collections++;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
    }
}
