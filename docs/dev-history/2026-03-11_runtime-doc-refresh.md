# Change Summary

- refreshed the `Mcp.Net.LLM` README to describe the current stateless provider boundary and async-stream transport
- updated the LLM/provider-boundary design note to match the shipped `ChatClientRequest` and async-stream surface
- added an `Mcp.Net.Agent` runtime README describing the current session loop, turn summaries, and tool-executor seam

# Why

- the docs were behind the current runtime shape after the provider-boundary and continue/turn-summary work
- the repo needed a clear written description of the current agent/runtime split before the next tool and hygiene slices

# Major Files Changed

- `Mcp.Net.LLM/README.md`
- `docs/llm-provider-boundary.md`
- `Mcp.Net.Agent/README.md`

# Verification Notes

- documentation-only change
- content checked against the current `ChatSession`, `IChatClient`, and provider-boundary code paths during review
