using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The flag the engine's abort callback reads, so a decode that has already started can be stopped.
/// </summary>
/// <remarks>
/// <para>
/// <c>llama_decode</c> cannot be interrupted from outside; evaluating the prompt of a twenty-gigabyte model
/// is a single call that can run for minutes. The engine's answer is <c>abort_callback</c>, which ggml polls
/// between graph nodes and which aborts the call as soon as it returns true. Cancelling a request therefore
/// means setting this flag, and the decode returns 2 ("aborted") within a node or two.
/// </para>
/// <para>
/// The flag lives in unmanaged memory and the callback is a static function pointer: nothing has to be
/// pinned, the callback can be called from any of the engine's compute threads at any time, and the read is
/// a single byte load that cannot tear. The callback must never allocate, lock or throw, because it runs
/// inside the compute loop of every graph node.
/// </para>
/// </remarks>
internal sealed unsafe class EngineAbortFlag : IDisposable
{
    private byte* flag;

    /// <summary>Allocates the flag, cleared.</summary>
    public EngineAbortFlag()
    {
        flag = (byte*)NativeMemory.AllocZeroed(1);
    }

    /// <summary>The pointer to hand the engine as <c>abort_callback_data</c>.</summary>
    /// <returns>The pointer, or null once the flag has been released.</returns>
    public void* Data()
    {
        return flag;
    }

    /// <summary>The function pointer to hand the engine as <c>abort_callback</c>.</summary>
    /// <returns>The callback.</returns>
    public static delegate* unmanaged[Cdecl]<void*, byte> Callback()
    {
        return &OnAbort;
    }

    /// <summary>Asks the engine to abort whatever it is computing.</summary>
    public void Raise()
    {
        byte* current = flag;
        if (current != null) *current = 1;
    }

    /// <summary>Clears the flag, so the next decode is allowed to run.</summary>
    public void Reset()
    {
        byte* current = flag;
        if (current != null) *current = 0;
    }

    /// <summary>Whether an abort has been asked for and not yet cleared.</summary>
    public bool IsRaised => flag != null && *flag != 0;

    /// <summary>
    /// Releases the flag. The context that was given the pointer must already be freed, or a compute thread
    /// could still read it.
    /// </summary>
    public void Dispose()
    {
        byte* current = flag;
        if (current == null) return;

        flag = null;
        NativeMemory.Free(current);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte OnAbort(void* data)
    {
        return data == null ? (byte)0 : *(byte*)data;
    }
}
