using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// An ONNX graph that is loaded and ready to run: tensors in by name, tensors out by name. Obtained from
/// <see cref="OnnxModel"/>; disposing it releases the weights.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE RAW SURFACE. It knows nothing about tokens, caches or sampling: it runs the graph it was given
/// and hands back what the graph computes. Anything a model family needs around that - a key/value cache fed
/// from one step to the next, a tokenizer, a sampler - belongs to the caller, which is what makes the same
/// engine able to run graphs it has never seen.
/// </para>
/// <para>
/// A RUN CARRIES NO STATE FROM THE LAST ONE. Two runs with the same inputs produce the same outputs, and a
/// decoder's cache lives in the caller's hands: feed the previous run's <c>present</c> outputs back as this
/// run's <c>past</c> inputs, passing the very same <see cref="OnnxTensor"/> instances, and nothing is copied.
/// </para>
/// <para>
/// ONE RUN AT A TIME. An instance executes one run at a time and does not queue: a second call that arrives
/// while one is in flight is refused with <see cref="InferenceException"/> rather than left to wait. Load a
/// second instance to run two at once - the weights are not shared between instances.
/// </para>
/// <para>
/// Application code and tests can implement this interface themselves to stand in for a real graph.
/// </para>
/// </remarks>
public interface IOnnxModel : IDisposable, IAsyncDisposable
{
    /// <summary>What the graph says about itself: its inputs, its outputs, its operator sets and its producer.</summary>
    OnnxModelMetadata Metadata { get; }

    /// <summary>The options the graph was loaded with, with every default resolved to the value in use.</summary>
    OnnxRunnerOptions Options { get; }

    /// <summary>
    /// Runs the graph once. It is pure computation and touches no I/O, so it is synchronous; use
    /// <see cref="RunAsync"/> to keep it off a caller's thread.
    /// </summary>
    /// <param name="inputs">
    /// One tensor for every name in <see cref="OnnxModelMetadata.Inputs"/>. A name the graph does not declare
    /// is an error rather than an ignored extra.
    /// </param>
    /// <returns>
    /// One tensor for every name in <see cref="OnnxModelMetadata.Outputs"/>. Each one owns its array and the
    /// engine will not write to it again, so it can be held, read or fed back into the next run.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// An input is missing, is not declared by the graph, or carries an element type the graph does not
    /// declare for it.
    /// </exception>
    /// <exception cref="InferenceException">
    /// The graph could not be run on these inputs - shapes that do not agree, an index outside its tensor -
    /// or a run was already in flight on this instance.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The model has been disposed.</exception>
    IReadOnlyDictionary<string, OnnxTensor> Run(IReadOnlyDictionary<string, OnnxTensor> inputs);

    /// <summary>
    /// Runs the graph once on a thread pool thread, so that a caller on a user-interface or request thread is
    /// not blocked for the length of the run.
    /// </summary>
    /// <param name="inputs">One tensor for every name in <see cref="OnnxModelMetadata.Inputs"/>.</param>
    /// <param name="cancellationToken">
    /// A token that cancels the run. It is honoured between nodes: a run already inside one large matrix
    /// multiply finishes that node first.
    /// </param>
    /// <returns>One tensor for every name in <see cref="OnnxModelMetadata.Outputs"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An input is missing, is not declared by the graph, or carries the wrong element type.</exception>
    /// <exception cref="InferenceException">The graph could not be run on these inputs, or a run was already in flight.</exception>
    /// <exception cref="ObjectDisposedException">The model has been disposed.</exception>
    Task<IReadOnlyDictionary<string, OnnxTensor>> RunAsync(
        IReadOnlyDictionary<string, OnnxTensor> inputs,
        CancellationToken cancellationToken = default);
}
