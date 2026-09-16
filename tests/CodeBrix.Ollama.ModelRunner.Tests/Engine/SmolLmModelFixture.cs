using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Loads SmolLM 360M once for a whole test class, downloading it into the cache on the first run.
/// </summary>
/// <remarks>
/// The model is small enough to load in a second or two, but every test in the class shares one context, so
/// loading it once is also what makes the cache-reuse test meaningful: a fresh context would have nothing
/// cached to reuse.
/// </remarks>
public sealed class SmolLmModelFixture : IAsyncDisposable
{
    /// <summary>The file's name in the model cache.</summary>
    public const string FileName = "smollm-360m-instruct-add-basics-q8_0.gguf";

    /// <summary>Where the file is downloaded from when it is not cached.</summary>
    public const string Url =
        "https://huggingface.co/HuggingFaceTB/smollm-360M-instruct-v0.2-Q8_0-GGUF/resolve/main/"
        + "smollm-360m-instruct-add-basics-q8_0.gguf";

    /// <summary>How many bytes the finished file has.</summary>
    public const long Size = 386405440L;

    /// <summary>The file's SHA-256, checked only after a fresh download.</summary>
    public const string Sha256 = "b5a2e94a0be8c047bccc3f52bc2f27b79b0dbc688ef3102de54fa6a33b20198c";

    /// <summary>The model's embedding width, which every embedding it produces has.</summary>
    public const int EmbeddingLength = 960;

    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

    private IRunningModel model;
    private string path;

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

    /// <summary>The loaded model, loading it on the first call.</summary>
    /// <param name="cancellationToken">A token to cancel the load.</param>
    /// <returns>The running model.</returns>
    public async Task<IRunningModel> ModelAsync(CancellationToken cancellationToken)
    {
        string file = await PathAsync(cancellationToken);

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (model == null)
            {
                model = await ModelRunner.LoadAsync(
                    new ModelRunnerOptions
                    {
                        ModelPath = file,
                        GpuLayers = 0,
                        ContextSize = 2048,
                        FlashAttention = FlashAttentionMode.Disabled,
                    },
                    cancellationToken);
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
}
