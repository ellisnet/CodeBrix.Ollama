using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Loads Qwen 3.5 35B-A3B once for a whole test class, and keeps the engine's log of that load.
/// </summary>
/// <remarks>
/// <para>
/// The file is twenty gigabytes and the load takes minutes on a processor with no accelerator behind it, so
/// it happens exactly once and only when a test that needs it actually runs - the fixture's constructor does
/// nothing. Memory mapping is what makes the load possible at all on a machine with thirty-two gigabytes:
/// the weights are paged in on demand rather than read into memory.
/// </para>
/// <para>
/// The engine writes the sizes it settled on - the model buffer, the key/value cache, the compute buffer -
/// to its log while loading, and nowhere else. The lines are captured here so the tests can report them.
/// </para>
/// </remarks>
public sealed class Qwen35ModelFixture : IAsyncDisposable
{
    /// <summary>The file's name in the model cache.</summary>
    public const string FileName = "Qwen3.5-35B-A3B-Q4_K_M.gguf";

    /// <summary>Where the file is downloaded from when it is not cached.</summary>
    public const string Url =
        "https://huggingface.co/unsloth/Qwen3.5-35B-A3B-GGUF/resolve/main/Qwen3.5-35B-A3B-Q4_K_M.gguf";

    /// <summary>How many bytes the finished file has.</summary>
    public const long Size = 22016023168L;

    /// <summary>The file's SHA-256, checked only after a fresh download.</summary>
    public const string Sha256 = "3b46d1066bc91cc2d613e3bc22ce691dd77e6f0d33c9060690d24ce6de494375";

    /// <summary>The context size the tests load the model with.</summary>
    public const uint ContextSize = 4096;

    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
    private readonly List<string> log = new List<string>();

    private IRunningModel model;
    private string path;

    /// <summary>How long the load took, once it has happened.</summary>
    public TimeSpan LoadDuration { get; private set; }

    /// <summary>The lines the engine wrote about the sizes it settled on, in order.</summary>
    public IReadOnlyList<string> MemoryLines
    {
        get
        {
            List<string> interesting = new List<string>();
            lock (log)
            {
                foreach (string line in log)
                {
                    if (line.Contains("buffer size") || line.Contains("KV") || line.Contains("size = ")
                        || line.Contains("n_ctx") || line.Contains("model params"))
                    {
                        interesting.Add(line);
                    }
                }
            }

            return interesting;
        }
    }

    /// <summary>The path of the model file, downloading it if it is not cached.</summary>
    /// <param name="cancellationToken">A token to cancel the download.</param>
    /// <returns>The path.</returns>
    public async Task<string> PathAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (path == null)
            {
                path = await ModelDownloader.EnsureAsync(FileName, Url, Size, Sha256, cancellationToken);
            }

            return path;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The loaded model, loading it on the first call. Minutes, the first time.</summary>
    /// <param name="cancellationToken">A token to cancel the load.</param>
    /// <returns>The running model.</returns>
    public async Task<IRunningModel> ModelAsync(CancellationToken cancellationToken)
    {
        string file = await PathAsync(cancellationToken);

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (model != null) return model;

            ModelRunner.SetLogHandler(Collect);
            Stopwatch clock = Stopwatch.StartNew();

            try
            {
                model = await ModelRunner.LoadAsync(
                    new ModelRunnerOptions
                    {
                        ModelPath = file,
                        GpuLayers = 0,
                        ContextSize = ContextSize,
                        LoadMode = ModelLoadMode.MemoryMap,
                    },
                    cancellationToken);
            }
            finally
            {
                clock.Stop();
                LoadDuration = clock.Elapsed;
                ModelRunner.SetLogHandler(null);
            }

            return model;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Unloads the model.</summary>
    /// <returns>A task that completes when it is unloaded.</returns>
    public async ValueTask DisposeAsync()
    {
        IRunningModel loaded = model;
        model = null;
        if (loaded != null) await loaded.DisposeAsync();

        gate.Dispose();
    }

    private void Collect(ModelRunnerLogLevel level, string line)
    {
        lock (log)
        {
            log.Add(line);
        }
    }
}
