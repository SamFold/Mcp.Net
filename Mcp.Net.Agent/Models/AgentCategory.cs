namespace Mcp.Net.Agent.Models;

public enum AgentCategory
{
    General,
    Math,
    Research,
    Development,
    Creative,
    Custom,
    Verifier, // For agents that validate/test/review work
    Coordinator, // For agents that break down tasks and coordinate other agents
    Specialist, // Domain-specific expert agents
    Analytics, // For data analysis and processing
    Assistant, // General help/support agents
    Educator, // Teaching/explaining focused agents
    Uncategorized, // For agents that haven't been categorized yet
}
