using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests; //was previously: ollama/ollama parser/parser_test.go and api/types.go;

/// <summary>
/// Tests for <see cref="Modelfile.GetParameters"/>, which applies the typing Ollama's
/// api.FormatParams applies to PARAMETER lines.
/// </summary>
public sealed class ModelfileParametersTests
{
    /// <summary>Every parameter Ollama types as an integer lands on its typed property.</summary>
    [Fact]
    public void GetParameters_with_integer_parameters_maps_every_one()
    {
        //Arrange
        var modelfile = Modelfile.Parse("FROM foo\nPARAMETER num_keep 1\nPARAMETER seed 2\n"
            + "PARAMETER num_predict 3\nPARAMETER top_k 4\nPARAMETER repeat_last_n 5\n"
            + "PARAMETER num_ctx 4096\nPARAMETER num_batch 512\nPARAMETER num_gpu 33\n"
            + "PARAMETER main_gpu 1\nPARAMETER num_thread 8\nPARAMETER draft_num_predict 6\n");

        //Act
        var parameters = modelfile.GetParameters();

        //Assert
        parameters.NumKeep.Should().Be(1);
        parameters.Seed.Should().Be(2);
        parameters.NumPredict.Should().Be(3);
        parameters.TopK.Should().Be(4);
        parameters.RepeatLastN.Should().Be(5);
        parameters.NumCtx.Should().Be(4096);
        parameters.NumBatch.Should().Be(512);
        parameters.NumGpu.Should().Be(33);
        parameters.MainGpu.Should().Be(1);
        parameters.NumThread.Should().Be(8);
        parameters.DraftNumPredict.Should().Be(6);
    }

    /// <summary>Every parameter Ollama types as a float lands on its typed property.</summary>
    [Fact]
    public void GetParameters_with_float_parameters_maps_every_one()
    {
        //Arrange
        var modelfile = Modelfile.Parse("FROM foo\nPARAMETER top_p 0.9\nPARAMETER min_p 0.05\n"
            + "PARAMETER typical_p 1.0\nPARAMETER temperature 0.5\nPARAMETER repeat_penalty 1.1\n"
            + "PARAMETER presence_penalty 1.2\nPARAMETER frequency_penalty 1.3\n");

        //Act
        var parameters = modelfile.GetParameters();

        //Assert
        parameters.TopP.Should().Be(0.9f);
        parameters.MinP.Should().Be(0.05f);
        parameters.TypicalP.Should().Be(1.0f);
        parameters.Temperature.Should().Be(0.5f);
        parameters.RepeatPenalty.Should().Be(1.1f);
        parameters.PresencePenalty.Should().Be(1.2f);
        parameters.FrequencyPenalty.Should().Be(1.3f);
    }

    /// <summary>A float is read with the invariant culture, so a point is the decimal separator.</summary>
    /// <param name="value">The value as written in the Modelfile.</param>
    /// <param name="expected">The expected float.</param>
    [Theory]
    [InlineData("0.5", 0.5f)]
    [InlineData("1", 1f)]
    [InlineData("-0.25", -0.25f)]
    [InlineData("+2.5", 2.5f)]
    [InlineData("1e-2", 0.01f)]
    public void GetParameters_with_float_value_reads_it_with_the_invariant_culture(string value,
        float expected)
    {
        //Act
        var parameters = Modelfile.Parse("FROM foo\nPARAMETER temperature " + value + "\n")
            .GetParameters();

        //Assert
        parameters.Temperature.Should().Be(expected);
    }

    /// <summary>A negative integer is accepted, as Go's ParseInt accepts it.</summary>
    [Fact]
    public void GetParameters_with_negative_integer_reads_it()
    {
        //Act
        var parameters = Modelfile.Parse("FROM foo\nPARAMETER num_predict -1\n").GetParameters();

        //Assert
        parameters.NumPredict.Should().Be(-1);
    }

    /// <summary>use_mmap takes every boolean spelling Go accepts.</summary>
    /// <param name="value">The value as written in the Modelfile.</param>
    /// <param name="expected">The expected boolean.</param>
    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("t", true)]
    [InlineData("T", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    [InlineData("f", false)]
    [InlineData("F", false)]
    [InlineData("0", false)]
    public void GetParameters_with_use_mmap_reads_go_boolean_spellings(string value, bool expected)
    {
        //Act
        var parameters = Modelfile.Parse("FROM foo\nPARAMETER use_mmap " + value + "\n")
            .GetParameters();

        //Assert
        parameters.UseMmap.Should().Be(expected);
    }

    /// <summary>Repeated stop values are collected in the order they were written.</summary>
    [Fact]
    public void GetParameters_with_repeated_stop_collects_every_value_in_order()
    {
        //Arrange
        var modelfile = Modelfile.Parse("FROM foo\nPARAMETER stop \"### User:\"\n"
            + "PARAMETER stop <|eot_id|>\nPARAMETER stop </s>\n");

        //Act
        var parameters = modelfile.GetParameters();

        //Assert
        parameters.Stop.Should().Equal("### User:", "<|eot_id|>", "</s>");
    }

    /// <summary>A repeated parameter that is not stop keeps the value written last.</summary>
    [Fact]
    public void GetParameters_with_repeated_name_keeps_the_last_value()
    {
        //Act
        var parameters = Modelfile
            .Parse("FROM foo\nPARAMETER num_ctx 2048\nPARAMETER num_ctx 4096\n").GetParameters();

        //Assert
        parameters.NumCtx.Should().Be(4096);
    }

    /// <summary>A file without PARAMETER lines produces parameters with nothing set.</summary>
    [Fact]
    public void GetParameters_with_no_parameters_leaves_everything_unset()
    {
        //Act
        var parameters = Modelfile.Parse("FROM foo\n").GetParameters();

        //Assert
        parameters.NumCtx.Should().BeNull();
        parameters.Temperature.Should().BeNull();
        parameters.Stop.Should().BeNull();
        parameters.UseMmap.Should().BeNull();
        parameters.MainGpu.Should().BeNull();
    }

    /// <summary>Parameters Ollama no longer honours are dropped rather than reported as unknown.</summary>
    /// <param name="name">The deprecated parameter's name.</param>
    /// <param name="value">A value for it.</param>
    [Theory]
    [InlineData("penalize_newline", "true")]
    [InlineData("low_vram", "true")]
    [InlineData("f16_kv", "true")]
    [InlineData("logits_all", "true")]
    [InlineData("vocab_only", "true")]
    [InlineData("use_mlock", "true")]
    [InlineData("mirostat", "1")]
    [InlineData("mirostat_tau", "5.0")]
    [InlineData("mirostat_eta", "0.1")]
    public void GetParameters_with_deprecated_parameter_drops_it(string name, string value)
    {
        //Arrange
        var modelfile = Modelfile.Parse(
            "FROM foo\nPARAMETER " + name + " " + value + "\nPARAMETER num_ctx 4096\n");

        //Act
        var parameters = modelfile.GetParameters();

        //Assert
        parameters.NumCtx.Should().Be(4096);
        modelfile.DeprecatedParameters.Should().Equal(name);
        modelfile.ParameterLines.Should().HaveCount(2);
    }

    /// <summary>A parameter Ollama does not know is reported.</summary>
    /// <param name="name">The unknown parameter's name.</param>
    [Theory]
    [InlineData("numa")]
    [InlineData("num_gqa")]
    [InlineData("rope_frequency_base")]
    [InlineData("param1")]
    public void GetParameters_with_unknown_parameter_throws(string name)
    {
        //Act
        var exception = Catch("FROM foo\nPARAMETER " + name + " 1\n");

        //Assert
        exception.Should().NotBeNull();
        exception.LineNumber.Should().Be(0);
        exception.Message.Should().Be("unknown parameter '" + name + "'");
    }

    /// <summary>A value that is not an integer is reported.</summary>
    [Fact]
    public void GetParameters_with_bad_integer_value_throws()
    {
        //Act
        var exception = Catch("FROM foo\nPARAMETER num_ctx many\n");

        //Assert
        exception.Should().NotBeNull();
        exception.Message.Should().Be("invalid int value [many]");
    }

    /// <summary>A value that is not a float is reported.</summary>
    [Fact]
    public void GetParameters_with_bad_float_value_throws()
    {
        //Act
        var exception = Catch("FROM foo\nPARAMETER temperature warm\n");

        //Assert
        exception.Should().NotBeNull();
        exception.Message.Should().Be("invalid float value [warm]");
    }

    /// <summary>A value that is not one of Go's boolean spellings is reported.</summary>
    [Fact]
    public void GetParameters_with_bad_boolean_value_throws()
    {
        //Act
        var exception = Catch("FROM foo\nPARAMETER use_mmap maybe\n");

        //Assert
        exception.Should().NotBeNull();
        exception.Message.Should().Be("invalid bool value [maybe]");
    }

    /// <summary>An integer too large for the store's 32-bit fields is reported.</summary>
    [Fact]
    public void GetParameters_with_out_of_range_integer_throws()
    {
        //Act
        var exception = Catch("FROM foo\nPARAMETER seed 4294967296\n");

        //Assert
        exception.Should().NotBeNull();
        exception.Message.Should().StartWith("int value [4294967296]");
    }

    private static ModelfileParseException Catch(string input)
    {
        try
        {
            Modelfile.Parse(input).GetParameters();
        }
        catch (ModelfileParseException exception)
        {
            return exception;
        }

        return null;
    }
}
