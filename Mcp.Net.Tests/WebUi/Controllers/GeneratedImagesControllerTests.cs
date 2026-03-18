using FluentAssertions;
using Mcp.Net.WebUi.Controllers;
using Mcp.Net.WebUi.Images;
using Microsoft.AspNetCore.Mvc;

namespace Mcp.Net.Tests.WebUi.Controllers;

public class GeneratedImagesControllerTests
{
    [Fact]
    public void Get_WhenArtifactExists_ShouldReturnFileResponse()
    {
        var store = new GeneratedImageArtifactStore();
        var artifact = store.Store(BinaryData.FromBytes([4, 5, 6]), "image/png");
        var controller = new GeneratedImagesController(store);

        var result = controller.Get(artifact.Id);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("image/png");
        file.FileContents.Should().Equal([4, 5, 6]);
    }

    [Fact]
    public void Get_WhenArtifactDoesNotExist_ShouldReturnNotFound()
    {
        var controller = new GeneratedImagesController(new GeneratedImageArtifactStore());

        var result = controller.Get("missing");

        result.Should().BeOfType<NotFoundResult>();
    }
}
