using Mcp.Net.LLM.Interfaces;
using Mcp.Net.LLM.Models;
using Mcp.Net.WebUi.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace Mcp.Net.WebUi.Controllers;

/// <summary>
/// Controller for managing agent definitions
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AgentsController : ControllerBase
{
    private readonly ILogger<AgentsController> _logger;
    private readonly IAgentManager _agentManager;

    public AgentsController(ILogger<AgentsController> logger, IAgentManager agentManager)
    {
        _logger = logger;
        _agentManager = agentManager;
    }

    /// <summary>
    /// Get all available agents with optional category filtering
    /// </summary>
    /// <param name="category">Optional category to filter by</param>
    [HttpGet]
    public async Task<IActionResult> GetAgents([FromQuery] string? category = null)
    {
        try
        {
            AgentCategory? categoryFilter = null;

            if (
                !string.IsNullOrEmpty(category)
                && Enum.TryParse<AgentCategory>(category, true, out var parsedCategory)
            )
            {
                categoryFilter = parsedCategory;
            }

            var agents = await _agentManager.GetAgentsAsync(categoryFilter);
            var agentDtos = agents.Select(AgentSummaryDto.FromAgentDefinition);

            _logger.LogInformation("Retrieved {Count} agents", agents.Count());
            return Ok(agentDtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agents");
            return StatusCode(500, new { error = "Error retrieving agents", message = ex.Message });
        }
    }

    /// <summary>
    /// Get a single agent by ID
    /// </summary>
    /// <param name="id">Agent ID</param>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAgentById(string id)
    {
        try
        {
            var agent = await _agentManager.GetAgentByIdAsync(id);

            if (agent == null)
            {
                _logger.LogWarning("Agent {AgentId} not found", id);
                return NotFound(new { error = "Agent not found" });
            }

            var agentDto = AgentDetailsDto.FromAgentDefinition(agent);

            _logger.LogInformation("Retrieved agent {AgentId}", id);
            return Ok(agentDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agent {AgentId}", id);
            return StatusCode(500, new { error = "Error retrieving agent", message = ex.Message });
        }
    }

    /// <summary>
    /// Create a new agent
    /// </summary>
    /// <param name="createDto">Agent creation data</param>
    [HttpPost]
    public async Task<IActionResult> CreateAgent([FromBody] CreateAgentDto createDto)
    {
        try
        {
            // For now, use a default user ID
            // In the future, this would come from authentication
            const string userId = "default-user";

            var agentDefinition = createDto.ToAgentDefinition(userId);
            var createdAgent = await _agentManager.CreateAgentAsync(agentDefinition, userId);

            var resultDto = AgentDetailsDto.FromAgentDefinition(createdAgent);

            _logger.LogInformation(
                "Created agent {AgentId} with name '{Name}'",
                createdAgent.Id,
                createdAgent.Name
            );

            return CreatedAtAction(nameof(GetAgentById), new { id = createdAgent.Id }, resultDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating agent");
            return StatusCode(500, new { error = "Error creating agent", message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing agent
    /// </summary>
    /// <param name="id">Agent ID</param>
    /// <param name="updateDto">Agent update data</param>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAgent(string id, [FromBody] UpdateAgentDto updateDto)
    {
        try
        {
            // For now, use a default user ID
            // In the future, this would come from authentication
            const string userId = "default-user";

            var existingAgent = await _agentManager.GetAgentByIdAsync(id);
            if (existingAgent == null)
            {
                _logger.LogWarning("Agent {AgentId} not found for update", id);
                return NotFound(new { error = "Agent not found" });
            }

            // Prevent modification of system default agents
            if (existingAgent.IsSystemDefault)
            {
                _logger.LogWarning(
                    "Attempt to update system default agent {AgentId} was blocked",
                    id
                );
                return BadRequest(
                    new
                    {
                        error = "Cannot modify system default agents",
                        message = "System default agents cannot be modified. Create a clone if you wish to customize it.",
                    }
                );
            }

            // Apply updates to the existing agent
            updateDto.UpdateAgentDefinition(existingAgent, userId);

            var success = await _agentManager.UpdateAgentAsync(existingAgent, userId);
            if (!success)
            {
                _logger.LogWarning("Failed to update agent {AgentId}", id);
                return StatusCode(500, new { error = "Failed to update agent" });
            }

            _logger.LogInformation("Updated agent {AgentId}", id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent {AgentId}", id);
            return StatusCode(500, new { error = "Error updating agent", message = ex.Message });
        }
    }

    /// <summary>
    /// Delete an agent
    /// </summary>
    /// <param name="id">Agent ID</param>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAgent(string id)
    {
        try
        {
            // Fetch the agent first to check if it's a system default
            var agent = await _agentManager.GetAgentByIdAsync(id);
            if (agent == null)
            {
                _logger.LogWarning("Agent {AgentId} not found for deletion", id);
                return NotFound(new { error = "Agent not found" });
            }

            // Prevent deletion of system default agents
            if (agent.IsSystemDefault)
            {
                _logger.LogWarning(
                    "Attempt to delete system default agent {AgentId} was blocked",
                    id
                );
                return BadRequest(
                    new
                    {
                        error = "Cannot delete system default agents",
                        message = "System default agents cannot be deleted. Create a clone if you wish to modify it.",
                    }
                );
            }

            var success = await _agentManager.DeleteAgentAsync(id);
            if (!success)
            {
                _logger.LogWarning("Failed to delete agent {AgentId}", id);
                return StatusCode(500, new { error = "Failed to delete agent" });
            }

            _logger.LogInformation("Deleted agent {AgentId}", id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent {AgentId}", id);
            return StatusCode(500, new { error = "Error deleting agent", message = ex.Message });
        }
    }

    /// <summary>
    /// Clone an existing agent
    /// </summary>
    /// <param name="id">ID of the agent to clone</param>
    /// <param name="request">Clone options</param>
    [HttpPost("{id}/clone")]
    public async Task<IActionResult> CloneAgent(string id, [FromBody] CloneAgentRequestDto request)
    {
        try
        {
            // For now, use a default user ID
            // In the future, this would come from authentication
            const string userId = "default-user";

            var clonedAgent = await _agentManager.CloneAgentAsync(id, userId, request.NewName);

            var resultDto = AgentDetailsDto.FromAgentDefinition(clonedAgent);

            _logger.LogInformation(
                "Cloned agent {SourceAgentId} to {NewAgentId} with name '{NewName}'",
                id,
                clonedAgent.Id,
                clonedAgent.Name
            );

            return CreatedAtAction(nameof(GetAgentById), new { id = clonedAgent.Id }, resultDto);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Agent {AgentId} not found for cloning", id);
            return NotFound(new { error = "Agent not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cloning agent {AgentId}", id);
            return StatusCode(500, new { error = "Error cloning agent", message = ex.Message });
        }
    }
}
