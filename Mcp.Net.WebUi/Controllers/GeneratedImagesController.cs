using Mcp.Net.WebUi.Images;
using Microsoft.AspNetCore.Mvc;

namespace Mcp.Net.WebUi.Controllers;

[ApiController]
[Route("api/generated-images")]
public class GeneratedImagesController : ControllerBase
{
    private readonly GeneratedImageArtifactStore _artifactStore;

    public GeneratedImagesController(GeneratedImageArtifactStore artifactStore)
    {
        _artifactStore = artifactStore;
    }

    [HttpGet("{artifactId}")]
    public IActionResult Get(string artifactId)
    {
        if (!_artifactStore.TryGet(artifactId, out var artifact))
        {
            return NotFound();
        }

        return File(artifact!.Data.ToArray(), artifact.MediaType);
    }
}
