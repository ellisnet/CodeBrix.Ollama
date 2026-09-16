using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Works out what kind of model a bundle holds from the names of its files alone, which is all that can
/// be known without opening anything. The answer becomes the config layer's model format, so that a
/// later listing can tell a checkpoint from an exported graph without reading a byte of either.
/// </summary>
/// <remarks>
/// <para>
/// Only the weight-bearing files decide: an exported graph (<c>.onnx</c>), a TensorFlow checkpoint
/// (<c>.ckpt</c> and the <c>.ckpt.data-*</c>, <c>.ckpt.index</c> and <c>.ckpt.meta</c> files that go with
/// it), or a PyTorch-family checkpoint (<c>.bin</c>, <c>.pt</c>, <c>.pth</c>, <c>.safetensors</c>).
/// Configuration, tokenizer, licence and readme files say nothing about the format and are passed over.
/// </para>
/// <para>
/// One kind gives that kind's name, several give <c>mixed</c> - a repository that ships both a
/// checkpoint and an export is genuinely both - and none at all gives the caller's own answer, because
/// what a bundle of nothing but configuration files should be called depends on where it came from.
/// </para>
/// </remarks>
internal static class BundleFormatDetector
{
    /// <summary>Every weight-bearing file is an exported graph.</summary>
    public const string Onnx = "onnx";

    /// <summary>Every weight-bearing file belongs to a TensorFlow checkpoint.</summary>
    public const string TensorFlowCheckpoint = "tensorflow-checkpoint";

    /// <summary>Every weight-bearing file is a PyTorch-family checkpoint.</summary>
    public const string PyTorch = "pytorch";

    /// <summary>More than one kind of weight-bearing file is present.</summary>
    public const string Mixed = "mixed";

    /// <summary>The answer for a Hugging Face repository whose files decide nothing.</summary>
    public const string HuggingFace = "huggingface";

    /// <summary>The answer for a list of addresses whose files decide nothing.</summary>
    public const string Files = "files";

    /// <summary>The answer for a folder on disk whose files decide nothing.</summary>
    public const string Imported = "imported";

    /// <summary>
    /// Decides the model format of a set of publisher paths.
    /// </summary>
    /// <param name="paths">
    /// The publisher's relative paths, with forward slashes. <see langword="null"/> entries are passed
    /// over.
    /// </param>
    /// <param name="undecided">
    /// What to answer when no file says anything about the format: <see cref="HuggingFace"/>,
    /// <see cref="Files"/> or <see cref="Imported"/>.
    /// </param>
    /// <returns>The model format.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="paths"/> is <see langword="null"/>.</exception>
    public static string Detect(IEnumerable<string> paths, string undecided)
    {
        if (paths == null)
        {
            throw new ArgumentNullException(nameof(paths));
        }

        bool onnx = false;
        bool checkpoint = false;
        bool pytorch = false;

        foreach (string path in paths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            string name = FileNameOf(path).ToLowerInvariant();
            if (name.EndsWith(".onnx", StringComparison.Ordinal))
            {
                onnx = true;
            }
            else if (name.EndsWith(".ckpt", StringComparison.Ordinal)
                || name.Contains(".ckpt.", StringComparison.Ordinal))
            {
                checkpoint = true;
            }
            else if (name.EndsWith(".bin", StringComparison.Ordinal)
                || name.EndsWith(".pt", StringComparison.Ordinal)
                || name.EndsWith(".pth", StringComparison.Ordinal)
                || name.EndsWith(".safetensors", StringComparison.Ordinal))
            {
                pytorch = true;
            }
        }

        int kinds = (onnx ? 1 : 0) + (checkpoint ? 1 : 0) + (pytorch ? 1 : 0);
        if (kinds > 1)
        {
            return Mixed;
        }
        if (onnx)
        {
            return Onnx;
        }
        if (checkpoint)
        {
            return TensorFlowCheckpoint;
        }
        if (pytorch)
        {
            return PyTorch;
        }
        return undecided;
    }

    /// <summary>
    /// The last segment of a relative path, which is the file's own name.
    /// </summary>
    /// <param name="path">The path, with forward slashes.</param>
    /// <returns>The file name.</returns>
    private static string FileNameOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path.Substring(slash + 1);
    }
}
