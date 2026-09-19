namespace CodeBrix.Ollama.ModelRunner; //was previously: midi_tokenizer.py@f504d5cb58f769ab0f2909c679238f6621034573

/// <summary>
/// The last note of one pitch on one channel while a track is being read, so that the next note of the same
/// pitch can cut it short - and, when nothing is left of it, say which entry to take out of the collection.
/// </summary>
internal sealed class SkyTntLastNote
{
    /// <summary>Creates the record.</summary>
    /// <param name="key">The key the row is stored under.</param>
    /// <param name="row">The row itself, which is edited in place when it is shortened.</param>
    internal SkyTntLastNote(string key, SkyTntEventRow row)
    {
        Key = key;
        Row = row;
    }

    /// <summary>The key the row is stored under.</summary>
    internal string Key { get; }

    /// <summary>The row itself.</summary>
    internal SkyTntEventRow Row { get; }
}
