using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

#pragma warning disable xUnit1051

namespace CodeBrix.Ollama.ModelRunner.Tests;

public sealed class MuseCocoModelTests
{
    private static string Fixture(string kind) => Path.Combine(AppContext.BaseDirectory, "Drivers", "MuseCoco", "Fixtures", kind);
    private static MuseCocoGenerationOptions Greedy(int maximum = 20) => new MuseCocoGenerationOptions
        { MaximumTokens = maximum, MinimumTokens = 0, TopK = 1, Seed = 0 };

    [Fact]
    public async Task Reusing_recurrent_state_buffers_preserves_generation_and_request_isolation()
    {
        using MuseCocoMusicModel reused = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture("music"),
            new OnnxRunnerOptions { ReuseBuffers = true }, TestContext.Current.CancellationToken);
        using MuseCocoMusicModel allocated = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture("music"),
            new OnnxRunnerOptions { ReuseBuffers = false }, TestContext.Current.CancellationToken);
        var options = new MuseCocoGenerationOptions { MaximumTokens = 14, MinimumTokens = 14, Seed = 812, TopK = 15 };
        var expected = await allocated.GenerateAsync(options: options, cancellationToken: TestContext.Current.CancellationToken);
        var first = await reused.GenerateAsync(options: options, cancellationToken: TestContext.Current.CancellationToken);
        var second = await reused.GenerateAsync(options: options, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(expected.TokenIds, first.TokenIds);
        Assert.Equal(first.TokenIds, second.TokenIds);
    }

    [Fact]
    public async Task WordPiece_matches_the_publishers_tokenizer_on_Unicode_special_tokens_and_truncation()
    {
        using JsonDocument tokenizerJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Fixture("text"), "wordpiece.json")));
        var tokenizer = new BertWordPieceTokenizer(tokenizerJson.RootElement);
        using JsonDocument cases = JsonDocument.Parse(await File.ReadAllTextAsync(Fixture("tokenizer-oracle.json")));
        foreach (JsonElement row in cases.RootElement.EnumerateArray())
        {
            string text = row.GetProperty("text").GetString();
            BertWordPieceEncoding actual = tokenizer.Encode(text, 512, TestContext.Current.CancellationToken);
            long[] expected = row.GetProperty("tokens").EnumerateArray().Select(v => v.GetInt64()).ToArray();
            Assert.True(expected.SequenceEqual(actual.Ids), "Tokenizer disagreement for: " + text.Substring(0, Math.Min(text.Length, 120)));
            Assert.Equal(row.GetProperty("truncated").GetBoolean(), actual.Truncated);
        }
    }

    [Fact]
    public async Task Text_predictions_can_be_inspected_edited_and_used_for_MIDI_generation()
    {
        using MuseCocoTextModel text = await MuseCocoTextModel.LoadFromDirectoryAsync(Fixture("text"));
        using MuseCocoMusicModel music = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture("music"));
        MusicAttributePrediction prediction = await text.PredictAsync("A slow piano melody in a major key.");
        Assert.True(music.Schema.IsCompatibleWith(text.Schema));
        Assert.Equal("present", prediction.Attributes.Values["instrument.piano"]);
        Assert.Equal("slow", prediction.Attributes.Values["tempo"]);
        Assert.Equal(60, prediction.Probabilities.Count);
        Assert.All(prediction.Probabilities.Values, values => Assert.InRange(values.Sum(v => (double)v), .999999, 1.000001));
        MusicAttributes edited = prediction.Attributes.With("tempo", "moderate").With("structure", "AB");
        Assert.Equal("slow", prediction.Attributes.Values["tempo"]);
        Assert.Equal("moderate", edited.Values["tempo"]);
        MuseCocoGenerationResult result = await music.GenerateAsync(edited, Greedy());
        Assert.Same(edited, result.Attributes);
        Assert.True(result.EndedWithEos);
        using JsonDocument expected = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Fixture("music"), "expected.json")));
        Assert.Equal(expected.RootElement.GetProperty("tokens").EnumerateArray().Select(v => v.GetInt32()), result.TokenIds);
        MidiEvent[] notes = result.Score.Events.Where(e => e.Kind == MidiEventKind.Note).ToArray();
        Assert.Equal(2, notes.Length);
        Assert.Equal((0L, 0, 60, 480L, 82), (notes[0].Tick, notes[0].Channel, notes[0].NoteNumber, notes[0].DurationTicks, notes[0].Velocity));
        Assert.Equal((1920L, 9, 36, 240L, 98), (notes[1].Tick, notes[1].Channel, notes[1].NoteNumber, notes[1].DurationTicks, notes[1].Velocity));
        Assert.Equal(480, result.Score.TicksPerQuarterNote);
        byte[] midi = MidiFile.Write(result.Score);
        Assert.Equal(new byte[] { 77, 84, 104, 100 }, midi.Take(4));
    }

    [Theory]
    [InlineData(OnnxKernelPath.Scalar)]
    [InlineData(OnnxKernelPath.Vector)]
    public async Task Portable_paths_run_both_models(OnnxKernelPath path)
    {
        var options = new OnnxRunnerOptions { KernelPath = path, Threads = 1 };
        using MuseCocoTextModel text = await MuseCocoTextModel.LoadFromDirectoryAsync(Fixture("text"), options);
        using MuseCocoMusicModel music = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture("music"), options);
        MusicAttributePrediction prediction = await text.PredictAsync("");
        Assert.Equal("major", prediction.Attributes.Values["key"]);
        MuseCocoGenerationResult result = await music.GenerateAsync(prediction.Attributes, Greedy());
        Assert.Equal(2, result.Score.Events.Count(e => e.Kind == MidiEventKind.Note));
    }

    [Fact]
    public async Task File_maps_load_both_models_without_a_model_store_dependency()
    {
        IReadOnlyDictionary<string, string> Files(string kind) => Directory.GetFiles(Fixture(kind))
            .ToDictionary(Path.GetFileName, p => p, StringComparer.Ordinal);
        using MuseCocoTextModel text = await MuseCocoTextModel.LoadFromFilesAsync(Files("text"));
        using MuseCocoMusicModel music = await MuseCocoMusicModel.LoadFromFilesAsync(Files("music"));
        Assert.Equal(14, (await music.GenerateAsync((await text.PredictAsync("piano")).Attributes, Greedy())).TokenIds.Count);
    }

    [Fact]
    public async Task Fixed_seed_repeats_and_every_request_begins_with_fresh_state()
    {
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture("music"));
        var options = new MuseCocoGenerationOptions { MaximumTokens = 20, MinimumTokens = 0, Seed = 20260921, TopK = 2 };
        MuseCocoGenerationResult first = await model.GenerateAsync(options: options);
        await model.GenerateAsync(options: Greedy(7));
        MuseCocoGenerationResult second = await model.GenerateAsync(options: options);
        Assert.Equal(first.TokenIds, second.TokenIds);
        Assert.Equal(20260921, first.Seed);
        options.Seed = null;
        MuseCocoGenerationResult fresh1 = await model.GenerateAsync(options: options);
        MuseCocoGenerationResult fresh2 = await model.GenerateAsync(options: options);
        Assert.NotEqual(fresh1.Seed, fresh2.Seed);
        options.Seed = fresh1.Seed;
        Assert.Equal(fresh1.TokenIds, (await model.GenerateAsync(options: options)).TokenIds);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    [InlineData(13, 2)]
    public async Task Token_limits_cleanup_incomplete_notes_like_the_publisher(int tokens, int notes)
    {
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture("music"));
        MuseCocoGenerationResult result = await model.GenerateAsync(options: Greedy(tokens));
        Assert.False(result.EndedWithEos);
        Assert.Equal(tokens, result.TokenIds.Count);
        Assert.Equal(notes, result.Score.Events.Count(e => e.Kind == MidiEventKind.Note));
    }

    [Fact]
    public async Task Cancellation_mid_generation_releases_the_instance_and_discards_partial_state()
    {
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture("music"));
        using var cancellation = new CancellationTokenSource();
        var progress = new MuseCocoTestProgress(count => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.GenerateAsync(options: Greedy(), progress: progress,
            cancellationToken: cancellation.Token));
        Assert.Equal(14, (await model.GenerateAsync(options: Greedy())).TokenIds.Count);
        using MuseCocoTextModel text = await MuseCocoTextModel.LoadFromDirectoryAsync(Fixture("text"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => text.PredictAsync("piano", cancellation.Token));
        Assert.Equal("present", (await text.PredictAsync("piano")).Attributes.Values["instrument.piano"]);
    }

    [Fact]
    public async Task Concurrent_requests_and_disposal_during_generation_are_refused()
    {
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture("music"));
        using var release = new ManualResetEventSlim();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new MuseCocoTestProgress(count =>
        {
            if (count != 1) return;
            reached.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test barrier was not released.");
        });
        Task<MuseCocoGenerationResult> active = model.GenerateAsync(options: Greedy(), progress: progress);
        try
        {
            await reached.Task.WaitAsync(TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InferenceException>(() => model.GenerateAsync(options: Greedy()));
            Assert.Throws<InvalidOperationException>(() => model.Dispose());
        }
        finally
        {
            release.Set();
            await active;
        }
        model.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.GenerateAsync(options: Greedy()));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(4294967295L)]
    public void Reserved_and_negative_seeds_are_rejected(long seed)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new MuseCocoGenerationOptions { Seed = seed });

    [Fact]
    public async Task Invalid_attributes_options_prompts_and_bundle_contracts_are_rejected()
    {
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture("music"));
        MusicAttributes attributes = model.Schema.CreateAttributes();
        Assert.Throws<ArgumentException>(() => attributes.With("tempo", "loud"));
        Assert.Throws<ArgumentException>(() => attributes.With("unknown", "slow"));
        Assert.Throws<ArgumentOutOfRangeException>(() => attributes.WithIndex("tempo", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => model.DecodeTokens(new[] { int.MaxValue }));
        var invalid = Greedy();
        invalid.Temperature = double.NaN;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => model.GenerateAsync(options: invalid));
        invalid = Greedy(); invalid.MinimumTokens = 21;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => model.GenerateAsync(options: invalid));
        invalid = Greedy(); invalid.MaximumTokens = model.MaximumGenerationTokens + 1;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => model.GenerateAsync(options: invalid));
        await Assert.ThrowsAsync<ModelLoadException>(() => MuseCocoTextModel.LoadFromDirectoryAsync(Fixture("music")));
        await Assert.ThrowsAsync<ModelLoadException>(() => MuseCocoMusicModel.LoadFromFilesAsync(new Dictionary<string, string>()));
        using MuseCocoTextModel text = await MuseCocoTextModel.LoadFromDirectoryAsync(Fixture("text"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => text.PredictAsync(null));
        Assert.True((await text.PredictAsync(string.Concat(Enumerable.Repeat("piano ", 600)))).WasTruncated);
        text.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => text.PredictAsync("piano"));
    }
}
