using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Proves that the pickle interpreter behind the PyTorch checkpoint reader is a reader and not an interpreter of
/// programs: it builds the value tree a state dictionary needs, and refuses - by name - every construct that
/// would let a checkpoint's author decide what runs.
/// </summary>
public sealed class RestrictedUnpicklerTests
{
    [Fact]
    public void Load_builds_a_dictionary_of_tensors()
    {
        //Arrange
        byte[] pickle = new PickleBuilder()
            .Protocol()
            .EmptyDict()
            .Mark()
            .Unicode("weight")
            .Global("torch._utils", "_rebuild_tensor_v2")
            .Mark()
            .Mark()
            .Unicode("storage")
            .Global("torch", "FloatStorage")
            .Unicode("0")
            .Unicode("cpu")
            .Int(6)
            .Tuple()
            .PersistentId()
            .Int(0)
            .Mark().Int(2).Int(3).Tuple()
            .Mark().Int(3).Int(1).Tuple()
            .Opcode(0x89)
            .Global("collections", "OrderedDict")
            .EmptyTuple()
            .Reduce()
            .Tuple()
            .Reduce()
            .SetItems()
            .Stop();

        //Act
        object result = RestrictedUnpickler.Load(pickle);

        //Assert
        var state = result as OrderedDictionary<string, object>;
        state.Should().NotBeNull();
        state.Count.Should().Be(1);
        var tensor = state["weight"] as PickleTensor;
        tensor.Should().NotBeNull();
        tensor.Storage.DataType.Should().Be(CheckpointDataType.F32);
        tensor.Storage.ElementCount.Should().Be(6);
        tensor.Shape.Should().BeEquivalentTo(new long[] { 2, 3 });
        tensor.Strides.Should().BeEquivalentTo(new long[] { 3, 1 });
    }

    [Fact]
    public void Load_refuses_a_global_that_would_run_a_command()
    {
        //Arrange
        byte[] pickle = new PickleBuilder()
            .Protocol()
            .Global("os", "system")
            .Mark()
            .Unicode("echo hello")
            .Tuple()
            .Reduce()
            .Stop();

        //Act
        Action act = () => RestrictedUnpickler.Load(pickle);

        //Assert
        act.Should().Throw<PickleRefusedException>().Which.Construct.Should().Be("os.system");
    }

    [Fact]
    public void Load_refuses_a_global_that_would_evaluate_code()
    {
        //Arrange
        byte[] pickle = new PickleBuilder()
            .Protocol()
            .Global("builtins", "eval")
            .Mark()
            .Unicode("1 + 1")
            .Tuple()
            .Reduce()
            .Stop();

        //Act
        Action act = () => RestrictedUnpickler.Load(pickle);

        //Assert
        act.Should().Throw<PickleRefusedException>().Which.Construct.Should().Be("builtins.eval");
    }

    [Fact]
    public void Load_refuses_a_torch_global_that_is_not_on_the_allow_list()
    {
        //Arrange
        byte[] pickle = new PickleBuilder()
            .Protocol()
            .Global("torch._utils", "_rebuild_from_type_v2")
            .EmptyTuple()
            .Reduce()
            .Stop();

        //Act
        Action act = () => RestrictedUnpickler.Load(pickle);

        //Assert
        act.Should().Throw<PickleRefusedException>().Which.Construct
            .Should().Be("torch._utils._rebuild_from_type_v2");
    }

    [Fact]
    public void Load_refuses_a_storage_class_used_as_a_call()
    {
        //Arrange
        byte[] pickle = new PickleBuilder()
            .Protocol()
            .Global("torch", "FloatStorage")
            .EmptyTuple()
            .Reduce()
            .Stop();

        //Act
        Action act = () => RestrictedUnpickler.Load(pickle);

        //Assert
        act.Should().Throw<PickleRefusedException>().Which.Message
            .Should().Contain("only inside a persistent id");
    }

    [Theory]
    [InlineData(0x93, "STACK_GLOBAL")]
    [InlineData(0x95, "FRAME")]
    [InlineData(0x62, "BUILD")]
    [InlineData(0x81, "NEWOBJ")]
    [InlineData(0x8c, "SHORT_BINUNICODE")]
    [InlineData(0x97, "NEXT_BUFFER")]
    [InlineData((byte)'i', "INST")]
    [InlineData((byte)'o', "OBJ")]
    [InlineData((byte)'T', "BINSTRING")]
    public void Load_refuses_an_opcode_it_does_not_implement(byte opcode, string expected)
    {
        //Arrange
        byte[] pickle = new PickleBuilder().Protocol().Opcode(opcode).ToArray();

        //Act
        Action act = () => RestrictedUnpickler.Load(pickle);

        //Assert
        act.Should().Throw<PickleRefusedException>().Which.Construct.Should().Be(expected);
    }

    [Theory]
    [InlineData((byte)3)]
    [InlineData((byte)4)]
    [InlineData((byte)5)]
    public void Load_refuses_a_protocol_beyond_the_one_it_implements(byte protocol)
    {
        //Arrange
        byte[] pickle = new PickleBuilder().Protocol(protocol).ToArray();

        //Act
        Action act = () => RestrictedUnpickler.Load(pickle);

        //Assert
        act.Should().Throw<PickleRefusedException>().Which.Construct.Should().Be("PROTO " + protocol);
    }

    [Fact]
    public void Load_refuses_a_persistent_id_of_the_wrong_shape()
    {
        //Arrange
        byte[] pickle = new PickleBuilder()
            .Protocol()
            .Mark()
            .Unicode("storage")
            .Unicode("0")
            .Tuple()
            .PersistentId()
            .Stop();

        //Act
        Action act = () => RestrictedUnpickler.Load(pickle);

        //Assert
        act.Should().Throw<PickleRefusedException>().Which.Construct.Should().Be("persistent id");
    }

    [Fact]
    public void Load_refuses_a_persistent_id_that_is_not_a_storage()
    {
        //Arrange
        byte[] pickle = new PickleBuilder()
            .Protocol()
            .Mark()
            .Unicode("module")
            .Global("torch", "FloatStorage")
            .Unicode("0")
            .Unicode("cpu")
            .Int(4)
            .Tuple()
            .PersistentId()
            .Stop();

        //Act
        Action act = () => RestrictedUnpickler.Load(pickle);

        //Assert
        act.Should().Throw<PickleRefusedException>().Which.Construct.Should().Be("persistent id");
    }

    [Fact]
    public void Load_refuses_a_dictionary_key_that_is_not_a_string()
    {
        //Arrange
        byte[] pickle = new PickleBuilder()
            .Protocol()
            .EmptyDict()
            .Mark()
            .Int(7)
            .Int(8)
            .SetItems()
            .Stop();

        //Act
        Action act = () => RestrictedUnpickler.Load(pickle);

        //Assert
        act.Should().Throw<PickleRefusedException>().Which.Construct.Should().Be("SETITEMS");
    }

    [Fact]
    public void Load_refuses_a_rebuilt_tensor_whose_first_argument_is_not_a_storage()
    {
        //Arrange
        byte[] pickle = new PickleBuilder()
            .Protocol()
            .Global("torch._utils", "_rebuild_tensor_v2")
            .Mark()
            .Unicode("not a storage")
            .Int(0)
            .Mark().Int(2).Tuple()
            .Mark().Int(1).Tuple()
            .Tuple()
            .Reduce()
            .Stop();

        //Act
        Action act = () => RestrictedUnpickler.Load(pickle);

        //Assert
        act.Should().Throw<PickleRefusedException>().Which.Construct
            .Should().Be("torch._utils._rebuild_tensor_v2");
    }

    [Fact]
    public void Load_refuses_a_stream_that_ends_part_way_through_an_opcode()
    {
        //Arrange
        byte[] pickle = new PickleBuilder().Protocol().Opcode((byte)'K').ToArray();

        //Act
        Action act = () => RestrictedUnpickler.Load(pickle);

        //Assert
        act.Should().Throw<CheckpointFormatException>().Which.Message.Should().Contain("ends part way through");
    }

    [Fact]
    public void Load_refuses_a_memo_entry_that_was_never_written()
    {
        //Arrange
        byte[] pickle = new PickleBuilder().Protocol().Opcode((byte)'h').Opcode(5).Stop();

        //Act
        Action act = () => RestrictedUnpickler.Load(pickle);

        //Assert
        act.Should().Throw<PickleRefusedException>().Which.Construct.Should().Be("BINGET");
    }
}
