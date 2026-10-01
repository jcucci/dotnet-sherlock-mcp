using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Middleware;

namespace Sherlock.MCP.Tests;

internal static class TestMiddleware
{
    public static ToolMiddleware Fresh => new(new InMemoryToolResponseCache(), new NoopTelemetry(), new RuntimeOptions());
}
