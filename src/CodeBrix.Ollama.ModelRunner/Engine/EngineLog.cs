using System;
using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A tap on the native engine's log that keeps the last few dozen lines, so that the cause of a failed load
/// or a failed decode - which the engine always writes to its log and never returns - can be attached to the
/// exception the caller sees.
/// </summary>
/// <remarks>
/// <para>
/// The engine offers exactly one log handler and the public contract lets an application own it through
/// <see cref="ModelRunner.SetLogHandler"/>. The tap therefore chains: <see cref="Arm"/> takes whatever
/// handler is installed as its downstream and puts itself in its place, and every line is recorded here and
/// then passed on unchanged. Arming again after an application has set its own handler picks that handler up
/// as the new downstream, so the two never fight over the single slot and no line is lost to either.
/// </para>
/// <para>
/// There is one tap but the lines it keeps are not shared. A process can have several models loaded at once,
/// and a single buffer would let one model's decode wipe the lines another model's load is about to be asked
/// for. The buffer is therefore per thread, which is per operation: every native call a model makes is made
/// on that model's own worker thread (see <see cref="EngineWorker"/>) and a probe runs the whole of its work
/// on one thread of its own, so the lines a thread records are exactly the lines of the operation it is
/// running. Lines the engine writes from one of its compute threads belong to no operation and are kept on
/// that thread, where nothing asks for them.
/// </para>
/// </remarks>
internal static class EngineLog
{
    private const int Capacity = 50;

    private static readonly object Gate = new object();
    private static readonly Action<ModelRunnerLogLevel, string> Tap = OnLine;

    [ThreadStatic]
    private static Queue<string> lines;

    private static Action<ModelRunnerLogLevel, string> downstream;

    /// <summary>Installs the tap, keeping whatever handler was installed as the one to forward to.</summary>
    public static void Arm()
    {
        Action<ModelRunnerLogLevel, string> current = NativeLog.Handler;
        if (ReferenceEquals(current, Tap)) return;

        lock (Gate)
        {
            downstream = current;
        }

        NativeLog.SetHandler(Tap);
    }

    /// <summary>
    /// Starts an operation on the calling thread: the lines this thread recorded before now are forgotten,
    /// so the tail this operation is asked for carries only its own.
    /// </summary>
    public static void Clear()
    {
        Queue<string> own = lines;
        if (own != null) own.Clear();
    }

    /// <summary>The lines this thread recorded since the last <see cref="Clear"/>, oldest first, one per line.</summary>
    /// <returns>The text, or an empty string when nothing was recorded.</returns>
    public static string Tail()
    {
        Queue<string> own = lines;
        if (own == null || own.Count == 0) return string.Empty;

        StringBuilder text = new StringBuilder();
        foreach (string line in own)
        {
            if (text.Length > 0) text.Append(Environment.NewLine);
            text.Append(line);
        }

        return text.ToString();
    }

    /// <summary>Builds a message with the engine's own last words appended, for an exception.</summary>
    /// <param name="message">What the binding knows.</param>
    /// <returns>The message, with the log tail appended when there is one.</returns>
    public static string Describe(string message)
    {
        string tail = Tail();
        if (tail.Length == 0) return message;

        return message + Environment.NewLine + "The engine's log for this operation:" + Environment.NewLine + tail;
    }

    private static void OnLine(ModelRunnerLogLevel level, string line)
    {
        Queue<string> own = lines;
        if (own == null)
        {
            own = new Queue<string>(Capacity);
            lines = own;
        }

        own.Enqueue(line);
        while (own.Count > Capacity) own.Dequeue();

        Action<ModelRunnerLogLevel, string> target;
        lock (Gate)
        {
            target = downstream;
        }

        if (target == null) return;

        try
        {
            target(level, line);
        }
        catch (Exception)
        {
            // The application's handler is not allowed to break a load by throwing; NativeLog would swallow
            // this anyway, and swallowing it here keeps the tap out of the application's stack trace.
        }
    }
}
