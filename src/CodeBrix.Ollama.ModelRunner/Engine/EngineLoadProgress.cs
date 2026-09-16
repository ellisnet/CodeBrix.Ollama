using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The state behind the engine's model-loading progress callback: where to report the fraction to, and the
/// token that can stop the load.
/// </summary>
/// <remarks>
/// The engine calls <c>progress_callback</c> repeatedly while it reads tensors and aborts the load as soon
/// as the callback returns false, which is the only way to cancel a load already in progress. The instance
/// is reached through a <see cref="GCHandle"/> so the callback can stay a static function pointer; the
/// handle is freed by <see cref="Dispose"/> once the load has returned, and the engine never keeps the
/// pointer beyond that call.
/// </remarks>
internal sealed unsafe class EngineLoadProgress : IDisposable
{
    private readonly IProgress<float> progress;
    private readonly CancellationToken cancellationToken;

    private GCHandle handle;

    /// <summary>Creates the state and pins it for the duration of the load.</summary>
    /// <param name="progress">Where to report the fraction, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token that cancels the load.</param>
    public EngineLoadProgress(IProgress<float> progress, CancellationToken cancellationToken)
    {
        this.progress = progress;
        this.cancellationToken = cancellationToken;
        handle = GCHandle.Alloc(this);
    }

    /// <summary>Whether the load was stopped because the token was cancelled.</summary>
    public bool WasCancelled { get; private set; }

    /// <summary>The pointer to hand the engine as <c>progress_callback_user_data</c>.</summary>
    /// <returns>The pointer.</returns>
    public void* Data()
    {
        return (void*)GCHandle.ToIntPtr(handle);
    }

    /// <summary>The function pointer to hand the engine as <c>progress_callback</c>.</summary>
    /// <returns>The callback.</returns>
    public static delegate* unmanaged[Cdecl]<float, void*, byte> Callback()
    {
        return &OnProgress;
    }

    /// <summary>Releases the handle. The load must have returned.</summary>
    public void Dispose()
    {
        if (handle.IsAllocated) handle.Free();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte OnProgress(float fraction, void* userData)
    {
        try
        {
            if (userData == null) return 1;

            GCHandle target = GCHandle.FromIntPtr((IntPtr)userData);
            if (!(target.Target is EngineLoadProgress state)) return 1;

            if (state.cancellationToken.IsCancellationRequested)
            {
                state.WasCancelled = true;
                return 0;
            }

            IProgress<float> sink = state.progress;
            if (sink != null) sink.Report(fraction);
        }
        catch (Exception)
        {
            // An exception must never unwind into native frames, and a progress report is not worth
            // abandoning a load that is otherwise going well.
        }

        return 1;
    }
}
