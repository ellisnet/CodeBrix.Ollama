using System.IO;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Where the Ollama template fixtures live. They are Ollama's own <c>template/testdata</c> copied
/// verbatim and are shipped beside the test assembly.
/// </summary>
internal static class OllamaTemplateFixtures
{
    /// <summary>The directory holding the copied Ollama test data.</summary>
    internal static string Directory
        => Path.Combine(Path.GetDirectoryName(typeof(OllamaTemplateFixtures).Assembly.Location) ?? ".",
            "Fixtures", "OllamaGo");
}
