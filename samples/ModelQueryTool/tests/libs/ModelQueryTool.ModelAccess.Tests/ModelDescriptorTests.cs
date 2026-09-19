using System;
using ModelQueryTool.ModelAccess.Models;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ModelAccess.Tests;

/// <summary>
/// What a descriptor insists on. A descriptor that exists is one the stager can act on, so every
/// argument is checked where it is given rather than where it is used.
/// </summary>
public class ModelDescriptorTests
{
    /// <summary>A blank store name is refused.</summary>
    [Fact]
    public void ModelDescriptor_refuses_a_blank_name()
    {
        //Act
        Action act = () => new ModelDescriptor("   ", "Something");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A store name the model-name grammar does not accept is refused.</summary>
    [Fact]
    public void ModelDescriptor_refuses_a_name_that_is_not_a_model_name()
    {
        //Act
        Action act = () => new ModelDescriptor("hf.co//broken:tag", "Something");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A blank display name is refused.</summary>
    [Fact]
    public void ModelDescriptor_refuses_a_blank_display_name()
    {
        //Act
        Action act = () => new ModelDescriptor(TinyModelHarness.ModelName, " ");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A negative expected total is refused.</summary>
    [Fact]
    public void ModelDescriptor_refuses_a_negative_expected_total()
    {
        //Act
        Action act = () => new ModelDescriptor(TinyModelHarness.ModelName, "Something", -1L);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>A digest that carries the manifest's prefix is refused; the plain hexadecimal is wanted.</summary>
    [Fact]
    public void ModelDescriptor_refuses_a_digest_that_carries_the_sha256_prefix()
    {
        //Act
        Action act = () => new ModelDescriptor(
            TinyModelHarness.ModelName,
            "Something",
            0L,
            "sha256:" + new string('a', 64));

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A digest that is not hexadecimal is refused.</summary>
    [Fact]
    public void ModelDescriptor_refuses_a_digest_that_is_not_hexadecimal()
    {
        //Act
        Action act = () => new ModelDescriptor(
            TinyModelHarness.ModelName,
            "Something",
            0L,
            new string('z', 64));

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A digest is kept in lower case, whatever case it was written in.</summary>
    [Fact]
    public void WeightsSha256_is_kept_in_lower_case()
    {
        //Act
        var descriptor = new ModelDescriptor(
            TinyModelHarness.ModelName,
            "Something",
            0L,
            new string('A', 64));

        //Assert
        descriptor.WeightsSha256.Should().Be(new string('a', 64));
    }

    /// <summary>Expecting no particular digest is written down as expecting nothing.</summary>
    [Fact]
    public void WeightsSha256_is_nothing_when_no_digest_is_expected()
    {
        //Act
        var descriptor = new ModelDescriptor(TinyModelHarness.ModelName, "Something", 12L);

        //Assert
        descriptor.WeightsSha256.Should().BeNull();
        descriptor.ExpectedTotalBytes.Should().Be(12L);
        descriptor.DisplayName.Should().Be("Something");
        descriptor.Name.Should().Be(TinyModelHarness.ModelName);
    }
}
