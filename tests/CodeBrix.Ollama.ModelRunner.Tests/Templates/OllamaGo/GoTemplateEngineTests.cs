using System;
using System.Text;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Tests for the Go text/template language itself - the port of Go's scanner, parser and executor that
/// renders an Ollama chat template.
/// </summary>
public sealed class GoTemplateEngineTests
{
    /// <summary>Literal text passes through untouched.</summary>
    [Fact]
    public void Render_literal_text()
    {
        //Act
        var rendered = Render("hello world", null);

        //Assert
        rendered.Should().Be("hello world");
    }

    /// <summary>Trim markers eat the whitespace on the side they point at, newlines included.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="expected">The expected output.</param>
    [Theory]
    [InlineData("a\n  {{- \"b\" }}", "ab")]
    [InlineData("{{ \"a\" -}}  \n b", "ab")]
    [InlineData("a\n{{- \"b\" -}}\nc", "abc")]
    [InlineData("a {{ \"b\" }} c", "a b c")]
    [InlineData("a{{/* a comment */}}b", "ab")]
    [InlineData("a\n{{- /* a comment */ -}}\nb", "ab")]
    public void Render_trim_markers_and_comments(string template, string expected)
    {
        //Act
        var rendered = Render(template, null);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>Constants of every kind render the way Go renders them.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="expected">The expected output.</param>
    [Theory]
    [InlineData("{{ \"text\" }}", "text")]
    [InlineData("{{ `raw\\ntext` }}", "raw\\ntext")]
    [InlineData("{{ 42 }}", "42")]
    [InlineData("{{ -7 }}", "-7")]
    [InlineData("{{ 0x10 }}", "16")]
    [InlineData("{{ 2.5 }}", "2.5")]
    [InlineData("{{ 1e6 }}", "1e+06")]
    [InlineData("{{ true }}", "true")]
    [InlineData("{{ false }}", "false")]
    [InlineData("{{ 'A' }}", "65")]
    [InlineData("{{ print nil }}", "<nil>")]
    public void Render_constants(string template, string expected)
    {
        //Act
        var rendered = Render(template, null);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>Field chains walk structs and maps alike.</summary>
    [Fact]
    public void Render_field_chains()
    {
        //Arrange
        var inner = new GoMap();
        inner.Set("Name", "ada");
        var data = new GoMap();
        data.Set("User", inner);

        //Act
        var rendered = Render("{{ .User.Name }}", data);

        //Assert
        rendered.Should().Be("ada");
    }

    /// <summary>A key a map does not have renders as Go's placeholder and counts as false.</summary>
    [Fact]
    public void Render_missing_key_yields_no_value()
    {
        //Act
        var rendered = Render("[{{ .Missing }}][{{ if .Missing }}yes{{ else }}no{{ end }}]", new GoMap());

        //Assert
        rendered.Should().Be("[<no value>][no]");
    }

    /// <summary>Variables can be declared, read and reassigned.</summary>
    [Fact]
    public void Render_variables()
    {
        //Act
        var rendered = Render("{{ $x := \"a\" }}{{ $x }}{{ $x = \"b\" }}{{ $x }}", null);

        //Assert
        rendered.Should().Be("ab");
    }

    /// <summary>The root value stays reachable through the dollar variable inside a range.</summary>
    [Fact]
    public void Render_root_variable_inside_range()
    {
        //Arrange
        var data = new GoMap();
        data.Set("Tag", "t");
        data.Set("Items", new GoSlice(new object[] { "a", "b" }));

        //Act
        var rendered = Render("{{ range .Items }}{{ $.Tag }}{{ . }}{{ end }}", data);

        //Assert
        rendered.Should().Be("tatb");
    }

    /// <summary>A pipeline passes each stage's value on as the last argument of the next.</summary>
    [Fact]
    public void Render_pipeline()
    {
        //Act
        var rendered = Render("{{ \"x\" | printf \"<%s>\" | printf \"[%s]\" }}", null);

        //Assert
        rendered.Should().Be("[<x>]");
    }

    /// <summary>A parenthesised sub-pipeline is evaluated first.</summary>
    [Fact]
    public void Render_parenthesised_sub_pipeline()
    {
        //Act
        var rendered = Render("{{ printf \"%d\" (len \"abcd\") }}", null);

        //Assert
        rendered.Should().Be("4");
    }

    /// <summary>An if-else-if chain picks the first true branch.</summary>
    /// <param name="value">The value to test.</param>
    /// <param name="expected">The branch that should run.</param>
    [Theory]
    [InlineData("a", "first")]
    [InlineData("b", "second")]
    [InlineData("c", "other")]
    public void Render_if_else_chain(string value, string expected)
    {
        //Arrange
        var data = new GoMap();
        data.Set("V", value);

        //Act
        var rendered = Render("{{ if eq .V \"a\" }}first{{ else if eq .V \"b\" }}second{{ else }}other{{ end }}", data);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>With sets dot when its value is true, and falls to else when it is not.</summary>
    [Fact]
    public void Render_with_and_else()
    {
        //Arrange
        var data = new GoMap();
        data.Set("Some", "here");
        data.Set("None", string.Empty);

        //Act
        var rendered = Render("{{ with .Some }}[{{ . }}]{{ end }}{{ with .None }}[{{ . }}]{{ else }}empty{{ end }}",
            data);

        //Assert
        rendered.Should().Be("[here]empty");
    }

    /// <summary>Range offers the index and the element, and falls to else when there is nothing to walk.</summary>
    [Fact]
    public void Render_range_forms()
    {
        //Arrange
        var data = new GoMap();
        data.Set("Items", new GoSlice(new object[] { "a", "b" }));
        data.Set("Empty", new GoSlice());

        //Act
        var rendered = Render("{{ range $i, $v := .Items }}{{ $i }}{{ $v }}{{ end }}"
            + "|{{ range .Empty }}x{{ else }}none{{ end }}", data);

        //Assert
        rendered.Should().Be("0a1b|none");
    }

    /// <summary>Ranging a map visits its keys in sorted order.</summary>
    [Fact]
    public void Render_range_over_map_is_sorted()
    {
        //Arrange
        var map = new GoMap();
        map.Set("b", 2L);
        map.Set("a", 1L);
        map.Set("c", 3L);
        var data = new GoMap();
        data.Set("M", map);

        //Act
        var rendered = Render("{{ range $k, $v := .M }}{{ $k }}={{ $v }};{{ end }}", data);

        //Assert
        rendered.Should().Be("a=1;b=2;c=3;");
    }

    /// <summary>Break leaves a range loop and continue skips the rest of one turn.</summary>
    [Fact]
    public void Render_range_break_and_continue()
    {
        //Arrange
        var data = new GoMap();
        data.Set("Items", new GoSlice(new object[] { 1L, 2L, 3L, 4L }));

        //Act
        var broken = Render("{{ range .Items }}{{ if eq . 3 }}{{ break }}{{ end }}{{ . }}{{ end }}", data);
        var skipped = Render("{{ range .Items }}{{ if eq . 3 }}{{ continue }}{{ end }}{{ . }}{{ end }}", data);

        //Assert
        broken.Should().Be("12");
        skipped.Should().Be("124");
    }

    /// <summary>Ranging an integer counts up from zero.</summary>
    [Fact]
    public void Render_range_over_integer()
    {
        //Act
        var rendered = Render("{{ range 3 }}{{ . }}{{ end }}", null);

        //Assert
        rendered.Should().Be("012");
    }

    /// <summary>The boolean builtins return the deciding argument, not a bare true or false.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="expected">The expected output.</param>
    [Theory]
    [InlineData("{{ and \"a\" \"b\" }}", "b")]
    [InlineData("{{ and \"\" \"b\" }}", "")]
    [InlineData("{{ or \"\" \"b\" }}", "b")]
    [InlineData("{{ or \"a\" \"b\" }}", "a")]
    [InlineData("{{ not \"\" }}", "true")]
    [InlineData("{{ not \"a\" }}", "false")]
    public void Render_and_or_not(string template, string expected)
    {
        //Act
        var rendered = Render(template, null);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>The comparison builtins follow Go's rules, including eq with several candidates.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="expected">The expected output.</param>
    [Theory]
    [InlineData("{{ eq 1 2 1 }}", "true")]
    [InlineData("{{ eq 1 2 3 }}", "false")]
    [InlineData("{{ eq \"a\" \"a\" }}", "true")]
    [InlineData("{{ ne 1 2 }}", "true")]
    [InlineData("{{ lt 1 2 }}", "true")]
    [InlineData("{{ le 2 2 }}", "true")]
    [InlineData("{{ gt 3 2 }}", "true")]
    [InlineData("{{ ge 2 3 }}", "false")]
    [InlineData("{{ lt \"a\" \"b\" }}", "true")]
    public void Render_comparisons(string template, string expected)
    {
        //Act
        var rendered = Render(template, null);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>The length, indexing and slicing builtins work on strings, slices and maps.</summary>
    [Fact]
    public void Render_len_index_and_slice()
    {
        //Arrange
        var map = new GoMap();
        map.Set("k", "v");
        var data = new GoMap();
        data.Set("Items", new GoSlice(new object[] { "a", "b", "c" }));
        data.Set("M", map);
        data.Set("S", "hello");

        //Act
        var rendered = Render("{{ len .Items }}|{{ index .Items 1 }}|{{ index .M \"k\" }}"
            + "|{{ len (slice .Items 1) }}|{{ slice .S 1 3 }}|{{ len .S }}", data);

        //Assert
        rendered.Should().Be("3|b|v|2|el|5");
    }

    /// <summary>Length counts a string's bytes, as Go's len does.</summary>
    [Fact]
    public void Render_len_counts_utf8_bytes()
    {
        //Arrange
        var data = new GoMap();
        data.Set("S", "café");

        //Act
        var rendered = Render("{{ len .S }}", data);

        //Assert
        rendered.Should().Be("5");
    }

    /// <summary>The printing builtins follow Go's spacing rules.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="expected">The expected output.</param>
    [Theory]
    [InlineData("{{ print \"a\" \"b\" }}", "ab")]
    [InlineData("{{ print 1 2 }}", "1 2")]
    [InlineData("{{ println \"a\" \"b\" }}", "a b\n")]
    [InlineData("{{ printf \"%s-%d-%q-%v\" \"a\" 7 \"b\" true }}", "a-7-\"b\"-true")]
    [InlineData("{{ printf \"%5d|%-5d|%05d\" 42 42 42 }}", "   42|42   |00042")]
    [InlineData("{{ printf \"%.2f\" 1.005 }}", "1.00")]
    [InlineData("{{ printf \"100%%\" }}", "100%")]
    public void Render_print_builtins(string template, string expected)
    {
        //Act
        var rendered = Render(template, null);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>Truth follows Go: zero values are false and everything else is true.</summary>
    /// <param name="key">The key to test.</param>
    /// <param name="expected">Whether it counts as true.</param>
    [Theory]
    [InlineData("EmptyString", "no")]
    [InlineData("Text", "yes")]
    [InlineData("Zero", "no")]
    [InlineData("One", "yes")]
    [InlineData("False", "no")]
    [InlineData("True", "yes")]
    [InlineData("EmptySlice", "no")]
    [InlineData("Slice", "yes")]
    [InlineData("EmptyMap", "no")]
    [InlineData("Map", "yes")]
    [InlineData("Nil", "no")]
    public void Render_truthiness(string key, string expected)
    {
        //Arrange
        var filled = new GoMap();
        filled.Set("k", "v");
        var data = new GoMap();
        data.Set("EmptyString", string.Empty);
        data.Set("Text", "x");
        data.Set("Zero", 0L);
        data.Set("One", 1L);
        data.Set("False", false);
        data.Set("True", true);
        data.Set("EmptySlice", new GoSlice());
        data.Set("Slice", new GoSlice(new object[] { 1L }));
        data.Set("EmptyMap", new GoMap());
        data.Set("Map", filled);
        data.Set("Nil", null);

        //Act
        var rendered = Render("{{ if ." + key + " }}yes{{ else }}no{{ end }}", data);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>The json function escapes the way Go's encoding/json does and sorts map keys.</summary>
    [Fact]
    public void Render_json_function()
    {
        //Arrange
        var map = new GoMap();
        map.Set("b", "<tag>");
        map.Set("a", 1L);
        var data = new GoMap();
        data.Set("M", map);

        //Act
        var rendered = Render("{{ json .M }}", data);

        //Assert
        rendered.Should().Be("{\"a\":1,\"b\":\"\\u003ctag\\u003e\"}");
    }

    /// <summary>A named template can be defined and invoked.</summary>
    [Fact]
    public void Render_define_and_template()
    {
        //Act
        var rendered = Render("{{ define \"greet\" }}hi {{ . }}{{ end }}{{ template \"greet\" \"ada\" }}", null);

        //Assert
        rendered.Should().Be("hi ada");
    }

    /// <summary>A call to a function the template does not have is rejected at parse time.</summary>
    [Fact]
    public void Parse_with_an_unknown_function_throws()
    {
        //Act
        var thrown = Record.Exception(() => Render("{{ nosuchfunc 1 }}", null));

        //Assert
        thrown.Should().BeOfType<ChatTemplateException>();
        thrown.Message.Should().Contain("not defined");
    }

    /// <summary>A variable that was never declared is rejected at parse time.</summary>
    [Fact]
    public void Parse_with_an_undefined_variable_throws()
    {
        //Act
        var thrown = Record.Exception(() => Render("{{ $nope }}", null));

        //Assert
        thrown.Should().BeOfType<ChatTemplateException>();
        thrown.Message.Should().Contain("undefined variable");
    }

    /// <summary>%f, %e and %E default to a precision of six; only %g, %G and %v use the shortest form.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="expected">The expected output.</param>
    [Theory]
    [InlineData("{{ printf \"%f\" 2.5 }}", "2.500000")]
    [InlineData("{{ printf \"%f\" 1.0 }}", "1.000000")]
    [InlineData("{{ printf \"%e\" 1234.5678 }}", "1.234568e+03")]
    [InlineData("{{ printf \"%E\" 1234.5678 }}", "1.234568E+03")]
    [InlineData("{{ printf \"%g\" 2.5 }}", "2.5")]
    [InlineData("{{ printf \"%v\" 2.5 }}", "2.5")]
    [InlineData("{{ printf \"%.2f\" 2.5 }}", "2.50")]
    public void Render_printf_float_default_precision(string template, string expected)
    {
        //Act
        var rendered = Render(template, null);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>A precision truncates the input of %s and %q, counted in runes.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="expected">The expected output.</param>
    [Theory]
    [InlineData("{{ printf \"%.3s\" \"abcdef\" }}", "abc")]
    [InlineData("{{ printf \"%.9s\" \"abcdef\" }}", "abcdef")]
    [InlineData("{{ printf \"%.2q\" \"abcdef\" }}", "\"ab\"")]
    [InlineData("{{ printf \"%.2s\" \"\u4e2d\u6587abc\" }}", "\u4e2d\u6587")]
    public void Render_printf_string_precision(string template, string expected)
    {
        //Act
        var rendered = Render(template, null);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>%q on an integer is a quoted rune, and on a value that is no rune it is the placeholder.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="expected">The expected output.</param>
    [Theory]
    [InlineData("{{ printf \"%q\" 65 }}", "'A'")]
    [InlineData("{{ printf \"%q\" 0x4e2d }}", "'\u4e2d'")]
    [InlineData("{{ printf \"%q\" -1 }}", "'\ufffd'")]
    [InlineData("{{ printf \"%q\" true }}", "%!q(bool=true)")]
    public void Render_printf_quoted_rune(string template, string expected)
    {
        //Act
        var rendered = Render(template, null);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>%d keeps all 64 bits of an unsigned value above the signed maximum.</summary>
    [Fact]
    public void Render_printf_large_unsigned_integer()
    {
        //Arrange
        var data = new GoMap();
        data.Set("N", ulong.MaxValue);

        //Act
        var rendered = Render("{{ printf \"%d|%x\" .N .N }}", data);

        //Assert
        rendered.Should().Be("18446744073709551615|ffffffffffffffff");
    }

    /// <summary>A width pads to a count of runes, not of UTF-16 code units.</summary>
    [Fact]
    public void Render_printf_width_counts_runes()
    {
        //Arrange
        var data = new GoMap();
        data.Set("S", "\U0001D11E");

        //Act
        var rendered = Render("{{ printf \"%3s|\" .S }}", data);

        //Assert
        rendered.Should().Be("  \U0001D11E|");
    }

    /// <summary>The plus and hash flags of %v name a struct's fields, and the hash flag quotes strings.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="expected">The expected output.</param>
    [Theory]
    [InlineData("{{ printf \"%v\" .S }}", "{user hi}")]
    [InlineData("{{ printf \"%+v\" .S }}", "{Role:user Content:hi}")]
    [InlineData("{{ printf \"%#v\" .S }}", "api.Message{Role:\"user\", Content:\"hi\"}")]
    public void Render_printf_struct_flags(string template, string expected)
    {
        //Arrange
        var message = new GoStruct("api.Message");
        message.Add("Role", "role", false, "user");
        message.Add("Content", "content", false, "hi");
        var data = new GoMap();
        data.Set("S", message);

        //Act
        var rendered = Render(template, data);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>NaN is equal to nothing, itself included, because Go compares floats with ==.</summary>
    [Fact]
    public void Render_eq_with_nan_is_false()
    {
        //Arrange
        var data = new GoMap();
        data.Set("N", double.NaN);

        //Act
        var rendered = Render("{{ eq .N .N }}|{{ ne .N .N }}", data);

        //Assert
        rendered.Should().Be("false|true");
    }

    /// <summary>Go's text/template will not range over a string, so neither will we.</summary>
    [Fact]
    public void Render_range_over_a_string_throws()
    {
        //Arrange
        var data = new GoMap();
        data.Set("S", "ab");

        //Act
        var thrown = Record.Exception(() => Render("{{ range .S }}x{{ end }}", data));

        //Assert
        thrown.Should().BeOfType<ChatTemplateException>();
        thrown.Message.Should().Contain("range can't iterate over");
    }

    /// <summary>Ranging an unsigned integer counts up from zero, as ranging a signed one does.</summary>
    [Fact]
    public void Render_range_over_unsigned_integer()
    {
        //Arrange
        var data = new GoMap();
        data.Set("N", 3UL);

        //Act
        var rendered = Render("{{ range .N }}{{ . }}{{ end }}", data);

        //Assert
        rendered.Should().Be("012");
    }

    /// <summary>An identifier character is a Unicode letter or digit; an astral symbol is neither.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="expected">The error the scanner or parser reports.</param>
    [Theory]
    [InlineData("{{ \U0001F600 }}", "unrecognized character in action")]
    [InlineData("{{ \U0001D400 }}", "not defined")]
    public void Parse_with_an_astral_identifier_character(string template, string expected)
    {
        //Act
        var thrown = Record.Exception(() => Render(template, null));

        //Assert
        thrown.Should().BeOfType<ChatTemplateException>();
        thrown.Message.Should().Contain(expected);
    }

    /// <summary>Branches nested past the depth cap are reported rather than overflowing the stack.</summary>
    [Fact]
    public void Parse_with_branches_nested_too_deeply_throws()
    {
        //Arrange
        var template = new StringBuilder();
        template.Insert(0, "{{ if 1 }}", 600);
        template.Insert(template.Length, "{{ end }}", 600);

        //Act
        var thrown = Record.Exception(() => Render(template.ToString(), null));

        //Assert
        thrown.Should().BeOfType<ChatTemplateException>();
        thrown.Message.Should().Contain("max nesting depth exceeded");
    }

    /// <summary>Branches nested inside the depth cap still parse and render.</summary>
    [Fact]
    public void Parse_with_branches_nested_within_the_cap_renders()
    {
        //Arrange
        var template = new StringBuilder();
        template.Insert(0, "{{ if 1 }}", 100);
        template.Append('x');
        template.Insert(template.Length, "{{ end }}", 100);

        //Act
        var rendered = Render(template.ToString(), null);

        //Assert
        rendered.Should().Be("x");
    }

    /// <summary>Template text holding an unpaired surrogate is refused, not turned into mojibake.</summary>
    [Fact]
    public void Parse_with_an_unpaired_surrogate_throws()
    {
        //Act
        var thrown = Record.Exception(() => Render("a\ud800b", null));

        //Assert
        thrown.Should().BeOfType<ChatTemplateException>();
        thrown.Message.Should().Contain("unpaired UTF-16 surrogate");
    }

    /// <summary>Quoting a rune that is not valid UTF-8 yields the replacement character, as Go does.</summary>
    /// <param name="codePoint">The code point to quote.</param>
    /// <param name="expected">The expected quoted text.</param>
    [Theory]
    [InlineData(0xD800, "'\ufffd'")]
    [InlineData(0x110000, "'\ufffd'")]
    [InlineData(65, "'A'")]
    public void QuoteRune_replaces_an_invalid_rune(int codePoint, string expected)
    {
        //Act
        var quoted = GoQuote.QuoteRune(codePoint);

        //Assert
        quoted.Should().Be(expected);
    }

    /// <summary>A hex or octal escape is a raw byte, and a run of them is decoded as one UTF-8 sequence.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="expected">The expected output.</param>
    [Theory]
    [InlineData("{{ \"\\xe4\\xb8\\xad\" }}", "\u4e2d")]
    [InlineData("{{ \"\\303\\251\" }}", "\u00e9")]
    [InlineData("{{ \"a\\x62c\" }}", "abc")]
    [InlineData("{{ \"\\u4e2d\" }}", "\u4e2d")]
    public void Render_string_constant_with_byte_escapes(string template, string expected)
    {
        //Act
        var rendered = Render(template, null);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>%q escapes whatever Go's strconv.IsPrint calls unprintable, spaces and formats included.</summary>
    /// <param name="value">The text to quote.</param>
    /// <param name="expected">The expected quoted text.</param>
    [Theory]
    [InlineData("a\u00a0b", "\"a\\u00a0b\"")]
    [InlineData("a\u200bb", "\"a\\u200bb\"")]
    [InlineData("a\u200eb", "\"a\\u200eb\"")]
    [InlineData("a\u2028b", "\"a\\u2028b\"")]
    [InlineData("a\u2029b", "\"a\\u2029b\"")]
    [InlineData("a\u00adb", "\"a\\u00adb\"")]
    [InlineData("a\u0378b", "\"a\\u0378b\"")]
    [InlineData("a\u00e9 b", "\"a\u00e9 b\"")]
    [InlineData("a\U0001F600b", "\"a\U0001F600b\"")]
    public void Render_printf_quoting_follows_is_print(string value, string expected)
    {
        //Arrange
        var data = new GoMap();
        data.Set("S", value);

        //Act
        var rendered = Render("{{ printf \"%q\" .S }}", data);

        //Assert
        rendered.Should().Be(expected);
    }

    /// <summary>The date functions ignore string arguments and refuse anything else, as Go's call does.</summary>
    [Fact]
    public void Render_current_date_argument_types()
    {
        //Arrange
        OllamaTemplateFuncs.TodayOverride = new DateTime(2024, 3, 4, 0, 0, 0, DateTimeKind.Local);
        try
        {
            //Act
            var plain = Render("{{ currentDate }}|{{ yesterdayDate }}", null);
            var withText = Render("{{ currentDate \"2006-01-02\" }}", null);
            var thrown = Record.Exception(() => Render("{{ currentDate 1 }}", null));

            //Assert
            plain.Should().Be("2024-03-04|2024-03-03");
            withText.Should().Be("2024-03-04");
            thrown.Should().BeOfType<ChatTemplateException>();
            thrown.Message.Should().Contain("expected string");
        }
        finally
        {
            OllamaTemplateFuncs.TodayOverride = null;
        }
    }

    private static string Render(string template, GoMap data)
        => GoTemplate.Parse(string.Empty, template, OllamaTemplateFuncs.Funcs).Execute(data);
}
