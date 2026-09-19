using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ModelQueryTool.ModelRunning.Tests.Fakes;

namespace ModelQueryTool.ModelRunning.Tests.Infrastructure;

/// <summary>
/// A host wired to a fake loader, with every state change it announced kept in order. One of these is made
/// per test, and disposing it disposes the host.
/// </summary>
internal sealed class ModelHostHarness : IAsyncDisposable
{
    /// <summary>The path the harness starts the host with.</summary>
    public const string ModelPath = "/models/fake-model-q4_k_m.gguf";

    private readonly List<ModelHostState> _states = new List<ModelHostState>();

    /// <summary>Creates the harness.</summary>
    /// <param name="options">What the host loads and converses with, or null for the defaults.</param>
    public ModelHostHarness(ModelHostOptions options = null)
    {
        Loader = new FakeModelLoader();
        Host = new ModelHost(options, Loader.LoadAsync);
        Host.StateChanged += OnStateChanged;
    }

    /// <summary>Gets the loader the host was built with.</summary>
    public FakeModelLoader Loader { get; }

    /// <summary>Gets the host under test.</summary>
    public ModelHost Host { get; }

    /// <summary>Gets the model handed out by the last load, or null when none has been.</summary>
    public FakeRunningModel Model => Loader.Current;

    /// <summary>Gets the states the host announced, in order.</summary>
    public IReadOnlyList<ModelHostState> States
    {
        get
        {
            lock (_states)
            {
                return _states.ToArray();
            }
        }
    }

    /// <summary>Collects a whole turn.</summary>
    /// <param name="updates">The turn.</param>
    /// <returns>Its pieces, in order.</returns>
    public static async Task<List<ChatTurnUpdate>> DrainAsync(IAsyncEnumerable<ChatTurnUpdate> updates)
    {
        List<ChatTurnUpdate> pieces = new List<ChatTurnUpdate>();

        await foreach (ChatTurnUpdate update in updates)
        {
            pieces.Add(update);
        }

        return pieces;
    }

    /// <summary>Makes a message of the given number of one-token words.</summary>
    /// <param name="prefix">What each word starts with.</param>
    /// <param name="count">How many words.</param>
    /// <returns>The message.</returns>
    public static string Words(string prefix, int count)
    {
        string[] words = new string[count];

        for (int index = 0; index < count; index++)
        {
            words[index] = prefix + index.ToString();
        }

        return string.Join(" ", words);
    }

    /// <summary>Starts the host on the harness's model path.</summary>
    /// <param name="cancellationToken">A token to abandon the load.</param>
    /// <returns>A task that completes when the model is loaded.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Host.StartAsync(ModelPath, null, cancellationToken);
    }

    /// <summary>Sends a message and collects the whole turn.</summary>
    /// <param name="userText">What the user wrote.</param>
    /// <param name="cancellationToken">A token to stop the turn.</param>
    /// <returns>The pieces of the turn, in order.</returns>
    public Task<List<ChatTurnUpdate>> SendAsync(string userText, CancellationToken cancellationToken)
    {
        return DrainAsync(Host.SendAsync(userText, cancellationToken));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Host.StateChanged -= OnStateChanged;

        return Host.DisposeAsync();
    }

    private void OnStateChanged(object sender, ModelHostState state)
    {
        lock (_states)
        {
            _states.Add(state);
        }
    }
}
