using System;
using System.Collections.Generic;
using System.Linq;
using Mcp.Net.Core.Models.Completion;
using Mcp.Net.Core.Models.Prompts;
using Mcp.Net.Core.Models.Resources;

namespace Mcp.Net.WebUi.DTOs;

public static class DtoMappers
{
    public static PromptSummaryDto ToPromptSummary(Prompt prompt)
    {
        if (prompt == null)
        {
            throw new ArgumentNullException(nameof(prompt));
        }

        var arguments = prompt.Arguments is { Length: > 0 }
            ? prompt.Arguments
                .Select(arg => new PromptArgumentDto
                {
                    Name = arg.Name,
                    Description = arg.Description,
                    Required = arg.Required,
                })
                .ToArray()
            : Array.Empty<PromptArgumentDto>();

        return new PromptSummaryDto
        {
            Name = prompt.Name,
            Title = prompt.Title,
            Description = prompt.Description,
            Arguments = arguments,
        };
    }

    public static ResourceSummaryDto ToResourceSummary(Resource resource)
    {
        if (resource == null)
        {
            throw new ArgumentNullException(nameof(resource));
        }

        return new ResourceSummaryDto
        {
            Uri = resource.Uri,
            Name = resource.Name,
            Description = resource.Description,
            MimeType = resource.MimeType,
        };
    }

    public static CompletionResultDto ToCompletionResult(CompletionValues values)
    {
        if (values == null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        return new CompletionResultDto
        {
            Values = values.Values,
            Total = values.Total,
            HasMore = values.HasMore,
        };
    }
}
