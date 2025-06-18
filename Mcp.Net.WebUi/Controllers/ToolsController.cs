using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Tools;
using Microsoft.AspNetCore.Mvc;

namespace Mcp.Net.WebUi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ToolsController : ControllerBase
{
    private readonly ILogger<ToolsController> _logger;
    private readonly ToolRegistry _toolRegistry;
    private readonly IAgentManager? _agentManager;

    public ToolsController(
        ILogger<ToolsController> logger,
        ToolRegistry toolRegistry,
        IAgentManager? agentManager = null
    )
    {
        _logger = logger;
        _toolRegistry = toolRegistry;
        _agentManager = agentManager;
    }

    /// <summary>
    /// Get all available tools
    /// </summary>
    [HttpGet]
    public IActionResult GetTools()
    {
        var tools = _toolRegistry.AllTools;
        return Ok(tools);
    }

    /// <summary>
    /// Get all enabled tools
    /// </summary>
    [HttpGet("enabled")]
    public IActionResult GetEnabledTools()
    {
        var tools = _toolRegistry.EnabledTools;
        return Ok(tools);
    }

    /// <summary>
    /// Enable specific tools by name
    /// </summary>
    [HttpPost("enabled")]
    public IActionResult EnableTools([FromBody] string[] toolNames)
    {
        try
        {
            _toolRegistry.SetEnabledTools(toolNames);
            _logger.LogInformation("Enabled tools: {ToolNames}", string.Join(", ", toolNames));
            return Ok(_toolRegistry.EnabledTools);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling tools");
            return StatusCode(500, new { error = "Error enabling tools", message = ex.Message });
        }
    }

    /// <summary>
    /// Get available tool categories
    /// </summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetToolCategories()
    {
        try
        {
            if (_agentManager == null)
            {
                _logger.LogWarning("AgentManager not available for tool categories");
                return StatusCode(501, new { error = "Tool categories not available" });
            }

            var categories = await _agentManager.GetToolCategoriesAsync();
            return Ok(categories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tool categories");
            return StatusCode(
                500,
                new { error = "Error retrieving tool categories", message = ex.Message }
            );
        }
    }

    /// <summary>
    /// Get tools by category
    /// </summary>
    /// <param name="category">Category name</param>
    [HttpGet("categories/{category}")]
    public async Task<IActionResult> GetToolsByCategory(string category)
    {
        try
        {
            if (_agentManager == null)
            {
                _logger.LogWarning("AgentManager not available for tools by category");
                return StatusCode(501, new { error = "Tool categories not available" });
            }

            var toolIds = await _agentManager.GetToolsByCategoryAsync(category);

            var tools = _toolRegistry.AllTools.Where(t => toolIds.Contains(t.Name)).ToList();

            return Ok(tools);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tools for category {Category}", category);
            return StatusCode(
                500,
                new
                {
                    error = $"Error retrieving tools for category {category}",
                    message = ex.Message,
                }
            );
        }
    }
}
