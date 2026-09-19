using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The whole MIDI-generating driver, over the tiny two-graph model checked in beside these tests.
/// </summary>
/// <remarks>
/// <para>
/// NOTHING HERE IS MUSIC AND NOTHING HERE PRETENDS TO BE. The tiny model's weights are made-up numbers; what
/// it writes is a valid piece of nothing in particular. What is being tested is everything AROUND the model:
/// the loop, both caches, the masks, the stop conditions, the order the events come out in, what cancelling
/// does, and the file that comes out at the end.
/// </para>
/// <para>
/// THE ONE MUSICAL ASSERTION THIS SUITE CANNOT MAKE is that the events are the right events - that needs the
/// publisher's own model and the publisher's own Python, and it is made in the end-to-end suite, gated.
/// </para>
/// </remarks>
public sealed class MidiGenerationModelTests
{
    /// <summary>A bundle laid out as a directory loads and says what it is.</summary>
    /// <returns>A task that completes when the model has been loaded and read.</returns>
    [Fact]
    public async Task LoadFromDirectoryAsync_loads_a_bundle_and_says_what_it_is()
    {
        //Arrange and act
        await using IMidiGenerationModel model = await MidiGenerationModel.LoadFromDirectoryAsync(
            SkyTntFixtures.TinyModelDirectory, null, TestContext.Current.CancellationToken);

        //Assert
        model.Metadata.Architecture.Should().Be("MIDIModel");
        model.Metadata.TicksPerQuarterNote.Should().Be(480);
        model.Metadata.VocabularySize.Should().Be(3406);
        model.Metadata.MaximumTokensPerEvent.Should().Be(8);
        model.Metadata.MaximumContextEvents.Should().Be(4096);
        model.Metadata.BaseGraphFileName.Should().Be("onnx/model_base.onnx");
        model.Metadata.TokenGraphFileName.Should().Be("onnx/model_token.onnx");
        model.Options.Threads.Should().NotBeNull();
    }

    /// <summary>A bundle held as (logical name to path) pairs loads the same way.</summary>
    /// <returns>A task that completes when the model has been loaded.</returns>
    [Fact]
    public async Task LoadFromFilesAsync_loads_a_bundle_held_under_other_names()
    {
        //Arrange and act
        await using IMidiGenerationModel model = await MidiGenerationModel.LoadFromFilesAsync(
            SkyTntFixtures.TinyModelFiles(), null, TestContext.Current.CancellationToken);

        //Assert
        model.Metadata.Architecture.Should().Be("MIDIModel");
    }

    /// <summary>The thread count the caller asked for is the one the graphs run with.</summary>
    /// <returns>A task that completes when the model has been loaded.</returns>
    [Fact]
    public async Task LoadFromDirectoryAsync_runs_with_the_threads_it_was_given()
    {
        //Arrange and act
        await using IMidiGenerationModel model = await MidiGenerationModel.LoadFromDirectoryAsync(
            SkyTntFixtures.TinyModelDirectory, new OnnxRunnerOptions { Threads = 2 },
            TestContext.Current.CancellationToken);

        //Assert
        model.Options.Threads.Should().Be(2);
    }

    /// <summary>
    /// A cap the caller sets reaches both graphs, because this driver passes the options object through to
    /// each of them: with no thread count stated, a cap of one leaves them running on one thread.
    /// </summary>
    /// <returns>A task that completes when the model has been loaded.</returns>
    [Fact]
    public async Task LoadFromDirectoryAsync_honours_a_thread_cap()
    {
        //Arrange and act
        await using IMidiGenerationModel model = await MidiGenerationModel.LoadFromDirectoryAsync(
            SkyTntFixtures.TinyModelDirectory, new OnnxRunnerOptions { MaxThreads = 1 },
            TestContext.Current.CancellationToken);

        //Assert
        model.Options.Threads.Should().Be(1);
        model.Options.MaxThreads.Should().Be(1);
    }

    /// <summary>A thread count the caller stated wins over a cap, here as everywhere.</summary>
    /// <returns>A task that completes when the model has been loaded.</returns>
    [Fact]
    public async Task LoadFromDirectoryAsync_lets_a_stated_thread_count_win_over_the_cap()
    {
        //Arrange and act
        await using IMidiGenerationModel model = await MidiGenerationModel.LoadFromDirectoryAsync(
            SkyTntFixtures.TinyModelDirectory, new OnnxRunnerOptions { Threads = 2, MaxThreads = 1 },
            TestContext.Current.CancellationToken);

        //Assert
        model.Options.Threads.Should().Be(2);
    }

    /// <summary>A generation produces events that decode into music of the shape the model describes.</summary>
    /// <returns>A task that completes when the piece has been written.</returns>
    [Fact]
    public async Task GenerateAsync_produces_events_in_the_models_own_units()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();

        //Act
        IReadOnlyList<MidiEvent> events = await Collect(model, Greedy(200));

        //Assert
        events.Should().NotBeEmpty();
        foreach (MidiEvent item in events)
        {
            item.Tick.Should().BeGreaterThanOrEqualTo(0);
            item.Track.Should().BeGreaterThanOrEqualTo(0);
            if (item.Kind == MidiEventKind.Note)
            {
                item.DurationTicks.Should().BeGreaterThan(0);
                item.Channel.Should().BeInRange(0, 15);
            }
        }
    }

    /// <summary>
    /// The model decides when the piece is finished, and it is the CACHE that lets it: the tiny model's answer
    /// for the ending token grows with how much it has already written.
    /// </summary>
    /// <returns>A task that completes when the piece has been written.</returns>
    [Fact]
    public async Task GenerateAsync_stops_when_the_model_says_the_piece_is_finished()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();

        //Act
        IReadOnlyList<MidiEvent> events = await Collect(model, Greedy(500));

        //Assert
        events.Count.Should().BeLessThan(500);
        events.Count.Should().BeGreaterThan(1);
    }

    /// <summary>Asked for fewer events than the model would write, it writes exactly that many.</summary>
    /// <returns>A task that completes when the piece has been written.</returns>
    [Fact]
    public async Task GenerateAsync_stops_at_the_number_of_events_it_was_asked_for()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();

        //Act
        IReadOnlyList<MidiEvent> events = await Collect(model, Greedy(4));

        //Assert
        events.Should().HaveCount(4);
    }

    /// <summary>The same settings and the same seed write the same piece.</summary>
    /// <returns>A task that completes when both pieces have been written.</returns>
    [Fact]
    public async Task GenerateAsync_with_the_same_seed_writes_the_same_piece()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();
        MidiGenerationOptions options = new MidiGenerationOptions
        {
            MaximumEvents = 20,
            TopK = 12,
            TopP = 0.95,
            Seed = 20260918,
        };

        //Act
        IReadOnlyList<MidiEvent> first = await Collect(model, options);
        IReadOnlyList<MidiEvent> again = await Collect(model, options);

        //Assert
        Describe(again).Should().Equal(Describe(first));
    }

    /// <summary>Greedy generation needs no seed at all to write the same piece twice.</summary>
    /// <returns>A task that completes when both pieces have been written.</returns>
    [Fact]
    public async Task GenerateAsync_greedy_writes_the_same_piece_whatever_the_seed()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();

        //Act
        IReadOnlyList<MidiEvent> first = await Collect(
            model, new MidiGenerationOptions { MaximumEvents = 30, TopK = 1, TopP = 1.0, Seed = 1 });
        IReadOnlyList<MidiEvent> again = await Collect(
            model, new MidiGenerationOptions { MaximumEvents = 30, TopK = 1, TopP = 1.0, Seed = 999999 });

        //Assert
        Describe(again).Should().Equal(Describe(first));
    }

    /// <summary>
    /// AN EVENT IS HANDED OVER BEFORE THE PIECE IS FINISHED, which is the whole point of the enumeration: a
    /// player can be fed while the rest of the music is still being written.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task GenerateAsync_hands_an_event_over_before_the_piece_is_finished()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();
        int total = (await Collect(model, Greedy(200))).Count;
        total.Should().BeGreaterThan(2);

        //Act
        await using IAsyncEnumerator<MidiEvent> events = model
            .GenerateAsync(Greedy(200), TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        bool first = await events.MoveNextAsync();
        MidiEvent one = events.Current;
        bool second = await events.MoveNextAsync();

        //Assert
        first.Should().BeTrue();
        one.Should().NotBeNull();
        second.Should().BeTrue();
    }

    /// <summary>Walking away from a generation leaves the model ready for the next one.</summary>
    /// <returns>A task that completes when both generations have run.</returns>
    [Fact]
    public async Task GenerateAsync_that_is_walked_away_from_leaves_the_model_usable()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();
        List<MidiEvent> taken = new List<MidiEvent>();

        //Act
        await foreach (MidiEvent item in model
            .GenerateAsync(Greedy(200), TestContext.Current.CancellationToken)
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            taken.Add(item);
            if (taken.Count == 2) break;
        }

        IReadOnlyList<MidiEvent> afterwards = await Collect(model, Greedy(5));

        //Assert
        taken.Should().HaveCount(2);
        afterwards.Should().HaveCount(5);
    }

    /// <summary>Cancelling stops the generation and leaves what was already handed over.</summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task GenerateAsync_that_is_cancelled_keeps_what_it_had_already_handed_over()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        List<MidiEvent> taken = new List<MidiEvent>();

        //Act
        Func<Task> act = async () =>
        {
            await foreach (MidiEvent item in model.GenerateAsync(Greedy(200), cancellation.Token))
            {
                taken.Add(item);
                if (taken.Count == 3) await cancellation.CancelAsync();
            }
        };

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        taken.Should().HaveCount(3);

        IReadOnlyList<MidiEvent> afterwards = await Collect(model, Greedy(5));
        afterwards.Should().HaveCount(5);
    }

    /// <summary>A second generation while one is still going is refused rather than queued.</summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task GenerateAsync_refuses_a_second_generation_while_one_is_going()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();
        await using IAsyncEnumerator<MidiEvent> first = model
            .GenerateAsync(Greedy(200), TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await first.MoveNextAsync();

        //Act
        Func<Task> act = async () =>
        {
            await foreach (MidiEvent item in model
                .GenerateAsync(Greedy(2), TestContext.Current.CancellationToken)
                .WithCancellation(TestContext.Current.CancellationToken))
            {
                _ = item;
            }
        };

        //Assert
        await act.Should().ThrowAsync<InferenceException>();
    }

    /// <summary>
    /// The horizon every event carries holds: nothing that comes later is earlier than it.
    /// </summary>
    /// <returns>A task that completes when the piece has been written.</returns>
    [Fact]
    public async Task GenerateAsync_never_hands_over_an_event_before_a_horizon_it_has_already_given()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();

        //Act
        IReadOnlyList<MidiEvent> events = await Collect(model, Greedy(200));

        //Assert
        long horizon = 0;
        foreach (MidiEvent item in events)
        {
            item.Tick.Should().BeGreaterThanOrEqualTo(horizon);
            item.HorizonTicks.Should().BeLessThanOrEqualTo(item.Tick);
            if (item.HorizonTicks > horizon) horizon = item.HorizonTicks;
        }
    }

    /// <summary>The events of the prompt come first, so the enumeration is the whole piece.</summary>
    /// <returns>A task that completes when the piece has been written.</returns>
    [Fact]
    public async Task GenerateAsync_hands_the_prompts_own_events_over_first()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();
        MidiGenerationOptions options = Greedy(3);
        options.BeatsPerMinute = 96;
        options.TimeSignatureNumerator = 3;
        options.TimeSignatureDenominator = 4;

        //Act
        IReadOnlyList<MidiEvent> events = await Collect(model, options);

        //Assert
        events[0].Kind.Should().Be(MidiEventKind.TimeSignature);
        events[0].Numerator.Should().Be(3);
        events[0].Denominator.Should().Be(4);
        events[1].Kind.Should().Be(MidiEventKind.Tempo);
        events[1].BeatsPerMinute.Should().BeApproximately(96, 0.01);
        events.Should().HaveCount(5);
    }

    /// <summary>Asked not to, it hands over only what it wrote itself.</summary>
    /// <returns>A task that completes when the piece has been written.</returns>
    [Fact]
    public async Task GenerateAsync_can_leave_the_prompts_own_events_out()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();
        MidiGenerationOptions options = Greedy(3);
        options.BeatsPerMinute = 96;
        options.IncludePromptEvents = false;

        //Act
        IReadOnlyList<MidiEvent> events = await Collect(model, options);

        //Assert
        events.Should().HaveCount(3);
        events[0].Kind.Should().NotBe(MidiEventKind.Tempo);
    }

    /// <summary>A piece to continue moves the music that follows it along in time.</summary>
    /// <returns>A task that completes when the piece has been written.</returns>
    [Fact]
    public async Task GenerateAsync_continues_a_piece_after_the_end_of_it()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();
        MidiScore prompt = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 0, 60, 100, 240),
            MidiEvent.Note(1920, 0, 0, 64, 100, 240),
        });
        MidiGenerationOptions options = Greedy(3);
        options.Prompt = prompt;
        options.IncludePromptEvents = false;

        //Act
        IReadOnlyList<MidiEvent> events = await Collect(model, options);

        //Assert
        events.Should().HaveCount(3);
        events[0].Tick.Should().BeGreaterThanOrEqualTo(1920);
    }

    /// <summary>What was generated becomes a piece at the model's own resolution.</summary>
    /// <returns>A task that completes when the piece has been gathered.</returns>
    [Fact]
    public async Task ToScore_gathers_what_was_generated_into_a_piece()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();
        IReadOnlyList<MidiEvent> events = await Collect(model, Greedy(200));

        //Act
        MidiScore score = model.ToScore(events);

        //Assert
        score.TicksPerQuarterNote.Should().Be(480);
        score.Events.Should().NotBeEmpty();
        score.Events.Count.Should().BeLessThanOrEqualTo(events.Count);
    }

    /// <summary>The positions in the file are the positions the events were handed over with.</summary>
    /// <returns>A task that completes when the file has been written and read.</returns>
    [Fact]
    public async Task ToScore_and_the_file_keep_the_positions_the_events_were_handed_over_with()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();
        IReadOnlyList<MidiEvent> events = await Collect(model, Greedy(200));
        MidiScore score = model.ToScore(events);

        //Act
        MidiScore read = MidiFile.Read(MidiFile.Write(score));

        //Assert
        read.TicksPerQuarterNote.Should().Be(score.TicksPerQuarterNote);
        read.Events.Should().HaveCount(score.Events.Count);
        for (int i = 0; i < read.Events.Count; i++)
        {
            read.Events[i].Tick.Should().Be(score.Events[i].Tick);
            read.Events[i].Kind.Should().Be(score.Events[i].Kind);
        }
    }

    /// <summary>A generation can be saved as a file and read back.</summary>
    /// <returns>A task that completes when the file has been written and read.</returns>
    [Fact]
    public async Task SaveAsync_writes_a_file_that_reads_back()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();
        IReadOnlyList<MidiEvent> events = await Collect(model, Greedy(200));
        string path = Path.Combine(
            Path.GetTempPath(), "codebrix-ollama-generated-" + Guid.NewGuid().ToString("N") + ".mid");

        try
        {
            //Act
            await model.SaveAsync(path, events, TestContext.Current.CancellationToken);
            MidiScore read = await MidiFile.ReadAsync(path, TestContext.Current.CancellationToken);

            //Assert
            read.TicksPerQuarterNote.Should().Be(480);
            read.Events.Should().NotBeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>A model that has been disposed refuses to be used again.</summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task GenerateAsync_after_disposal_is_refused()
    {
        //Arrange
        IMidiGenerationModel model = await Load();
        model.Dispose();
        Action act = () => model.GenerateAsync(Greedy(1), TestContext.Current.CancellationToken);

        //Act and assert
        act.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>An option outside its range is refused where the caller wrote it.</summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task GenerateAsync_refuses_an_option_outside_its_range_before_enumerating()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();
        Action act = () => model.GenerateAsync(
            new MidiGenerationOptions { MaximumEvents = 0 }, TestContext.Current.CancellationToken);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>Gathering nothing at all is refused.</summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task ToScore_of_nothing_is_refused()
    {
        //Arrange
        await using IMidiGenerationModel model = await Load();
        Action act = () => model.ToScore(null);

        //Act and assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>Loading needs a directory.</summary>
    [Fact]
    public void LoadFromDirectoryAsync_without_a_directory_is_refused()
    {
        //Arrange
        Func<Task> act = async () => await MidiGenerationModel.LoadFromDirectoryAsync(
            "   ", null, TestContext.Current.CancellationToken);

        //Act and assert
        act.Should().ThrowAsync<ArgumentException>();
    }

    private static Task<IMidiGenerationModel> Load() =>
        MidiGenerationModel.LoadFromDirectoryAsync(
            SkyTntFixtures.TinyModelDirectory, new OnnxRunnerOptions { Threads = 2 },
            TestContext.Current.CancellationToken);

    private static MidiGenerationOptions Greedy(int events) => new MidiGenerationOptions
    {
        MaximumEvents = events,
        TopK = 1,
        TopP = 1.0,
        Temperature = 1.0,
        Seed = 20260918,
    };

    private static async Task<IReadOnlyList<MidiEvent>> Collect(
        IMidiGenerationModel model, MidiGenerationOptions options)
    {
        List<MidiEvent> events = new List<MidiEvent>();
        await foreach (MidiEvent item in model
            .GenerateAsync(options, TestContext.Current.CancellationToken)
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            events.Add(item);
        }

        return events;
    }

    private static List<string> Describe(IReadOnlyList<MidiEvent> events)
    {
        List<string> lines = new List<string>();
        foreach (MidiEvent item in events) lines.Add(item.ToString());
        return lines;
    }
}
