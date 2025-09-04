using System.ComponentModel;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class ConfigTools
{
    [McpServerTool]
    [Description("Gets current runtime options for paging, caching, and safety")] 
    public static string GetRuntimeOptions(RuntimeOptions options)
    {
        var result = new
        {
            searchRoots = options.SearchRoots.ToArray(),
            defaultMaxItems = options.DefaultMaxItems,
            cacheTtlSeconds = options.CacheTtlSeconds,
            enableStreaming = options.EnableStreaming,
            includeNonPublicByDefault = options.IncludeNonPublicByDefault
        };

        return JsonHelpers.Envelope("runtime.options", result);
    }

    [McpServerTool]
    [Description("Updates runtime options. Omit fields to keep current values")]
    public static string UpdateRuntimeOptions(
        RuntimeOptions options,
        [Description("Default page size (maxItems)")] int? defaultMaxItems = null,
        [Description("Cache TTL in seconds")] int? cacheTtlSeconds = null,
        [Description("Enable server-side streaming")] bool? enableStreaming = null,
        [Description("Include non-public members by default")] bool? includeNonPublicByDefault = null,
        [Description("Add search roots (absolute paths)")] string[]? addSearchRoots = null,
        [Description("Remove search roots (absolute paths)")] string[]? removeSearchRoots = null)
    {
        if (defaultMaxItems is > 0) options.DefaultMaxItems = defaultMaxItems.Value;
        if (cacheTtlSeconds is > 0) options.CacheTtlSeconds = cacheTtlSeconds.Value;
        if (enableStreaming.HasValue) options.EnableStreaming = enableStreaming.Value;
        if (includeNonPublicByDefault.HasValue) options.IncludeNonPublicByDefault = includeNonPublicByDefault.Value;

        if (addSearchRoots is { Length: > 0 })
        {
            foreach (var root in addSearchRoots)
            {
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root) && !options.SearchRoots.Contains(root))
                {
                    options.SearchRoots.Add(root);
                }
            }
        }

        if (removeSearchRoots is { Length: > 0 })
        {
            foreach (var root in removeSearchRoots)
            {
                options.SearchRoots.Remove(root);
            }
        }

        return GetRuntimeOptions(options);
    }
}

