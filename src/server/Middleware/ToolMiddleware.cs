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
        if (TryGetCached(cacheKey, noCache, out var cached))
            return cached;

        var sw = Stopwatch.StartNew();
        var result = action();
        return Store(cacheKey, result, sw, noCache);
    }

    public async Task<string> ExecuteAsync(string cacheKey, Func<Task<string>> action, bool noCache = false)
    {
        if (TryGetCached(cacheKey, noCache, out var cached))
            return cached;

        var sw = Stopwatch.StartNew();
        var result = await action();
        return Store(cacheKey, result, sw, noCache);
    }

    public async Task<string> ExecuteWhenCacheableAsync(string cacheKey, Func<Task<(string Result, bool Cacheable)>> action, bool noCache = false)
    {
        if (TryGetCached(cacheKey, noCache, out var cached))
            return cached;

        var sw = Stopwatch.StartNew();
        var (result, cacheable) = await action();
        return Store(cacheKey, result, sw, noCache || !cacheable);
    }

    private bool TryGetCached(string cacheKey, bool noCache, out string cached)
    {
        if (!noCache && _cache.TryGet(cacheKey, out var hit) && hit != null)
        {
            _telemetry.Increment("cache.hit");
            cached = hit;
            return true;
        }

        cached = string.Empty;
        return false;
    }

    private string Store(string cacheKey, string result, Stopwatch sw, bool noCache)
    {
        sw.Stop();
        _telemetry.TrackDuration("tool.duration", sw.Elapsed);

        if (!noCache)
            _cache.Set(cacheKey, result, TimeSpan.FromSeconds(Math.Max(1, _options.CacheTtlSeconds)));

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

