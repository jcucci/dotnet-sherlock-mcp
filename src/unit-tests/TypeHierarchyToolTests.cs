using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class TypeHierarchyToolTests
{
    private readonly ITypeAnalysisService _typeAnalysis = new TypeAnalysisService();
    private readonly IReverseLookupService _reverseLookup = new ReverseLookupService();
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;

    [Fact]
    public void GetTypeHierarchy_WithoutScope_ReturnsNullDerivedTypes_AndNote()
    {
        var result = TypeAnalysisTools.GetTypeHierarchy(
            _typeAnalysis, _reverseLookup, _testAssemblyPath, "BaseSample");

        Assert.DoesNotContain("\"error\"", result);
        var data = JsonDocument.Parse(result).RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("DerivedTypes").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("Note").GetString()));
    }

    [Fact]
    public void GetTypeHierarchy_WithScope_PopulatesDerivedTypes_AndNullNote()
    {
        var result = TypeAnalysisTools.GetTypeHierarchy(
            _typeAnalysis, _reverseLookup, _testAssemblyPath, "BaseSample",
            additionalAssemblies: new[] { _testAssemblyPath });

        Assert.DoesNotContain("\"error\"", result);
        var data = JsonDocument.Parse(result).RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("Note").ValueKind);

        var derived = data.GetProperty("DerivedTypes");
        Assert.Equal(JsonValueKind.Array, derived.ValueKind);
        Assert.True(derived.GetArrayLength() > 0);

        var hit = Enumerable.Range(0, derived.GetArrayLength())
            .Select(i => derived[i])
            .FirstOrDefault(e => e.GetProperty("TypeFullName").GetString()!.Contains("DerivedSample"));
        Assert.Equal(JsonValueKind.Object, hit.ValueKind);
        Assert.Equal("baseType", hit.GetProperty("Kind").GetString());
        Assert.True(hit.TryGetProperty("AssemblyPath", out _));
    }

    [Fact]
    public void GetTypeHierarchy_TypeNotFound_ReturnsError()
    {
        var result = TypeAnalysisTools.GetTypeHierarchy(
            _typeAnalysis, _reverseLookup, _testAssemblyPath, "NoSuchTypeXyz");

        Assert.Contains("TypeNotFound", result);
    }

    [Fact]
    public void GetTypeHierarchy_MissingAdditionalAssembly_ReturnsError()
    {
        var result = TypeAnalysisTools.GetTypeHierarchy(
            _typeAnalysis, _reverseLookup, _testAssemblyPath, "BaseSample",
            additionalAssemblies: new[] { "/tmp/does-not-exist.dll" });

        Assert.Contains("AssemblyNotFound", result);
        Assert.Contains("does-not-exist", result);
    }
}
