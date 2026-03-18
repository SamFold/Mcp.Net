# Change Summary

- added typed multimodal user-message DTOs and SignalR hub support so the Web UI backend can forward text-and-image user turns into `ChatSession`
- added an OpenAI-backed local `generate_image` tool that returns image artifacts as normal tool-result resource links
- added generated-image artifact storage and `/api/generated-images/{artifactId}` serving, plus transcript and tool-execution mapping for inline image rendering

# Why

- the Web UI backend needed to stop collapsing user images back to plain text when messages crossed the browser/session boundary
- image generation is a better fit as a normal local tool than as a special provider-chat mode, because the model can decide when to use it during regular conversation
- the browser needed stable artifact URLs rather than raw inline bytes in generic tool text

# Major Files Changed

- `Mcp.Net.WebUi/Hubs/ChatHub.cs`
- `Mcp.Net.WebUi/DTOs/ChatTranscriptEntryDto.cs`
- `Mcp.Net.WebUi/DTOs/UserMessageContentPartDto.cs`
- `Mcp.Net.WebUi/Chat/ChatTranscriptEntryMapper.cs`
- `Mcp.Net.WebUi/Sessions/SessionHost.cs`
- `Mcp.Net.WebUi/Images/OpenAiImageGenerationService.cs`
- `Mcp.Net.WebUi/Tools/GenerateImageTool.cs`
- `Mcp.Net.WebUi/Controllers/GeneratedImagesController.cs`
- `Mcp.Net.WebUi/Startup/WebUiStartup.cs`
- `Mcp.Net.WebUi/README.md`

# Verification Notes

- `dotnet test Mcp.Net.Tests/Mcp.Net.Tests.csproj -c Release --filter "FullyQualifiedName~Mcp.Net.Tests.WebUi"`
