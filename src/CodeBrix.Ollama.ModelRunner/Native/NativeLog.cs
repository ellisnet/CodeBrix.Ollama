using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: LLamaSharp LLama/Native/NativeLogConfig.cs (reference only);

/// <summary>
/// Routes the native engine's log output to a managed handler, one whole line at a time.
/// </summary>
/// <remarks>
/// <para>
/// The engine writes partial lines: a single log statement arrives as several calls, and a call may carry
/// several newlines. The text is therefore buffered here and handed on only when a line is complete, which
/// is the shape a logger wants. The idea of buffering until the newline is LLamaSharp's; the buffering here
/// is global rather than per-thread, because the engine interleaves its own lines and a per-thread buffer
/// would split one line across two of them.
/// </para>
/// <para>
/// The callback is a static function pointer, so nothing has to be pinned and nothing can be collected while
/// the engine holds it. It is installed once, when the library loads, and never removed: with no handler set
/// the lines are discarded, which is what the public contract promises as the default and is better than the
/// engine's own default of writing to standard error.
/// </para>
/// <para>
/// The handler runs on the engine's threads, outside the lock that guards the buffer, and any exception it
/// throws is swallowed - an exception must never unwind through native frames.
/// </para>
/// </remarks>
internal static unsafe class NativeLog
{
    /// <summary>How long the unfinished line may grow before it is handed on without its newline.</summary>
    /// <remarks>
    /// The engine is not obliged to end what it writes with a newline, and a tensor dump or a progress bar
    /// that never does would otherwise keep the buffer growing for the life of the process. Sixty-four
    /// kibicharacters is far longer than any line the engine writes on purpose, so the cap only ever fires
    /// on output that was never going to end.
    /// </remarks>
    internal const int MaxPendingLineLength = 64 * 1024;

    private static readonly object Gate = new object();
    private static readonly StringBuilder Line = new StringBuilder();

    private static Action<ModelRunnerLogLevel, string> handler;
    private static ModelRunnerLogLevel currentLevel = ModelRunnerLogLevel.Info;
    private static bool installed;

    /// <summary>The handler the engine's log lines are being sent to, or <see langword="null"/>.</summary>
    public static Action<ModelRunnerLogLevel, string> Handler
    {
        get
        {
            lock (Gate) return handler;
        }
    }

    /// <summary>
    /// Sends every later log line to a handler. <see langword="null"/> discards them.
    /// </summary>
    /// <param name="value">The handler, or <see langword="null"/>.</param>
    /// <remarks>
    /// Setting a handler loads the native library, so the lines written while a model loads are captured.
    /// Clearing it before anything has loaded does not.
    /// </remarks>
    public static void SetHandler(Action<ModelRunnerLogLevel, string> value)
    {
        lock (Gate)
        {
            handler = value;
            Line.Clear();
        }

        if (value != null) NativeLibraryLoader.EnsureLoaded();
    }

    /// <summary>
    /// Installs the native callback, without loading or checking anything else.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="NativeLibraryLoader.EnsureLoaded"/> once the library is in memory but before the
    /// backends start, so even the start-up lines come through the handler.
    /// </remarks>
    public static void Install()
    {
        lock (Gate)
        {
            if (installed) return;
            installed = true;
        }

        NativeMethods.llama_log_set(&OnNativeLog, null);
        NativeMethods.ggml_log_set(&OnNativeLog, null);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnNativeLog(GgmlLogLevel level, byte* text, void* userData)
    {
        try
        {
            if (text == null) return;

            string chunk = Marshal.PtrToStringUTF8((IntPtr)text);
            if (string.IsNullOrEmpty(chunk)) return;

            Dispatch(level, chunk);
        }
        catch (Exception)
        {
            // An exception must never unwind into native frames. A log line is not worth ending the process.
        }
    }

    /// <summary>Records a chunk's severity, buffers it, and hands whatever lines completed to the handler.</summary>
    /// <param name="level">The severity the engine reported for this chunk.</param>
    /// <param name="chunk">The text the engine emitted, which is rarely a whole line.</param>
    /// <remarks>
    /// The severity and the handler are both read inside the lock and the lines are emitted outside it. The
    /// severity in particular has to be captured there: it is engine-wide state that the next chunk from any
    /// thread overwrites, so reading it again while calling the handler could label a line with a later
    /// statement's severity. The handler runs outside the lock because it is application code and may take
    /// as long as it likes.
    /// </remarks>
    internal static void Dispatch(GgmlLogLevel level, string chunk)
    {
        Action<ModelRunnerLogLevel, string> target;
        ModelRunnerLogLevel lineLevel;
        string[] complete;

        lock (Gate)
        {
            // GGML_LOG_LEVEL_CONT continues the previous statement and carries no severity of its own.
            if (level != GgmlLogLevel.Cont) currentLevel = Translate(level);

            lineLevel = currentLevel;
            target = handler;
            complete = Append(chunk);
        }

        if (target == null || complete == null) return;

        foreach (string line in complete)
        {
            target(lineLevel, line);
        }
    }

    /// <summary>Adds a chunk to the buffer and returns whatever whole lines that completed, or null.</summary>
    /// <param name="chunk">The text the engine emitted.</param>
    /// <returns>The completed lines, without their newline, or <see langword="null"/> when none completed.</returns>
    /// <remarks>
    /// The caller holds <c>Gate</c>. Separated out so a test can exercise the buffering alone. An unfinished
    /// line that reaches <see cref="MaxPendingLineLength"/> is handed on as it stands rather than held for a
    /// newline that may never arrive.
    /// </remarks>
    internal static string[] Append(string chunk)
    {
        Line.Append(chunk);

        List<string> lines = null;

        int newline = IndexOfNewline(Line);
        while (newline >= 0)
        {
            int length = newline;
            if (length > 0 && Line[length - 1] == '\r') length--;

            lines ??= new List<string>();
            lines.Add(Line.ToString(0, length));
            Line.Remove(0, newline + 1);
            newline = IndexOfNewline(Line);
        }

        if (Line.Length >= MaxPendingLineLength)
        {
            lines ??= new List<string>();
            lines.Add(Line.ToString());
            Line.Clear();
        }

        return lines == null ? null : lines.ToArray();
    }

    private static int IndexOfNewline(StringBuilder builder)
    {
        for (int i = 0; i < builder.Length; i++)
        {
            if (builder[i] == '\n') return i;
        }

        return -1;
    }

    private static ModelRunnerLogLevel Translate(GgmlLogLevel level) =>
        level switch
        {
            GgmlLogLevel.Debug => ModelRunnerLogLevel.Debug,
            GgmlLogLevel.Warn => ModelRunnerLogLevel.Warning,
            GgmlLogLevel.Error => ModelRunnerLogLevel.Error,
            _ => ModelRunnerLogLevel.Info,
        };
}
