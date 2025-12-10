using System.Diagnostics;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Telemetry;

namespace Sherlock.MCP.Server.Middleware;

public record ExecutionResult(string Response, bool WasCached, int CacheTtlSeconds);

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

    public ExecutionResult ExecuteWithMeta(string cacheKey, Func<string> action, bool noCache = false)
    {
        var wasCached = false;

        if (!noCache && _cache.TryGet(cacheKey, out var cached) && cached != null)
        {
            _telemetry.Increment("cache.hit");
            wasCached = true;
            return new ExecutionResult(cached, wasCached, _options.CacheTtlSeconds);
        }

        var sw = Stopwatch.StartNew();
        var result = action();
        sw.Stop();
        _telemetry.TrackDuration("tool.duration", sw.Elapsed);

        if (!noCache)
        {
            _cache.Set(cacheKey, result, TimeSpan.FromSeconds(Math.Max(1, _options.CacheTtlSeconds)));
        }

        return new ExecutionResult(result, wasCached, _options.CacheTtlSeconds);
    }

    public int CacheTtlSeconds => _options.CacheTtlSeconds;
}

