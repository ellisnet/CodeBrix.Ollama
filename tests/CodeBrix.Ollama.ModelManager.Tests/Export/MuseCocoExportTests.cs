using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

public sealed class MuseCocoExportTests
{
    [Fact]
    public void Custom_BERT_is_detected_and_both_routes_need_only_the_fixed_exporter_modules()
    {
        Assert.Equal(ExportRoute.MuseCocoText, OnnxExport.ChooseRoute(Array.Empty<ResolvedFile>(),
            "{\"architectures\":[\"BertForAttributModel\"]}"));
        Assert.Equal(new[] { "torch", "numpy", "onnx" }, OnnxExport.ModulesFor(ExportRoute.MuseCocoMusic));
        Assert.Equal(new[] { "torch", "numpy", "onnx" }, OnnxExport.ModulesFor(ExportRoute.MuseCocoText));
    }

    [Fact]
    public void Embedded_schema_covers_all_classifier_heads_and_every_music_attribute_token()
    {
        using JsonDocument schema = JsonDocument.Parse(MuseCocoAssets.Read("musecoco-attributes.json"));
        using JsonDocument vocabulary = JsonDocument.Parse(MuseCocoAssets.Read("musecoco-vocabulary.json"));
        string[] words = vocabulary.RootElement.EnumerateArray().Select(v => v.GetString()).ToArray();
        JsonElement[] definitions = schema.RootElement.GetProperty("definitions").EnumerateArray().ToArray();
        Assert.Equal(1253, words.Length);
        Assert.Equal(63, definitions.Length);
        Assert.Equal(Enumerable.Range(0, 60), definitions.Select(d => d.GetProperty("bertHead").GetInt32()).Where(i => i >= 0).Order());
        foreach (JsonElement definition in definitions)
        {
            int count = definition.GetProperty("values").GetArrayLength();
            Assert.Equal(count, definition.GetProperty("tokens").GetArrayLength());
            Assert.InRange(definition.GetProperty("default").GetInt32(), 0, count - 1);
            foreach (JsonElement token in definition.GetProperty("tokens").EnumerateArray()) Assert.Contains(token.GetString(), words);
        }
        Assert.DoesNotContain(typeof(ModelStore).Assembly.GetReferencedAssemblies(), a => a.Name == "CodeBrix.Ollama.ModelRunner");
    }

    [Theory]
    [InlineData(ExportRoute.MuseCocoMusic)]
    [InlineData(ExportRoute.MuseCocoText)]
    public async Task Reduced_precision_is_rejected_before_starting_Python(ExportRoute route)
    {
        using var temporary = new TempStoreDirectory();
        string source = Path.Combine(temporary.DirectoryPath, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "config.json"), "{}", TestContext.Current.CancellationToken);
        using var store = new ModelStore(new ModelStoreOptions { StoreDirectory = Path.Combine(temporary.DirectoryPath, "store") });
        await store.ImportBundleAsync("local/muse:source", source, cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ExportToOnnxAsync("local/muse:source",
            new ExportOptions { Route = route, Precision = "int4" }, cancellationToken: TestContext.Current.CancellationToken));
    }
}
