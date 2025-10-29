using System.Threading.Tasks;
using FluentAssertions;
using Mcp.Net.Examples.SimpleServer;
using Mcp.Net.Examples.SimpleServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mcp.Net.Tests.SimpleServer;

[CollectionDefinition("CSharpCodeExecutionTests", DisableParallelization = true)]
public sealed class CSharpCodeExecutionTestCollection { }

[Collection("CSharpCodeExecutionTests")]
public class CSharpCodeExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_SelfContained_WritesConsoleOutput()
    {
        var service = new CSharpCodeExecutionService();

        var result = await service.ExecuteAsync(
            "Console.WriteLine(\"Hello from tests\");",
            CodeExecutionMode.SelfContained,
            timeoutMs: 5000
        );

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Hello from tests");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_RawScript_ReturnsValue()
    {
        var service = new CSharpCodeExecutionService();

        var result = await service.ExecuteAsync(
            "int Square(int value) => value * value;\nConsole.WriteLine(Square(6));",
            CodeExecutionMode.RawScript,
            timeoutMs: 5000
        );

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("36");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidCode_ReturnsCompilationError()
    {
        var service = new CSharpCodeExecutionService();

        var result = await service.ExecuteAsync(
            "Console.Writeline(\"missing e\");",
            CodeExecutionMode.SelfContained,
            timeoutMs: 5000
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Compilation error");
    }
}

[Collection("CSharpCodeExecutionTests")]
public class CodeExecutionToolsTests
{
    private readonly CSharpCodeExecutionService _service = new();
    private readonly CodeExecutionTools _tools;

    public CodeExecutionToolsTests()
    {
        _tools = new CodeExecutionTools(_service, NullLogger<CodeExecutionTools>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultsToSelfContainedMode()
    {
        var response = await _tools.ExecuteAsync(
            "Console.WriteLine(\"Hi\");",
            mode: null,
            timeoutMs: 5000
        );

        response.Success.Should().BeTrue();
        response.Mode.Should().Be(nameof(CodeExecutionMode.SelfContained));
        response.Output.Should().Contain("Hi");
        response.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownMode_AddsWarningAndFallsBack()
    {
        var response = await _tools.ExecuteAsync(
            "Console.WriteLine(\"Hi\");",
            mode: "mystery-mode",
            timeoutMs: 5000
        );

        response.Success.Should().BeTrue();
        response.Mode.Should().Be(nameof(CodeExecutionMode.SelfContained));
        response.Warnings.Should().NotBeEmpty();
        response.Warnings.Should().ContainMatch("*mystery-mode*");
    }

    [Fact]
    public async Task ExecuteAsync_TruncatesLongOutput()
    {
        var response = await _tools.ExecuteAsync(
            "Console.WriteLine(new string('a', 40000));",
            mode: null,
            timeoutMs: 5000
        );

        response.Success.Should().BeTrue(response.Error ?? "Tool returned no error message");
        response.Output.Should().NotBeNull();
        response.Output!.Length.Should().Be(32768);
        response.OutputTruncated.Should().BeTrue();
        response.Warnings.Should().ContainMatch("*truncated*");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidTimeout_AddsWarning()
    {
        var response = await _tools.ExecuteAsync(
            "Console.WriteLine(\"Hi\");",
            mode: null,
            timeoutMs: -5
        );

        response.Success.Should().BeTrue();
        response.TimeoutMs.Should().Be(5000);
        response.Warnings.Should().ContainMatch("*Timeout must be positive*");
    }

    [Fact]
    public async Task ExecuteAsync_CompilationError_SurfacesFromService()
    {
        var response = await _tools.ExecuteAsync(
            "Console.Writeline(\"missing e\");",
            mode: null,
            timeoutMs: 5000
        );

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("Compilation error");
    }
}
