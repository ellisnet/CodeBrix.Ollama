using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The budget both template engines are held to: every limit in <see cref="TemplateLimits"/> tripped by a
/// hostile template, the depth guards that used to be missing (each case here overflowed the stack, or
/// threw something other than <see cref="ChatTemplateException"/>, before they existed), cancellation and
/// the timeout, and proof that real templates on a long chat with tools stay far inside the defaults.
/// </summary>
/// <remarks>
/// No test here lets a stack overflow happen: every deep case is refused by a guard long before the stack
/// is in danger, and the stack-safety tests run the deepest PERMITTED templates on a thread whose stack is a
/// quarter of the smallest default a .NET thread gets.
/// </remarks>
public sealed class TemplateLimitsTests
{
    private static readonly IReadOnlyDictionary<string, object> NoVariables = new Dictionary<string, object>();

    // ---------------------------------------------------------------- defaults and validation

    /// <summary>The defaults are the documented ones.</summary>
    [Fact]
    public void Defaults_are_the_documented_values()
    {
        //Act
        var limits = new TemplateLimits();

        //Assert
        limits.MaxRenderSteps.Should().Be(1_000_000);
        limits.MaxOutputChars.Should().Be(8_000_000);
        limits.MaxStringChars.Should().Be(8_000_000);
        limits.MaxCollectionItems.Should().Be(1_000_000);
        limits.MaxNestingDepth.Should().Be(256);
        limits.RenderTimeout.Should().Be(TimeSpan.FromSeconds(10));
        new ModelRunnerOptions().TemplateLimits.Should().NotBeNull();
    }

    /// <summary>A count below one or a timeout that is not positive is refused where the limits are used.</summary>
    /// <param name="which">Which limit to break.</param>
    [Theory]
    [InlineData("steps")]
    [InlineData("output")]
    [InlineData("string")]
    [InlineData("items")]
    [InlineData("depth")]
    [InlineData("timeout")]
    public void Parse_with_a_limit_out_of_range_throws(string which)
    {
        //Arrange
        var limits = new TemplateLimits();
        switch (which)
        {
            case "steps": limits.MaxRenderSteps = 0; break;
            case "output": limits.MaxOutputChars = 0; break;
            case "string": limits.MaxStringChars = -1; break;
            case "items": limits.MaxCollectionItems = 0; break;
            case "depth": limits.MaxNestingDepth = 0; break;
            default: limits.RenderTimeout = TimeSpan.Zero; break;
        }

        //Act
        Action jinja = () => JinjaTemplate.Parse("x", limits);
        Action ollama = () => OllamaTemplate.Parse("x", limits);

        //Assert
        jinja.Should().Throw<ArgumentException>().Which.Message.Should().Contain("TemplateLimits.");
        ollama.Should().Throw<ArgumentException>().Which.Message.Should().Contain("TemplateLimits.");
    }

    /// <summary>The limits are copied at parse, so a later change to the object does not reach the template.</summary>
    [Fact]
    public void Parse_copies_the_limits()
    {
        //Arrange
        var limits = new TemplateLimits { MaxRenderSteps = 50 };
        JinjaTemplate template = JinjaTemplate.Parse("{% for i in range(100) %}.{% endfor %}", limits);
        limits.MaxRenderSteps = 1_000_000;

        //Act
        Action act = () => template.Render(NoVariables, TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxRenderSteps");
    }

    // ---------------------------------------------------------------- Jinja: parse-time nesting

    /// <summary>
    /// Statements nested a hundred thousand deep - which recursed straight through the parser and
    /// overflowed the stack - are refused once they pass the limit.
    /// </summary>
    [Fact]
    public void Jinja_parse_with_statements_nested_past_the_limit_throws()
    {
        //Arrange
        string source = Repeat("{% if true %}", 100_000) + "x" + Repeat("{% endif %}", 100_000);

        //Act
        Action act = () => JinjaTemplate.Parse(source);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxNestingDepth");
    }

    /// <summary>Every block statement counts towards the nesting, not only <c>if</c>.</summary>
    /// <param name="open">The opening tag.</param>
    /// <param name="close">The closing tag.</param>
    [Theory]
    [InlineData("{% for i in [1] %}", "{% endfor %}")]
    [InlineData("{% macro m() %}", "{% endmacro %}")]
    [InlineData("{% filter upper %}", "{% endfilter %}")]
    [InlineData("{% set s %}", "{% endset %}")]
    [InlineData("{% with a = 1 %}", "{% endwith %}")]
    [InlineData("{% generation %}", "{% endgeneration %}")]
    public void Jinja_parse_with_any_block_nested_past_the_limit_throws(string open, string close)
    {
        //Arrange
        string source = Repeat(open, 5_000) + Repeat(close, 5_000);

        //Act
        Action act = () => JinjaTemplate.Parse(source);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxNestingDepth");
    }

    /// <summary>Statements nested well inside the limit still parse and render.</summary>
    [Fact]
    public void Jinja_render_with_statements_nested_inside_the_limit_renders()
    {
        //Arrange
        string source = Repeat("{% if true %}", 100) + "x" + Repeat("{% endif %}", 100);

        //Act
        string rendered = JinjaTemplate.Parse(source).Render(NoVariables, TestContext.Current.CancellationToken);

        //Assert
        rendered.Should().Be("x");
    }

    /// <summary>
    /// Long flat chains are parsed by loops but build a tree one level deeper per link, and that tree was
    /// walked recursively when it rendered - another stack overflow. They are refused at parse.
    /// </summary>
    /// <param name="link">One link of the chain.</param>
    /// <param name="start">What the chain starts from.</param>
    [Theory]
    [InlineData(" + 1", "1")]
    [InlineData(" ~ 'a'", "'a'")]
    [InlineData(" and true", "true")]
    [InlineData(" or false", "false")]
    [InlineData(" == 1", "1")]
    [InlineData(" * 1", "1")]
    [InlineData(" ** 1", "1")]
    [InlineData("|trim", "'a'")]
    [InlineData(".a", "x")]
    [InlineData("[0]", "x")]
    [InlineData("()", "x")]
    [InlineData(" is defined", "x")]
    public void Jinja_parse_with_a_long_flat_chain_throws(string link, string start)
    {
        //Arrange
        string source = "{{ " + start + Repeat(link, 100_000) + " }}";

        //Act
        Action act = () => JinjaTemplate.Parse(source);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxNestingDepth");
    }

    /// <summary>
    /// A chain wrapped around a tall parenthesized chain is as tall as both together, even though the parser
    /// itself never recursed that deep: the height of what it builds is what is checked.
    /// </summary>
    [Fact]
    public void Jinja_parse_with_chains_stacked_through_parentheses_throws()
    {
        //Arrange
        string inner = "(1" + Repeat(" + 1", 200) + ")";
        string source = "{{ " + inner + Repeat(" + 1", 200) + " }}";

        //Act
        Action act = () => JinjaTemplate.Parse(source);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxNestingDepth");
    }

    /// <summary>
    /// An elif chain is flat - one if node with many branches - so ten thousand of them parse and render,
    /// and the right branch is taken.
    /// </summary>
    [Fact]
    public void Jinja_render_with_a_long_elif_chain_renders()
    {
        //Arrange
        var source = new StringBuilder("{% if n == 0 %}0");
        for (int i = 1; i < 10_000; i++)
        {
            source.Append("{% elif n == ").Append(i).Append(" %}").Append(i);
        }

        source.Append("{% endif %}");
        var variables = new Dictionary<string, object> { ["n"] = 9_998L };

        //Act
        string rendered = JinjaTemplate.Parse(source.ToString())
            .Render(variables, TestContext.Current.CancellationToken);

        //Assert
        rendered.Should().Be("9998");
    }

    /// <summary>A run of ten thousand adjacent string literals is joined in linear time and renders.</summary>
    [Fact]
    public void Jinja_render_with_many_adjacent_string_literals_renders()
    {
        //Arrange
        string source = "{{ " + Repeat("'ab' ", 10_000) + "}}";

        //Act
        string rendered = JinjaTemplate.Parse(source).Render(NoVariables, TestContext.Current.CancellationToken);

        //Assert
        rendered.Length.Should().Be(20_000);
    }

    /// <summary>A caller's own nesting limit is the one enforced.</summary>
    [Fact]
    public void Jinja_parse_honours_a_lower_nesting_limit()
    {
        //Arrange
        var limits = new TemplateLimits { MaxNestingDepth = 8 };
        string deep = Repeat("{% if true %}", 10) + Repeat("{% endif %}", 10);
        string shallow = Repeat("{% if true %}", 4) + "x" + Repeat("{% endif %}", 4);

        //Act
        Action act = () => JinjaTemplate.Parse(deep, limits);
        string rendered = JinjaTemplate.Parse(shallow, limits).Render(NoVariables, TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("more than 8 levels");
        rendered.Should().Be("x");
    }

    // ---------------------------------------------------------------- Jinja: render-time limits

    /// <summary>A macro that recurses with a deep body is stopped by the render's nesting count.</summary>
    [Fact]
    public void Jinja_render_with_deep_macro_recursion_throws()
    {
        //Arrange
        string body = Repeat("{% if true %}", 100) + "{{ f(n + 1) }}" + Repeat("{% endif %}", 100);
        JinjaTemplate template = JinjaTemplate.Parse("{% macro f(n) %}" + body + "{% endmacro %}{{ f(0) }}");

        //Act
        Action act = () => template.Render(NoVariables, TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxNestingDepth");
    }

    /// <summary>Honest recursion - a macro walking a nested schema - still works.</summary>
    [Fact]
    public void Jinja_render_with_bounded_macro_recursion_renders()
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse(
            "{% macro f(n) %}{% if n > 0 %}({{ f(n - 1) }}){% endif %}{% endmacro %}{{ f(20) }}");

        //Act
        string rendered = template.Render(NoVariables, TestContext.Current.CancellationToken);

        //Assert
        rendered.Should().Be(Repeat("(", 20) + Repeat(")", 20));
    }

    /// <summary>
    /// A list that contains itself, or a value nested deeper than the limit, is refused when it is printed or
    /// compared rather than recursing until the stack overflows.
    /// </summary>
    /// <param name="source">The template.</param>
    [Theory]
    [InlineData("{% set l = [] %}{% do l.append(l) %}{{ l }}")]
    [InlineData("{% set l = [] %}{% do l.append(l) %}{{ l == l }}")]
    [InlineData("{% set l = [] %}{% do l.append(l) %}{{ l < l }}")]
    [InlineData("{% set d = {} %}{% do d.update({'a': 1}) %}{% set d.a = d %}{{ d }}")]
    [InlineData("{% set ns = namespace(x=1) %}{% for i in range(5000) %}{% set ns.x = [ns.x] %}{% endfor %}{{ ns.x|string }}")]
    public void Jinja_render_with_a_self_containing_or_too_deep_value_throws(string source)
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse(source);

        //Act
        Action act = () => template.Render(NoVariables, TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxNestingDepth");
    }

    /// <summary>An endless or enormous loop is stopped by the step count.</summary>
    [Fact]
    public void Jinja_render_with_an_enormous_loop_throws()
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse(
            "{% for i in range(1000000) %}{% for j in range(1000000) %}{% endfor %}{% endfor %}");

        //Act
        Action act = () => template.Render(NoVariables, TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxRenderSteps");
    }

    /// <summary>Text written past the output limit is refused.</summary>
    [Fact]
    public void Jinja_render_with_too_much_output_throws()
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse(
            "{% for i in range(100000) %}{{ 'x' * 1000 }}{% endfor %}");

        //Act
        Action act = () => template.Render(NoVariables, TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxOutputChars");
    }

    /// <summary>
    /// Every way of building a huge string from a small template is refused - the width- and count-driven
    /// ones before anything is allocated.
    /// </summary>
    /// <param name="source">The template.</param>
    [Theory]
    [InlineData("{{ 'x' * 1000000000 }}")]
    [InlineData("{{ 1000000000000 * 'x' }}")]
    [InlineData("{{ 'x'|center(2000000000) }}")]
    [InlineData("{{ 'x'.center(2000000000) }}")]
    [InlineData("{{ 'x'.ljust(2000000000) }}")]
    [InlineData("{{ 'x'.rjust(2000000000) }}")]
    [InlineData("{{ 'x'.zfill(2000000000) }}")]
    [InlineData("{{ 'a\nb'|indent(2000000000) }}")]
    [InlineData("{{ '%999999999d' % 1 }}")]
    [InlineData("{{ '%99999999999999999999s' % 'x' }}")]
    [InlineData("{{ '%.999999999f' % 1.5 }}")]
    [InlineData("{{ ('x' * 4000000).replace('', 'yy') }}")]
    [InlineData("{{ ('x' * 4000000).replace('x', 'yyy') }}")]
    [InlineData("{{ [1, [2]]|tojson(indent=2000000000) }}")]
    [InlineData("{% set ns = namespace(s='x') %}{% for i in range(40) %}{% set ns.s = ns.s ~ ns.s %}{% endfor %}")]
    [InlineData("{% set ns = namespace(s='x') %}{% for i in range(40) %}{% set ns.s = ns.s + ns.s %}{% endfor %}")]
    [InlineData("{% set s = 'x' * 1000000 %}{{ [s, s, s, s, s, s, s, s, s]|join('') }}")]
    [InlineData("{% set s = 'x' * 1000000 %}{{ ''.join([s, s, s, s, s, s, s, s, s]) }}")]
    [InlineData("{% set s = 'x' * 1000000 %}{{ [s, s, s, s, s, s, s, s, s]|string }}")]
    public void Jinja_render_building_a_huge_string_throws(string source)
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse(source);

        //Act
        Action act = () => template.Render(NoVariables, TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxStringChars");
    }

    /// <summary>Every way of building a huge list from a small template is refused.</summary>
    /// <param name="source">The template.</param>
    [Theory]
    [InlineData("{{ range(2000000)|length }}")]
    [InlineData("{{ range(0, 9000000000000000000, 1)|length }}")]
    [InlineData("{{ [1]|batch(100000000, 0)|list|length }}")]
    [InlineData("{{ [1]|slice(100000000)|length }}")]
    [InlineData("{% set l = [1] %}{% for i in range(40) %}{% do l.extend(l) %}{% endfor %}")]
    [InlineData("{% set ns = namespace(l=[1]) %}{% for i in range(40) %}{% set ns.l = ns.l + ns.l %}{% endfor %}")]
    [InlineData("{% for c in 'x' * 2000000 %}{% endfor %}")]
    public void Jinja_render_building_a_huge_collection_throws(string source)
    {
        //Arrange
        JinjaTemplate template = JinjaTemplate.Parse(source);

        //Act
        Action act = () => template.Render(NoVariables, TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxCollectionItems");
    }

    /// <summary>A render that runs past its time limit is abandoned.</summary>
    [Fact]
    public void Jinja_render_past_the_timeout_throws()
    {
        //Arrange
        var limits = new TemplateLimits { MaxRenderSteps = int.MaxValue, RenderTimeout = TimeSpan.FromMilliseconds(50) };
        JinjaTemplate template = JinjaTemplate.Parse(
            "{% for i in range(1000000) %}{% for j in range(1000000) %}{% endfor %}{% endfor %}", limits);

        //Act
        Action act = () => template.Render(NoVariables, TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("RenderTimeout");
    }

    /// <summary>A long render stops when its token is cancelled.</summary>
    [Fact]
    public void Jinja_render_stops_when_cancelled()
    {
        //Arrange
        var limits = new TemplateLimits { MaxRenderSteps = int.MaxValue, RenderTimeout = null };
        JinjaTemplate template = JinjaTemplate.Parse(
            "{% for i in range(1000000) %}{% for j in range(1000000) %}{% endfor %}{% endfor %}", limits);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        source.CancelAfter(TimeSpan.FromMilliseconds(50));

        //Act
        Action act = () => template.Render(NoVariables, source.Token);

        //Assert
        act.Should().Throw<OperationCanceledException>();
    }

    // ---------------------------------------------------------------- Ollama (Go) dialect

    /// <summary>
    /// Five thousand nested parentheses passed the old cap of ten thousand and recursed through the parser
    /// far past the stack; they are now refused at the nesting limit.
    /// </summary>
    [Fact]
    public void Ollama_parse_with_deeply_nested_parentheses_throws()
    {
        //Arrange
        string text = "{{ " + Repeat("(", 5_000) + "1" + Repeat(")", 5_000) + " }}";

        //Act
        Action act = () => OllamaTemplate.Parse(text);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("max expression depth exceeded");
    }

    /// <summary>
    /// Each block is parsed by a tree of its own, which used to start counting from zero, so blocks nested in
    /// blocks recursed without any limit at all.
    /// </summary>
    [Fact]
    public void Ollama_parse_with_deeply_nested_blocks_throws()
    {
        //Arrange
        var text = new StringBuilder();
        for (int i = 0; i < 50_000; i++)
        {
            text.Append("{{ block \"b").Append(i).Append("\" . }}");
        }

        text.Append(Repeat("{{ end }}", 50_000));

        //Act
        Action act = () => OllamaTemplate.Parse(text.ToString());

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxNestingDepth");
    }

    /// <summary>Parentheses inside branches share the one count.</summary>
    [Fact]
    public void Ollama_parse_counts_branches_and_parentheses_together()
    {
        //Arrange
        string text = Repeat("{{ if 1 }}", 200) + "{{ " + Repeat("(", 100) + "1" + Repeat(")", 100) + " }}"
            + Repeat("{{ end }}", 200);

        //Act
        Action act = () => OllamaTemplate.Parse(text);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxNestingDepth");
    }

    /// <summary>A template that invokes itself, with a deep body, is stopped by the render's nesting count.</summary>
    [Fact]
    public void Ollama_render_with_deep_template_recursion_throws()
    {
        //Arrange
        string body = Repeat("{{ if 1 }}", 100) + "{{ template \"r\" . }}" + Repeat("{{ end }}", 100);
        OllamaTemplate template = OllamaTemplate.Parse(
            "{{ define \"r\" }}" + body + "{{ end }}{{ template \"r\" . }}{{ .Messages }}");

        //Act
        Action act = () => template.Render(new OllamaTemplateValues(), TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxNestingDepth");
    }

    /// <summary>A range over an enormous number is stopped by the step count.</summary>
    [Fact]
    public void Ollama_render_with_an_enormous_range_throws()
    {
        //Arrange
        OllamaTemplate template = OllamaTemplate.Parse("{{ range 1000000000000 }}{{ end }}{{ .Messages }}");

        //Act
        Action act = () => template.Render(new OllamaTemplateValues(), TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxRenderSteps");
    }

    /// <summary>Text written past the output limit is refused.</summary>
    [Fact]
    public void Ollama_render_with_too_much_output_throws()
    {
        //Arrange
        OllamaTemplate template = OllamaTemplate.Parse(
            "{{ range 100000 }}{{ printf \"%5000s\" \"\" }}{{ end }}{{ .Messages }}");

        //Act
        Action act = () => template.Render(new OllamaTemplateValues(), TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxOutputChars");
    }

    /// <summary>Printf widths and precisions, and a string doubled in a loop, are refused.</summary>
    /// <param name="text">The template.</param>
    [Theory]
    [InlineData("{{ printf \"%999999999s\" \"x\" }}")]
    [InlineData("{{ printf \"%99999999999999999999d\" 1 }}")]
    [InlineData("{{ printf \"%.999999999f\" 1.5 }}")]
    [InlineData("{{ $x := \"ab\" }}{{ range 40 }}{{ $x = printf \"%s%s\" $x $x }}{{ end }}")]
    [InlineData("{{ $x := \"ab\" }}{{ range 40 }}{{ $x = print $x $x }}{{ end }}")]
    public void Ollama_render_building_a_huge_string_throws(string text)
    {
        //Arrange
        OllamaTemplate template = OllamaTemplate.Parse(text + "{{ .Messages }}");

        //Act
        Action act = () => template.Render(new OllamaTemplateValues(), TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxStringChars");
    }

    /// <summary>A render that runs past its time limit is abandoned.</summary>
    [Fact]
    public void Ollama_render_past_the_timeout_throws()
    {
        //Arrange
        var limits = new TemplateLimits { MaxRenderSteps = int.MaxValue, RenderTimeout = TimeSpan.FromMilliseconds(50) };
        OllamaTemplate template = OllamaTemplate.Parse("{{ range 1000000000000 }}{{ end }}{{ .Messages }}", limits);

        //Act
        Action act = () => template.Render(new OllamaTemplateValues(), TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("RenderTimeout");
    }

    /// <summary>A long render stops when its token is cancelled.</summary>
    [Fact]
    public void Ollama_render_stops_when_cancelled()
    {
        //Arrange
        var limits = new TemplateLimits { MaxRenderSteps = int.MaxValue, RenderTimeout = null };
        OllamaTemplate template = OllamaTemplate.Parse("{{ range 1000000000000 }}{{ end }}{{ .Messages }}", limits);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        source.CancelAfter(TimeSpan.FromMilliseconds(50));

        //Act
        Action act = () => template.Render(new OllamaTemplateValues(), source.Token);

        //Assert
        act.Should().Throw<OperationCanceledException>();
    }

    // ---------------------------------------------------------------- the chat renderer

    /// <summary>The chat renderer holds a template to the limits it was given.</summary>
    [Fact]
    public void ChatTemplateRenderer_applies_its_limits()
    {
        //Arrange
        var renderer = new ChatTemplateRenderer(ChatTemplateDialect.Jinja,
            "{% for m in messages %}{% for i in range(100) %}.{% endfor %}{% endfor %}", null, null, "", "",
            new TemplateLimits { MaxRenderSteps = 50 });
        var request = new ChatRequest();
        request.Messages.Add(new ChatMessage(ChatRole.User, "hi"));

        //Act
        Action act = () => renderer.Render(request, TestContext.Current.CancellationToken);

        //Assert
        act.Should().Throw<ChatTemplateException>().Which.Message.Should().Contain("MaxRenderSteps");
    }

    /// <summary>The managed dialects render off the engine thread; only the native one needs it.</summary>
    [Fact]
    public void ChatTemplateRenderer_only_the_native_dialect_renders_on_the_engine()
    {
        //Assert
        new ChatTemplateRenderer(ChatTemplateDialect.Jinja, "x", null, null, "", "").RendersOnEngine.Should().BeFalse();
        new ChatTemplateRenderer(ChatTemplateDialect.Ollama, null, "x", null, "", "").RendersOnEngine.Should().BeFalse();
        new ChatTemplateRenderer(ChatTemplateDialect.Native, null, null, "x", "", "").RendersOnEngine.Should().BeTrue();
    }

    // ---------------------------------------------------------------- honest templates stay far inside

    /// <summary>
    /// Every real Jinja template in the fixtures and in Ollama's index, on a 401-message chat with tool calls
    /// and twenty tools, renders using under a tenth of the step budget and nesting far inside the limit.
    /// </summary>
    [Fact]
    public void Real_jinja_templates_on_a_long_chat_with_tools_stay_far_inside_the_defaults()
    {
        //Arrange
        ChatRequest request = LongChatWithTools();
        var templates = new List<KeyValuePair<string, string>>();
        foreach (string file in Directory.GetFiles(JinjaFixtureLoader.Directory, "*.jinja"))
        {
            templates.Add(new KeyValuePair<string, string>(Path.GetFileName(file), File.ReadAllText(file)));
        }

        foreach (OllamaNamedTemplate entry in OllamaTemplateBuiltIns.Index)
        {
            templates.Add(new KeyValuePair<string, string>("index:" + entry.Name, entry.Template));
        }

        int rendered = 0;
        long mostSteps = 0;
        int deepest = 0;

        //Act
        foreach (KeyValuePair<string, string> template in templates)
        {
            JinjaTemplate compiled;
            try
            {
                compiled = JinjaTemplate.Parse(template.Value);
            }
            catch (ChatTemplateException error) when (!error.Message.Contains("TemplateLimits"))
            {
                continue;
            }

            using (TemplateBudget.Open(null, TestContext.Current.CancellationToken))
            {
                try
                {
                    compiled.Render(ChatJinjaVariables.Build(request, "<s>", "</s>"),
                        TestContext.Current.CancellationToken);
                }
                catch (ChatTemplateException error) when (!error.Message.Contains("TemplateLimits"))
                {
                    // the template's own raise_exception() about the conversation's shape, not a limit
                    continue;
                }

                rendered++;
                mostSteps = Math.Max(mostSteps, TemplateBudget.Current.StepsTaken);
                deepest = Math.Max(deepest, TemplateBudget.Current.DeepestLevel);
            }
        }

        //Assert
        TestContext.Current.TestOutputHelper?.WriteLine($"{rendered} Jinja templates: most steps {mostSteps}, deepest {deepest}");
        rendered.Should().BeGreaterThan(10);
        mostSteps.Should().BeLessThan(new TemplateLimits().MaxRenderSteps / 10);
        deepest.Should().BeLessThan(new TemplateLimits().MaxNestingDepth / 4);
    }

    /// <summary>Every Ollama built-in, on the same long chat, stays as far inside.</summary>
    [Fact]
    public void Ollama_built_ins_on_a_long_chat_with_tools_stay_far_inside_the_defaults()
    {
        //Arrange
        ChatRequest request = LongChatWithTools();
        long mostSteps = 0;
        int deepest = 0;

        //Act
        foreach (string name in OllamaTemplate.BuiltInNames)
        {
            OllamaTemplate template = OllamaTemplate.BuiltIn(name);
            using (TemplateBudget.Open(null, TestContext.Current.CancellationToken))
            {
                template.Render(ChatOllamaValues.Build(request), TestContext.Current.CancellationToken);
                mostSteps = Math.Max(mostSteps, TemplateBudget.Current.StepsTaken);
                deepest = Math.Max(deepest, TemplateBudget.Current.DeepestLevel);
            }
        }

        //Assert
        TestContext.Current.TestOutputHelper?.WriteLine($"Ollama built-ins: most steps {mostSteps}, deepest {deepest}");
        mostSteps.Should().BeLessThan(new TemplateLimits().MaxRenderSteps / 10);
        deepest.Should().BeLessThan(new TemplateLimits().MaxNestingDepth / 4);
    }

    // ---------------------------------------------------------------- stack safety of the defaults

    /// <summary>
    /// The deepest template the default limit lets through - in each of the shapes that cost the most stack
    /// per level - parses and renders on a thread with a 1 MiB stack, the smallest default a .NET thread
    /// gets, and it is the nesting LIMIT that refuses the next level, not the stack backstop. Each shape is
    /// grown one level at a time until it is refused, so every depth up to the deepest permitted one runs.
    /// </summary>
    /// <param name="shape">Which deep shape to build.</param>
    [Theory]
    [MemberData(nameof(DeepShapes))]
    public void Deepest_permitted_template_fits_a_one_megabyte_stack(string shape)
    {
        //Act
        (int deepest, Exception failure, string refusal) = GrowUntilRefused(shape, 1024 * 1024);

        //Assert
        failure.Should().BeNull();
        deepest.Should().BeGreaterThan(10);
        refusal.Should().Contain("MaxNestingDepth");
        refusal.Should().NotContain("for the stack");
    }

    /// <summary>
    /// On a thread with a stack far smaller than any default, the same shapes are refused with
    /// <see cref="ChatTemplateException"/> by the stack backstop instead of overflowing the stack and ending
    /// the process.
    /// </summary>
    /// <param name="shape">Which deep shape to build.</param>
    [Theory]
    [MemberData(nameof(DeepShapes))]
    public void Deep_template_on_a_small_stack_is_refused_not_overflowed(string shape)
    {
        //Act
        (int _, Exception failure, string refusal) = GrowUntilRefused(shape, 192 * 1024);

        //Assert
        failure.Should().BeNull();
        refusal.Should().Contain("MaxNestingDepth");
    }

    /// <summary>The deep shapes the stack tests grow.</summary>
    public static TheoryData<string> DeepShapes => new TheoryData<string>
    {
        "jinja-parentheses", "jinja-not", "jinja-negation", "jinja-statements", "jinja-chain", "jinja-filters",
        "jinja-calls", "jinja-macro-recursion", "jinja-nested-value",
        "ollama-parentheses", "ollama-branches", "ollama-else-if", "ollama-blocks", "ollama-template-calls",
    };

    private static (int Deepest, Exception Failure, string Refusal) GrowUntilRefused(string shape, int stackSize)
    {
        Func<int, string> render = Shape(shape);
        int limit = new TemplateLimits().MaxNestingDepth;
        Exception failure = null;
        string refusal = null;
        int deepest = -1;
        var thread = new Thread(() =>
        {
            try
            {
                for (int n = 1; n <= limit * 4; n++)
                {
                    try
                    {
                        render(n);
                        deepest = n;
                    }
                    catch (ChatTemplateException error) when (error.Message.Contains("MaxNestingDepth"))
                    {
                        refusal = error.Message;
                        return;
                    }
                }

                failure = new InvalidOperationException("the shape was never refused at the nesting limit");
            }
            catch (Exception error)
            {
                failure = error;
            }
        }, stackSize);

        thread.Start();
        thread.Join();
        return (deepest, failure, refusal);
    }

    private static Func<int, string> Shape(string shape)
    {
        switch (shape)
        {
            case "jinja-parentheses":
                return n => RenderJinja("{{ " + Repeat("(", n) + "1" + Repeat(")", n) + " }}");
            case "jinja-not":
                return n => RenderJinja("{{ " + Repeat("not ", n) + "true }}");
            case "jinja-negation":
                return n => RenderJinja("{{ " + Repeat("- ", n) + "1 }}");
            case "jinja-statements":
                return n => RenderJinja(Repeat("{% for i in [1] %}{% if true %}", n) + "{{ i }}"
                    + Repeat("{% endif %}{% endfor %}", n));
            case "jinja-chain":
                return n => RenderJinja("{{ 1" + Repeat(" + 1", n) + " }}");
            case "jinja-filters":
                return n => RenderJinja("{{ 'a'" + Repeat("|trim", n) + " }}");
            case "jinja-calls":
                return n => RenderJinja("{{ " + Repeat("dict(a=", n) + "1" + Repeat(")", n) + " }}");
            case "jinja-macro-recursion":
                return n => RenderJinja(
                    "{% macro f(n) %}{% if n > 0 %}{{ f(n - 1) }}{% endif %}{% endmacro %}{{ f(" + n + ") }}");
            case "jinja-nested-value":
                return n => RenderJinja("{% set ns = namespace(x=1) %}{% for i in range(" + n
                    + ") %}{% set ns.x = [ns.x] %}{% endfor %}{{ ns.x }}{{ ns.x == ns.x }}{{ ns.x < ns.x }}");
            case "ollama-parentheses":
                return n => RenderOllama("{{ " + Repeat("(", n) + "1" + Repeat(")", n) + " }}");
            case "ollama-branches":
                return n => RenderOllama(Repeat("{{ range 1 }}{{ with 1 }}", n) + "x" + Repeat("{{ end }}{{ end }}", n));
            case "ollama-else-if":
                return n => RenderOllama("{{ if 0 }}" + Repeat("{{ else if 0 }}", n) + "{{ else }}x{{ end }}");
            case "ollama-blocks":
                {
                    return n =>
                    {
                        var text = new StringBuilder();
                        for (int i = 0; i < n; i++)
                        {
                            text.Append("{{ block \"b").Append(i).Append("\" . }}");
                        }

                        return RenderOllama(text.Append('x').Append(Repeat("{{ end }}", n)).ToString());
                    };
                }

            default:
                {
                    return n =>
                    {
                        var text = new StringBuilder();
                        for (int i = 0; i < n; i++)
                        {
                            text.Append("{{ define \"t").Append(i).Append("\" }}{{ template \"t").Append(i + 1)
                                .Append("\" . }}{{ end }}");
                        }

                        text.Append("{{ define \"t").Append(n).Append("\" }}x{{ end }}{{ template \"t0\" . }}");
                        return RenderOllama(text.ToString());
                    };
                }
        }
    }

    private static string RenderJinja(string source)
        => JinjaTemplate.Parse(source).Render(NoVariables, CancellationToken.None);

    private static string RenderOllama(string text)
        => OllamaTemplate.Parse(text + "{{ .Messages }}").Render(new OllamaTemplateValues(), CancellationToken.None);

    // ---------------------------------------------------------------- helpers

    private static string Repeat(string text, int count)
    {
        var builder = new StringBuilder(text.Length * count);
        for (int i = 0; i < count; i++)
        {
            builder.Append(text);
        }

        return builder.ToString();
    }

    private static ChatRequest LongChatWithTools()
    {
        var request = new ChatRequest();
        for (int t = 0; t < 20; t++)
        {
            request.Tools.Add(new ToolDefinition
            {
                Name = "tool_" + t,
                Description = "Does thing number " + t + " for the user, carefully.",
                ParametersJsonSchema =
                    "{\"type\":\"object\",\"properties\":{"
                    + "\"query\":{\"type\":\"string\",\"description\":\"What to look for\"},"
                    + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":50},"
                    + "\"mode\":{\"type\":\"string\",\"enum\":[\"fast\",\"slow\",\"exact\"]},"
                    + "\"tags\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},"
                    + "\"where\":{\"type\":\"object\",\"properties\":{"
                    + "\"city\":{\"type\":\"string\"},\"lat\":{\"type\":\"number\"},\"lon\":{\"type\":\"number\"}},"
                    + "\"required\":[\"city\"]}},"
                    + "\"required\":[\"query\"]}",
            });
        }

        string paragraph = new string('w', 400) + " and then some more words.";
        request.Messages.Add(new ChatMessage(ChatRole.System, "You are a careful assistant. " + paragraph));
        for (int round = 0; round < 100; round++)
        {
            request.Messages.Add(new ChatMessage(ChatRole.User, "Question " + round + ": " + paragraph));

            var calling = new ChatMessage(ChatRole.Assistant, string.Empty);
            calling.ToolCalls.Add(new ToolCall
            {
                Id = "call_" + round,
                Name = "tool_" + (round % 20),
                ArgumentsJson = "{\"query\":\"q" + round + "\",\"limit\":5,\"where\":{\"city\":\"Eugene\",\"lat\":44.05,\"lon\":-123.09}}",
            });
            request.Messages.Add(calling);

            request.Messages.Add(new ChatMessage(ChatRole.Tool, "{\"result\":\"" + paragraph + "\"}")
            {
                ToolCallId = "call_" + round,
                ToolName = "tool_" + (round % 20),
            });

            request.Messages.Add(new ChatMessage(ChatRole.Assistant, "Answer " + round + ": " + paragraph));
        }

        request.Messages.Add(new ChatMessage(ChatRole.User, "And finally? " + paragraph));
        return request;
    }
}
