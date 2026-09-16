namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What a source states about the licence of a bundle. The library reports this and applies no rule of
/// its own: whether a set of model files may be used, shipped or published is the consumer's decision,
/// and nothing here refuses a pull over a licence or the absence of one.
/// </summary>
public sealed class LicenseRecord
{
    private static readonly LicenseRecord Nothing = new LicenseRecord(null, null, null);

    /// <summary>
    /// Initializes a record that states a licence identifier and nothing else.
    /// </summary>
    /// <param name="licenseId">The SPDX identifier, for example <c>apache-2.0</c>.</param>
    public LicenseRecord(string licenseId)
        : this(licenseId, null, null)
    {
    }

    /// <summary>
    /// Initializes a record.
    /// </summary>
    /// <param name="licenseId">
    /// The SPDX identifier the source states, for example <c>apache-2.0</c> or <c>mit</c>, or
    /// <see langword="null"/> when the source states none.
    /// </param>
    /// <param name="licenseSource">
    /// The address the statement was read from - a repository page, a licence file - or
    /// <see langword="null"/>.
    /// </param>
    /// <param name="note">
    /// Anything a human should know that the identifier does not say, or <see langword="null"/>.
    /// </param>
    public LicenseRecord(string licenseId, string licenseSource, string note)
    {
        LicenseId = string.IsNullOrWhiteSpace(licenseId) ? null : licenseId.Trim();
        LicenseSource = string.IsNullOrWhiteSpace(licenseSource) ? null : licenseSource.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    /// <summary>
    /// A record that states nothing, which is what a source with no licence information reports.
    /// </summary>
    public static LicenseRecord None
    {
        get { return Nothing; }
    }

    /// <summary>
    /// The SPDX identifier the source states, or <see langword="null"/> when it states none.
    /// </summary>
    public string LicenseId { get; }

    /// <summary>
    /// The address the statement was read from, or <see langword="null"/>.
    /// </summary>
    public string LicenseSource { get; }

    /// <summary>
    /// A free-text note, or <see langword="null"/>.
    /// </summary>
    public string Note { get; }

    /// <summary>Whether the source stated anything at all about the licence.</summary>
    public bool IsStated
    {
        get { return LicenseId != null || LicenseSource != null || Note != null; }
    }

    /// <summary>
    /// Returns the identifier, or a note that nothing is stated.
    /// </summary>
    /// <returns>A one-line description of the record.</returns>
    public override string ToString()
    {
        if (LicenseId != null)
        {
            return LicenseSource == null ? LicenseId : LicenseId + " (" + LicenseSource + ")";
        }
        return LicenseSource == null ? "no licence stated" : "no licence stated (" + LicenseSource + ")";
    }
}
