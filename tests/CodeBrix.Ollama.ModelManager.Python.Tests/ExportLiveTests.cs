using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager.Tests;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>
/// The tests that really export real models: SkyTNT's published ONNX pair passed through into the first
/// derived bundle this library ever writes, and the MuPT 190M checkpoint converted by the ONNX Runtime
/// GenAI model builder at two precisions. Each one checks the derived bundle the way a consumer would -
/// it lists, it shows its provenance, it resolves and it materializes - and then removes it.
/// </summary>
/// <remarks>
/// <para>
/// TWO GATES, because these tests need both what the live tests need and what this project needs:
/// CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1 for the download and CODEBRIX_OLLAMA_RUN_PYTHON_TESTS=1 for the
/// interpreter. The models are pulled into the test-model cache and KEPT there, so only the first run
/// pays for the download; the derived bundles are removed at the end of each test.
/// </para>
/// <para>
/// A conversion writes gigabytes into the system temporary directory and reads them back. On a machine
/// whose temporary directory is in memory - which is most Linux desktops - point TMPDIR at a real file
/// system before opening these gates.
/// </para>
/// </remarks>
public sealed class ExportLiveTests
{
    /// <summary>The assembly's one interpreter, taken so that the gate and the environment agree.</summary>
    private readonly PythonTestFixture _fixture;

    /// <summary>Where the sizes and durations of the exports are written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Takes the assembly's interpreter and the output the measurements go to.
    /// </summary>
    /// <param name="fixture">The assembly fixture.</param>
    /// <param name="output">Where each export reports its size and duration.</param>
    public ExportLiveTests(PythonTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task the_publishers_onnx_pair_becomes_a_derived_bundle_with_the_same_bytes()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        MusicModel model = MusicModelDefinitions.SkyTntMidiModelTv2oMediumOnnxOnly;
        string sourceName = await ExportTestStore.EnsureAsync(store, model, cancellationToken);
        ResolvedModel source = await store.ResolveAsync(sourceName, cancellationToken);
        await ExportTestStore.RemoveAsync(store, DerivedName(sourceName), cancellationToken);

        //Act
        Stopwatch stopwatch = Stopwatch.StartNew();
        ExportResult result = await store.ExportToOnnxAsync(sourceName, null, null, cancellationToken);
        stopwatch.Stop();

        try
        {
            //Assert
            result.RouteUsed.Should().Be(ExportRoute.PublisherOnnx);
            result.Tool.Should().Be("publisher");
            result.Name.Should().Be(DerivedName(sourceName));

            ResolvedModel derived = await store.ResolveAsync(result.Name, cancellationToken);
            derived.Files.Select(file => file.Name).Should().Equal(
                "README.md", "config.json", "generation_config.json",
                "onnx/model_base.onnx", "onnx/model_token.onnx");

            foreach (ResolvedFile file in derived.Files)
            {
                ResolvedFile original = source.Files.Single(candidate => candidate.Name == file.Name);
                file.Size.Should().Be(original.Size);
                file.Digest.Should().Be(original.Digest);
                new FileInfo(file.BlobPath).Length.Should().Be(original.Size);
            }

            foreach (ExpectedBundleFile expected in model.ExpectedFiles)
            {
                ResolvedFile file = derived.Files.Single(candidate => candidate.Name == expected.Path);
                file.Digest.Should().Be("sha256:" + expected.Sha256);
            }

            ModelInfo info = await store.ShowAsync(result.Name, cancellationToken);
            info.DerivedFrom.Should().Be(StoredName(sourceName));
            info.Tool.Should().Be("publisher");
            info.Format.Should().Be("onnx");
            info.License.LicenseId.Should().Be("apache-2.0");
            info.Settings["route"].Should().Be("PublisherOnnx");

            (await store.ListAsync(cancellationToken)).Select(summary => summary.DisplayName)
                .Should().Contain(result.Name);

            using var materialized = new TempExportDirectory();
            IReadOnlyList<string> written = await store.MaterializeAsync(
                result.Name, materialized.DirectoryPath, null, cancellationToken);
            written.Should().HaveCount(5);
            new FileInfo(Path.Combine(materialized.DirectoryPath, "onnx", "model_base.onnx")).Length
                .Should().Be(821713887L);

            Report("SkyTNT pass-through", ExportTestStore.TotalBytes(derived.Files), stopwatch.Elapsed);
        }
        finally
        {
            await ExportTestStore.RemoveAsync(store, result.Name, cancellationToken);
        }
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public Task the_mupt_checkpoint_is_exported_by_the_genai_builder_at_fp32()
        => ExportMuPtAsync("fp32", "hf.co/m-a-p/MuPT-v1-8192-190M:onnx-fp32");

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public Task the_mupt_checkpoint_is_exported_by_the_genai_builder_at_int4()
        => ExportMuPtAsync("int4", "hf.co/m-a-p/MuPT-v1-8192-190M:onnx-int4");

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task the_mupt_checkpoint_is_exported_by_optimum_as_the_fallback_route()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        string sourceName = await ExportTestStore.EnsureAsync(
            store, MusicModelDefinitions.MuPtV1_190M, cancellationToken);
        const string outputName = "hf.co/m-a-p/MuPT-v1-8192-190M:onnx-optimum";
        await ExportTestStore.RemoveAsync(store, outputName, cancellationToken);
        var options = new ExportOptions
        {
            Route = ExportRoute.Optimum,
            OutputName = outputName,
            AllowRemoteCode = true,
            Overwrite = true
        };

        //Act
        Stopwatch stopwatch = Stopwatch.StartNew();
        ExportResult result = await store.ExportToOnnxAsync(
            sourceName, options, null, cancellationToken);
        stopwatch.Stop();

        try
        {
            //Assert
            result.RouteUsed.Should().Be(ExportRoute.Optimum);
            result.Tool.Should().Be("optimum");
            result.ToolVersion.Should().NotBeNullOrEmpty();
            result.Files.Should().Contain("model.onnx");

            ModelInfo info = await store.ShowAsync(outputName, cancellationToken);
            info.Tool.Should().Be("optimum");
            info.DerivedFrom.Should().Be(StoredName(sourceName));
            info.Format.Should().Be("onnx");

            ResolvedModel derived = await store.ResolveAsync(outputName, cancellationToken);
            Report(
                "MuPT 190M through Optimum (" + result.ToolVersion + ")",
                ExportTestStore.TotalBytes(derived.Files),
                stopwatch.Elapsed);
            _output.WriteLine("files: " + string.Join(", ", result.Files));
        }
        finally
        {
            await ExportTestStore.RemoveAsync(store, outputName, cancellationToken);
        }
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task the_automatic_route_chooses_the_builder_for_the_mupt_checkpoint()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        string sourceName = await ExportTestStore.EnsureAsync(
            store, MusicModelDefinitions.MuPtV1_190M, cancellationToken);
        ResolvedModel source = await store.ResolveAsync(sourceName, cancellationToken);
        string configJson = await File.ReadAllTextAsync(
            source.Files.Single(file => file.Name == "config.json").BlobPath, cancellationToken);

        //Act
        ExportRoute route = OnnxExport.ChooseRoute(source.Files, configJson);

        //Assert
        route.Should().Be(ExportRoute.GenAiBuilder);
        OnnxExport.ReadArchitectures(configJson).Should().Contain("LlamaForCausalLM");
    }

    /// <summary>
    /// Exports the MuPT checkpoint with the GenAI builder at one precision and checks the derived bundle
    /// it wrote. The builder meets the checkpoint's own tokenizer class here, so remote code is allowed:
    /// this is the one test subject that needs it, and what it means is written down beside the option.
    /// </summary>
    /// <param name="precision">The precision to export at.</param>
    /// <param name="outputName">The name the derived bundle is stored under.</param>
    /// <returns>A task that completes when the derived bundle has been checked and removed.</returns>
    private async Task ExportMuPtAsync(string precision, string outputName)
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        string sourceName = await ExportTestStore.EnsureAsync(
            store, MusicModelDefinitions.MuPtV1_190M, cancellationToken);
        await ExportTestStore.RemoveAsync(store, outputName, cancellationToken);
        var options = new ExportOptions
        {
            Route = ExportRoute.GenAiBuilder,
            Precision = precision,
            OutputName = outputName,
            AllowRemoteCode = true,
            Overwrite = true
        };
        var statuses = new List<string>();
        var progress = new Progress<PullProgress>(report => statuses.Add(report.Status));

        //Act
        Stopwatch stopwatch = Stopwatch.StartNew();
        ExportResult result = await store.ExportToOnnxAsync(
            sourceName, options, progress, cancellationToken);
        stopwatch.Stop();

        try
        {
            //Assert
            result.RouteUsed.Should().Be(ExportRoute.GenAiBuilder);
            result.Tool.Should().Be("onnxruntime-genai");
            result.ToolVersion.Should().NotBeNullOrEmpty();
            result.Name.Should().Be(outputName);
            result.Files.Should().Contain("model.onnx");
            result.Files.Should().Contain("genai_config.json");

            ModelInfo info = await store.ShowAsync(outputName, cancellationToken);
            info.DerivedFrom.Should().Be(StoredName(sourceName));
            info.Tool.Should().Be("onnxruntime-genai");
            info.ToolVersion.Should().Be(result.ToolVersion);
            info.Settings["precision"].Should().Be(precision);
            info.Settings["allowRemoteCode"].Should().Be("true");
            info.Format.Should().Be("onnx");
            info.License.LicenseId.Should().Be("apache-2.0");

            ResolvedModel derived = await store.ResolveAsync(outputName, cancellationToken);
            derived.Files.Single(file => file.Name == "model.onnx").Size.Should().BeGreaterThan(0);

            using var materialized = new TempExportDirectory();
            IReadOnlyList<string> written = await store.MaterializeAsync(
                outputName, materialized.DirectoryPath, null, cancellationToken);
            written.Should().HaveCount(result.Files.Count);
            File.Exists(Path.Combine(materialized.DirectoryPath, "model.onnx")).Should().BeTrue();

            Report(
                "MuPT 190M " + precision + " (" + result.ToolVersion + ")",
                ExportTestStore.TotalBytes(derived.Files),
                stopwatch.Elapsed);
            _output.WriteLine("files: " + string.Join(", ", result.Files));
        }
        finally
        {
            await ExportTestStore.RemoveAsync(store, outputName, cancellationToken);
        }
    }

    /// <summary>
    /// The name a model is stored under, which is the name a derived bundle records having come from:
    /// a definition name may leave the tag out, and the store never does.
    /// </summary>
    /// <param name="name">The name as the definition writes it.</param>
    /// <returns>The stored name.</returns>
    private static string StoredName(string name) => ModelName.Parse(name).DisplayShortest();

    /// <summary>
    /// The name an export gives a derived bundle when the caller names none.
    /// </summary>
    /// <param name="sourceName">The source model name.</param>
    /// <returns>The derived name.</returns>
    private static string DerivedName(string sourceName)
    {
        ModelName source = ModelName.Parse(sourceName);
        return new ModelName(source.Host, source.Namespace, source.Model, "onnx", source.ProtocolScheme)
            .DisplayShortest();
    }

    /// <summary>
    /// Writes one export's size and duration where the run's output keeps it.
    /// </summary>
    /// <param name="what">What was exported.</param>
    /// <param name="bytes">The size of what was written.</param>
    /// <param name="elapsed">How long it took.</param>
    private void Report(string what, long bytes, TimeSpan elapsed)
        => _output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0}: {1} bytes ({2:F1} MiB) in {3:F1} s",
            what,
            bytes,
            bytes / (1024.0 * 1024.0),
            elapsed.TotalSeconds));
}
