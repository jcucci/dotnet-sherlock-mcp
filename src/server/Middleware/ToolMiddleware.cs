using System.Diagnostics;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Telemetry;

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
}

