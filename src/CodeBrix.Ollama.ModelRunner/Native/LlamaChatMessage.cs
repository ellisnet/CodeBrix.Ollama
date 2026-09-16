using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// One message handed to <c>llama_chat_apply_template</c>: <c>struct llama_chat_message</c>.
/// </summary>
/// <remarks>Both pointers are NUL-terminated UTF-8 owned by the caller for the length of the call.</remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LlamaChatMessage
{
    /// <summary>The role, for example <c>"user"</c>.</summary>
    public byte* Role;

    /// <summary>The message text.</summary>
    public byte* Content;
}
