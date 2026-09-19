using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CodeBrix.Ollama.Core.Tests;

/// <summary>
/// Writes out everything a library compiled against CodeBrix.Ollama.Core can reach - every non-private type
/// and member of the assembly's own namespace - in one canonical form, and hashes it.
/// </summary>
/// <remarks>
/// <para>
/// THE FORM HAS TO BE THE SAME EVERYWHERE. It is built from reflection alone, sorted with the ordinal
/// comparer and formatted with the invariant culture, so the same source produces the same hash on any
/// machine, in any configuration and in any build order. Nothing that legitimately differs between builds -
/// the assembly's version, its module identifier, the order reflection happens to return members in - goes
/// into it.
/// </para>
/// <para>
/// WHAT IS LEFT OUT. Anything the compiler wrote for itself: a type outside the
/// <c>CodeBrix.Ollama.Core</c> namespace (the embedded attributes the compiler emits and
/// <c>&lt;PrivateImplementationDetails&gt;</c> have no namespace or one of Microsoft's), a type marked
/// compiler-generated, a nested private type, and every private member. A library cannot reach any of them,
/// so none of them is part of the contract.
/// </para>
/// </remarks>
internal static class CoreSurface
{
    /// <summary>The namespace every type of the shared project lives in.</summary>
    internal const string Namespace = "CodeBrix.Ollama.Core";

    /// <summary>Writes the whole non-private surface out, one member to a line, in a fixed order.</summary>
    /// <returns>The canonical text.</returns>
    internal static string Describe()
    {
        Assembly assembly = typeof(CoreContract).Assembly;
        List<string> lines = new List<string>();
        foreach (Type type in assembly.GetTypes()
            .Where(IsPartOfTheSurface)
            .OrderBy(type => type.ToString(), StringComparer.Ordinal))
        {
            lines.Add(DescribeType(type));
            List<string> members = new List<string>();
            foreach (MemberInfo member in type.GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly))
            {
                string described = DescribeMember(member);
                if (described != null)
                {
                    members.Add(described);
                }
            }

            members.Sort(StringComparer.Ordinal);
            lines.AddRange(members);
        }

        return string.Join("\n", lines) + "\n";
    }

    /// <summary>Hashes what <see cref="Describe"/> wrote.</summary>
    /// <returns>The SHA-256 of the canonical text, lower-case hexadecimal.</returns>
    internal static string Hash()
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(Describe()));
        return Convert.ToHexStringLower(digest);
    }

    private static bool IsPartOfTheSurface(Type type)
    {
        if (!string.Equals(type.Namespace, Namespace, StringComparison.Ordinal))
        {
            return false;
        }

        if (type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
        {
            return false;
        }

        return !type.IsNestedPrivate;
    }

    private static string DescribeType(Type type)
    {
        string kind = type.IsEnum ? "enum"
            : type.IsInterface ? "interface"
            : type.IsValueType ? "struct"
            : "class";
        //Only the interfaces the type itself adds. An enum's base, System.Enum, brings IComparable,
        //IConvertible, IFormattable and ISpanFormattable with it, and what the framework hangs on its own
        //base types is not this project's surface.
        IEnumerable<Type> inherited = type.BaseType == null
            ? Enumerable.Empty<Type>()
            : type.BaseType.GetInterfaces();
        string bases = string.Join(", ", type.GetInterfaces().Except(inherited)
            .Select(each => each.ToString())
            .OrderBy(each => each, StringComparer.Ordinal));
        return "TYPE " + TypeVisibility(type) + " " + kind + " " + type + " base="
            + (type.BaseType == null ? "-" : type.BaseType.ToString())
            + " implements=" + (bases.Length == 0 ? "-" : bases);
    }

    private static string DescribeMember(MemberInfo member)
    {
        if (member is Type)
        {
            //Nested types are walked by the type loop of their own.
            return null;
        }

        if (member is FieldInfo field)
        {
            if (field.IsPrivate
                || field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
            {
                return null;
            }

            string constant = field.IsLiteral
                ? " = " + Convert.ToString(field.GetRawConstantValue(), CultureInfo.InvariantCulture)
                : string.Empty;
            return "  FIELD " + Visibility(field.Attributes & FieldAttributes.FieldAccessMask)
                + (field.IsStatic ? " static" : string.Empty)
                + (field.IsInitOnly ? " readonly" : string.Empty)
                + " " + field.FieldType + " " + field.Name + constant;
        }

        if (member is ConstructorInfo constructor)
        {
            return constructor.IsPrivate
                ? null
                : "  CTOR " + Visibility(constructor.Attributes & MethodAttributes.MemberAccessMask)
                    + " (" + Parameters(constructor) + ")";
        }

        if (member is MethodInfo method)
        {
            return method.IsPrivate
                ? null
                : "  METHOD " + Visibility(method.Attributes & MethodAttributes.MemberAccessMask)
                    + (method.IsStatic ? " static" : string.Empty)
                    + (method.IsAbstract ? " abstract" : string.Empty)
                    + (method.IsVirtual && !method.IsAbstract ? " virtual" : string.Empty)
                    + " " + method.ReturnType + " " + method.Name + "(" + Parameters(method) + ")";
        }

        if (member is PropertyInfo property)
        {
            string accessors = (Accessor("get", property.GetMethod) + Accessor("set", property.SetMethod))
                .Trim();
            return accessors.Length == 0
                ? null
                : "  PROPERTY " + property.PropertyType + " " + property.Name + " { " + accessors + " }";
        }

        if (member is EventInfo declared)
        {
            return declared.AddMethod == null || declared.AddMethod.IsPrivate
                ? null
                : "  EVENT " + declared.EventHandlerType + " " + declared.Name;
        }

        return "  MEMBER " + member.MemberType + " " + member.Name;
    }

    private static string Accessor(string name, MethodInfo accessor) =>
        accessor == null || accessor.IsPrivate
            ? string.Empty
            : name + ":" + Visibility(accessor.Attributes & MethodAttributes.MemberAccessMask) + " ";

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters()
            .Select(each => (each.ParameterType.IsByRef ? "byref " : string.Empty)
                + each.ParameterType + " " + each.Name));

    private static string TypeVisibility(Type type)
    {
        if (type.IsNested)
        {
            return type.IsNestedPublic ? "public"
                : type.IsNestedFamily ? "protected"
                : type.IsNestedFamORAssem ? "protected-internal"
                : type.IsNestedFamANDAssem ? "private-protected"
                : "internal";
        }

        return type.IsPublic ? "public" : "internal";
    }

    private static string Visibility(FieldAttributes access) => access switch
    {
        FieldAttributes.Public => "public",
        FieldAttributes.Family => "protected",
        FieldAttributes.FamORAssem => "protected-internal",
        FieldAttributes.FamANDAssem => "private-protected",
        _ => "internal",
    };

    private static string Visibility(MethodAttributes access) => access switch
    {
        MethodAttributes.Public => "public",
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected-internal",
        MethodAttributes.FamANDAssem => "private-protected",
        _ => "internal",
    };
}
