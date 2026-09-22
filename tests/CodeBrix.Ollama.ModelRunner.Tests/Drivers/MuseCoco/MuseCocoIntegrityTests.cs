using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

public sealed class MuseCocoIntegrityTests
{
    [Theory]
    [InlineData("music", "graph", "../outside.onnx")]
    [InlineData("music", "vocabulary", "missing.json")]
    [InlineData("music", "layers", "24")]
    [InlineData("music", "headSize", "wrong")]
    [InlineData("music", "prefixPositionStart", "1")]
    [InlineData("music", "ticksPerQuarterNote", "96")]
    [InlineData("text", "classifierCount", "59")]
    [InlineData("text", "maxSequenceLength", "1")]
    [InlineData("text", "format", "future-format")]
    public async Task Invalid_manifest_paths_dimensions_and_graph_contracts_are_refused(string kind, string property, string value)
    {
        using var scratch = new TempScratchDirectory();
        Dictionary<string, string> files = Files(kind);
        JsonNode metadata = JsonNode.Parse(await File.ReadAllTextAsync(files["musecoco.json"], TestContext.Current.CancellationToken));
        metadata[property] = int.TryParse(value, out int number) ? JsonValue.Create(number) : JsonValue.Create(value);
        string path = scratch.Combine("musecoco.json");
        await File.WriteAllTextAsync(path, metadata.ToJsonString(), TestContext.Current.CancellationToken);
        files["musecoco.json"] = path;
        await Assert.ThrowsAsync<ModelLoadException>(async () =>
        {
            using IDisposable model = kind == "music"
                ? await MuseCocoMusicModel.LoadFromFilesAsync(files, cancellationToken: TestContext.Current.CancellationToken)
                : await MuseCocoTextModel.LoadFromFilesAsync(files, cancellationToken: TestContext.Current.CancellationToken);
        });
    }

    [Theory]
    [InlineData("text", "wordpiece.json", "[]")]
    [InlineData("music", "vocabulary.json", "{}")]
    [InlineData("music", "music-attributes.json", "{")]
    public async Task Corrupt_companions_report_a_model_load_error(string kind, string name, string json)
    {
        using var scratch = new TempScratchDirectory();
        Dictionary<string, string> files = Files(kind);
        string path = scratch.Combine(name);
        await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);
        files[name] = path;
        await Assert.ThrowsAsync<ModelLoadException>(async () =>
        {
            using IDisposable model = kind == "music"
                ? await MuseCocoMusicModel.LoadFromFilesAsync(files, cancellationToken: TestContext.Current.CancellationToken)
                : await MuseCocoTextModel.LoadFromFilesAsync(files, cancellationToken: TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public void Runner_assembly_has_no_store_Python_or_external_inference_reference()
    {
        string[] references = typeof(MuseCocoMusicModel).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain("CodeBrix.Ollama.ModelManager", references);
        Assert.DoesNotContain("CodeBrix.Python", references);
        Assert.DoesNotContain("Python.Runtime", references);
        Assert.DoesNotContain("Microsoft.ML.OnnxRuntime", references);
    }

    private static Dictionary<string, string> Files(string kind)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Drivers", "MuseCoco", "Fixtures", kind);
        return Directory.EnumerateFiles(folder).ToDictionary(Path.GetFileName, p => p, StringComparer.Ordinal);
    }
}
