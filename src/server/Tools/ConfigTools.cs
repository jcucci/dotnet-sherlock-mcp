using System.ComponentModel;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.SourceLink;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class ConfigTools
{
    [McpServerTool(Title = "Get Runtime Options", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets current runtime configuration (default page sizes, cache TTL, search roots). Use to understand current settings before update_runtime_options.")]
    public static string GetRuntimeOptions(RuntimeOptions options)
    {
        var result = new
        {
            searchRoots = options.SearchRoots.ToArray(),
            defaultMaxItems = options.DefaultMaxItems,
            cacheTtlSeconds = options.CacheTtlSeconds,
            includeNonPublicByDefault = options.IncludeNonPublicByDefault,
            maxLoadedAssemblies = options.MaxLoadedAssemblies,
            maxCachedResponses = options.MaxCachedResponses,
            maxAssemblyHandles = options.MaxAssemblyHandles,
            sourceFetch = SourceFetchName(options.SourceFetch),
            sourceFetchHosts = options.SourceFetchHosts.ToArray()
        };

        return JsonHelpers.Envelope("runtime.options", result);
    }

    [McpServerTool(Title = "Update Runtime Options", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Updates runtime configuration. Reduce defaultMaxItems for smaller responses, increase cacheTtlSeconds for better performance. Omit fields to keep current values.")]
    public static string UpdateRuntimeOptions(
        RuntimeOptions options,
        [Description("Default page size (maxItems)")] int? defaultMaxItems = null,
        [Description("Cache TTL in seconds")] int? cacheTtlSeconds = null,
        [Description("Include non-public members by default")] bool? includeNonPublicByDefault = null,
        [Description("Add search roots (absolute paths)")] string[]? addSearchRoots = null,
        [Description("Remove search roots (absolute paths)")] string[]? removeSearchRoots = null,
        [Description("Maximum assemblies kept loaded in the inspection cache")] int? maxLoadedAssemblies = null,
        [Description("Maximum cached tool responses kept in memory")] int? maxCachedResponses = null,
        [Description("Maximum assembly handles kept in the on-disk handle registry")] int? maxAssemblyHandles = null,
        [Description("Source Link fetching for get_member_source: 'off', 'known-hosts' (only sourceFetchHosts) or 'any'")] string? sourceFetch = null,
        [Description("Add hosts get_member_source may fetch Source Link files from, e.g. 'git.example.com' or '*.example.com'")] string[]? addSourceFetchHosts = null,
        [Description("Remove hosts from sourceFetchHosts")] string[]? removeSourceFetchHosts = null)
    {
        if (sourceFetch != null)
        {
            if (!RuntimeOptions.TryParseSourceFetch(sourceFetch, out var mode))
                return JsonHelpers.Error("InvalidArgument", "sourceFetch must be 'off', 'known-hosts' or 'any'");
            options.SourceFetch = mode;
        }

        if (defaultMaxItems is > 0) options.DefaultMaxItems = defaultMaxItems.Value;
        if (cacheTtlSeconds is > 0) options.CacheTtlSeconds = cacheTtlSeconds.Value;
        if (maxLoadedAssemblies is > 0) options.MaxLoadedAssemblies = maxLoadedAssemblies.Value;
        if (maxCachedResponses is > 0) options.MaxCachedResponses = maxCachedResponses.Value;
        if (maxAssemblyHandles is > 0) options.MaxAssemblyHandles = maxAssemblyHandles.Value;
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

        if (addSourceFetchHosts is { Length: > 0 } || removeSourceFetchHosts is { Length: > 0 })
            options.SourceFetchHosts = UpdatedHosts(options.SourceFetchHosts, addSourceFetchHosts ?? [], removeSourceFetchHosts ?? []);

        return GetRuntimeOptions(options);
    }

    private static string[] UpdatedHosts(IReadOnlyList<string> current, string[] add, string[] remove)
    {
        var removed = remove.Select(host => host.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return current
            .Concat(add.Select(host => host.Trim()).Where(host => host.Length > 0))
            .Where(host => !removed.Contains(host))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string SourceFetchName(SourceFetchMode mode) => mode switch
    {
        SourceFetchMode.Off => "off",
        SourceFetchMode.AnyHost => "any",
        _ => "known-hosts"
    };
}

