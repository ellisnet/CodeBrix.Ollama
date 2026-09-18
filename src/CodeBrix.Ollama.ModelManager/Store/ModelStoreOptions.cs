using System;
using System.IO;
using System.Net.Http;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Configuration for a <see cref="ModelStore"/>. Every property has a default that matches Ollama's
/// own behaviour, so <c>new ModelStore(new ModelStoreOptions())</c> operates on the same store a local
/// Ollama install would use.
/// </summary>
public sealed class ModelStoreOptions
{
    /// <summary>
    /// The store directory. When <see langword="null"/>, <see cref="ResolveDefaultStoreDirectory"/>
    /// is used: the OLLAMA_MODELS environment variable if set, otherwise <c>~/.ollama/models</c>.
    /// </summary>
    public string StoreDirectory { get; set; }

    /// <summary>
    /// The registry host a name without a host part is pulled from. Default "registry.ollama.ai".
    /// </summary>
    public string DefaultRegistryHost { get; set; } = "registry.ollama.ai";

    /// <summary>
    /// The namespace a name without a namespace part belongs to. Default "library".
    /// </summary>
    public string DefaultNamespace { get; set; } = "library";

    /// <summary>
    /// The tag a name without a tag part refers to. Default "latest".
    /// </summary>
    public string DefaultTag { get; set; } = "latest";

    /// <summary>
    /// Whether a name whose scheme is <c>http://</c> may be pulled over plain HTTP. Default
    /// <see langword="false"/>: such a name is rejected. HTTPS is always allowed.
    /// </summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>
    /// A bearer token offered as <c>Authorization: Bearer</c> when a registry answers a request with
    /// 401 and a challenge, as Ollama does; it is never sent before a challenge, and never to a host
    /// a download was redirected to. <see langword="null"/> to have no credentials to offer. Public
    /// models need none.
    /// </summary>
    public string BearerToken { get; set; }

    /// <summary>
    /// The maximum number of byte ranges of one blob downloaded concurrently. Default 16, as Ollama.
    /// </summary>
    public int MaxConcurrentParts { get; set; } = 16;

    /// <summary>
    /// The smallest byte range a blob is split into. Default 100 MB, as Ollama.
    /// </summary>
    public long MinPartSize { get; set; } = 100L * 1024 * 1024;

    /// <summary>
    /// The largest byte range a blob is split into. Default 1000 MB, as Ollama.
    /// </summary>
    public long MaxPartSize { get; set; } = 1000L * 1024 * 1024;

    /// <summary>
    /// How long a byte range may go without receiving data before its request is abandoned and retried.
    /// Default 30 seconds, as Ollama.
    /// </summary>
    public TimeSpan StallTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many times a failed byte-range request is retried, with exponential back-off, before the
    /// pull fails. Default 6, as Ollama.
    /// </summary>
    public int MaxRetries { get; set; } = 6;

    /// <summary>
    /// The interval between progress reports while a blob is downloading. Default 100 milliseconds.
    /// </summary>
    public TimeSpan ProgressInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The handler every registry request goes through. When <see langword="null"/>, a
    /// <see cref="SocketsHttpHandler"/> that follows redirects is created. Tests supply a fake handler
    /// here; nothing in the library reaches the network any other way.
    /// </summary>
    public HttpMessageHandler HttpMessageHandler { get; set; }

    /// <summary>
    /// The User-Agent header sent on registry requests. Default
    /// "CodeBrix.Ollama.ModelManager/&lt;assembly version&gt;".
    /// </summary>
    public string UserAgent { get; set; }

    /// <summary>
    /// Where the Python features of this library find CPython. Nothing here is read, and nothing of the
    /// embedding layer is loaded, until a Python feature is used; obtaining, listing, resolving and
    /// materializing models never look at it. Never <see langword="null"/>.
    /// </summary>
    public PythonOptions Python { get; set; } = new PythonOptions();

    /// <summary>
    /// Resolves the directory a default-configured store uses: the OLLAMA_MODELS environment variable
    /// when it is set and not empty, otherwise <c>~/.ollama/models</c> under the user's home directory.
    /// </summary>
    /// <returns>An absolute directory path. The directory need not exist yet.</returns>
    public static string ResolveDefaultStoreDirectory()
    {
        string fromEnvironment = Environment.GetEnvironmentVariable("OLLAMA_MODELS");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".ollama", "models");
    }
}
