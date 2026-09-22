using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A loaded graph: the plan, the weights, the arena, and the one run that is allowed to be in flight.
/// </summary>
/// <remarks>
/// <para>
/// A RUN CARRIES NOTHING INTO THE NEXT ONE. The only thing that survives between runs is the arena's pool of
/// arrays, which holds no meaning - every kernel fills the tensor it is given before anything reads it. So
/// two runs with the same inputs give the same outputs whatever ran in between, and a decoder's cache lives
/// in the driver's hands rather than here.
/// </para>
/// <para>
/// ONE RUN AT A TIME, and the second caller is refused rather than queued. Waiting would need a queue whose
/// depth nobody chose and would hide the mistake; an application that wants two runs at once loads the model
/// twice, and is then paying for two copies of the weights on purpose rather than by accident.
/// </para>
/// </remarks>
internal sealed class OnnxSession : IOnnxModel
{
    private readonly OnnxExecutionPlan _plan;
    private readonly OnnxExecutionSettings _settings;
    private readonly OnnxArena _arena;
    private int _running;
    private int _disposed;

    /// <summary>Creates a session over a plan.</summary>
    /// <param name="plan">The loaded graph.</param>
    /// <param name="options">The options it was loaded with, already copied.</param>
    /// <param name="settings">Those options resolved against the processor.</param>
    internal OnnxSession(OnnxExecutionPlan plan, OnnxRunnerOptions options, OnnxExecutionSettings settings)
    {
        _plan = plan;
        _settings = settings;
        _arena = new OnnxArena(options.ReuseBuffers);
        Options = options;
    }

    /// <inheritdoc />
    public OnnxModelMetadata Metadata => _plan.Metadata;

    /// <inheritdoc />
    public OnnxRunnerOptions Options { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, OnnxTensor> Run(IReadOnlyDictionary<string, OnnxTensor> inputs) =>
        Execute(inputs, CancellationToken.None);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, OnnxTensor>> RunAsync(
        IReadOnlyDictionary<string, OnnxTensor> inputs, CancellationToken cancellationToken = default)
    {
        if (inputs == null) throw new ArgumentNullException(nameof(inputs));

        return Task.Run(() => Execute(inputs, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Internal recurrent-driver path. Destinations belong exclusively to the driver and must not alias
    /// any input or each other. The public Run/RunAsync ownership contract is unchanged.
    /// </summary>
    internal Task<IReadOnlyDictionary<string, OnnxTensor>> RunIntoAsync(
        IReadOnlyDictionary<string, OnnxTensor> inputs, IReadOnlyDictionary<string, OnnxTensor> outputBuffers,
        CancellationToken cancellationToken)
        => Task.Run(() => Execute(inputs, cancellationToken, outputBuffers), cancellationToken);

    /// <summary>Releases the weights and the pooled buffers.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        foreach (OnnxPlanSlot slot in _plan.Slots) slot.Initializer = null;
        foreach (OnnxPlanNode node in _plan.Nodes) node.State = null;
        _arena.Clear();
    }

    /// <summary>Releases the weights and the pooled buffers.</summary>
    /// <returns>A completed task; there is no I/O to wait for.</returns>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private IReadOnlyDictionary<string, OnnxTensor> Execute(
        IReadOnlyDictionary<string, OnnxTensor> inputs, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, OnnxTensor> outputBuffers = null)
    {
        if (inputs == null) throw new ArgumentNullException(nameof(inputs));
        ObjectDisposedException.ThrowIf(_disposed != 0, typeof(IOnnxModel));

        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            throw new InferenceException(
                "This model is already running. One model runs one inference at a time; load a second model"
                + " to run two at once.");
        }

        try
        {
            return RunPlan(inputs, cancellationToken, outputBuffers);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private IReadOnlyDictionary<string, OnnxTensor> RunPlan(
        IReadOnlyDictionary<string, OnnxTensor> inputs, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, OnnxTensor> outputBuffers)
    {
        OnnxPlanSlot[] slots = _plan.Slots;
        OnnxValue[] values = new OnnxValue[slots.Length];

        foreach (OnnxPlanSlot slot in slots)
        {
            if (slot.Kind == OnnxSlotKind.Initializer) values[slot.Index] = slot.Initializer;
        }

        BindInputs(inputs, values);

        OnnxTensor[] destinations = BindOutputBuffers(inputs, outputBuffers);
        OnnxOperatorContext context = new OnnxOperatorContext(values, _arena, _settings, destinations);
        OnnxPlanNode[] nodes = _plan.Nodes;

        for (int i = 0; i < nodes.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnnxPlanNode node = nodes[i];
            context.Bind(node);

            try
            {
                node.Kernel.Run(context);
            }
            catch (Exception exception) when (exception is not ModelRunnerException
                and not OperationCanceledException and not OutOfMemoryException)
            {
                // A kernel that walks off the end of a buffer, divides an integer by nought or overflows a
                // length has been handed shapes the graph does not really allow. It is still the model's
                // fault and not the process's, so it comes back as an inference failure naming the node.
                throw new InferenceException(
                    node.Describe() + " failed: " + exception.Message, exception);
            }

            foreach (int slot in node.Release)
            {
                OnnxValue value = values[slot];
                if (value == null) continue;

                value.Release(_arena);
                values[slot] = null;
            }
        }

        return Collect(values, destinations);
    }

    private void BindInputs(IReadOnlyDictionary<string, OnnxTensor> inputs, OnnxValue[] values)
    {
        int matched = 0;
        for (int i = 0; i < _plan.InputSlots.Length; i++)
        {
            OnnxPlanSlot slot = _plan.Slots[_plan.InputSlots[i]];
            OnnxValueMetadata declared = _plan.Metadata.Inputs[i];
            if (!inputs.TryGetValue(slot.Name, out OnnxTensor tensor) || tensor == null)
            {
                throw new ArgumentException(
                    "The graph takes an input named '" + slot.Name + "' and none was given.", nameof(inputs));
            }

            if (tensor.ElementType != declared.ElementType)
            {
                throw new ArgumentException(
                    "The input '" + slot.Name + "' is declared " + OnnxTensor.Name(declared.ElementType)
                    + " and a " + OnnxTensor.Name(tensor.ElementType) + " tensor was given.",
                    nameof(inputs));
            }

            long[] shape = new long[tensor.Shape.Count];
            for (int d = 0; d < shape.Length; d++) shape[d] = tensor.Shape[d];

            values[slot.Index] = OnnxValue.Wrap(tensor.ElementType, tensor.Data(), shape);
            matched++;
        }

        if (matched != inputs.Count)
        {
            foreach (string name in inputs.Keys)
            {
                bool declared = false;
                for (int i = 0; i < _plan.InputSlots.Length; i++)
                {
                    if (string.Equals(_plan.Slots[_plan.InputSlots[i]].Name, name, StringComparison.Ordinal))
                    {
                        declared = true;
                        break;
                    }
                }

                if (!declared)
                {
                    throw new ArgumentException(
                        "The graph takes no input named '" + name + "'; it takes "
                        + string.Join(", ", Names()) + ".",
                        nameof(inputs));
                }
            }
        }
    }

    private OnnxTensor[] BindOutputBuffers(IReadOnlyDictionary<string, OnnxTensor> inputs,
        IReadOnlyDictionary<string, OnnxTensor> buffers)
    {
        if (buffers == null) return null;
        var destinations = new OnnxTensor[_plan.Slots.Length];
        var arrays = new HashSet<Array>();
        foreach (OnnxTensor input in inputs.Values) arrays.Add(input.Data());
        foreach (KeyValuePair<string, OnnxTensor> pair in buffers)
        {
            if (pair.Value == null || !arrays.Add(pair.Value.Data()))
                throw new ArgumentException("ONNX output buffers must not alias inputs or each other.", nameof(buffers));
            int found = -1;
            foreach (int slot in _plan.OutputSlots)
                if (_plan.Slots[slot].Name == pair.Key) { found = slot; break; }
            if (found < 0) throw new ArgumentException("Unknown bound ONNX output: " + pair.Key, nameof(buffers));
            destinations[found] = pair.Value;
        }
        return destinations;
    }

    private IReadOnlyDictionary<string, OnnxTensor> Collect(OnnxValue[] values, OnnxTensor[] destinations)
    {
        Dictionary<string, OnnxTensor> outputs =
            new Dictionary<string, OnnxTensor>(_plan.OutputSlots.Length, StringComparer.Ordinal);

        for (int i = 0; i < _plan.OutputSlots.Length; i++)
        {
            OnnxPlanSlot slot = _plan.Slots[_plan.OutputSlots[i]];
            OnnxValue value = values[slot.Index];
            if (value == null)
            {
                throw new InferenceException(
                    "The graph names '" + slot.Name + "' as an output and the run produced nothing for it.");
            }

            OnnxTensor target = destinations?[slot.Index];
            if (target == null) outputs[slot.Name] = Detach(value);
            else
            {
                OnnxOutputBinding.Validate(target, value.ElementType, value.Shape);
                if (!ReferenceEquals(value.Buffer.Data, target.Data())) Array.Copy(value.Buffer.Data, target.Data(), value.Count);
                outputs[slot.Name] = target;
            }

            // Now that the caller has its own tensor, the run's own hold on the buffer goes: a buffer that
            // was only renamed on its way out - a reshape of something the arena lent us - goes back into
            // the pool instead of quietly leaving it one array smaller on every run.
            if (slot.Kind != OnnxSlotKind.Initializer)
            {
                value.Release(_arena);
                values[slot.Index] = null;
            }
        }

        return outputs;
    }

    private OnnxTensor Detach(OnnxValue value)
    {
        // A tensor handed back has to own an array of exactly its own length that nothing will write to
        // again. A node that WROTE an output of the graph was given such an array to begin with, so the
        // common case costs nothing; one that only renamed a tensor - a reshape of an input, say - is
        // copied, because the array underneath it belongs to somebody else.
        if (value.Buffer.EngineOwned && !value.Buffer.Poolable && value.Buffer.Capacity == value.Count)
        {
            return OnnxTensor.Adopt(value.ElementType, value.Buffer.Data, value.Shape);
        }

        Array copy = OnnxArena.Allocate(value.ElementType, value.Count);
        Array.Copy(value.Buffer.Data, 0, copy, 0, value.Count);
        return OnnxTensor.Adopt(value.ElementType, copy, value.Shape);
    }

    private IEnumerable<string> Names()
    {
        foreach (int slot in _plan.InputSlots) yield return _plan.Slots[slot].Name;
    }
}
