using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What <see cref="ModelRunner.QuantizeAsync"/> does: the checks, the one native call and the care taken over
/// the file it writes, kept out of the public contract file so the contract stays a contract.
/// </summary>
/// <remarks>
/// <para>
/// THE ENGINE IS GIVEN A FILE OF ITS OWN TO WRITE, never the caller's output path. The native quantizer
/// creates its output file and fills it as it goes, so a failure half way - a source it cannot read at the
/// requested type, a full disk - would otherwise leave a half-written model at the name the caller asked for,
/// and would already have destroyed anything that was there before. The engine therefore writes beside the
/// output, under a name carrying a marker and a unique suffix, and that file is moved on to the output path
/// only once the call has returned success. A failure removes it and leaves everything else as it was.
/// </para>
/// <para>
/// The move is a rename within one directory, so it costs nothing however large the model is, and it is the
/// reason the temporary file cannot be put under the system temporary directory: that is very often another
/// file system, and a rename across file systems is a copy of every byte.
/// </para>
/// </remarks>
internal static class ModelQuantizer
{
    /// <summary>The marker the file the engine writes into carries until it is finished.</summary>
    private const string PartialMarker = ".quantizing-";

    /// <summary>
    /// Rewrites a GGUF model at another quantization.
    /// </summary>
    /// <param name="inputPath">The path of the GGUF file to read.</param>
    /// <param name="outputPath">The path of the GGUF file to write.</param>
    /// <param name="type">The file type to write.</param>
    /// <param name="options">How to quantize, or <see langword="null"/> for the engine's own defaults.</param>
    /// <param name="cancellationToken">A token to cancel the operation before the engine starts.</param>
    /// <returns>What was read, what was written and how long it took.</returns>
    /// <exception cref="ArgumentException">A path is not set, or the two name one file.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not one of the engine's types.</exception>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="inputPath"/>.</exception>
    /// <exception cref="DirectoryNotFoundException">The output's directory does not exist.</exception>
    /// <exception cref="NativeLibraryException">The native engine could not be loaded.</exception>
    /// <exception cref="ModelLoadException">The engine would not quantize the file.</exception>
    public static async Task<QuantizeResult> QuantizeAsync(
        string inputPath,
        string outputPath,
        GgufQuantizationType type,
        QuantizeOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("A quantization needs the path of the GGUF file to read.", nameof(inputPath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("A quantization needs the path of the GGUF file to write.", nameof(outputPath));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "The file type " + (int)type + " is not one the inference engine writes. Use a member of "
                    + nameof(GgufQuantizationType) + ".");
        }

        string input = Path.GetFullPath(inputPath.Trim());
        string output = Path.GetFullPath(outputPath.Trim());

        if (string.Equals(input, output, PathComparison))
        {
            throw new ArgumentException(
                "A quantization reads one file and writes another; it cannot write over the file it is"
                    + " reading ('" + input + "').",
                nameof(outputPath));
        }

        if (!File.Exists(input))
        {
            throw new FileNotFoundException("There is no GGUF file at '" + input + "'.", input);
        }

        string directory = Path.GetDirectoryName(output);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                "The quantized model would be written to '" + output + "', and there is no directory '"
                    + directory + "' to write it in.");
        }

        QuantizeOptions effective = options ?? new QuantizeOptions();
        long inputBytes = new FileInfo(input).Length;

        NativeLibraryLoader.EnsureLoaded();
        EngineLog.Arm();

        //A quantization touches no context and holds nothing open afterwards, so it runs on a thread of its
        //own rather than on a worker: there is nothing for a worker to keep affinity with. The token is
        //honoured here, before the thread is taken, and cannot reach the native call once it has started.
        return await Task.Run(
                () => Quantize(input, output, type, effective, inputBytes, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// How a path of this platform's file system is compared with another: Linux tells two names apart by
    /// their case and the other platforms this package runs on do not.
    /// </summary>
    private static StringComparison PathComparison
        => OperatingSystem.IsLinux() || OperatingSystem.IsAndroid()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Runs the engine's quantizer and puts what it wrote in its place.
    /// </summary>
    /// <param name="input">The full path of the file to read.</param>
    /// <param name="output">The full path of the file to write.</param>
    /// <param name="type">The file type to write.</param>
    /// <param name="options">How to quantize.</param>
    /// <param name="inputBytes">The size of the file being read.</param>
    /// <param name="cancellationToken">A token checked before the native call starts.</param>
    /// <returns>What was read, what was written and how long it took.</returns>
    /// <exception cref="ModelLoadException">The engine returned a failure.</exception>
    private static unsafe QuantizeResult Quantize(
        string input,
        string output,
        GgufQuantizationType type,
        QuantizeOptions options,
        long inputBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EngineLog.Clear();

        //Every field but the four set here stays at what the engine itself calls the default, which is what
        //makes a call with no options the call its own tool makes with no switches.
        LlamaModelQuantizeParams parameters = NativeDefaults.QuantizeParams;
        parameters.Ftype = (LlamaFtype)type;
        parameters.NThread = options.Threads;
        parameters.AllowRequantize = options.AllowRequantize ? (byte)1 : (byte)0;
        parameters.Pure = options.Pure ? (byte)1 : (byte)0;

        string partial = output + PartialMarker + Guid.NewGuid().ToString("N").Substring(0, 8);
        var watch = new Stopwatch();

        try
        {
            watch.Start();
            uint failed = NativeMethods.llama_model_quantize(input, partial, &parameters);
            watch.Stop();

            if (failed != 0)
            {
                throw new ModelLoadException(EngineLog.Describe(
                    "The engine would not quantize '" + input + "' as " + type + "; it returned " + failed
                        + "."));
            }

            if (!File.Exists(partial))
            {
                throw new ModelLoadException(EngineLog.Describe(
                    "The engine reported success quantizing '" + input + "' as " + type + " but wrote no"
                        + " file."));
            }

            long outputBytes = new FileInfo(partial).Length;
            File.Move(partial, output, true);

            return new QuantizeResult
            {
                InputPath = input,
                OutputPath = output,
                Type = type,
                InputBytes = inputBytes,
                OutputBytes = outputBytes,
                Elapsed = watch.Elapsed
            };
        }
        finally
        {
            TryDelete(partial);
        }
    }

    /// <summary>
    /// Removes the file the engine was writing into, if it is still there.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <remarks>
    /// It runs on every road out, and on the one that succeeded there is nothing left to remove because the
    /// file has been moved away. A failure to remove it is not allowed to replace the exception that is on
    /// its way to the caller, which is the whole reason for the catches.
    /// </remarks>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
