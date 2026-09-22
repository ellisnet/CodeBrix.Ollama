using System;
using System.IO;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>Fixed architecture data from Microsoft Muzic's MIT-licensed MuseCoco implementation.</summary>
internal static class MuseCocoAssets
{
    internal static string Read(string name)
    {
        using Stream stream = typeof(MuseCocoAssets).Assembly.GetManifestResourceStream(
            "CodeBrix.Ollama.ModelManager.Export.Assets." + name);
        if (stream == null)
        {
            throw new InvalidOperationException("Missing embedded MuseCoco architecture data: " + name);
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
