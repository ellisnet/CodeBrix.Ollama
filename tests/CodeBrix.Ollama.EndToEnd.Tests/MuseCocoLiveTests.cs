using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;
using CodeBrix.Ollama.ModelRunner;
using Xunit;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>Real local checkpoints: public staging, independent quantization and managed MIDI generation.</summary>
public sealed class MuseCocoLiveTests
{
    private readonly ITestOutputHelper _output;

    public MuseCocoLiveTests(ITestOutputHelper output) => _output = output;

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests, TestGates.RunMuseCocoTests })]
    public async Task Original_checkpoints_stage_reduce_and_generate_at_all_three_precisions()
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        string musicSource = Require("CODEBRIX_OLLAMA_MUSECOCO_MUSIC_SOURCE");
        string textSource = Require("CODEBRIX_OLLAMA_MUSECOCO_TEXT_SOURCE");
        string root = Require("CODEBRIX_OLLAMA_MUSECOCO_TEST_DIRECTORY");
        Directory.CreateDirectory(root);
        using var store = new ModelStore(new ModelStoreOptions
        {
            StoreDirectory = Path.Combine(root, "store"),
            Python = new PythonOptions { VirtualEnvironment = TestGates.RequireVirtualEnvironment() }
        });
        foreach (string kind in new[] { "music", "text" })
        {
            string source = "local/musecoco-" + kind + ":source";
            string full = "local/musecoco-" + kind + ":fp32";
            await store.ImportBundleAsync(source, kind == "music" ? musicSource : textSource,
                new ImportOptions { Link = true }, cancellationToken: cancel);
            ExportResult export = await store.ExportToOnnxAsync(source, new ExportOptions
            {
                Route = kind == "music" ? ExportRoute.MuseCocoMusic : ExportRoute.MuseCocoText,
                OutputName = full, Overwrite = true
            }, cancellationToken: cancel);
            Assert.Contains("musecoco.json", export.Files);
            Assert.Contains("music-attributes.json", export.Files);
            await store.MaterializeAsync(full, Path.Combine(root, kind + "-fp32"),
                new MaterializeOptions { Link = MaterializeLink.Hardlink, Overwrite = true }, cancellationToken: cancel);
            foreach (string precision in new[] { "int8", "int4" })
            {
                ReduceResult reduced = await store.ReduceOnnxAsync(full, new ReduceOptions
                {
                    Engine = ReduceEngine.Managed,
                    Mode = precision == "int8" ? ReduceMode.WeightOnlyInt8 : ReduceMode.WeightOnlyInt4,
                    BlockSize = 128, OutputName = "local/musecoco-" + kind + ":" + precision, Overwrite = true
                }, cancellationToken: cancel);
                Assert.True(reduced.ReducedBytes < reduced.SourceBytes / 2);
                await store.MaterializeAsync(reduced.Name, Path.Combine(root, kind + "-" + precision),
                    new MaterializeOptions { Link = MaterializeLink.Hardlink, Overwrite = true }, cancellationToken: cancel);
                _output.WriteLine(kind + " " + precision + " graph bytes: " + reduced.ReducedBytes);
            }
        }

        var predictions = new Dictionary<string, MusicAttributes>();
        var runner = new OnnxRunnerOptions { Threads = 4 };
        foreach (string precision in new[] { "fp32", "int8", "int4" })
        {
            using MuseCocoTextModel text = await MuseCocoTextModel.LoadFromDirectoryAsync(
                Path.Combine(root, "text-" + precision), runner, cancel);
            MusicAttributePrediction prediction = await text.PredictAsync("A slow solo piano piece in a major key.", cancel);
            Assert.Equal(60, prediction.Probabilities.Count);
            Assert.False(prediction.WasTruncated);
            Assert.Equal("present", prediction.Attributes.Values["instrument.piano"]);
            MusicAttributePrediction repeated = await text.PredictAsync("A slow solo piano piece in a major key.", cancel);
            Assert.Equal(prediction.Attributes.Values, repeated.Attributes.Values);
            predictions[precision] = prediction.Attributes;
            if (precision != "fp32") Assert.Contains("MatMulNBits", text.Metadata.Operators);
        }

        foreach (string precision in new[] { "fp32", "int8", "int4" })
        {
            using MuseCocoMusicModel music = await MuseCocoMusicModel.LoadFromDirectoryAsync(
                Path.Combine(root, "music-" + precision), runner, cancel);
            // The mixed case deliberately uses INT8 BERT with INT4 music.
            MusicAttributes attributes = predictions[precision == "int4" ? "int8" : precision].With("tempo", "moderate");
            var options = new MuseCocoGenerationOptions { MaximumTokens = 128, MinimumTokens = 128, Seed = 20260921, TopK = 15 };
            var timer = Stopwatch.StartNew();
            MuseCocoGenerationResult result = await music.GenerateAsync(attributes, options, cancellationToken: cancel);
            Assert.Equal(128, result.TokenIds.Count);
            Assert.Contains(result.Score.Events, e => e.Kind == MidiEventKind.Note);
            Assert.All(result.Score.Events.Where(e => e.Kind == MidiEventKind.Note), e =>
            {
                Assert.InRange(e.NoteNumber, 0, 127);
                Assert.InRange(e.Velocity, 1, 127);
                Assert.True(e.DurationTicks > 0);
            });
            await MidiFile.WriteAsync(Path.Combine(root, "generated-" + precision + ".mid"), result.Score, cancel);
            _output.WriteLine(precision + " generation seconds: " + timer.Elapsed.TotalSeconds);
            if (precision == "int4")
            {
                MuseCocoGenerationResult repeated = await music.GenerateAsync(attributes, options, cancellationToken: cancel);
                Assert.Equal(result.TokenIds, repeated.TokenIds);
            }
        }
    }

    private static string Require(string name)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Set " + name + " to a local directory.");
        return Path.GetFullPath(value);
    }
}
