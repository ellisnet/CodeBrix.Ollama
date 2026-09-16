using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: CodeBrix.Ollama llama-native-tools/wrapper/codebrix_llama.c;

/// <summary>
/// The two entry points this repository adds to the otherwise unchanged upstream surface, and the static
/// constructor that installs the library resolver.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>
    /// Puts the resolver in place before any import can run, so an import called without going through
    /// <see cref="NativeLibraryLoader.EnsureLoaded" /> first still finds the library under runtimes/&lt;rid&gt;/native/.
    /// </summary>
    static NativeMethods()
    {
        NativeLibraryLoader.EnsureResolverInstalled();
    }

    /// <summary>The library's build record: upstream commit and tag, the runtime identifier, build date and host.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* codebrix_llama_build_info();

    /// <summary>The runtime identifier the library was built for, for example "osx-x64".</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* codebrix_llama_rid();
}
