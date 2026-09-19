using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;

namespace ModelQueryTool.ModelRunning.Tests.Fakes;

/// <summary>
/// Stands in for the model runner's own loading: it keeps the options of every load it was asked for, hands
/// out a <see cref="FakeRunningModel"/>, reports whatever load progress a test asked for, and fails the
/// loads a test told it to fail.
/// </summary>
public sealed class FakeModelLoader
{
    private readonly Queue<Exception> _failures = new Queue<Exception>();

    /// <summary>Gets the options of every load, in order.</summary>
    public IList<ModelRunnerOptions> Loads { get; } = new List<ModelRunnerOptions>();

    /// <summary>Gets the models handed out, in order.</summary>
    public IList<FakeRunningModel> Models { get; } = new List<FakeRunningModel>();

    /// <summary>Gets the fractions reported to each load's progress, in order.</summary>
    public IList<float> ProgressFractions { get; } = new List<float>();

    /// <summary>Gets or sets what to do to each new model before it is handed out.</summary>
    public Action<FakeRunningModel> Prepare { get; set; }

    /// <summary>Gets the model handed out last, or null when none has been.</summary>
    public FakeRunningModel Current => Models.Count == 0 ? null : Models[Models.Count - 1];

    /// <summary>Makes the next load - or, called more than once, the next loads - fail.</summary>
    /// <param name="failure">The exception the load fails with.</param>
    /// <returns>The same loader, for chaining.</returns>
    public FakeModelLoader FailNextLoad(Exception failure)
    {
        _failures.Enqueue(failure);

        return this;
    }

    /// <summary>Loads a model, the way <c>ModelRunner.LoadAsync</c> would.</summary>
    /// <param name="options">The options the host built.</param>
    /// <param name="cancellationToken">A token to abandon the load.</param>
    /// <returns>The loaded model.</returns>
    public Task<IRunningModel> LoadAsync(ModelRunnerOptions options, CancellationToken cancellationToken)
    {
        Loads.Add(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (options.LoadProgress != null)
        {
            foreach (float fraction in ProgressFractions)
            {
                options.LoadProgress.Report(fraction);
            }
        }

        if (_failures.Count > 0)
        {
            return Task.FromException<IRunningModel>(_failures.Dequeue());
        }

        FakeRunningModel model = new FakeRunningModel();
        Prepare?.Invoke(model);
        Models.Add(model);

        return Task.FromResult<IRunningModel>(model);
    }
}
