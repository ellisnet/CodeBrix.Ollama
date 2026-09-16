namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// How the model file's bytes are brought into memory. Mirrors the engine's load-mode enumeration.
/// </summary>
public enum ModelLoadMode
{
    /// <summary>Read the file into allocated memory.</summary>
    Read = 0,

    /// <summary>
    /// Memory-map the file, so the operating system pages the weights in on demand and can share them with
    /// other processes. The default, and the only mode that lets a model close to the size of physical memory
    /// load at all.
    /// </summary>
    MemoryMap = 1,

    /// <summary>Read the file and lock it in physical memory so it can never be swapped or compressed.</summary>
    LockInMemory = 2,

    /// <summary>Memory-map the file and lock the mapping in physical memory.</summary>
    MemoryMapAndLock = 3,

    /// <summary>Read the file with direct I/O where the platform offers it, bypassing the page cache.</summary>
    DirectIo = 4,
}
