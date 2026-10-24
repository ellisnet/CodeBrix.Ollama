using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What <see cref="IModelStore.VerifyAsync"/> found when it re-hashed every blob of a stored model: how
/// many blobs and bytes it read, and every blob that is missing, changed or mis-sized.
/// </summary>
public sealed class VerifyResult
{
    /// <summary>
    /// Initializes a result.
    /// </summary>
    /// <param name="name">The model's name, in its shortest display form.</param>
    /// <param name="blobsChecked">How many distinct blobs the manifest names.</param>
    /// <param name="bytesHashed">How many bytes were read and hashed.</param>
    /// <param name="failures">Every blob that did not pass, in manifest order.</param>
    public VerifyResult(string name, int blobsChecked, long bytesHashed, IReadOnlyList<VerifyFailure> failures)
    {
        Name = name;
        BlobsChecked = blobsChecked;
        BytesHashed = bytesHashed;
        Failures = failures ?? Array.Empty<VerifyFailure>();
    }

    /// <summary>The model's name, in its shortest display form.</summary>
    public string Name { get; }

    /// <summary>
    /// How many distinct blobs the manifest names - its content layers and its config - each of them
    /// checked once even when several layers share it.
    /// </summary>
    public int BlobsChecked { get; }

    /// <summary>How many bytes were read and hashed.</summary>
    public long BytesHashed { get; }

    /// <summary>Every blob that did not pass, in manifest order. Empty when the model is intact.</summary>
    public IReadOnlyList<VerifyFailure> Failures { get; }

    /// <summary>Whether every blob is present, hashes to its digest and has the recorded size.</summary>
    public bool IsIntact
    {
        get { return Failures.Count == 0; }
    }

    /// <summary>
    /// Returns the name, the blob count and how many failed.
    /// </summary>
    /// <returns>A one-line description of the result.</returns>
    public override string ToString()
    {
        return Name + ": " + BlobsChecked.ToString(CultureInfo.InvariantCulture) + " blobs, "
            + (IsIntact ? "intact" : Failures.Count.ToString(CultureInfo.InvariantCulture) + " failed");
    }
}
