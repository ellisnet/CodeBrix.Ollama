using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Language-level tests for <see cref="JinjaTemplate"/>: every row is a template and the text it must
/// render to with no variables at all, so each row reads as one small piece of the language.
/// </summary>
public sealed class JinjaTemplateTests
{
    /// <summary>Arithmetic, concatenation and comparison follow Python.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{{ 1 + 2 }}", "3")]
    [InlineData("{{ 10 - 3 }}", "7")]
    [InlineData("{{ 3 * 4 }}", "12")]
    [InlineData("{{ 7 / 2 }}", "3.5")]
    [InlineData("{{ 7 // 2 }}", "3")]
    [InlineData("{{ -7 // 2 }}", "-4")]
    [InlineData("{{ 7 % 3 }}", "1")]
    [InlineData("{{ -7 % 3 }}", "2")]
    [InlineData("{{ 2 ** 10 }}", "1024")]
    [InlineData("{{ 1 + 2 * 3 }}", "7")]
    [InlineData("{{ (1 + 2) * 3 }}", "9")]
    [InlineData("{{ 1.0 }}", "1.0")]
    [InlineData("{{ 1.5 }}", "1.5")]
    [InlineData("{{ 0.1 + 0.2 }}", "0.30000000000000004")]
    [InlineData("{{ 1e20 }}", "1e+20")]
    [InlineData("{{ 'a' + 'b' }}", "ab")]
    [InlineData("{{ 'ab' * 3 }}", "ababab")]
    [InlineData("{{ [1, 2] + [3] }}", "[1, 2, 3]")]
    [InlineData("{{ 'a' ~ 1 ~ true ~ none }}", "a1TrueNone")]
    [InlineData("{{ 1 == 1.0 }}", "True")]
    [InlineData("{{ true == 1 }}", "True")]
    [InlineData("{{ 'a' == 1 }}", "False")]
    [InlineData("{{ 1 != 2 }}", "True")]
    [InlineData("{{ 2 > 1 and 1 < 2 }}", "True")]
    [InlineData("{{ 1 > 2 or 2 > 1 }}", "True")]
    [InlineData("{{ not 0 }}", "True")]
    [InlineData("{{ 'b' in 'abc' }}", "True")]
    [InlineData("{{ 'a' in {'a': 1} }}", "True")]
    [InlineData("{{ 'x' not in ['a'] }}", "True")]
    [InlineData("{{ 'y' if 1 > 2 else 'n' }}", "n")]
    [InlineData("{{ true }}{{ false }}{{ none }}", "TrueFalseNone")]
    [InlineData("{{ True }}{{ False }}{{ None }}", "TrueFalseNone")]
    public void Render_evaluates_expressions(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>Containers print the way Python prints them, and index and slice the same way.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{{ [1, 'a', none, true] }}", "[1, 'a', None, True]")]
    [InlineData("{{ {'a': 1} }}", "{'a': 1}")]
    [InlineData("{{ [1,2,3][1] }}", "2")]
    [InlineData("{{ [1,2,3][-1] }}", "3")]
    [InlineData("{{ [1,2,3][5] }}", "")]
    [InlineData("{{ 'abcdef'[1:4] }}", "bcd")]
    [InlineData("{{ 'abcdef'[::-1] }}", "fedcba")]
    [InlineData("{{ [1,2,3][::-1] }}", "[3, 2, 1]")]
    [InlineData("{{ [1,2,3][1:] }}", "[2, 3]")]
    [InlineData("{{ 'abc'[:-1] }}", "ab")]
    [InlineData("{{ {'a': {'b': 2}}['a']['b'] }}", "2")]
    [InlineData("{{ {'a': 1}.a }}", "1")]
    [InlineData("{{ {'a': 1}['a'] }}", "1")]
    public void Render_reads_containers(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>The tojson filter reproduces Python's json.dumps spacing and indenting.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{{ {'a': 1, 'b': [1, 2]} | tojson }}", "{\"a\": 1, \"b\": [1, 2]}")]
    [InlineData("{{ {} | tojson }}", "{}")]
    [InlineData("{{ [] | tojson }}", "[]")]
    [InlineData("{{ [1, 2] | tojson }}", "[1, 2]")]
    [InlineData("{{ none | tojson }}", "null")]
    [InlineData("{{ true | tojson }}", "true")]
    [InlineData("{{ 'a\"b' | tojson }}", "\"a\\\"b\"")]
    [InlineData("{{ 'café' | tojson }}", "\"café\"")]
    [InlineData("{{ {'a': 1} | tojson(indent=2) }}", "{\n  \"a\": 1\n}")]
    [InlineData("{{ {'a': [1]} | tojson(indent=4) }}", "{\n    \"a\": [\n        1\n    ]\n}")]
    public void Render_serializes_with_tojson(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>Undefined values are falsy, print as nothing, and survive attribute access.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{{ missing }}", "")]
    [InlineData("{{ missing is defined }}", "False")]
    [InlineData("{{ missing is undefined }}", "True")]
    [InlineData("{{ missing | default('x') }}", "x")]
    [InlineData("{{ missing.attribute }}", "")]
    [InlineData("{{ missing['key'] }}", "")]
    [InlineData("{{ missing.a.b.c is defined }}", "False")]
    [InlineData("{{ '' | default('x', true) }}", "x")]
    [InlineData("{{ 'a' | default('x', true) }}", "a")]
    [InlineData("{{ 'a' in missing }}", "False")]
    [InlineData("{% if missing %}yes{% else %}no{% endif %}", "no")]
    public void Render_treats_missing_values_as_undefined(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>Whitespace control, trim_blocks and lstrip_blocks behave as transformers configures them.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("a\n{%- if true %}b{% endif %}", "ab")]
    [InlineData("{% if true %}\n  x\n{% endif %}", "  x\n")]
    [InlineData("{% if true %}\n  x\n  {% endif %}", "  x\n")]
    [InlineData("{% if true +%}\n  x\n{% endif %}", "\n  x\n")]
    [InlineData("a  {{- 'b' -}}  c", "abc")]
    [InlineData("a{{ 'b' }}c", "abc")]
    [InlineData("x{# c #}y", "xy")]
    [InlineData("{#- c -#}\n x", "x")]
    [InlineData("  {% if true %}x{% endif %}", "x")]
    [InlineData("z  {% if true %}x{% endif %}", "z  x")]
    [InlineData("{% raw %}{{ not_a_tag }}{% endraw %}", "{{ not_a_tag }}")]
    [InlineData("hello\n", "hello")]
    [InlineData("hello\n\n", "hello\n")]
    public void Render_applies_whitespace_control(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>Loops expose the full loop object, filter their sequence and honour break and continue.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{% for x in [1,2,3] %}{{ x }}{% endfor %}", "123")]
    [InlineData("{% for x in [1,2,3] %}{{ loop.index }}{{ loop.index0 }}{{ loop.revindex }}{{ loop.revindex0 }}|{% endfor %}", "1032|2121|3210|")]
    [InlineData("{% for x in [1,2] %}{{ loop.first }}{{ loop.last }}{{ loop.length }}|{% endfor %}", "TrueFalse2|FalseTrue2|")]
    [InlineData("{% for x in [] %}a{% else %}b{% endfor %}", "b")]
    [InlineData("{% for x in [1,2,3] %}{{ loop.previtem }}-{{ loop.nextitem }}|{% endfor %}", "-2|1-3|2-|")]
    [InlineData("{% for x in [1,2,3,4] %}{{ loop.cycle('a','b') }}{% endfor %}", "abab")]
    [InlineData("{% for k, v in {'a': 1, 'b': 2}.items() %}{{ k }}={{ v }};{% endfor %}", "a=1;b=2;")]
    [InlineData("{% for x in [1,2,3,4] if x % 2 == 0 %}{{ x }}{{ loop.last }}|{% endfor %}", "2False|4True|")]
    [InlineData("{% for x in [1,2,3] %}{% if x == 2 %}{% continue %}{% endif %}{{ x }}{% endfor %}", "13")]
    [InlineData("{% for x in [1,2,3] %}{% if x == 3 %}{% break %}{% endif %}{{ x }}{% endfor %}", "12")]
    [InlineData("{% for c in 'ab' %}{{ c }}.{% endfor %}", "a.b.")]
    [InlineData("{% for x in range(3) %}{{ x }}{% endfor %}", "012")]
    [InlineData("{% set c = 0 %}{% for x in [1,2,3] %}{% set c = c + 1 %}{{ c }}{% endfor %}{{ c }}", "1110")]
    public void Render_runs_loops(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>set, namespace, macros, call blocks and filter blocks all work.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{% set x = 1 %}{{ x }}", "1")]
    [InlineData("{% set a, b = [1, 2] %}{{ a }}{{ b }}", "12")]
    [InlineData("{% set x %}hi{% endset %}{{ x }}", "hi")]
    [InlineData("{% set x | upper %}hi{% endset %}{{ x }}", "HI")]
    [InlineData("{% set ns = namespace(v=0) %}{% for i in [1,2,3] %}{% set ns.v = ns.v + i %}{% endfor %}{{ ns.v }}", "6")]
    [InlineData("{% set ns = namespace() %}{% set ns.a = 'x' %}{{ ns.a }}", "x")]
    [InlineData("{% macro m(a, b=2) %}{{ a }}-{{ b }}{% endmacro %}{{ m(1) }}|{{ m(1, 3) }}|{{ m(b=9, a=8) }}", "1-2|1-3|8-9")]
    [InlineData("{% macro m(a) %}[{{ a }}]{% endmacro %}{% set y = m('q')|upper %}{{ y }}", "[Q]")]
    [InlineData("{% macro m() %}[{{ caller() }}]{% endmacro %}{% call m() %}inner{% endcall %}", "[inner]")]
    [InlineData("{% filter upper %}hi{% endfilter %}", "HI")]
    [InlineData("{% generation %}gen{% endgeneration %}", "gen")]
    [InlineData("{% set l = [1] %}{% do l.append(2) %}{{ l }}", "[1, 2]")]
    [InlineData("{% with %}{% set z = 1 %}{{ z }}{% endwith %}{{ z is defined }}", "1False")]
    public void Render_runs_statements(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>The filter library behaves as Jinja's does.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{{ '  hi  ' | trim }}", "hi")]
    [InlineData("{{ 'hi' | upper }}{{ 'HI' | lower }}", "HIhi")]
    [InlineData("{{ 'hello world' | title }}", "Hello World")]
    [InlineData("{{ 'hello WORLD' | capitalize }}", "Hello world")]
    [InlineData("{{ [1,2,3] | length }}", "3")]
    [InlineData("{{ 'abc' | count }}", "3")]
    [InlineData("{{ [1,2,3] | join('-') }}", "1-2-3")]
    [InlineData("{{ 'a-b' | replace('-', '+') }}", "a+b")]
    [InlineData("{{ [1,2,3] | first }}{{ [1,2,3] | last }}", "13")]
    [InlineData("{{ 'abc' | list }}", "['a', 'b', 'c']")]
    [InlineData("{{ 1 | string }}", "1")]
    [InlineData("{{ '42' | int + 1 }}", "43")]
    [InlineData("{{ '4.5' | float }}", "4.5")]
    [InlineData("{{ 'x' | safe }}", "x")]
    [InlineData("{{ '<a>' | escape }}", "&lt;a&gt;")]
    [InlineData("{{ [3,1,2] | sort }}", "[1, 2, 3]")]
    [InlineData("{{ [3,1,2] | sort(reverse=true) }}", "[3, 2, 1]")]
    [InlineData("{{ [1,1,2] | unique | list }}", "[1, 2]")]
    [InlineData("{{ [1,2,3] | sum }}", "6")]
    [InlineData("{{ [1,2,3] | min }}{{ [1,2,3] | max }}", "13")]
    [InlineData("{{ -3 | abs }}", "3")]
    [InlineData("{{ 3.14159 | round(2) }}", "3.14")]
    [InlineData("{{ [1,2,3,4] | batch(2) | list }}", "[[1, 2], [3, 4]]")]
    [InlineData("{{ [1,2,3,4] | slice(2) | list }}", "[[1, 2], [3, 4]]")]
    [InlineData("{{ {'b': 2, 'a': 1} | items }}", "[['b', 2], ['a', 1]]")]
    [InlineData("{{ {'b': 2, 'a': 1} | dictsort }}", "[['a', 1], ['b', 2]]")]
    [InlineData("{{ [{'n': 1}, {'n': 2}] | map(attribute='n') | list }}", "[1, 2]")]
    [InlineData("{{ ['a','b'] | map('upper') | list }}", "['A', 'B']")]
    [InlineData("{{ [{'r': 'a'}, {'r': 'b'}] | selectattr('r', 'equalto', 'a') | list }}", "[{'r': 'a'}]")]
    [InlineData("{{ [{'r': 'a'}, {'r': 'b'}] | rejectattr('r', 'equalto', 'a') | list }}", "[{'r': 'b'}]")]
    [InlineData("{{ [1,2,3] | select('odd') | list }}", "[1, 3]")]
    [InlineData("{{ [1,2,3] | reject('odd') | list }}", "[2]")]
    [InlineData("{{ '<b>hi</b>' | striptags }}", "hi")]
    [InlineData("{{ 'a b c' | wordcount }}", "3")]
    [InlineData("{{ '%s-%d' | format('a', 2) }}", "a-2")]
    [InlineData("{{ 'ab' | center(6) }}", "  ab  ")]
    [InlineData("{{ 'abc' | center(6) }}", " abc  ")]
    [InlineData("{{ [1,2] | reverse | list }}", "[2, 1]")]
    [InlineData("{{ 'a b' | urlencode }}", "a%20b")]
    [InlineData("{{ {'a': 1} | attr('a') }}", "1")]
    [InlineData("{{ [{'k': 1}, {'k': 1}] | groupby('k') | length }}", "1")]
    public void Render_applies_filters(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>The test library behaves as Jinja's does.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{{ 1 is number }}{{ 'a' is string }}{{ [] is iterable }}{{ {} is mapping }}", "TrueTrueTrueTrue")]
    [InlineData("{{ 'a' is iterable }}{{ 'a' is sequence }}{{ 'a' is not mapping }}", "TrueTrueTrue")]
    [InlineData("{{ 1 is integer }}{{ 1.0 is float }}{{ true is boolean }}", "TrueTrueTrue")]
    [InlineData("{{ true is true }}{{ false is false }}{{ 1 is not true }}", "TrueTrueTrue")]
    [InlineData("{{ 4 is even }}{{ 3 is odd }}{{ 9 is divisibleby 3 }}", "TrueTrueTrue")]
    [InlineData("{{ none is none }}{{ 'a' is not none }}", "TrueTrue")]
    [InlineData("{{ 'a' is lower }}{{ 'A' is upper }}", "TrueTrue")]
    [InlineData("{{ 1 is eq 1 }}{{ 1 is ne 2 }}{{ 1 is lt 2 }}{{ 2 is ge 2 }}", "TrueTrueTrueTrue")]
    [InlineData("{{ 'a' is in ['a'] }}", "True")]
    [InlineData("{% set a = 'x' %}{{ a is sameas a }}", "True")]
    public void Render_applies_tests(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>String, list and mapping methods behave as Python's do.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{{ 'a,b,c'.split(',') }}", "['a', 'b', 'c']")]
    [InlineData("{{ 'a,b,c'.split(',', 1) }}", "['a', 'b,c']")]
    [InlineData("{{ '  a  b '.split() }}", "['a', 'b']")]
    [InlineData("{{ ' x '.strip() }}", "x")]
    [InlineData("{{ 'xxaxx'.strip('x') }}", "a")]
    [InlineData("{{ 'abc'.startswith('a') }}{{ 'abc'.endswith('c') }}", "TrueTrue")]
    [InlineData("{{ 'a-b'.replace('-', '_') }}", "a_b")]
    [InlineData("{{ 'abcb'.find('b') }}{{ 'abcb'.rfind('b') }}", "13")]
    [InlineData("{{ '-'.join(['a','b']) }}", "a-b")]
    [InlineData("{{ 'abc'.upper() }}{{ 'ABC'.lower() }}", "ABCabc")]
    [InlineData("{{ 'ab'.zfill(4) }}", "00ab")]
    [InlineData("{{ 'a,b'.partition(',') }}", "['a', ',', 'b']")]
    [InlineData("{{ 'abc'.count('b') }}", "1")]
    [InlineData("{{ 'ab'.isalpha() }}{{ '12'.isdigit() }}{{ ' '.isspace() }}", "TrueTrueTrue")]
    [InlineData("{{ '{} and {}'.format('a', 'b') }}", "a and b")]
    [InlineData("{% set d = {'a': 1} %}{{ d.get('a') }}{{ d.get('b', 'x') }}{{ d.keys() }}{{ d.values() }}", "1x['a'][1]")]
    [InlineData("{% set l = [3,1] %}{% do l.insert(0, 9) %}{{ l }}{{ l.index(1) }}", "[9, 3, 1]2")]
    public void Render_calls_methods(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>The global functions are available.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{{ range(3) }}", "[0, 1, 2]")]
    [InlineData("{{ range(1, 6, 2) }}", "[1, 3, 5]")]
    [InlineData("{{ dict(a=1) }}", "{'a': 1}")]
    [InlineData("{{ namespace(a=1).a }}", "1")]
    public void Render_calls_functions(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>strftime_now formats the current moment with the Python directives.</summary>
    [Fact]
    public void Render_strftime_now_formats_the_current_time()
    {
        //Arrange
        var expected = System.DateTime.Now;

        //Act
        string rendered = Render("{{ strftime_now('%Y|%m|%d|%B|%b|%A|%a') }}");

        //Assert
        rendered.Should().Be(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0:D4}|{1:D2}|{2:D2}|{3}|{4}|{5}|{6}",
            expected.Year,
            expected.Month,
            expected.Day,
            System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(expected.Month),
            System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(expected.Month),
            System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(expected.DayOfWeek),
            System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedDayName(expected.DayOfWeek)));
    }

    /// <summary>Variables reach the template, and any list or dictionary shape is accepted.</summary>
    [Fact]
    public void Render_accepts_ordinary_dotnet_values()
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse(
            "{{ name }}|{{ count }}|{{ ratio }}|{{ flag }}|{{ items | join(',') }}|{{ map.key }}");
        var variables = new Dictionary<string, object>
        {
            ["name"] = "qwen",
            ["count"] = 3,
            ["ratio"] = 0.5f,
            ["flag"] = true,
            ["items"] = new[] { "a", "b" },
            ["map"] = new Dictionary<string, string> { ["key"] = "value" },
        };

        //Act
        string rendered = template.Render(variables);

        //Assert
        rendered.Should().Be("qwen|3|0.5|True|a,b|value");
    }

    /// <summary>A template can be rendered more than once, with different variables each time.</summary>
    [Fact]
    public void Render_can_be_called_repeatedly()
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse("{{ x }}");

        //Act
        string first = template.Render(new Dictionary<string, object> { ["x"] = 1 });
        string second = template.Render(new Dictionary<string, object> { ["x"] = 2 });

        //Assert
        first.Should().Be("1");
        second.Should().Be("2");
    }

    /// <summary>Appending to a list inside a template does not change the caller's list.</summary>
    [Fact]
    public void Render_does_not_mutate_the_callers_values()
    {
        //Arrange
        var items = new List<object> { 1L };
        JinjaTemplate template = JinjaTemplate.Parse("{% do items.append(2) %}{{ items }}");

        //Act
        string rendered = template.Render(new Dictionary<string, object> { ["items"] = items });

        //Assert
        rendered.Should().Be("[1, 2]");
        items.Should().HaveCount(1);
    }

    /// <summary>The source a template was parsed from is kept unchanged.</summary>
    [Fact]
    public void Source_keeps_the_original_text()
    {
        JinjaTemplate.Parse("hello\n").Source.Should().Be("hello\n");
    }

    /// <summary>Integer arithmetic keeps every digit instead of passing through a double.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{{ 9007199254740993 + 2 }}", "9007199254740995")]
    [InlineData("{{ 1000000007 * 1000000009 }}", "1000000016000000063")]
    [InlineData("{{ 9007199254740993 - 1 }}", "9007199254740992")]
    [InlineData("{{ 2 ** 62 }}", "4611686018427387904")]
    [InlineData("{{ 9007199254740993 // 2 }}", "4503599627370496")]
    [InlineData("{{ 9007199254740993 % 2 }}", "1")]
    public void Render_keeps_integer_precision(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>The format filter honours the whole printf spec, not just the conversion letter.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{{ '%5.2f'|format(3.14159) }}", " 3.14")]
    [InlineData("{{ '%-6d|'|format(42) }}", "42    |")]
    [InlineData("{{ '%+d'|format(7) }}", "+7")]
    [InlineData("{{ '%05d'|format(42) }}", "00042")]
    [InlineData("{{ '%#x'|format(255) }}", "0xff")]
    [InlineData("{{ '%.3s'|format('abcdef') }}", "abc")]
    [InlineData("{{ '%10s|'|format('ab') }}", "        ab|")]
    [InlineData("{{ '%d%%'|format(5) }}", "5%")]
    [InlineData("{{ '%.2e'|format(12345.0) }}", "1.23e+04")]
    [InlineData("{{ '%08.3f'|format(-3.14159) }}", "-003.142")]
    [InlineData("{{ '%s and %s'|format('a', 'b') }}", "a and b")]
    public void Render_applies_the_printf_spec(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>The string methods follow CPython on the awkward arguments.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{{ 'a b c d'.rsplit(None, 2) }}", "['a b', 'c', 'd']")]
    [InlineData("{{ '  a b  c  '.rsplit(None, 1) }}", "['  a b', 'c']")]
    [InlineData("{{ '  a b  c  '.split(None, 1) }}", "['a', 'b  c  ']")]
    [InlineData("{{ 'abc'.rsplit(None, 1) }}", "['abc']")]
    [InlineData("{{ '   '.rsplit(None, 1) }}", "[]")]
    [InlineData("{{ ''.splitlines() }}", "[]")]
    [InlineData("{{ 'a\\nb'.splitlines() }}", "['a', 'b']")]
    [InlineData("{{ 'ab'.replace('', '-') }}", "-a-b-")]
    [InlineData("{{ 'ab'.replace('', '-', 2) }}", "-a-b")]
    [InlineData("{{ ''.replace('', '-') }}", "-")]
    [InlineData("{{ 'ab'.replace('', '-', 0) }}", "ab")]
    public void Render_applies_python_string_methods(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>The filters that carry an easily-lost edge case behave the way Jinja does.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{{ [none]|min }}", "None")]
    [InlineData("{{ [none]|max }}", "None")]
    [InlineData("{{ missing|default(none) }}", "None")]
    [InlineData("{{ missing|default }}", "")]
    [InlineData("{{ missing|default('x') }}", "x")]
    [InlineData("{{ 0.5|round }}", "0.0")]
    [InlineData("{{ 1.5|round }}", "2.0")]
    [InlineData("{{ 2.5|round }}", "2.0")]
    [InlineData("{{ 3.5|round }}", "4.0")]
    [InlineData("{{ 2.675|round(2) }}", "2.67")]
    [InlineData("{{ 2.5|round(0, 'ceil') }}", "3.0")]
    [InlineData("{{ 2.5|round(0, 'floor') }}", "2.0")]
    public void Render_applies_filter_edge_cases(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>lstrip_blocks only eats whitespace that starts a line.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{{ 'x' }}   {% if true %}a{% endif %}", "x   a")]
    [InlineData("y\n   {% if true %}a{% endif %}", "y\na")]
    [InlineData("   {% if true %}a{% endif %}", "a")]
    [InlineData("{{ 'x' }}\t{% if true %}a{% endif %}", "x\ta")]
    [InlineData("x{# a - #}   y", "x   y")]
    [InlineData("x{# a -#}   y", "xy")]
    public void Render_strips_block_indent_only_at_a_line_start(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>A macro sees varargs and kwargs when its body reads them.</summary>
    /// <param name="source">The template.</param>
    /// <param name="expected">The expected rendering.</param>
    [Theory]
    [InlineData("{% macro m() %}{{ varargs }}{% endmacro %}{{ m(1, 2) }}", "[1, 2]")]
    [InlineData("{% macro m() %}{{ kwargs }}{% endmacro %}{{ m(a=1) }}", "{'a': 1}")]
    [InlineData("{% macro m(a) %}{{ a }}{{ varargs }}{% endmacro %}{{ m(1, 2) }}", "1[2]")]
    public void Render_binds_varargs_and_kwargs_when_the_macro_reads_them(string source, string expected)
    {
        Render(source).Should().Be(expected);
    }

    /// <summary>sort keeps equal elements in the order they arrived in.</summary>
    [Fact]
    public void Render_sort_is_stable()
    {
        //Arrange
        var items = new List<object>();
        for (int i = 0; i < 40; i++)
        {
            items.Add(new Dictionary<string, object>
            {
                ["k"] = 1L,
                ["v"] = (long)i,
            });
        }

        JinjaTemplate template = JinjaTemplate.Parse(
            "{% for item in items|sort(attribute='k') %}{{ item.v }},{% endfor %}");

        //Act
        string rendered = template.Render(new Dictionary<string, object> { ["items"] = items });

        //Assert
        rendered.Should().Be(string.Concat(Enumerable.Range(0, 40).Select(i => i + ",")));
    }

    /// <summary>dictsort keeps equal values in the order the mapping listed them.</summary>
    [Fact]
    public void Render_dictsort_is_stable()
    {
        //Arrange
        var mapping = new Dictionary<string, object>();
        for (int i = 0; i < 40; i++)
        {
            mapping["k" + i.ToString("D2", CultureInfo.InvariantCulture)] = 1L;
        }

        JinjaTemplate template = JinjaTemplate.Parse(
            "{% for pair in m|dictsort(by='value') %}{{ pair[0] }},{% endfor %}");

        //Act
        string rendered = template.Render(new Dictionary<string, object> { ["m"] = mapping });

        //Assert
        rendered.Should().Be(string.Concat(
            Enumerable.Range(0, 40).Select(i => "k" + i.ToString("D2", CultureInfo.InvariantCulture) + ",")));
    }

    private static string Render(string source)
    {
        return JinjaTemplate.Parse(source).Render(new Dictionary<string, object>());
    }
}
