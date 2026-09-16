using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// The tests that really download the four families of music-generation models the bundle work was
/// built for: a SkyTNT MIDI model, MuPT, MuseCoco and Magenta's Music Transformer. Each one pulls its
/// definition into a temporary store, checks every file against what the publisher stated on
/// 2026-09-16, lays the tree out on disk again, and removes model and store, so nothing is left on the
/// machine afterwards.
/// </summary>
/// <remarks>
/// <para>
/// Three gates, all test-side: <see cref="MusicModelDefinitions.LiveTestsGate"/> alone opens the three
/// small definitions (about 1.05 GiB together, a few minutes on a domestic connection); the two
/// MuseCoco checkpoints need <see cref="MusicModelDefinitions.MuseCocoTestsGate"/> as well (14.8 GiB);
/// and the seven remaining repositories need
/// <see cref="MusicModelDefinitions.LargeMusicTestsGate"/> (10.4 GiB). The large runs belong in a
/// backgrounded run of the built test executable, with its output kept in a log.
/// </para>
/// <para>
/// What each definition expects is data, not code: the whole procedure lives in one helper and every
/// test here is a single call to it.
/// </para>
/// </remarks>
public sealed class MusicModelLiveTests
{
    private readonly ITestOutputHelper output;

    /// <summary>Creates the test class with the output the download timings are written to.</summary>
    /// <param name="output">Where each pull reports its size, its duration and its rate.</param>
    public MusicModelLiveTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [EnvGatedFact(MusicModelDefinitions.LiveTestsGate)]
    public Task The_skytnt_tv2o_medium_repository_is_pulled_resolved_materialized_and_deleted() =>
        PullResolveMaterializeAndDeleteAsync(MusicModelDefinitions.SkyTntMidiModelTv2oMedium, output);

    [EnvGatedFact(MusicModelDefinitions.LiveTestsGate)]
    public Task The_mupt_v1_190m_repository_is_pulled_resolved_materialized_and_deleted() =>
        PullResolveMaterializeAndDeleteAsync(MusicModelDefinitions.MuPtV1_190M, output);

    [EnvGatedFact(MusicModelDefinitions.LiveTestsGate)]
    public Task The_magenta_unconditional_checkpoint_is_pulled_resolved_materialized_and_deleted() =>
        PullResolveMaterializeAndDeleteAsync(MusicModelDefinitions.MagentaMusicTransformerUnconditional, output);

    [EnvGatedFact(new[] { MusicModelDefinitions.LiveTestsGate, MusicModelDefinitions.MuseCocoTestsGate })]
    public Task The_musecoco_text2attribute_checkpoint_is_pulled_resolved_materialized_and_deleted() =>
        PullResolveMaterializeAndDeleteAsync(MusicModelDefinitions.MuseCocoText2Attribute, output);

    [EnvGatedFact(new[] { MusicModelDefinitions.LiveTestsGate, MusicModelDefinitions.MuseCocoTestsGate })]
    public Task The_musecoco_attribute2music_checkpoint_is_pulled_resolved_materialized_and_deleted() =>
        PullResolveMaterializeAndDeleteAsync(MusicModelDefinitions.MuseCocoAttribute2Music, output);

    [EnvGatedFact(new[] { MusicModelDefinitions.LiveTestsGate, MusicModelDefinitions.LargeMusicTestsGate })]
    public Task The_skytnt_midi_model_repository_is_pulled_resolved_materialized_and_deleted() =>
        PullResolveMaterializeAndDeleteAsync(MusicModelDefinitions.SkyTntMidiModel, output);

    [EnvGatedFact(new[] { MusicModelDefinitions.LiveTestsGate, MusicModelDefinitions.LargeMusicTestsGate })]
    public Task The_skytnt_tv2o_medium_full_repository_is_pulled_resolved_materialized_and_deleted() =>
        PullResolveMaterializeAndDeleteAsync(MusicModelDefinitions.SkyTntMidiModelTv2oMediumFull, output);

    [EnvGatedFact(new[] { MusicModelDefinitions.LiveTestsGate, MusicModelDefinitions.LargeMusicTestsGate })]
    public Task The_skytnt_tv2om_jpop_lora_repository_is_pulled_resolved_materialized_and_deleted() =>
        PullResolveMaterializeAndDeleteAsync(MusicModelDefinitions.SkyTntMidiModelTv2omJpopLora, output);

    [EnvGatedFact(new[] { MusicModelDefinitions.LiveTestsGate, MusicModelDefinitions.LargeMusicTestsGate })]
    public Task The_skytnt_tv2om_touhou_lora_repository_is_pulled_resolved_materialized_and_deleted() =>
        PullResolveMaterializeAndDeleteAsync(MusicModelDefinitions.SkyTntMidiModelTv2omTouhouLora, output);

    [EnvGatedFact(new[] { MusicModelDefinitions.LiveTestsGate, MusicModelDefinitions.LargeMusicTestsGate })]
    public Task The_mupt_v1_1_97b_repository_is_pulled_resolved_materialized_and_deleted() =>
        PullResolveMaterializeAndDeleteAsync(MusicModelDefinitions.MuPtV1_1_97B, output);

    [EnvGatedFact(new[] { MusicModelDefinitions.LiveTestsGate, MusicModelDefinitions.LargeMusicTestsGate })]
    public Task The_magenta_melody_conditioned_checkpoint_is_pulled_resolved_materialized_and_deleted() =>
        PullResolveMaterializeAndDeleteAsync(MusicModelDefinitions.MagentaMusicTransformerMelodyConditioned, output);

    [EnvGatedFact(new[] { MusicModelDefinitions.LiveTestsGate, MusicModelDefinitions.LargeMusicTestsGate })]
    public Task The_magenta_primer_files_are_pulled_resolved_materialized_and_deleted() =>
        PullResolveMaterializeAndDeleteAsync(MusicModelDefinitions.MagentaMusicTransformerPrimers, output);

    /// <summary>
    /// Pulls one definition from its publisher into a temporary store, checks the progress stream, what
    /// the store reports about the result, the blob behind every expected file, the tree a materialize
    /// writes and the listing, then deletes the model and checks that no blob is left. Both temporary
    /// directories are removed however the test ends.
    /// </summary>
    /// <param name="model">The definition to pull and everything the publisher stated about it.</param>
    /// <param name="output">Where the size, the duration and the rate of the download are written.</param>
    /// <returns>A task that completes when the store is empty again.</returns>
    private static async Task PullResolveMaterializeAndDeleteAsync(MusicModel model, ITestOutputHelper output)
    {
        //Arrange
        var storeDirectory = new TempStoreDirectory();
        var materializeDirectory = new TempStoreDirectory();
        try
        {
            using var store = new ModelStore(
                new ModelStoreOptions { StoreDirectory = storeDirectory.DirectoryPath });

            //Act
            var statuses = new List<string>();
            Stopwatch stopwatch = Stopwatch.StartNew();
            await foreach (PullProgress report in store.PullAsync(
                model.Definition.Name,
                model.Definition.ToPullOptions(),
                TestContext.Current.CancellationToken))
            {
                statuses.Add(report.Status);
            }
            stopwatch.Stop();
            double seconds = stopwatch.Elapsed.TotalSeconds;
            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1} bytes in {2:F1} s ({3:F1} MB/s)",
                model.Definition.Name,
                model.ExpectedBytes,
                seconds,
                model.ExpectedBytes / seconds / (1024.0 * 1024.0)));

            //Assert
            statuses[0].Should().StartWith("listing");
            statuses[statuses.Count - 1].Should().Be("success");

            ModelInfo info = await store.ShowAsync(
                model.Definition.Name, TestContext.Current.CancellationToken);
            info.License.LicenseId.Should().Be(model.ExpectedLicenseId);
            info.Format.Should().NotBeNullOrEmpty();
            if (model.ExpectedRevision != null)
            {
                info.Config.AdditionalProperties["revision"].GetString()
                    .Should().Be(model.ExpectedRevision);
            }

            ResolvedModel resolved = await store.ResolveAsync(
                model.Definition.Name, TestContext.Current.CancellationToken);
            resolved.ModelPath.Should().BeNull();
            resolved.Files.Should().HaveCount(model.ExpectedFiles.Count);
            foreach (ExpectedBundleFile expected in model.ExpectedFiles)
            {
                ResolvedFile file = resolved.Files.Single(candidate => candidate.Name == expected.Path);
                file.Size.Should().Be(expected.Size);
                file.Digest.Should().Be("sha256:" + expected.Sha256);
                new FileInfo(file.BlobPath).Length.Should().Be(expected.Size);
            }

            IReadOnlyList<string> written = await store.MaterializeAsync(
                model.Definition.Name,
                materializeDirectory.DirectoryPath,
                null,
                TestContext.Current.CancellationToken);
            written.Should().HaveCount(model.ExpectedFiles.Count);
            foreach (ExpectedBundleFile expected in model.ExpectedFiles)
            {
                string path = Path.Combine(
                    materializeDirectory.DirectoryPath,
                    expected.Path.Replace('/', Path.DirectorySeparatorChar));
                File.Exists(path).Should().BeTrue();
                new FileInfo(path).Length.Should().Be(expected.Size);
            }

            IReadOnlyList<ModelSummary> listed = await store.ListAsync(
                TestContext.Current.CancellationToken);
            listed.Should().HaveCount(1);
            listed[0].DisplayName.Should().Be(ModelName.Parse(model.Definition.Name).DisplayShortest());

            await store.DeleteAsync(model.Definition.Name, TestContext.Current.CancellationToken);
            (await store.ListAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
            Directory.GetFiles(storeDirectory.Paths.BlobsDirectory)
                .Where(path => Path.GetFileName(path).StartsWith("sha256-", StringComparison.Ordinal))
                .Should().BeEmpty();
        }
        finally
        {
            storeDirectory.Dispose();
            materializeDirectory.Dispose();
        }
    }
}
