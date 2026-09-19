using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One operator set a graph is written against: the domain and the version of it the file declares.
/// </summary>
/// <remarks>
/// The empty domain is the standard <c>ai.onnx</c> set, and it is the one the engine implements. A file may
/// declare further domains - a vendor's contributed operators, for instance - and declaring one is not itself
/// refused: what is refused, when the model is loaded, is a NODE whose operator the engine does not
/// implement, named with its domain.
/// </remarks>
public sealed class OnnxOpsetImport
{
    /// <summary>Builds the description of one declared operator set.</summary>
    /// <param name="domain">The domain, with the empty string for the standard set.</param>
    /// <param name="version">The version of that set.</param>
    internal OnnxOpsetImport(string domain, long version)
    {
        Domain = domain;
        Version = version;
    }

    /// <summary>The domain. The empty string is the standard <c>ai.onnx</c> set.</summary>
    public string Domain { get; }

    /// <summary>The version of that operator set the graph is written against.</summary>
    public long Version { get; }

    /// <summary>Returns the domain and the version.</summary>
    /// <returns>A one-line description, for example <c>ai.onnx 14</c>.</returns>
    public override string ToString()
    {
        string domain = string.IsNullOrEmpty(Domain) ? "ai.onnx" : Domain;
        return domain + " " + Version.ToString(CultureInfo.InvariantCulture);
    }
}
