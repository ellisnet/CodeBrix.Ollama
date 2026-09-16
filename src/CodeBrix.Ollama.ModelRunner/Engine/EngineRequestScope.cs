using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The marker one call chain carries while it is streaming from a model, so that a second call into the
/// same model from inside that chain can be refused instead of waiting for a turn that will never come.
/// </summary>
/// <remarks>
/// <para>
/// A streaming request holds the model's request gate from the first token to the last. A caller that
/// reaches back into the same model from inside its own <c>await foreach</c> would therefore wait on a gate
/// only that same call chain can release: a deadlock with nothing to time out. The engine marks the chain
/// instead. The marker is put in place by the synchronous method that hands back the enumerable - an
/// <c>async</c> method's own changes to an <see cref="System.Threading.AsyncLocal{T}"/> are undone when it
/// returns, so the marker has to be set before the state machine starts - and this mutable object is what
/// lets the enumeration flip the flag where the caller can see it, because both sides hold the same
/// reference.
/// </para>
/// <para>
/// One scope belongs to one model. A chain that streams from two models carries the innermost one's scope;
/// the check only ever refuses a call to the model the scope names.
/// </para>
/// </remarks>
internal sealed class EngineRequestScope
{
    /// <summary>Creates the marker for one model.</summary>
    /// <param name="model">The model the chain is streaming from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public EngineRequestScope(object model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        Model = model;
    }

    /// <summary>The model this marker speaks for.</summary>
    public object Model { get; }

    /// <summary>Whether an enumeration on this chain is holding that model's request gate right now.</summary>
    public bool StreamHoldsTheGate { get; set; }
}
