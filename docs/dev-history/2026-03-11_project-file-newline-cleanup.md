# Change Summary

- normalized the trailing newline state of the `Mcp.Net.Agent` and `Mcp.Net.LLM` project files

# Why

- the files were already dirty only because of end-of-file newline drift, and the user requested a full cleanup commit set

# Major Files Changed

- `Mcp.Net.Agent/Mcp.Net.Agent.csproj`
- `Mcp.Net.LLM/Mcp.Net.LLM.csproj`

# Verification Notes

- no behavioral change
- included as housekeeping only
