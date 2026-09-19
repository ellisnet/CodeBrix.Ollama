using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: conversion/base.py@b10221, conversion/llama.py@b10221;

/// <summary>
/// Turns a transformers checkpoint directory into one GGUF file.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole conversion: read <c>config.json</c>, open the weights, decide what to call the result and
/// what numeric type to write, map every tensor on to the name the inference engine expects, permute the query
/// and key projections, read the tokenizer, and write the file. Nothing is loaded into memory that does not have
/// to be - the weights are read one tensor at a time and written as they are read.
/// </para>
/// <para>
/// The order things are written in is not an implementation detail: it is the order the inference engine's own
/// converter writes them in, so that a file produced here and a file produced there are the same bytes.
/// </para>
/// </remarks>
internal static class GgufConversion
{
    /// <summary>The tool name recorded against a conversion.</summary>
    internal const string ToolName = "CodeBrix.Ollama.ModelManager";

    /// <summary>The value of <c>general.quantization_version</c> that this GGUF version carries.</summary>
    internal const uint QuantizationVersion = 2;

    /// <summary>The status reported before the checkpoint is read.</summary>
    internal const string ReadingStatus = "reading checkpoint";

    /// <summary>The status reported before the GGUF file is written.</summary>
    internal const string WritingStatus = "writing gguf";

    /// <summary>Converts a checkpoint directory into a GGUF file.</summary>
    /// <param name="sourceDirectory">The directory holding <c>config.json</c>, the weights and the tokenizer.</param>
    /// <param name="outputPath">The GGUF file to write.</param>
    /// <param name="options">The conversion options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token that cancels the conversion.</param>
    /// <returns>What was converted and how.</returns>
    internal static Task<ConvertResult> ConvertAsync(string sourceDirectory, string outputPath,
        ConvertOptions options, CancellationToken cancellationToken = default)
    {
        return ConvertAsync(sourceDirectory, outputPath, options, null, cancellationToken);
    }

    /// <summary>Converts a checkpoint directory into a GGUF file, reporting each phase as it begins.</summary>
    /// <param name="sourceDirectory">The directory holding <c>config.json</c>, the weights and the tokenizer.</param>
    /// <param name="outputPath">The GGUF file to write.</param>
    /// <param name="options">The conversion options, or <see langword="null"/> for the defaults.</param>
    /// <param name="progress">Where the statuses go, or <see langword="null"/> to report nothing.</param>
    /// <param name="cancellationToken">A token that cancels the conversion.</param>
    /// <returns>What was converted and how.</returns>
    internal static async Task<ConvertResult> ConvertAsync(string sourceDirectory, string outputPath,
        ConvertOptions options, IProgress<PullProgress> progress, CancellationToken cancellationToken)
    {
        if (outputPath == null)
        {
            throw new ArgumentNullException(nameof(outputPath));
        }

        using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                   1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            return await ConvertAsync(sourceDirectory, output, options, progress, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Converts a checkpoint directory and writes the GGUF file to a stream.</summary>
    /// <param name="sourceDirectory">The directory holding <c>config.json</c>, the weights and the tokenizer.</param>
    /// <param name="destination">The stream the GGUF file is written to.</param>
    /// <param name="options">The conversion options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token that cancels the conversion.</param>
    /// <returns>What was converted and how.</returns>
    internal static Task<ConvertResult> ConvertAsync(string sourceDirectory, Stream destination,
        ConvertOptions options, CancellationToken cancellationToken = default)
    {
        return ConvertAsync(sourceDirectory, destination, options, null, cancellationToken);
    }

    /// <summary>
    /// Converts a checkpoint directory and writes the GGUF file to a stream, reporting each phase as it begins.
    /// </summary>
    /// <param name="sourceDirectory">The directory holding <c>config.json</c>, the weights and the tokenizer.</param>
    /// <param name="destination">The stream the GGUF file is written to.</param>
    /// <param name="options">The conversion options, or <see langword="null"/> for the defaults.</param>
    /// <param name="progress">Where the statuses go, or <see langword="null"/> to report nothing.</param>
    /// <param name="cancellationToken">A token that cancels the conversion.</param>
    /// <returns>What was converted and how.</returns>
    internal static async Task<ConvertResult> ConvertAsync(string sourceDirectory, Stream destination,
        ConvertOptions options, IProgress<PullProgress> progress, CancellationToken cancellationToken)
    {
        if (sourceDirectory == null)
        {
            throw new ArgumentNullException(nameof(sourceDirectory));
        }

        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        ConvertOptions effective = options ?? new ConvertOptions();
        if (!Directory.Exists(sourceDirectory))
        {
            throw new CheckpointFormatException("The folder \"" + sourceDirectory + "\" does not exist.");
        }

        progress?.Report(new PullProgress(ReadingStatus));
        HuggingFaceConfig config = await HuggingFaceConfig.LoadAsync(sourceDirectory, cancellationToken)
            .ConfigureAwait(false);
        string modelArchitecture = RequireSupportedArchitecture(config, effective.Architecture);
        var architecture = new LlamaArchitecture(config, modelArchitecture);

        using (CheckpointWeights reader = await CheckpointWeights
                   .OpenAsync(sourceDirectory, cancellationToken).ConfigureAwait(false))
        {
            long sourceBytes = reader.TotalBytes;
            IReadOnlyList<CheckpointTensor> ordered = reader.Tensors;
            GgufFileType fileType = ResolveFileType(effective.OutputType, ordered);
            List<GgufWriterTensor> tensors = Plan(reader, ordered, architecture, fileType,
                out long totalParameters);

            var writer = new GgufWriter(LlamaArchitecture.ArchitectureName);
            writer.AddString("general.type", "model");

            string modelId = effective.ModelId ?? Path.GetFileName(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory)));
            ModelCard card = await ModelCard.LoadAsync(sourceDirectory, cancellationToken).ConfigureAwait(false);
            ModelMetadata metadata = ModelMetadata.Load(card, config, modelId, totalParameters);
            metadata.Name = metadata.Name ?? modelId;
            if (metadata.SizeLabel == null && totalParameters > 0)
            {
                metadata.SizeLabel = ModelIdComponents.SizeLabelFromParameterCount(totalParameters);
            }

            metadata.WriteTo(writer);
            architecture.WriteParameters(writer, fileType);
            writer.AddUInt32("general.quantization_version", QuantizationVersion);

            TokenizerConfig tokenizerConfig = await TokenizerConfig
                .LoadAsync(sourceDirectory, cancellationToken).ConfigureAwait(false);
            TokenizerExport tokenizer = await VocabularyExport
                .LoadAsync(sourceDirectory, config, tokenizerConfig, effective.AddedSpecialTokens,
                    cancellationToken).ConfigureAwait(false);
            tokenizer.WriteTo(writer);

            for (int i = 0; i < tensors.Count; i++)
            {
                writer.AddTensor(tensors[i]);
            }

            progress?.Report(new PullProgress(WritingStatus));
            long written = await writer.WriteAsync(destination, cancellationToken).ConfigureAwait(false);
            return new ConvertResult(modelId, CheckpointArchitecture.Llama, tensors.Count, written, sourceBytes,
                ToOutputType(fileType), ToolName, typeof(GgufConversion).Assembly.GetName().Version.ToString());
        }
    }

    /// <summary>Decides the file type a conversion writes, from what was asked for and what the weights are.</summary>
    /// <param name="requested">The type the caller asked for.</param>
    /// <param name="tensors">The tensors in the order the container holds them.</param>
    /// <returns>The file type.</returns>
    internal static GgufFileType ResolveFileType(GgufOutputType requested, IReadOnlyList<CheckpointTensor> tensors)
    {
        switch (requested)
        {
            case GgufOutputType.F32:
                return GgufFileType.F32;
            case GgufOutputType.F16:
                return GgufFileType.F16;
            case GgufOutputType.BF16:
                return GgufFileType.BF16;
            default:
                break;
        }

        // The heuristic reads the weights, not the configuration: a finetune's configuration may say one thing
        // while its weights say another, and it is the weights that are being written.
        for (int i = 0; i < tensors.Count; i++)
        {
            if (tensors[i].Shape.Count < 2)
            {
                continue;
            }

            if (tensors[i].DataType == CheckpointDataType.BF16)
            {
                return GgufFileType.BF16;
            }

            if (tensors[i].DataType == CheckpointDataType.F16)
            {
                return GgufFileType.F16;
            }

            break;
        }

        return GgufFileType.F16;
    }

    private static GgufOutputType ToOutputType(GgufFileType fileType)
    {
        switch (fileType)
        {
            case GgufFileType.F32:
                return GgufOutputType.F32;
            case GgufFileType.BF16:
                return GgufOutputType.BF16;
            default:
                return GgufOutputType.F16;
        }
    }

    /// <summary>
    /// The model architecture a checkpoint is read as, or a refusal naming what its configuration said.
    /// </summary>
    /// <param name="config">The checkpoint's configuration.</param>
    /// <param name="requested">The architecture the caller asked for.</param>
    /// <returns>The architecture name, as the configuration spells it.</returns>
    /// <exception cref="NotSupportedException">This version does not read that architecture.</exception>
    internal static string RequireSupportedArchitecture(HuggingFaceConfig config,
        CheckpointArchitecture requested)
    {
        IReadOnlyList<string> architectures = config.Architectures;
        string named = architectures.Count > 0 ? architectures[0] : null;
        if (requested == CheckpointArchitecture.Llama)
        {
            return named ?? "LlamaForCausalLM";
        }

        for (int i = 0; i < LlamaArchitecture.SupportedModelArchitectures.Length; i++)
        {
            if (string.Equals(named, LlamaArchitecture.SupportedModelArchitectures[i], StringComparison.Ordinal))
            {
                return named;
            }
        }

        string described = named != null
            ? "architectures[0] is \"" + named + "\""
            : "model_type is \"" + (config.ModelType ?? "absent") + "\" and there is no architectures entry";
        throw new NotSupportedException("The checkpoint's config.json says " + described +
            ". This version converts the llama family only.");
    }

    private static List<GgufWriterTensor> Plan(ICheckpointReader reader, IReadOnlyList<CheckpointTensor> tensors,
        LlamaArchitecture architecture, GgufFileType fileType, out long totalParameters)
    {
        var planned = new List<GgufWriterTensor>();
        totalParameters = 0;
        for (int i = 0; i < tensors.Count; i++)
        {
            CheckpointTensor tensor = tensors[i];
            if (LlamaArchitecture.IsDropped(tensor.Name))
            {
                continue;
            }

            string mapped = architecture.MapTensorName(tensor.Name);
            GgufTensorType type = architecture.GetTensorType(mapped, tensor.Shape.Count, fileType);
            long permuteHeadCount = architecture.GetPermuteHeadCount(tensor.Name);
            var shape = new List<long>(tensor.Shape.Count);
            for (int dimension = tensor.Shape.Count - 1; dimension >= 0; dimension--)
            {
                shape.Add(tensor.Shape[dimension]);
            }

            long byteCount = tensor.ElementCount * GgufTensorTypes.GetTypeSize(type);
            long rowCount = tensor.Shape.Count > 0 ? tensor.Shape[0] : 1;
            CheckpointTensor source = tensor;
            GgufTensorType target = type;
            long heads = permuteHeadCount;
            planned.Add(new GgufWriterTensor(mapped, type, shape, byteCount,
                async (destination, cancellationToken) =>
                {
                    using (Stream values = await reader.OpenTensorAsync(source, cancellationToken)
                               .ConfigureAwait(false))
                    {
                        await TensorDataConverter.ConvertAsync(values, destination, source.DataType, target,
                            source.ElementCount, heads, rowCount, cancellationToken).ConfigureAwait(false);
                    }
                }));
            totalParameters += tensor.ElementCount;
        }

        return planned;
    }
}
