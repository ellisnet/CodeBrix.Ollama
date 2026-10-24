using System;
using System.IO;
using System.Linq;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.Core.Tests;

/// <summary>
/// Covers the cap on sub-graph nesting: a graph that nests control-flow bodies within reach of any real model
/// reads and walks as before, and one nested deeper is refused with the codec's ordinary format exception
/// instead of recursing until the stack overflows and the process dies.
/// </summary>
public sealed class OnnxGraphProtoTests
{
    //The cap the codec enforces: the main graph plus this many levels of sub-graph below it.
    private const int Cap = 64;

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, true)]
    [InlineData(Cap, false)]
    [InlineData(Cap, true)]
    public void Parse_reads_sub_graphs_nested_up_to_the_cap(int depth, bool repeated)
    {
        //Arrange
        byte[] bytes = OnnxModel.FromProto(BuildNestedModel(depth, repeated), null).Serialize();

        //Act
        OnnxModel model = OnnxModel.Parse(bytes, null);

        //Assert
        model.EnumerateTensors().Select(tensor => tensor.Name)
            .Should().ContainSingle().Which.Should().Be("innermost");
        model.Serialize().Should().BeEquivalentTo(bytes);
    }

    [Theory]
    [InlineData(Cap + 1, false)]
    [InlineData(Cap + 1, true)]
    [InlineData(2000, false)]
    public void Parse_refuses_sub_graphs_nested_past_the_cap(int depth, bool repeated)
    {
        //Arrange
        byte[] bytes = OnnxModel.FromProto(BuildNestedModel(depth, repeated), null).Serialize();

        //Act
        Action act = () => OnnxModel.Parse(bytes, null);

        //Assert
        act.Should().Throw<InvalidDataException>().Which.Message.Should().Contain("nests sub-graphs");
    }

    [Fact]
    public void Parse_counts_depth_afresh_after_a_refusal()
    {
        //Arrange
        byte[] tooDeep = OnnxModel.FromProto(BuildNestedModel(Cap + 1, false), null).Serialize();
        byte[] atCap = OnnxModel.FromProto(BuildNestedModel(Cap, false), null).Serialize();

        //Act
        Action refused = () => OnnxModel.Parse(tooDeep, null);
        refused.Should().Throw<InvalidDataException>();
        OnnxModel model = OnnxModel.Parse(atCap, null);

        //Assert
        model.EnumerateTensors().Should().ContainSingle();
    }

    [Fact]
    public void EnumerateTensors_refuses_an_in_memory_graph_nested_past_the_cap()
    {
        //Arrange
        OnnxModel model = OnnxModel.FromProto(BuildNestedModel(Cap + 1, false), null);

        //Act
        Action act = () => model.EnumerateTensors().ToList();

        //Assert
        act.Should().Throw<InvalidDataException>();
    }

    //A main graph whose one node carries a sub-graph, whose one node carries a sub-graph, and so on, depth
    //levels down; the innermost graph holds one initializer so a walk has something to find. The sub-graph
    //sits in the single "g" field of an If-style attribute, or in the repeated "graphs" field.
    private static OnnxModelProto BuildNestedModel(int depth, bool repeated)
    {
        OnnxGraphProto graph = new OnnxGraphProto { Name = "level" + depth };
        graph.Initializers.Add(new OnnxTensorProto
        {
            Name = "innermost",
            DataType = (int)OnnxTensorDataType.Float,
            RawData = new byte[] { 0, 0, 128, 63 },
        });

        for (int level = depth - 1; level >= 0; level--)
        {
            OnnxAttributeProto attribute = new OnnxAttributeProto { Name = repeated ? "bodies" : "then_branch" };
            if (repeated)
            {
                attribute.AttributeType = (int)OnnxAttributeType.Graphs;
                attribute.Graphs.Add(graph);
            }
            else
            {
                attribute.AttributeType = (int)OnnxAttributeType.Graph;
                attribute.Graph = graph;
            }

            OnnxNodeProto node = new OnnxNodeProto { OpType = "If", Name = "if" + level };
            node.Attributes.Add(attribute);
            graph = new OnnxGraphProto { Name = "level" + level };
            graph.Nodes.Add(node);
        }

        OnnxModelProto proto = new OnnxModelProto { IrVersion = 9, Graph = graph };
        proto.OpsetImports.Add(new OnnxOperatorSetId { Domain = string.Empty, Version = 17 });
        return proto;
    }
}
