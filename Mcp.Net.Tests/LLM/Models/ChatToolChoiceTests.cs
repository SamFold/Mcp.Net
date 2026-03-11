using FluentAssertions;
using Mcp.Net.LLM.Models;

namespace Mcp.Net.Tests.LLM.Models;

public class ChatToolChoiceTests
{
    [Fact]
    public void PredefinedChoices_ShouldExposeExpectedKinds()
    {
        ChatToolChoice.Auto.Kind.Should().Be(ChatToolChoiceKind.Auto);
        ChatToolChoice.None.Kind.Should().Be(ChatToolChoiceKind.None);
        ChatToolChoice.Required.Kind.Should().Be(ChatToolChoiceKind.Required);

        ChatToolChoice.Auto.Should().BeSameAs(ChatToolChoice.Auto);
        ChatToolChoice.None.Should().BeSameAs(ChatToolChoice.None);
        ChatToolChoice.Required.Should().BeSameAs(ChatToolChoice.Required);
    }

    [Fact]
    public void ForTool_ShouldCreateSpecificChoice()
    {
        var choice = ChatToolChoice.ForTool("search");

        choice.Kind.Should().Be(ChatToolChoiceKind.Specific);
        choice.ToolName.Should().Be("search");
    }

    [Fact]
    public void ForTool_WithoutName_ShouldThrow()
    {
        var act = () => ChatToolChoice.ForTool(" ");

        act.Should().Throw<ArgumentException>();
    }
}
