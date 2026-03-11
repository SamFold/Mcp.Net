using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Mcp.Net.Tests.LLM.ProjectBoundary;

public class McpNetLlmProjectBoundaryTests
{
    [Fact]
    public void McpNetLlmProject_ShouldNotReferenceMcpNetClient()
    {
        var document = XDocument.Load(FindRepoRootFile("Mcp.Net.LLM/Mcp.Net.LLM.csproj"));

        var projectReferences = document
            .Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include != null)
            .ToArray();

        projectReferences.Should().NotContain(reference =>
            reference!.Contains("Mcp.Net.Client", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void McpNetLlmProject_ShouldHaveNoProjectReferences()
    {
        var document = XDocument.Load(FindRepoRootFile("Mcp.Net.LLM/Mcp.Net.LLM.csproj"));

        var projectReferences = document
            .Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include != null)
            .ToArray();

        projectReferences.Should().BeEmpty(
            "Mcp.Net.LLM should be a standalone provider library with no project references"
        );
    }

    private static string FindRepoRootFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Mcp.Net.sln")))
            {
                return Path.Combine(directory.FullName, relativePath);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
