using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ModelRunning.Tests;

public class BasicTests
{
    [Fact]
    public void can_run_tests()
    {
        //Arrange
        var isRunning = true;

        //Assert
        isRunning.Should().Be(true);
    }
}
