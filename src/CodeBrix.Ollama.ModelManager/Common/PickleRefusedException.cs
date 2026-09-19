using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Thrown when a PyTorch checkpoint's pickle stream asks for something the restricted interpreter will not do.
/// </summary>
/// <remarks>
/// A pickle is a program, not a document: the ordinary Python interpreter for it can import any module and call
/// any callable the stream names. The reader in this library implements only the handful of opcodes a tensor
/// state dictionary needs and an allow-list of constructors, and refuses everything else here, naming the
/// construct in <see cref="Construct"/> so that a refusal can be read without a debugger.
/// </remarks>
public class PickleRefusedException : ModelManagerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PickleRefusedException"/> class.
    /// </summary>
    /// <param name="construct">The opcode, global name or shape that was refused.</param>
    /// <param name="message">The message that describes the error.</param>
    public PickleRefusedException(string construct, string message) : base(message)
    {
        Construct = construct;
    }

    /// <summary>
    /// The opcode name, the fully qualified global, or the shape of the value that the interpreter refused.
    /// </summary>
    public string Construct { get; }
}
