using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Tools;
using Sherlock.MCP.Tests.IlAnalysisFixtures;

namespace Sherlock.MCP.Tests;

public class IlAnalysisToolsTests
{
    private static readonly string TestAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private static readonly string Subject = typeof(CallChainSubject).FullName!;

    [Fact]
    public void GetMethodCalls_Mermaid_ReturnsFlowchartWithTransitiveCallees()
    {
        var data = Data(Call(format: "mermaid", depth: 3));
        var diagram = data.GetProperty("diagram").GetString()!;

        Assert.Equal("mermaid", data.GetProperty("format").GetString());
        Assert.Equal(3, data.GetProperty("depth").GetInt32());
        Assert.StartsWith("flowchart LR", diagram);
        Assert.Contains($"[\"{Subject}.StepTwo\"]", diagram);
        Assert.Contains("[\"System.Console.WriteLine\"]:::external", diagram);
        Assert.Contains("classDef external", diagram);
        Assert.False(data.TryGetProperty("calls", out _));
    }

    [Fact]
    public void GetMethodCalls_Json_ReportsFormat()
    {
        var data = Data(Call());

        Assert.Equal("json", data.GetProperty("format").GetString());
        Assert.Equal(JsonValueKind.Array, data.GetProperty("calls").ValueKind);
    }

    [Fact]
    public void GetMethodCalls_DepthWithJson_ReturnsInvalidArgument()
    {
        var error = JsonDocument.Parse(Call(depth: 2)).RootElement;

        Assert.Equal("InvalidArgument", error.GetProperty("code").GetString());
        Assert.Equal("depth > 1 requires format='mermaid'", error.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void GetMethodCalls_DepthOutOfRange_ReturnsInvalidArgument(int depth) =>
        Assert.Contains("InvalidArgument", Call(format: "mermaid", depth: depth));

    [Fact]
    public void GetMethodCalls_InvalidFormat_ReturnsError() =>
        Assert.Contains("InvalidFormat", Call(format: "dot"));

    [Fact]
    public void GetMethodCalls_JsonAndMermaid_DoNotShareCacheEntries()
    {
        var middleware = TestMiddleware.Fresh;

        var json = Data(Call(middleware: middleware));
        var mermaid = Data(Call(middleware: middleware, format: "mermaid"));

        Assert.Equal("json", json.GetProperty("format").GetString());
        Assert.Equal("mermaid", mermaid.GetProperty("format").GetString());
    }

    private static string Call(Server.Middleware.ToolMiddleware? middleware = null, string format = "json", int depth = 1) =>
        IlAnalysisTools.GetMethodCalls(
            new IlAnalysisService(), middleware ?? TestMiddleware.Fresh, TestHandles.Registry,
            typeName: Subject, methodName: nameof(CallChainSubject.Entry), assemblyPath: TestAssemblyPath,
            format: format, depth: depth).Text();

    private static JsonElement Data(string json) => JsonDocument.Parse(json).RootElement.GetProperty("data").Clone();
}
