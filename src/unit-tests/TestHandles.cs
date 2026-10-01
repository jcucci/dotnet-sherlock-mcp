using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Runtime.Telemetry;

namespace Sherlock.MCP.Tests;

internal static class TestHandles
{
    public static IAssemblyHandleRegistry Registry { get; } = Create();

    public static AssemblyHandleRegistry Create(string? stateDirectory = null) =>
        new(new RuntimeOptions { StateDirectory = stateDirectory ?? NewStateDirectory() }, new NoopTelemetry());

    public static string NewStateDirectory() =>
        Path.Combine(Path.GetTempPath(), "sherlock-tests", Guid.NewGuid().ToString("N"));
}
