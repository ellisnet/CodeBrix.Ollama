using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Thrown when one of the Python scripts that ship inside this package fails while it runs, for a reason
/// other than a module that is not installed. The message carries what Python said, and the properties
/// name the script and the feature that was running it.
/// </summary>
public class PythonScriptException : ModelManagerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PythonScriptException"/> class.
    /// </summary>
    /// <param name="feature">The feature that was running the script - "exporting to ONNX".</param>
    /// <param name="scriptName">The script that failed, such as <c>probe_version.py</c>.</param>
    /// <param name="pythonMessage">What Python said.</param>
    /// <param name="innerException">The exception that caused this one, when there is one.</param>
    public PythonScriptException(string feature, string scriptName, string pythonMessage, Exception innerException)
        : base(BuildMessage(feature, scriptName, pythonMessage), innerException)
    {
        Feature = feature;
        ScriptName = scriptName;
        PythonMessage = pythonMessage;
    }

    /// <summary>
    /// The feature that was running the script, as the caller named it.
    /// </summary>
    public string Feature { get; }

    /// <summary>
    /// The script that failed.
    /// </summary>
    public string ScriptName { get; }

    /// <summary>
    /// What Python said, with no wrapping.
    /// </summary>
    public string PythonMessage { get; }

    /// <summary>
    /// Composes the message: which script, what Python said, and what was running it.
    /// </summary>
    /// <param name="feature">The feature that was running the script.</param>
    /// <param name="scriptName">The script that failed.</param>
    /// <param name="pythonMessage">What Python said.</param>
    /// <returns>The complete message.</returns>
    private static string BuildMessage(string feature, string scriptName, string pythonMessage)
    {
        string script = string.IsNullOrWhiteSpace(scriptName) ? "(unnamed script)" : scriptName.Trim();
        string said = string.IsNullOrWhiteSpace(pythonMessage) ? "(no message)" : pythonMessage.Trim();
        string running = string.IsNullOrWhiteSpace(feature)
            ? string.Empty
            : " It was running for " + feature.Trim() + ".";
        return "The Python script '" + script + "' failed: " + said + "." + running;
    }
}
