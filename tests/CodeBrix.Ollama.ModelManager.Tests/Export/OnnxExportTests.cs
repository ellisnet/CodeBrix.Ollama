using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers what an export decides before it does anything: which route a model calls for, which of its
/// files a pass-through registers, which modules each route needs and what the provenance records.
/// None of it starts an interpreter or touches a file.
/// </summary>
public sealed class OnnxExportTests
{
    [Theory]
    [InlineData("model.onnx", true)]
    [InlineData("onnx/model_base.ONNX", true)]
    [InlineData("model.onnx_data", false)]
    [InlineData("model.safetensors", false)]
    [InlineData("", false)]
    public void IsOnnxFile_answers_for_the_names_a_bundle_carries(string path, bool expected)
        => OnnxExport.IsOnnxFile(path).Should().Be(expected);

    [Theory]
    [InlineData("model.safetensors", true)]
    [InlineData("pytorch_model.bin", true)]
    [InlineData("optimizer.pt", true)]
    [InlineData("weights/model.pth", true)]
    [InlineData("checkpoints/model_16.ckpt.index", true)]
    [InlineData("model.onnx", false)]
    [InlineData("config.json", false)]
    [InlineData("onnx/model.onnx_data", false)]
    public void IsSupersededCheckpoint_names_only_what_an_export_replaces(string path, bool expected)
        => OnnxExport.IsSupersededCheckpoint(path).Should().Be(expected);

    [Fact]
    public void PassThroughFiles_keeps_the_graphs_and_what_goes_with_them()
    {
        //Arrange
        IReadOnlyList<ResolvedFile> files = Files(
            "README.md", "config.json", "model.safetensors", "onnx/model_base.onnx", "onnx/model_base.onnx_data",
            "pytorch_model.bin");

        //Act
        IReadOnlyList<ResolvedFile> kept = OnnxExport.PassThroughFiles(files);

        //Assert
        kept.Count.Should().Be(4);
        kept[0].Name.Should().Be("README.md");
        kept[3].Name.Should().Be("onnx/model_base.onnx_data");
    }

    [Fact]
    public void ChooseRoute_takes_the_pass_through_when_the_publisher_shipped_a_graph()
        => OnnxExport.ChooseRoute(Files("config.json", "onnx/model.onnx"), "{\"architectures\":[\"LlamaForCausalLM\"]}")
            .Should().Be(ExportRoute.PublisherOnnx);

    [Fact]
    public void ChooseRoute_takes_the_builder_for_an_architecture_it_writes()
        => OnnxExport.ChooseRoute(Files("config.json", "pytorch_model.bin"), "{\"architectures\":[\"LlamaForCausalLM\"]}")
            .Should().Be(ExportRoute.GenAiBuilder);

    [Fact]
    public void ChooseRoute_takes_optimum_for_an_architecture_the_builder_does_not_write()
        => OnnxExport.ChooseRoute(Files("config.json", "pytorch_model.bin"), "{\"architectures\":[\"BertModel\"]}")
            .Should().Be(ExportRoute.Optimum);

    [Fact]
    public void ChooseRoute_takes_optimum_when_there_is_no_configuration_at_all()
        => OnnxExport.ChooseRoute(Files("pytorch_model.bin"), null).Should().Be(ExportRoute.Optimum);

    [Theory]
    [InlineData("{\"architectures\":[\"LlamaForCausalLM\"]}", "LlamaForCausalLM")]
    [InlineData("{\"architectures\":[]}", null)]
    [InlineData("{\"model_type\":\"llama\"}", null)]
    [InlineData("not json at all", null)]
    [InlineData(null, null)]
    public void ReadArchitectures_reads_what_a_configuration_file_names(string json, string expected)
    {
        //Act
        IReadOnlyList<string> architectures = OnnxExport.ReadArchitectures(json);

        //Assert
        if (expected == null)
        {
            architectures.Should().BeEmpty();
        }
        else
        {
            architectures.Should().Equal(expected);
        }
    }

    [Theory]
    [InlineData("LlamaForCausalLM", true)]
    [InlineData("Qwen2ForCausalLM", true)]
    [InlineData("WhisperForConditionalGeneration", true)]
    [InlineData("BertModel", false)]
    [InlineData("llamaforcausallm", false)]
    public void NamesBuilderArchitecture_is_the_list_the_builder_branches_on(string architecture, bool expected)
        => OnnxExport.NamesBuilderArchitecture(new[] { architecture }).Should().Be(expected);

    [Theory]
    [InlineData("LlamaForCausalLM", "text-generation-with-past")]
    [InlineData("GPT2LMHeadModel", "text-generation-with-past")]
    [InlineData("T5ForConditionalGeneration", "text2text-generation-with-past")]
    [InlineData("BertForSequenceClassification", "text-classification")]
    [InlineData("BertForTokenClassification", "token-classification")]
    [InlineData("BertForQuestionAnswering", "question-answering")]
    [InlineData("RobertaForMaskedLM", "fill-mask")]
    [InlineData("BertForMultipleChoice", "multiple-choice")]
    [InlineData("ViTForImageClassification", "image-classification")]
    [InlineData("SomethingElseEntirely", "auto")]
    public void TaskFor_reads_the_task_out_of_the_architecture_name(string architecture, string expected)
        => OnnxExport.TaskFor(new[] { architecture }).Should().Be(expected);

    [Fact]
    public void TaskFor_with_nothing_to_go_on_leaves_the_task_to_the_tool()
    {
        //Act and assert
        OnnxExport.TaskFor(null).Should().Be("auto");
        OnnxExport.TaskFor(Array.Empty<string>()).Should().Be("auto");
    }

    [Fact]
    public void ModulesFor_names_what_each_route_imports()
    {
        //Act and assert
        OnnxExport.ModulesFor(ExportRoute.GenAiBuilder).Should().Equal(
            "onnxruntime_genai", "torch", "transformers", "onnx");
        OnnxExport.ModulesFor(ExportRoute.Optimum).Should().Equal("optimum", "onnx", "torch", "transformers");
        OnnxExport.ModulesFor(ExportRoute.PublisherOnnx).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ExportRoute.GenAiBuilder, "onnxruntime-genai")]
    [InlineData(ExportRoute.Optimum, "optimum")]
    [InlineData(ExportRoute.PublisherOnnx, "publisher")]
    public void ToolFor_names_the_tool_a_route_records(ExportRoute route, string expected)
        => OnnxExport.ToolFor(route).Should().Be(expected);

    [Fact]
    public void SettingsFor_records_the_route_that_ran_and_the_one_that_was_asked_for()
    {
        //Arrange
        var options = new ExportOptions { Route = ExportRoute.Auto, Precision = "int4", AllowRemoteCode = true };

        //Act
        IReadOnlyDictionary<string, string> settings = OnnxExport.SettingsFor(ExportRoute.GenAiBuilder, options);

        //Assert
        settings["route"].Should().Be("GenAiBuilder");
        settings["requestedRoute"].Should().Be("Auto");
        settings["precision"].Should().Be("int4");
        settings["allowRemoteCode"].Should().Be("true");
        settings["executionProvider"].Should().Be("cpu");
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("tool_use", true)]
    [InlineData("rag-v1.2", true)]
    [InlineData("A", true)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("../evil", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("/etc/x", false)]
    [InlineData("name\n", false)]
    [InlineData("na me", false)]
    [InlineData("café", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSafeChatTemplateName_takes_only_a_plain_file_name(string name, bool expected)
        => OnnxExport.IsSafeChatTemplateName(name).Should().Be(expected);

    [Fact]
    public void IsSafeChatTemplateName_takes_64_characters_and_no_more()
    {
        //Act and assert
        OnnxExport.IsSafeChatTemplateName(new string('a', 64)).Should().BeTrue();
        OnnxExport.IsSafeChatTemplateName(new string('a', 65)).Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"chat_template\":\"{{ messages }}\"}")]
    [InlineData("{\"chat_template\":{\"default\":\"a\",\"tool_use\":\"b\"}}")]
    [InlineData("{\"chat_template\":[{\"name\":\"default\",\"template\":\"a\"},{\"name\":\"rag\",\"template\":\"b\"}]}")]
    [InlineData("{\"model_max_length\":2048}")]
    [InlineData("{\"chat_template\":null}")]
    [InlineData("[1,2,3]")]
    public void FindUnsafeChatTemplateName_passes_what_an_honest_publisher_writes(string json)
        => OnnxExport.FindUnsafeChatTemplateName(json).Should().BeNull();

    [Theory]
    [InlineData("{\"chat_template\":{\"../../../../tmp/owned\":\"x\"}}", "../../../../tmp/owned")]
    [InlineData("{\"chat_template\":{\"default\":\"a\",\"..\":\"x\"}}", "'..'")]
    [InlineData("{\"chat_template\":[{\"name\":\"default\",\"template\":\"a\"},{\"name\":\"/abs/path\",\"template\":\"b\"}]}",
        "/abs/path")]
    [InlineData("{\"chat_template\":{\"\\u002e\\u002e\\u002fescaped\":\"x\"}}", "../escaped")]
    [InlineData("{\"chat_template\":\"fine\",\"chat_template\":{\"a/b\":\"x\"}}", "a/b")]
    public void FindUnsafeChatTemplateName_names_the_bad_key(string json, string expected)
        => OnnxExport.FindUnsafeChatTemplateName(json).Should().Contain(expected);

    [Fact]
    public void FindUnsafeChatTemplateName_refuses_a_name_that_is_not_a_string()
        => OnnxExport.FindUnsafeChatTemplateName("{\"chat_template\":[{\"name\":7,\"template\":\"a\"}]}")
            .Should().Contain("not a string");

    [Fact]
    public void FindUnsafeChatTemplateName_refuses_a_file_it_cannot_read()
        => OnnxExport.FindUnsafeChatTemplateName("{\"chat_template\":{\"a\":NaN}}")
            .Should().Contain("cannot be read as JSON");

    [Theory]
    [InlineData("tokenizer_config.json", true)]
    [InlineData("chat_template.json", true)]
    [InlineData("processor_config.json", true)]
    [InlineData("sub/tokenizer_config.json", true)]
    [InlineData("config.json", false)]
    [InlineData("Tokenizer_Config.json", false)]
    [InlineData("", false)]
    public void IsChatTemplateFile_names_the_files_a_chat_template_is_read_from(string path, bool expected)
        => OnnxExport.IsChatTemplateFile(path).Should().Be(expected);

    [Theory]
    [InlineData(ExportRoute.GenAiBuilder, true)]
    [InlineData(ExportRoute.Optimum, true)]
    [InlineData(ExportRoute.PublisherOnnx, false)]
    [InlineData(ExportRoute.MuseCocoMusic, false)]
    [InlineData(ExportRoute.MuseCocoText, false)]
    public void SavesChatTemplates_is_true_for_the_two_routes_that_save_a_tokenizer(ExportRoute route, bool expected)
        => OnnxExport.SavesChatTemplates(route).Should().Be(expected);

    [Theory]
    [InlineData(ExportRoute.GenAiBuilder, true)]
    [InlineData(ExportRoute.Optimum, true)]
    [InlineData(ExportRoute.PublisherOnnx, false)]
    [InlineData(ExportRoute.MuseCocoMusic, false)]
    [InlineData(ExportRoute.MuseCocoText, false)]
    public void FloorsWaivedBy_waives_transformers_only_for_the_converting_routes_when_asked(
        ExportRoute route, bool waived)
    {
        //Arrange
        var options = new ExportOptions { AllowUnpatchedTransformers = true };

        //Act and assert
        if (waived)
        {
            OnnxExport.FloorsWaivedBy(route, options).Should().Equal("transformers");
        }
        else
        {
            OnnxExport.FloorsWaivedBy(route, options).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(ExportRoute.GenAiBuilder)]
    [InlineData(ExportRoute.Optimum)]
    public void FloorsWaivedBy_waives_nothing_by_default(ExportRoute route)
    {
        //Act and assert
        OnnxExport.FloorsWaivedBy(route, new ExportOptions()).Should().BeEmpty();
        OnnxExport.FloorsWaivedBy(route, null).Should().BeEmpty();
    }

    [Fact]
    public void RequireSafeChatTemplateNames_throws_the_export_refusal_naming_the_file_and_the_key()
    {
        //Act
        Action act = () => OnnxExport.RequireSafeChatTemplateNames(
            "tokenizer_config.json", "{\"chat_template\":{\"../x\":\"t\"}}", ExportRoute.Optimum);

        //Assert
        PythonScriptException thrown = act.Should().Throw<PythonScriptException>().Which;
        thrown.Feature.Should().Be("exporting to ONNX");
        thrown.ScriptName.Should().Be(OnnxExport.ScriptFor(ExportRoute.Optimum));
        thrown.PythonMessage.Should().Contain("tokenizer_config.json");
        thrown.PythonMessage.Should().Contain("'../x'");
        thrown.PythonMessage.Should().Contain("CVE-2026-9856");
    }

    /// <summary>
    /// Resolved files with the names a test cares about and nothing else that matters.
    /// </summary>
    /// <param name="names">The publisher paths.</param>
    /// <returns>The files.</returns>
    private static IReadOnlyList<ResolvedFile> Files(params string[] names)
    {
        var files = new List<ResolvedFile>(names.Length);
        foreach (string name in names)
        {
            files.Add(new ResolvedFile(name, "/blobs/sha256-" + names.Length, 1, "sha256:" + name));
        }
        return files;
    }
}
