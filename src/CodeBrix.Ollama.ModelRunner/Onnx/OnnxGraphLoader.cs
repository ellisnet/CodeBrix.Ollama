using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.Core;
using CoreModel = CodeBrix.Ollama.Core.OnnxModel;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Reads a graph file and turns it into an execution plan: the weights in the layout the kernels want, the
/// nodes in the order they run, and the lifetime of every tensor between them.
/// </summary>
/// <remarks>
/// <para>
/// EVERYTHING THAT WILL NOT RUN IS REFUSED HERE. An operator the engine does not implement, an operator set
/// too old for the semantics it implements, an element type it does not compute in, an attribute it would
/// otherwise ignore, a node that reads a tensor nothing has produced yet - each is named, with its node,
/// while the model is being loaded. A graph that loads runs.
/// </para>
/// <para>
/// IT DOES NOT KEEP A SECOND COPY OF THE WEIGHTS. As each stored tensor is turned into the engine's own
/// tensor, the bytes the codec read are let go, so the two are never both held for the whole model; a matrix
/// multiply's constant weight is turned round into [N, K] as it is read and the stored layout is dropped with
/// the rest. That is what the memory of a model of this size turns on.
/// </para>
/// </remarks>
internal static class OnnxGraphLoader
{
    /// <summary>Reads a graph file and builds its execution plan, on a thread of its own.</summary>
    /// <param name="location">Where the graph file and its side files are.</param>
    /// <param name="cancellationToken">A token that cancels the load.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ModelLoadException">The file is not there, is not a graph, or asks for something the engine does not do.</exception>
    /// <remarks>
    /// THE WHOLE LOAD IS SYNCHRONOUS INSIDE ONE BACKGROUND CALL, and that is deliberate rather than
    /// old-fashioned. Nearly all of it is arithmetic - parsing a protocol-buffer message, converting weights,
    /// turning matrices round - so there is nothing for an awaiting thread to wait on. It also keeps a
    /// model's worth of memory from being held far longer than it is needed: a local of an <c>async</c>
    /// method lives in a state-machine object that the returned task keeps alive, so the file's bytes - most
    /// of a gigabyte for a model of this size - would stay reachable for as long as the CALLER held the task
    /// it awaited. Here they are ordinary stack locals and go the moment the method returns.
    /// </remarks>
    internal static Task<OnnxExecutionPlan> LoadAsync(
        OnnxModelLocation location, CancellationToken cancellationToken) =>
        Task.Run(() => Load(location, cancellationToken), cancellationToken);

    private static OnnxExecutionPlan Load(OnnxModelLocation location, CancellationToken cancellationToken)
    {
        if (!BitConverter.IsLittleEndian)
        {
            throw new ModelLoadException(
                "ONNX stores a tensor's bytes little-endian and this engine reads them where they lie, so it"
                + " does not run on a big-endian processor.");
        }

        // THE LOAD'S OWN MEMORY IS ACCOUNTED FOR, because a model of this size abandons its own weight in
        // memory several times over while it is read. OnnxLoadReclaim says why, and why a small graph pays
        // nothing for it.
        OnnxLoadReclaim reclaim = new OnnxLoadReclaim();
        CoreModel model = Parse(location.ModelPath, cancellationToken);

        // The file's bytes were a local of the parse and nothing reaches them from here - the codec copied
        // what it needed out of them - so for a graph of any size they are a model's worth of memory the load
        // is about to ask for again.
        reclaim.Abandoned(new FileInfo(location.ModelPath).Length);
        OnnxGraphProto graph = Graph(model, location.ModelPath);
        long opset = RequireOpset(model, location.ModelPath);

        ResolveSideFiles(model, location, cancellationToken);

        string producerName = model.Proto.ProducerName;
        string producerVersion = model.Proto.ProducerVersion;
        long irVersion = model.Proto.IrVersion ?? 0;
        List<OnnxOpsetImport> opsets = new List<OnnxOpsetImport>(model.Proto.OpsetImports.Count);
        foreach (OnnxOperatorSetId import in model.Proto.OpsetImports)
        {
            opsets.Add(new OnnxOpsetImport(import.Domain ?? string.Empty, import.Version ?? 0));
        }

        Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.Ordinal);
        List<OnnxPlanSlot> slots = new List<OnnxPlanSlot>();

        ReadInitializers(graph, slots, byName, reclaim, cancellationToken);
        int[] inputSlots = ReadGraphInputs(graph, slots, byName, out List<OnnxValueMetadata> inputs);
        ReadNodeOutputs(graph, slots, byName);
        int[] outputSlots = ReadGraphOutputs(graph, slots, byName, out List<OnnxValueMetadata> outputs);

        OnnxPlanSlot[] table = slots.ToArray();
        int[] readers = new int[table.Length];
        OnnxPlanNode[] nodes = BuildNodes(graph, table, byName, opset, readers, reclaim);
        DropFoldedWeights(table, readers);
        Lifetimes(table, nodes);

        SortedSet<string> operators = new SortedSet<string>(StringComparer.Ordinal);
        foreach (OnnxPlanNode node in nodes) operators.Add(node.OpType);

        OnnxModelMetadata metadata = new OnnxModelMetadata(
            graph.Name, producerName, producerVersion, irVersion, opsets, inputs, outputs,
            new List<string>(operators));

        // Past this point nothing refers to the parsed message: the weights are the engine's now, in its own
        // layout, and what the codec read of them was let go as each one was converted.
        return new OnnxExecutionPlan(table, nodes, inputSlots, outputSlots, metadata);
    }

    private static CoreModel Parse(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ModelLoadException("No graph file was named.");
        }

        if (!File.Exists(path))
        {
            throw new ModelLoadException("There is no file at '" + path + "'.");
        }

        // The file's bytes are read, parsed and then let go. They are a LOCAL of a synchronous method, so
        // nothing keeps a reference to them past it, and the copy the codec made of each tensor is the only
        // one that survives.
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = File.ReadAllBytes(path);
        try
        {
            return CoreModel.Parse(bytes, Path.GetDirectoryName(path));
        }
        catch (Exception exception) when (exception is InvalidDataException or IndexOutOfRangeException
            or ArgumentOutOfRangeException or OverflowException)
        {
            throw new ModelLoadException(
                "The file '" + path + "' could not be read as an ONNX model: " + exception.Message, exception);
        }
    }

    private static OnnxGraphProto Graph(CoreModel model, string path)
    {
        OnnxGraphProto graph = model.Proto.Graph;
        if (graph == null)
        {
            throw new ModelLoadException("The file '" + path + "' carries no graph.");
        }

        return graph;
    }

    private static long RequireOpset(CoreModel model, string path)
    {
        long? standard = null;
        foreach (OnnxOperatorSetId import in model.Proto.OpsetImports)
        {
            if (string.IsNullOrEmpty(import.Domain) || string.Equals(import.Domain, "ai.onnx", StringComparison.Ordinal))
            {
                standard = import.Version ?? 0;
            }
        }

        if (standard == null)
        {
            throw new ModelLoadException(
                "The file '" + path + "' declares no standard operator set, so there is no way to know what"
                + " its operators mean.");
        }

        if (standard.Value < OnnxKernels.MinimumOpset || standard.Value > OnnxKernels.MaximumOpset)
        {
            throw new ModelLoadException(
                "The file '" + path + "' is written against ai.onnx opset "
                + standard.Value.ToString(CultureInfo.InvariantCulture)
                + ", and this engine implements opset "
                + OnnxKernels.MinimumOpset.ToString(CultureInfo.InvariantCulture) + " to "
                + OnnxKernels.MaximumOpset.ToString(CultureInfo.InvariantCulture)
                + ". Several operators changed meaning at opset 13 without changing shape, so an older graph"
                + " is refused rather than run under the newer meaning.");
        }

        return standard.Value;
    }

    private static void ResolveSideFiles(
        CoreModel model, OnnxModelLocation location, CancellationToken cancellationToken)
    {
        foreach (OnnxTensorProto tensor in model.EnumerateTensors())
        {
            if (!tensor.HasExternalData) continue;

            cancellationToken.ThrowIfCancellationRequested();
            string named = tensor.GetExternalDataValue(CoreModel.ExternalLocationKey);
            string path = location.Resolve(named);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                throw new ModelLoadException(
                    "The weight '" + tensor.Name + "' lives in the side file '" + named
                    + "', which is not among the files this model was loaded from.");
            }

            long offset = Number(tensor, CoreModel.ExternalOffsetKey, 0);
            long length = Number(tensor, CoreModel.ExternalLengthKey, -1);

            tensor.RawData = ReadSideFile(path, named, tensor.Name, offset, length);
            tensor.ClearExternalData();
        }
    }

    private static byte[] ReadSideFile(string path, string named, string weight, long offset, long length)
    {
        using FileStream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        if (length < 0) length = stream.Length - offset;
        if (offset < 0 || length < 0 || offset + length > stream.Length)
        {
            throw new ModelLoadException(
                "The weight '" + weight + "' points outside the side file '" + named + "'.");
        }

        if (length > int.MaxValue)
        {
            throw new ModelLoadException(
                "The weight '" + weight + "' is larger than a .NET array can be.");
        }

        byte[] payload = new byte[length];
        stream.Seek(offset, SeekOrigin.Begin);
        stream.ReadExactly(payload);
        return payload;
    }

    private static long Number(OnnxTensorProto tensor, string key, long fallback)
    {
        string text = tensor.GetExternalDataValue(key);
        if (string.IsNullOrEmpty(text)) return fallback;
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
        {
            throw new ModelLoadException(
                "The weight '" + tensor.Name + "' has a side-file '" + key + "' that is not a number.");
        }

        return value;
    }

    private static void ReadInitializers(
        OnnxGraphProto graph,
        List<OnnxPlanSlot> slots,
        Dictionary<string, int> byName,
        OnnxLoadReclaim reclaim,
        CancellationToken cancellationToken)
    {
        foreach (OnnxTensorProto tensor in graph.Initializers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(tensor.Name))
            {
                throw new ModelLoadException("The graph carries a weight with no name.");
            }

            if (byName.ContainsKey(tensor.Name))
            {
                throw new ModelLoadException("The graph carries two weights named '" + tensor.Name + "'.");
            }

            OnnxPlanSlot slot = new OnnxPlanSlot(slots.Count, tensor.Name)
            {
                Kind = OnnxSlotKind.Initializer,
                Initializer = OnnxTensorReader.Read(tensor, "The weight '" + tensor.Name + "'"),
            };

            // The codec's copy of the bytes is released the moment the engine has its own, so the two are
            // never both held for the whole model.
            long released = tensor.RawData == null ? 0 : tensor.RawData.LongLength;
            tensor.RawData = null;
            tensor.FloatData.Clear();
            tensor.FloatData.TrimExcess();
            tensor.Int32Data.Clear();
            tensor.Int32Data.TrimExcess();
            tensor.Int64Data.Clear();
            tensor.Int64Data.TrimExcess();
            tensor.DoubleData.Clear();
            tensor.DoubleData.TrimExcess();
            tensor.UInt64Data.Clear();
            tensor.UInt64Data.TrimExcess();

            byName.Add(slot.Name, slot.Index);
            slots.Add(slot);
            reclaim.Abandoned(released);
        }
    }

    private static int[] ReadGraphInputs(
        OnnxGraphProto graph,
        List<OnnxPlanSlot> slots,
        Dictionary<string, int> byName,
        out List<OnnxValueMetadata> described)
    {
        List<int> indices = new List<int>();
        described = new List<OnnxValueMetadata>();

        foreach (OnnxValueInfoProto input in graph.Inputs)
        {
            if (string.IsNullOrEmpty(input.Name))
            {
                throw new ModelLoadException("The graph declares an input with no name.");
            }

            // Before IR version 4 a weight was also listed as an input. It is a weight, and a run never
            // supplies it.
            if (byName.TryGetValue(input.Name, out int existing)
                && slots[existing].Kind == OnnxSlotKind.Initializer)
            {
                continue;
            }

            if (byName.ContainsKey(input.Name))
            {
                throw new ModelLoadException("The graph declares two inputs named '" + input.Name + "'.");
            }

            OnnxValueMetadata metadata = Describe(input, "input");
            OnnxPlanSlot slot = new OnnxPlanSlot(slots.Count, input.Name) { Kind = OnnxSlotKind.GraphInput };
            byName.Add(slot.Name, slot.Index);
            slots.Add(slot);
            indices.Add(slot.Index);
            described.Add(metadata);
        }

        return indices.ToArray();
    }

    private static void ReadNodeOutputs(
        OnnxGraphProto graph, List<OnnxPlanSlot> slots, Dictionary<string, int> byName)
    {
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            OnnxNodeProto node = graph.Nodes[i];
            foreach (string name in node.Outputs)
            {
                if (string.IsNullOrEmpty(name)) continue;

                if (byName.ContainsKey(name))
                {
                    throw new ModelLoadException(
                        "The graph produces '" + name + "' more than once; every tensor has one producer.");
                }

                OnnxPlanSlot slot = new OnnxPlanSlot(slots.Count, name)
                {
                    Kind = OnnxSlotKind.Computed,
                    Producer = i,
                };

                byName.Add(slot.Name, slot.Index);
                slots.Add(slot);
            }
        }
    }

    private static int[] ReadGraphOutputs(
        OnnxGraphProto graph,
        List<OnnxPlanSlot> slots,
        Dictionary<string, int> byName,
        out List<OnnxValueMetadata> described)
    {
        int[] indices = new int[graph.Outputs.Count];
        described = new List<OnnxValueMetadata>(graph.Outputs.Count);

        for (int i = 0; i < graph.Outputs.Count; i++)
        {
            OnnxValueInfoProto output = graph.Outputs[i];
            if (string.IsNullOrEmpty(output.Name) || !byName.TryGetValue(output.Name, out int slot))
            {
                throw new ModelLoadException(
                    "The graph names '" + output.Name + "' as an output and nothing produces it.");
            }

            slots[slot].IsGraphOutput = true;
            indices[i] = slot;
            described.Add(Describe(output, "output"));
        }

        return indices;
    }

    private static OnnxPlanNode[] BuildNodes(
        OnnxGraphProto graph,
        OnnxPlanSlot[] slots,
        Dictionary<string, int> byName,
        long opset,
        int[] readers,
        OnnxLoadReclaim reclaim)
    {
        OnnxPlanNode[] nodes = new OnnxPlanNode[graph.Nodes.Count];

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            OnnxNodeProto node = graph.Nodes[i];
            OnnxKernel kernel = OnnxKernels.Find(node.Domain, node.OpType);
            if (kernel == null)
            {
                string domain = OnnxKernels.NormalizeDomain(node.Domain);
                throw new ModelLoadException(
                    Describe(node, i) + ": this engine does not implement the operator '" + node.OpType
                    + "'" + (domain.Length == 0 ? string.Empty : " of the domain '" + domain + "'")
                    + ". It implements " + string.Join(", ", OnnxKernels.Supported) + ".");
            }

            int[] inputs = new int[node.Inputs.Count];
            for (int j = 0; j < node.Inputs.Count; j++)
            {
                string name = node.Inputs[j];
                if (string.IsNullOrEmpty(name))
                {
                    inputs[j] = -1;
                    continue;
                }

                if (!byName.TryGetValue(name, out int slot))
                {
                    throw new ModelLoadException(
                        Describe(node, i) + ": it reads '" + name + "', which the graph never produces.");
                }

                OnnxPlanSlot read = slots[slot];
                if (read.Kind == OnnxSlotKind.Computed && read.Producer >= i)
                {
                    throw new ModelLoadException(
                        Describe(node, i) + ": it reads '" + name
                        + "', which is produced later in the file. The nodes of an ONNX graph are required to"
                        + " be in an order where every input is already there.");
                }

                inputs[j] = slot;
                readers[slot]++;
            }

            int[] outputs = new int[node.Outputs.Count];
            for (int j = 0; j < node.Outputs.Count; j++)
            {
                string name = node.Outputs[j];
                outputs[j] = string.IsNullOrEmpty(name) ? -1 : byName[name];
            }

            OnnxPlanNode planned = new OnnxPlanNode(i, node.OpType, node.Name, kernel, inputs, outputs);
            for (int j = 0; j < outputs.Length; j++)
            {
                planned.OutputIsGraphOutput[j] = outputs[j] >= 0 && slots[outputs[j]].IsGraphOutput;
            }

            nodes[i] = planned;
        }

        // THE KERNELS ARE PREPARED IN A SECOND PASS, once every node's inputs have been counted. A kernel
        // that takes a weight into its own layout - a matrix multiply turning one round - can then let the
        // stored layout go THE MOMENT the last node that wanted it has been served, instead of at the end of
        // the load. On a model of this size that is the difference between holding one copy of the weights
        // and holding two.
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            OnnxNodeProto node = graph.Nodes[i];
            OnnxKernel kernel = nodes[i].Kernel;
            OnnxNodeLoadContext context =
                new OnnxNodeLoadContext(node, i, opset, slots, nodes[i].Inputs, readers);
            context.RequireInputs(kernel.MinInputs, kernel.MaxInputs);
            context.RequireOutputs(kernel.MinOutputs, kernel.MaxOutputs);
            context.AllowAttributes(kernel.Attributes);
            nodes[i].State = kernel.Prepare(context);

            // A kernel that took a weight into its own layout has just abandoned the stored one. Over a whole
            // decoder that is the model's weight again, and it piles up unless the collector is told.
            reclaim.Abandoned(context.ReleasedBytes);
        }

        return nodes;
    }

    private static void DropFoldedWeights(OnnxPlanSlot[] slots, int[] readers)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            OnnxPlanSlot slot = slots[i];
            if (slot.Kind != OnnxSlotKind.Initializer || slot.IsGraphOutput) continue;
            if (readers[i] != 0) continue;

            // Every node that named this weight took it into its own state, in its own layout. The stored
            // layout is not wanted again, and on a model of this size it is worth hundreds of megabytes.
            slot.Initializer = null;
            slot.FoldedIntoKernels = true;
        }
    }

    private static void Lifetimes(OnnxPlanSlot[] slots, OnnxPlanNode[] nodes)
    {
        foreach (OnnxPlanNode node in nodes)
        {
            foreach (int slot in node.Inputs)
            {
                if (slot >= 0) slots[slot].LastUse = node.Index;
            }
        }

        List<int>[] release = new List<int>[nodes.Length];
        foreach (OnnxPlanSlot slot in slots)
        {
            if (slot.Kind != OnnxSlotKind.Computed || slot.IsGraphOutput) continue;

            // A tensor nothing reads is let go the moment the node that made it has run; everything else is
            // let go when the last node that could read it has.
            int at = slot.LastUse >= 0 ? slot.LastUse : slot.Producer;
            if (at < 0) continue;

            (release[at] ??= new List<int>()).Add(slot.Index);
        }

        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i].Release = release[i] == null ? Array.Empty<int>() : release[i].ToArray();
        }
    }

    private static OnnxValueMetadata Describe(OnnxValueInfoProto info, string what)
    {
        OnnxTensorTypeProto type = info.Type?.TensorType;
        if (type?.ElementType == null)
        {
            throw new ModelLoadException(
                "The graph " + what + " '" + info.Name + "' states no element type.");
        }

        OnnxElementType element = (OnnxTensorDataType)type.ElementType.Value switch
        {
            OnnxTensorDataType.Float => OnnxElementType.Float,
            OnnxTensorDataType.Int64 => OnnxElementType.Int64,
            OnnxTensorDataType.Int32 => OnnxElementType.Int32,
            OnnxTensorDataType.Bool => OnnxElementType.Bool,
            OnnxTensorDataType.UInt8 or OnnxTensorDataType.Int8 => throw new ModelLoadException(
                "The graph " + what + " '" + info.Name + "' is "
                + OnnxTensorReader.Describe((OnnxTensorDataType)type.ElementType.Value)
                + ". The engine computes with the quantized 8-bit types INSIDE a quantized graph - a weight it"
                + " folded, an activation between DynamicQuantizeLinear and MatMulInteger - but the tensors it"
                + " is given and hands back do not carry them, so a graph that asks for one at its own edge is"
                + " refused."),
            _ => throw new ModelLoadException(
                "The graph " + what + " '" + info.Name + "' is "
                + OnnxTensorReader.Describe((OnnxTensorDataType)type.ElementType.Value)
                + ", and this engine computes in float, int64, int32 and bool. A 16-bit float WEIGHT is"
                + " widened as the model is read, because that is a storage format; a 16-bit float input or"
                + " output is not, because the graph would then be computing something else."),
        };

        List<OnnxDimension> shape = new List<OnnxDimension>();
        if (type.Shape != null)
        {
            foreach (OnnxTensorShapeDimension dimension in type.Shape.Dimensions)
            {
                shape.Add(dimension.DimensionValue.HasValue
                    ? OnnxDimension.Fixed(dimension.DimensionValue.Value)
                    : OnnxDimension.Symbolic(dimension.DimensionParameter));
            }
        }

        return new OnnxValueMetadata(info.Name, element, shape);
    }

    private static string Describe(OnnxNodeProto node, int index) => string.IsNullOrEmpty(node.Name)
        ? node.OpType + " node " + index.ToString(CultureInfo.InvariantCulture)
        : node.OpType + " node '" + node.Name + "'";
}
