using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The Python side of this library, in four calls. Most of what CodeBrix.Ollama.ModelManager does needs
/// no Python at all - obtaining a model, listing, resolving and materializing never touch it - and none
/// of the embedding layer is loaded until something here is called.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Check"/> describes what this process would use and never throws. <see cref="Require"/> is
/// what a feature calls before it runs Python, and throws a message a person can act on.
/// <see cref="Shutdown"/> ends Python for the process, once, at the end.
/// </para>
/// <para>
/// These calls are synchronous although they do real work, which is the one deliberate exception to this
/// library's async-only rule: an interpreter belongs to the process, its lifetime calls are synchronous
/// by nature, and nothing may be awaited while the interpreter lock is held.
/// </para>
/// </remarks>
public static class PythonSupport
{
    /// <summary>
    /// Whether an interpreter is running in this process, whoever started it.
    /// </summary>
    public static bool IsInitialized => PythonHost.IsInitialized;

    /// <summary>
    /// Which side owns the interpreter: this library, the host application, or nobody yet. It keeps
    /// reporting <see cref="PythonEngineOwner.ModelManager"/> after <see cref="Shutdown"/>, because that
    /// is who owned the interpreter that was ended.
    /// </summary>
    public static PythonEngineOwner Owner => PythonHost.Owner;

    /// <summary>
    /// Describes what Python this process would use, and whether each named module imports. It NEVER
    /// throws: a machine with no CPython at all produces a report saying so.
    /// </summary>
    /// <param name="options">
    /// Where to look for CPython. <see langword="null"/> is the same as a default
    /// <see cref="PythonOptions"/>, which leaves the environment variables to answer.
    /// </param>
    /// <param name="requiredModules">
    /// The modules to look for. With NONE, no interpreter is started and none is needed: the report is
    /// the library's path, whether it loads, its version and the environment in effect. With ANY, an
    /// interpreter IS started - under the ownership rules, exactly as a real feature would start it - and
    /// each module is imported inside it.
    /// </param>
    /// <returns>What was found, with a sentence in <see cref="PythonSupportReport.Problems"/> for
    /// everything that is wrong.</returns>
    public static PythonSupportReport Check(PythonOptions options, params string[] requiredModules)
        => PythonHost.Check(options, requiredModules);

    /// <summary>
    /// Checks the same things as <see cref="Check"/> and throws when a feature cannot run.
    /// </summary>
    /// <param name="options">Where to look for CPython; <see langword="null"/> for the defaults.</param>
    /// <param name="feature">
    /// The feature that needs Python, named as a phrase - "exporting to ONNX". It ends every message.
    /// </param>
    /// <param name="requiredModules">The modules the feature imports.</param>
    /// <exception cref="InvalidOperationException">
    /// Python has been shut down for this process by an earlier <see cref="Shutdown"/>.
    /// </exception>
    /// <exception cref="PythonNotAvailableException">
    /// No CPython shared library was found, the one that was found will not load, or its version is
    /// outside the supported range.
    /// </exception>
    /// <exception cref="PythonModuleNotInstalledException">
    /// A module the feature needs is not installed in the interpreter that would run it. The message
    /// carries the command that installs it there.
    /// </exception>
    public static void Require(PythonOptions options, string feature, params string[] requiredModules)
        => PythonHost.Require(options, feature, requiredModules);

    /// <summary>
    /// Ends Python for this process. Call it once, when the application is finished with every Python
    /// feature - not between operations, and never in the hope of starting a fresh interpreter, which
    /// cannot be done.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is idempotent, and it does NOTHING when the host application owns the interpreter: several
    /// stores may share a process, and a library that shut down somebody else's interpreter would break
    /// the rest of the application. Disposing a <see cref="ModelStore"/> does not call it either.
    /// </para>
    /// <para>
    /// When this library owns the interpreter it also requests bounded process-exit shutdown from the
    /// embedding layer. Call this method explicitly for reliable teardown after native Python modules
    /// such as torch; their finalization can interact with process shutdown beyond that fallback's scope.
    /// </para>
    /// </remarks>
    public static void Shutdown() => PythonHost.Shutdown();
}
