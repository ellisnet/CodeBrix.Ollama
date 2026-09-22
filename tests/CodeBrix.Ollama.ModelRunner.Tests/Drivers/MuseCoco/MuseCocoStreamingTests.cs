using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

public sealed class MuseCocoStreamingTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Drivers", "MuseCoco", "Fixtures", "music");

    private static MuseCocoGenerationOptions Greedy(int maximum = 20) => new MuseCocoGenerationOptions
        { MaximumTokens = maximum, MinimumTokens = 0, TopK = 1, Seed = 0 };

    [Fact]
    public async Task GenerateStreamingAsync_yields_the_first_bar_before_generating_the_second()
    {
        //Arrange
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(
            Fixture, cancellationToken: TestContext.Current.CancellationToken);
        int generated = 0;
        var progress = new MuseCocoTestProgress(count => generated = count);
        await using IAsyncEnumerator<MidiEvent> stream = model.GenerateStreamingAsync(options: Greedy(), progress: progress, cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        //Act
        while (await stream.MoveNextAsync() && stream.Current.Kind != MidiEventKind.Note) { }

        //Assert
        stream.Current.Kind.Should().Be(MidiEventKind.Note);
        stream.Current.NoteNumber.Should().Be(60);
        stream.Current.DurationTicks.Should().Be(480);
        generated.Should().Be(8, "the first bar closes at token 8, before the 14-token song is finished");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GenerateStreamingAsync_preserves_the_completed_scores_notes_and_metadata(bool reuse)
    {
        //Arrange
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(Fixture,
            new OnnxRunnerOptions { ReuseBuffers = reuse }, TestContext.Current.CancellationToken);
        MuseCocoGenerationResult expected = await model.GenerateAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken);

        //Act
        List<MidiEvent> streamed = await ReadAsync(model.GenerateStreamingAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        Notes(streamed).Should().Equal(Notes(expected.Score.Events));
        Metadata(streamed).Should().BeEquivalentTo(Metadata(expected.Score.Events));
        streamed.Select(e => e.Tick).Should().BeInAscendingOrder();
        streamed.Should().OnlyContain(e => e.HorizonTicks == e.Tick);
        MidiScore saved = MidiFile.Read(MidiFile.Write(new MidiScore(480, streamed)));
        Notes(saved.Events).Should().Equal(Notes(expected.Score.Events));
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    [InlineData(12, 1)]
    [InlineData(13, 2)]
    public async Task GenerateStreamingAsync_flushes_the_final_bar_with_the_same_cutoff_cleanup(int tokens, int notes)
    {
        //Arrange
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(
            Fixture, cancellationToken: TestContext.Current.CancellationToken);
        MuseCocoGenerationResult expected = await model.GenerateAsync(options: Greedy(tokens), cancellationToken: TestContext.Current.CancellationToken);

        //Act
        List<MidiEvent> streamed = await ReadAsync(model.GenerateStreamingAsync(options: Greedy(tokens), cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        Notes(streamed).Should().HaveCount(notes).And.Equal(Notes(expected.Score.Events));
    }

    [Fact]
    public async Task GenerateStreamingAsync_defers_generation_and_holds_the_instance_until_disposed()
    {
        //Arrange
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(
            Fixture, cancellationToken: TestContext.Current.CancellationToken);
        IAsyncEnumerable<MidiEvent> sequence = model.GenerateStreamingAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken);
        await model.GenerateAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<MidiEvent> stream = sequence.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        //Act
        bool moved = await stream.MoveNextAsync();
        Func<Task> secondScore = () => model.GenerateAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken);
        Func<Task> secondStream = () => ReadAsync(model.GenerateStreamingAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken));
        Action dispose = model.Dispose;

        //Assert
        moved.Should().BeTrue();
        await secondScore.Should().ThrowAsync<InferenceException>();
        await secondStream.Should().ThrowAsync<InferenceException>();
        dispose.Should().Throw<InvalidOperationException>();
        await stream.DisposeAsync();
        (await model.GenerateAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken)).TokenIds.Should().HaveCount(14);
    }

    [Fact]
    public async Task GenerateStreamingAsync_remains_busy_while_its_final_events_are_being_consumed()
    {
        //Arrange
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(
            Fixture, cancellationToken: TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<MidiEvent> stream = model.GenerateStreamingAsync(options: Greedy(7), cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        //Act
        while (await stream.MoveNextAsync() && stream.Current.Kind != MidiEventKind.Note) { }
        Func<Task> second = () => model.GenerateAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        await second.Should().ThrowAsync<InferenceException>();
        (await stream.MoveNextAsync()).Should().BeFalse();
        (await model.GenerateAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken)).TokenIds.Should().HaveCount(14);
    }

    [Fact]
    public async Task GenerateStreamingAsync_honors_enumerator_cancellation_while_a_bar_is_buffered()
    {
        //Arrange
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(
            Fixture, cancellationToken: TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var progress = new MuseCocoTestProgress(count => { if (count == 9) cancellation.Cancel(); });
        var received = new List<MidiEvent>();

        //Act
        Func<Task> consume = async () =>
        {
            await foreach (MidiEvent item in model.GenerateStreamingAsync(options: Greedy(), progress: progress, cancellationToken: TestContext.Current.CancellationToken)
                .WithCancellation(cancellation.Token)) received.Add(item);
        };

        //Assert
        await consume.Should().ThrowAsync<OperationCanceledException>();
        Notes(received).Should().ContainSingle().Which.Pitch.Should().Be(60);
        (await model.GenerateAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken)).TokenIds.Should().HaveCount(14);
    }

    [Fact]
    public async Task GenerateStreamingAsync_cancels_between_events_from_the_same_bar()
    {
        //Arrange
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(
            Fixture, cancellationToken: TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<MidiEvent> stream = model.GenerateStreamingAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(cancellation.Token);
        await stream.MoveNextAsync();

        //Act
        cancellation.Cancel();
        Func<Task> next = async () => { await stream.MoveNextAsync(); };

        //Assert
        await next.Should().ThrowAsync<OperationCanceledException>();
        (await model.GenerateAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken)).TokenIds.Should().HaveCount(14);
    }

    [Fact]
    public async Task GenerateStreamingAsync_stops_after_consumer_break_and_repeats_with_a_fixed_seed()
    {
        //Arrange
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(
            Fixture, cancellationToken: TestContext.Current.CancellationToken);
        var options = new MuseCocoGenerationOptions { MaximumTokens = 20, MinimumTokens = 0, TopK = 2, Seed = 20260921 };
        List<MidiEvent> expected = await ReadAsync(model.GenerateStreamingAsync(options: options, cancellationToken: TestContext.Current.CancellationToken));
        int generated = 0;

        //Act
        await foreach (MidiEvent item in model.GenerateStreamingAsync(options: Greedy(),
            progress: new MuseCocoTestProgress(count => generated = count), cancellationToken: TestContext.Current.CancellationToken))
        {
            if (item.Kind == MidiEventKind.Note) break;
        }
        List<MidiEvent> repeated = await ReadAsync(model.GenerateStreamingAsync(options: options, cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        generated.Should().Be(8);
        Notes(repeated).Should().Equal(Notes(expected));
        Metadata(repeated).Should().Equal(Metadata(expected));
    }

    [Fact]
    public async Task GenerateStreamingAsync_releases_the_instance_when_progress_throws()
    {
        //Arrange
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(
            Fixture, cancellationToken: TestContext.Current.CancellationToken);
        var progress = new MuseCocoTestProgress(_ => throw new InvalidOperationException("test progress"));

        //Act
        Func<Task> consume = () => ReadAsync(model.GenerateStreamingAsync(options: Greedy(), progress: progress, cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        await consume.Should().ThrowAsync<InvalidOperationException>().WithMessage("test progress");
        (await model.GenerateAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken)).TokenIds.Should().HaveCount(14);
    }

    [Fact]
    public async Task GenerateStreamingAsync_refuses_invalid_options_and_a_disposed_model()
    {
        //Arrange
        using MuseCocoMusicModel model = await MuseCocoMusicModel.LoadFromDirectoryAsync(
            Fixture, cancellationToken: TestContext.Current.CancellationToken);
        var options = Greedy();
        options.Temperature = double.NaN;
        Func<Task> invalid = () => ReadAsync(model.GenerateStreamingAsync(options: options, cancellationToken: TestContext.Current.CancellationToken));

        //Act and assert
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await ReadAsync(model.GenerateStreamingAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken))).Should().NotBeEmpty();
        model.Dispose();
        Func<Task> disposed = () => ReadAsync(model.GenerateStreamingAsync(options: Greedy(), cancellationToken: TestContext.Current.CancellationToken));
        await disposed.Should().ThrowAsync<ObjectDisposedException>();
    }

    private static async Task<List<MidiEvent>> ReadAsync(IAsyncEnumerable<MidiEvent> source)
    {
        var events = new List<MidiEvent>();
        await foreach (MidiEvent item in source.WithCancellation(TestContext.Current.CancellationToken)) events.Add(item);
        return events;
    }

    private static IEnumerable<(long Tick, int Track, int Channel, int Pitch, int Velocity, long Duration)> Notes(IEnumerable<MidiEvent> events) =>
        events.Where(e => e.Kind == MidiEventKind.Note).Select(e => (e.Tick, e.Track, e.Channel, e.NoteNumber, e.Velocity, e.DurationTicks));

    private static IEnumerable<(MidiEventKind Kind, long Tick, long Tempo, int Numerator, int Denominator)> Metadata(IEnumerable<MidiEvent> events) =>
        events.Where(e => e.Kind is MidiEventKind.Tempo or MidiEventKind.TimeSignature)
            .Select(e => (e.Kind, e.Tick, e.MicrosecondsPerQuarterNote, e.Numerator, e.Denominator));
}
