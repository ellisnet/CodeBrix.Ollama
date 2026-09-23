using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

public sealed class MuseCocoContinuationTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Drivers", "MuseCoco", "Fixtures", "music");
    private static MuseCocoGenerationOptions Greedy(int maximum = 14) => new MuseCocoGenerationOptions
        { MaximumTokens = maximum, MinimumTokens = 0, TopK = 1, Seed = 0 };

    [Fact]
    public async Task GenerateContinuationStreamingAsync_replays_recent_bars_without_reemitting_them()
    {
        //Arrange
        using var model = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture, cancellationToken: TestContext.Current.CancellationToken);
        MuseCocoContinuation continuation = model.CreateContinuation(contextBars: 1);

        //Act
        List<MidiEvent> first = await Read(model, continuation);
        int firstContextCount = continuation.ContextTokenCount;
        int progress = 0;
        List<MidiEvent> second = await Read(model, continuation, new MuseCocoTestProgress(n => progress = n));
        List<MidiEvent> third = await Read(model, continuation);

        //Assert: the fixture selects its output by position. Replaying eight tokens must
        // enter its second bar, proving those tokens reached inference at advancing positions.
        first.Where(e => e.Kind == MidiEventKind.Note).Select(e => e.NoteNumber).Should().Equal(60, 36);
        firstContextCount.Should().Be(8);
        second.Should().ContainSingle().Which.Should().Match<MidiEvent>(e => e.Kind == MidiEventKind.Note
            && e.NoteNumber == 36 && e.Channel == 9 && e.Track == 2 && e.Tick == 3840);
        third.Should().ContainSingle().Which.Tick.Should().Be(5760);
        progress.Should().Be(6, "replayed context is not newly generated output");
        continuation.ContextTokenCount.Should().Be(8);
        continuation.CompletedSections.Should().Be(3);
        continuation.LastGeneratedTokenCount.Should().Be(6);
        continuation.NextTick.Should().Be(7680);
        first.Concat(second).Concat(third).Select(e => e.Tick).Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    public async Task GenerateContinuationStreamingAsync_cleans_and_closes_the_partial_final_bar(int maximum, int notes)
    {
        //Arrange
        using var model = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture, cancellationToken: TestContext.Current.CancellationToken);
        MuseCocoContinuation continuation = model.CreateContinuation(1);

        //Act
        var events = new List<MidiEvent>();
        await foreach (MidiEvent item in model.GenerateContinuationStreamingAsync(continuation,
            options: Greedy(maximum), cancellationToken: TestContext.Current.CancellationToken)) events.Add(item);

        //Assert
        events.Count(e => e.Kind == MidiEventKind.Note).Should().Be(notes);
        continuation.NextTick.Should().Be(1920);
        continuation.Prompt.Select(id => Vocabulary()[id]).Last().Should().Be("b-1");
        continuation.LastGeneratedTokenCount.Should().Be(maximum);
    }

    [Fact]
    public async Task GenerateContinuationStreamingAsync_counts_context_against_position_capacity_without_poisoning_a_rejected_request()
    {
        //Arrange
        using var model = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture, cancellationToken: TestContext.Current.CancellationToken);
        MuseCocoContinuation continuation = model.CreateContinuation(1);
        await Read(model, continuation);
        var options = Greedy(model.MaximumGenerationTokens - continuation.ContextTokenCount + 1);

        //Act
        Func<Task> tooLong = async () =>
        {
            await foreach (MidiEvent item in model.GenerateContinuationStreamingAsync(continuation,
                options: options, cancellationToken: TestContext.Current.CancellationToken)) { }
        };

        //Assert
        await tooLong.Should().ThrowAsync<ArgumentOutOfRangeException>();
        continuation.CompletedSections.Should().Be(1);
        (await Read(model, continuation)).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerateContinuationStreamingAsync_invalidates_interrupted_context_and_releases_the_model(bool cancel)
    {
        //Arrange
        using var model = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture, cancellationToken: TestContext.Current.CancellationToken);
        MuseCocoContinuation continuation = model.CreateContinuation(1);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<MidiEvent> stream = model.GenerateContinuationStreamingAsync(continuation,
            options: Greedy(), cancellationToken: cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        (await stream.MoveNextAsync()).Should().BeTrue();
        Func<Task> busy = () => Read(model, model.CreateContinuation(1));
        await busy.Should().ThrowAsync<InferenceException>();

        //Act
        if (cancel)
        {
            cancellation.Cancel();
            Func<Task> move = async () => { await stream.MoveNextAsync(); };
            await move.Should().ThrowAsync<OperationCanceledException>();
        }
        await stream.DisposeAsync();

        //Assert
        Func<Task> interrupted = () => Read(model, continuation);
        await interrupted.Should().ThrowAsync<InvalidOperationException>().WithMessage("*interrupted*");
        (await Read(model, model.CreateContinuation(1))).Should().NotBeEmpty();
    }

    [Fact]
    public async Task GenerateContinuationStreamingAsync_refuses_context_from_another_model()
    {
        //Arrange
        using var first = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture, cancellationToken: TestContext.Current.CancellationToken);
        using var second = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture, cancellationToken: TestContext.Current.CancellationToken);
        MuseCocoContinuation continuation = first.CreateContinuation();

        //Act
        Func<Task> wrongOwner = () => Read(second, continuation);
        Action tooFew = () => first.CreateContinuation(0);
        Action tooMany = () => first.CreateContinuation(17);

        //Assert
        await wrongOwner.Should().ThrowAsync<ArgumentException>();
        tooFew.Should().Throw<ArgumentOutOfRangeException>();
        tooMany.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Take_restores_inherited_metadata_and_instrument_when_older_bars_are_trimmed()
    {
        //Arrange
        string[] vocabulary = "s-8 t-24 o-0 i-40 p-60 d-6 v-20 b-1 p-64 p-67 i-0".Split(' ');
        var ids = vocabulary.Select((word, id) => (word, id)).ToDictionary(p => p.word, p => p.id);
        int[] tokens = "s-8 t-24 o-0 i-40 p-60 d-6 v-20 b-1 o-0 p-64 d-6 v-20 b-1 o-0 p-67 d-6"
            .Split(' ').Select(w => ids[w]).ToArray();

        //Act
        int[] tail = Remigen2ContextWindow.Take(Array.Empty<int>(), tokens, vocabulary, ids, 2);
        MidiScore score = Remigen2Decoder.Decode(tail, vocabulary);

        //Assert
        tail.Select(id => vocabulary[id]).Should().Equal("s-8", "t-24", "o-0", "i-40", "p-64", "d-6", "v-20", "b-1", "b-1");
        score.Events.Should().Contain(e => e.Kind == MidiEventKind.ProgramChange && e.Program == 40);
        score.Events.Should().ContainSingle(e => e.Kind == MidiEventKind.Note).Which.NoteNumber.Should().Be(64);
    }

    private static async Task<List<MidiEvent>> Read(MuseCocoMusicModel model, MuseCocoContinuation continuation, IProgress<int> progress = null)
    {
        var events = new List<MidiEvent>();
        await foreach (MidiEvent item in model.GenerateContinuationStreamingAsync(continuation,
            options: Greedy(), progress: progress, cancellationToken: TestContext.Current.CancellationToken)) events.Add(item);
        return events;
    }

    private static string[] Vocabulary() => System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(Fixture, "vocabulary.json")));
}
