using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What a loaded ONNX graph says about itself: the tensors it takes and returns, the operator sets it is
/// written against, and who produced it.
/// </summary>
public sealed class OnnxModelMetadata
{
    /// <summary>Builds the description of one loaded graph.</summary>
    /// <param name="graphName">The graph's name.</param>
    /// <param name="producerName">The producing tool's name.</param>
    /// <param name="producerVersion">The producing tool's version.</param>
    /// <param name="irVersion">The ONNX intermediate-representation version the file is written in.</param>
    /// <param name="opsets">The operator sets the file declares.</param>
    /// <param name="inputs">The graph's inputs.</param>
    /// <param name="outputs">The graph's outputs.</param>
    /// <param name="operators">The distinct operator types the graph uses, sorted.</param>
    internal OnnxModelMetadata(
        string graphName,
        string producerName,
        string producerVersion,
        long irVersion,
        IReadOnlyList<OnnxOpsetImport> opsets,
        IReadOnlyList<OnnxValueMetadata> inputs,
        IReadOnlyList<OnnxValueMetadata> outputs,
        IReadOnlyList<string> operators)
    {
        GraphName = graphName;
        ProducerName = producerName;
        ProducerVersion = producerVersion;
        IrVersion = irVersion;
        Opsets = opsets;
        Inputs = inputs;
        Outputs = outputs;
        Operators = operators;
    }

    /// <summary>The graph's name, or <see langword="null"/> when the file states none.</summary>
    public string GraphName { get; }

    /// <summary>The name of the tool that wrote the file, or <see langword="null"/> when it states none.</summary>
    public string ProducerName { get; }

    /// <summary>The version of the tool that wrote the file, or <see langword="null"/> when it states none.</summary>
    public string ProducerVersion { get; }

    /// <summary>The ONNX intermediate-representation version the file is written in.</summary>
    public long IrVersion { get; }

    /// <summary>The operator sets the file declares, in the order it declares them.</summary>
    public IReadOnlyList<OnnxOpsetImport> Opsets { get; }

    /// <summary>
    /// The tensors a run must be given, in the order the graph declares them. An initializer that the file
    /// also lists as an input is not here: it is a weight, and a run never supplies it.
    /// </summary>
    public IReadOnlyList<OnnxValueMetadata> Inputs { get; }

    /// <summary>The tensors a run hands back, in the order the graph declares them.</summary>
    public IReadOnlyList<OnnxValueMetadata> Outputs { get; }

    /// <summary>The distinct operator types the graph uses, sorted, for diagnostics.</summary>
    public IReadOnlyList<string> Operators { get; }
}
