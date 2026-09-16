namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/exec.go (BSD-3-Clause);

/// <summary>
/// One field of a <see cref="GoStruct"/>, together with the JSON name and <c>omitempty</c> setting its
/// Go struct tag carries.
/// </summary>
internal sealed class GoStructField
{
    /// <summary>Creates a field.</summary>
    /// <param name="name">The field name a template reads it by.</param>
    /// <param name="jsonName">The name JSON marshalling writes, or <see langword="null"/> to skip the
    /// field entirely when marshalling.</param>
    /// <param name="omitEmpty">Whether JSON marshalling omits the field when its value is empty.</param>
    /// <param name="value">The value.</param>
    internal GoStructField(string name, string jsonName, bool omitEmpty, object value)
    {
        Name = name;
        JsonName = jsonName;
        OmitEmpty = omitEmpty;
        Value = value;
    }

    /// <summary>The field name a template reads it by.</summary>
    internal string Name { get; }

    /// <summary>The name JSON marshalling writes; <see langword="null"/> means the field is not marshalled.</summary>
    internal string JsonName { get; }

    /// <summary>Whether JSON marshalling omits the field when its value is empty.</summary>
    internal bool OmitEmpty { get; }

    /// <summary>The value.</summary>
    internal object Value { get; }
}
