using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Server.Middleware;

// Lightweight wrapper to add caching + timing around tool execution.
public sealed class ToolMiddleware
{
    private readonly IToolResponseCache _cache;
    private readonly ITelemetry _telemetry;
    private readonly RuntimeOptions _options;

    public ToolMiddleware(IToolResponseCache cache, ITelemetry telemetry, RuntimeOptions options)
    {
        _cache = cache;
        _telemetry = telemetry;
        _options = options;
    }

    public string Execute(string cacheKey, Func<string> action, bool noCache = false)
    {
        if (!noCache && _cache.TryGet(cacheKey, out var cached) && cached != null)
        {
            _telemetry.Increment("cache.hit");
            return cached;
        }

        var sw = Stopwatch.StartNew();
        var result = action();
        sw.Stop();
        _telemetry.TrackDuration("tool.duration", sw.Elapsed);

        if (!noCache)
        {
            _cache.Set(cacheKey, result, TimeSpan.FromSeconds(Math.Max(1, _options.CacheTtlSeconds)));
        }

        return result;
    }

    public CallToolResult Execute(string cacheKey, Func<ToolResponse> action, bool noCache = false)
    {
        var payload = Execute(
            cacheKey,
            () => JsonSerializer.Serialize(action().ToCallToolResult(), McpJsonUtilities.DefaultOptions),
            noCache);
        return JsonSerializer.Deserialize<CallToolResult>(payload, McpJsonUtilities.DefaultOptions)!;
    }
}

