using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class ReflectionToolsTests : IDisposable
{
    private readonly SharedInspectionContextProvider _contexts = new(new RuntimeOptions());
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;

    public void Dispose() => _contexts.Dispose();

    [Fact]
    public void GetAssemblyInfo_Summary_EnvelopeAndShape()
    {
        var result = ReflectionTools.GetAssemblyInfo(_contexts, _testAssemblyPath);

        Assert.DoesNotContain("\"error\"", result);
        var doc = JsonDocument.Parse(result);
        Assert.Equal("reflection.assemblyInfo", doc.RootElement.GetProperty("kind").GetString());

        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("summary", data.GetProperty("projection").GetString());
        Assert.False(string.IsNullOrEmpty(data.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrEmpty(data.GetProperty("version").GetString()));
        Assert.False(string.IsNullOrEmpty(data.GetProperty("targetFramework").GetString()));
        Assert.True(data.GetProperty("referencedAssemblies").GetArrayLength() > 0);
        Assert.False(data.TryGetProperty("attributes", out _), "Summary should not include attributes");
    }

    [Fact]
    public void GetAssemblyInfo_Full_IncludesAttributes()
    {
        var result = ReflectionTools.GetAssemblyInfo(_contexts, _testAssemblyPath, projection: "full");

        Assert.DoesNotContain("\"error\"", result);
        var data = JsonDocument.Parse(result).RootElement.GetProperty("data");
        Assert.Equal("full", data.GetProperty("projection").GetString());
        Assert.True(data.GetProperty("attributes").GetArrayLength() > 0);
    }

    [Fact]
    public void GetAssemblyInfo_InvalidProjection_ReturnsInvalidProjection()
    {
        var result = ReflectionTools.GetAssemblyInfo(_contexts, _testAssemblyPath, projection: "verbose");

        var doc = JsonDocument.Parse(result);
        Assert.Equal("error", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal("InvalidProjection", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void GetAssemblyInfo_ProjectionIsNormalized()
    {
        var result = ReflectionTools.GetAssemblyInfo(_contexts, _testAssemblyPath, projection: "  FULL  ");

        Assert.DoesNotContain("\"error\"", result);
        var data = JsonDocument.Parse(result).RootElement.GetProperty("data");
        Assert.Equal("full", data.GetProperty("projection").GetString());
        Assert.True(data.GetProperty("attributes").GetArrayLength() > 0);
    }

    [Fact]
    public void GetAssemblyInfo_MissingFile_ReturnsAssemblyNotFound()
    {
        var result = ReflectionTools.GetAssemblyInfo(_contexts, "/no/such/assembly.dll");

        var doc = JsonDocument.Parse(result);
        Assert.Equal("error", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal("AssemblyNotFound", doc.RootElement.GetProperty("code").GetString());
    }
}
