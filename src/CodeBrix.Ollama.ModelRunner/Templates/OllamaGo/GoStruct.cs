using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/exec.go (BSD-3-Clause);

/// <summary>
/// A Go struct: an ordered set of named fields. Struct values are always true, reading a field a struct
/// does not have is an error rather than a missing key, and JSON marshalling keeps the declared order.
/// </summary>
internal sealed class GoStruct
{
    private readonly Dictionary<string, GoStructField> _byName =
        new Dictionary<string, GoStructField>(StringComparer.Ordinal);

    /// <summary>Creates an empty struct.</summary>
    /// <param name="typeName">The Go type name, used in error messages.</param>
    internal GoStruct(string typeName)
    {
        TypeName = typeName;
    }

    /// <summary>The Go type name, used in error messages.</summary>
    internal string TypeName { get; }

    /// <summary>The fields, in declaration order.</summary>
    internal List<GoStructField> Fields { get; } = new List<GoStructField>();

    /// <summary>The struct type's own <c>String</c> method, when it has one.</summary>
    internal Func<GoStruct, string> Stringer { get; set; }

    /// <summary>Adds a field that JSON marshalling writes under the given name.</summary>
    /// <param name="name">The field name a template reads it by.</param>
    /// <param name="jsonName">The name JSON marshalling writes.</param>
    /// <param name="omitEmpty">Whether JSON marshalling omits the field when its value is empty.</param>
    /// <param name="value">The value.</param>
    internal void Add(string name, string jsonName, bool omitEmpty, object value)
    {
        GoStructField field = new GoStructField(name, jsonName, omitEmpty, value);
        Fields.Add(field);
        _byName[name] = field;
    }

    /// <summary>Looks a field up by the name a template reads it by.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="field">The field, or <see langword="null"/> when the struct has no such field.</param>
    /// <returns><see langword="true"/> when the struct has the field.</returns>
    internal bool TryGetField(string name, out GoStructField field) => _byName.TryGetValue(name, out field);
}
