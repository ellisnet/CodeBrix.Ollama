using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeBrix.Ollama.ModelManager;
using ModelQueryTool.ModelAccess.Models;

namespace ModelQueryTool.ModelAccess.Tests;

/// <summary>
/// One throw-away workstation: a folder under the system temporary directory that does not exist until
/// something creates it, and a registry in memory that serves a model of a few kilobytes under a name of
/// its own.
/// </summary>
/// <remarks>
/// Nothing here touches the folder the application really uses, and nothing here reaches the network.
/// The folder is removed when the harness is disposed, whatever the test did to it.
/// </remarks>
public sealed class TinyModelHarness : IDisposable
{
    /// <summary>The name the in-memory registry serves the model under.</summary>
    public const string ModelName = "hf.co/codebrix-testing/tiny-test-model:test";

    /// <summary>The repository part of that name, as the registry files it under.</summary>
    public const string Repository = "codebrix-testing/tiny-test-model";

    /// <summary>The tag part of that name.</summary>
    public const string Tag = "test";

    /// <summary>The name to show a person, which the descriptor carries.</summary>
    public const string DisplayName = "Tiny test model";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly List<ModelStager> _stagers = new List<ModelStager>();

    /// <summary>
    /// Builds the model, registers it and picks a folder that does not exist yet.
    /// </summary>
    public TinyModelHarness()
    {
        RootDirectory = Path.Combine(
            Path.GetTempPath(),
            "mqt-modelaccess-" + Guid.NewGuid().ToString("N"));
        StoreDirectory = Path.Combine(RootDirectory, "models");

        Registry = new InMemoryRegistryHandler();

        WeightsBytes = TinyGgufBuilder.BuildWeights("tiny-test-model", 4096);
        ProjectorBytes = TinyGgufBuilder.BuildProjector("tiny-test-projector", 1024);
        ConfigBytes = Encoding.UTF8.GetBytes(
            "{\"model_format\":\"gguf\",\"model_family\":\"llama\",\"model_families\":[\"llama\"],"
            + "\"model_type\":\"1M\",\"file_type\":\"Q4_K_M\"}");

        WeightsDigest = Registry.AddFile(WeightsBytes);
        ProjectorDigest = Registry.AddFile(ProjectorBytes);
        ConfigDigest = Registry.AddFile(ConfigBytes);

        var manifest = new ModelManifest
        {
            Config = new ModelLayer(MediaTypes.Config, ConfigDigest, ConfigBytes.Length)
        };
        manifest.Layers.Add(new ModelLayer(MediaTypes.Model, WeightsDigest, WeightsBytes.Length));
        manifest.Layers.Add(new ModelLayer(MediaTypes.Projector, ProjectorDigest, ProjectorBytes.Length));

        ManifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions);
        Registry.AddManifest(Repository, Tag, ManifestBytes);

        TotalBytes = WeightsBytes.Length + ProjectorBytes.Length + ConfigBytes.Length;
    }

    /// <summary>Gets the registry that exists only in memory.</summary>
    public InMemoryRegistryHandler Registry { get; }

    /// <summary>Gets the folder that stands in for the one the application owns.</summary>
    public string RootDirectory { get; }

    /// <summary>Gets the store folder inside it.</summary>
    public string StoreDirectory { get; }

    /// <summary>Gets the bytes the registry serves as the model's weights.</summary>
    public byte[] WeightsBytes { get; }

    /// <summary>Gets the bytes the registry serves as the model's projector.</summary>
    public byte[] ProjectorBytes { get; }

    /// <summary>Gets the bytes the registry serves as the model's configuration.</summary>
    public byte[] ConfigBytes { get; }

    /// <summary>Gets the exact manifest bytes the registry serves.</summary>
    public byte[] ManifestBytes { get; }

    /// <summary>Gets the digest of the weights, as a manifest spells one.</summary>
    public string WeightsDigest { get; }

    /// <summary>Gets the digest of the projector, as a manifest spells one.</summary>
    public string ProjectorDigest { get; }

    /// <summary>Gets the digest of the configuration, as a manifest spells one.</summary>
    public string ConfigDigest { get; }

    /// <summary>Gets what the whole model comes to: both files and the configuration.</summary>
    public long TotalBytes { get; }

    /// <summary>Gets where the store keeps the weights once they have been staged.</summary>
    public string WeightsBlobPath
    {
        get { return GetBlobPath(WeightsDigest); }
    }

    /// <summary>Gets where the store keeps the projector once it has been staged.</summary>
    public string ProjectorBlobPath
    {
        get { return GetBlobPath(ProjectorDigest); }
    }

    /// <summary>Gets where the store keeps the manifest once the model has been staged.</summary>
    public string ManifestPath
    {
        get
        {
            return Path.Combine(
                StoreDirectory, "manifests", "hf.co", "codebrix-testing", "tiny-test-model", "test");
        }
    }

    /// <summary>
    /// Describes the model this harness serves.
    /// </summary>
    /// <param name="expectedTotalBytes">
    /// What to expect the whole model to come to; the real total when it is not given.
    /// </param>
    /// <param name="weightsSha256">
    /// The digest to expect of the weights, or <see langword="null"/> to expect none. The real one when
    /// it is not given.
    /// </param>
    /// <returns>The descriptor.</returns>
    public ModelDescriptor CreateDescriptor(long expectedTotalBytes = -1L, string weightsSha256 = null)
    {
        return new ModelDescriptor(
            ModelName,
            DisplayName,
            expectedTotalBytes < 0L ? TotalBytes : expectedTotalBytes,
            weightsSha256 ?? StripPrefix(WeightsDigest));
    }

    /// <summary>
    /// Describes the model this harness serves, expecting no particular digest of its weights.
    /// </summary>
    /// <returns>The descriptor.</returns>
    public ModelDescriptor CreateUnpinnedDescriptor()
    {
        return new ModelDescriptor(ModelName, DisplayName, TotalBytes, null);
    }

    /// <summary>
    /// Creates a stager on this harness's folder and registry, and keeps it to be disposed afterwards.
    /// </summary>
    /// <param name="model">The model to stage, or <see langword="null"/> for this harness's own.</param>
    /// <returns>The stager.</returns>
    public ModelStager CreateStager(ModelDescriptor model = null)
    {
        var stager = new ModelStager(
            new ModelStagerOptions
            {
                Model = model ?? CreateDescriptor(),
                RootDirectory = RootDirectory
            },
            Registry);

        _stagers.Add(stager);
        return stager;
    }

    /// <summary>
    /// Where the store keeps the file a digest names.
    /// </summary>
    /// <param name="digest">The digest, as a manifest spells one.</param>
    /// <returns>The absolute path of the file.</returns>
    public string GetBlobPath(string digest)
    {
        return Path.Combine(StoreDirectory, "blobs", "sha256-" + StripPrefix(digest));
    }

    /// <summary>
    /// A digest without its <c>sha256:</c> prefix.
    /// </summary>
    /// <param name="digest">The digest, as a manifest spells one.</param>
    /// <returns>The hexadecimal characters alone.</returns>
    public static string StripPrefix(string digest)
    {
        return digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest.Substring(7) : digest;
    }

    /// <summary>Disposes every stager it handed out, the registry, and the throw-away folder.</summary>
    public void Dispose()
    {
        foreach (ModelStager stager in _stagers)
        {
            stager.Dispose();
        }

        Registry.Dispose();

        try
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
