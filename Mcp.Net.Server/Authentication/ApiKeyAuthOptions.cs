namespace Mcp.Net.Server.Authentication;

/// <summary>
/// Options for API key authentication
/// </summary>
/// <remarks>
/// This class provides configuration specific to API key authentication,
/// extending the base authentication options.
/// </remarks>
public class ApiKeyAuthOptions : AuthOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyAuthOptions"/> class
    /// with default values.
    /// </summary>
    public ApiKeyAuthOptions()
    {
        SchemeName = "ApiKey";
    }

    /// <summary>
    /// Gets or sets the header name for the API key
    /// </summary>
    public string HeaderName { get; set; } = "X-API-Key";

    /// <summary>
    /// Gets or sets the query parameter name for the API key
    /// </summary>
    public string QueryParamName { get; set; } = "api_key";

    /// <summary>
    /// Gets or sets a development-only API key
    /// </summary>
    /// <remarks>
    /// If specified, this API key will be automatically registered.
    /// THIS IS FOR DEVELOPMENT/TESTING ONLY. DO NOT USE THIS IN PRODUCTION.
    /// Using this in production will create a security vulnerability.
    /// </remarks>
    public string? DevelopmentApiKey { get; set; }

    /// <summary>
    /// Gets or sets whether to allow API keys from query parameters
    /// </summary>
    /// <remarks>
    /// For production scenarios, this should typically be set to false
    /// as query parameters may be logged in server logs.
    /// </remarks>
    public bool AllowQueryParam { get; set; } = true;

    /// <summary>
    /// Gets or sets additional API keys.
    /// </summary>
    /// <remarks>
    /// Dictionary mapping API keys to user IDs. This is for simple in-memory
    /// API key storage. For production scenarios, use a custom IApiKeyValidator.
    /// </remarks>
    public Dictionary<string, string> ApiKeys { get; set; } = new();

    /// <summary>
    /// Configures the options with a specific development API key.
    /// </summary>
    /// <param name="apiKey">The development API key</param>
    /// <returns>The options instance for chaining</returns>
    public ApiKeyAuthOptions WithApiKey(string apiKey)
    {
        DevelopmentApiKey = apiKey;
        return this;
    }

    /// <summary>
    /// Configures the options with multiple API keys.
    /// </summary>
    /// <param name="apiKeys">Dictionary mapping API keys to user IDs</param>
    /// <returns>The options instance for chaining</returns>
    public ApiKeyAuthOptions WithApiKeys(Dictionary<string, string> apiKeys)
    {
        ApiKeys = apiKeys;
        return this;
    }

    /// <summary>
    /// Gets whether any API keys are configured.
    /// </summary>
    public override bool IsSecurityConfigured =>
        base.IsSecurityConfigured || !string.IsNullOrEmpty(DevelopmentApiKey) || ApiKeys.Count > 0;
}
