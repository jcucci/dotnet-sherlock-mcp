using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class TypeHierarchyToolTests
{
    private readonly ITypeAnalysisService _typeAnalysis = new TypeAnalysisService();
    private readonly IReverseLookupService _reverseLookup = new ReverseLookupService();
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private static readonly IInspectionContextProvider Contexts = new SharedInspectionContextProvider(new RuntimeOptions());

    [Fact]
    public void GetTypeHierarchy_WithoutScope_ReturnsNullDerivedTypes_AndNote()
    {
        var result = TypeAnalysisTools.GetTypeHierarchy(
            _typeAnalysis,
            Contexts,
            _reverseLookup,
            TestMiddleware.Fresh,
            TestHandles.Registry,
            assemblyPath: _testAssemblyPath,
            typeName: "BaseSample");

        Assert.DoesNotContain("\"error\"", result);
        var data = JsonDocument.Parse(result).RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("DerivedTypes").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("Note").GetString()));
    }

    [Fact]
    public void GetTypeHierarchy_WithScope_PopulatesDerivedTypes_AndNullNote()
    {
        var result = TypeAnalysisTools.GetTypeHierarchy(
            _typeAnalysis,
            Contexts,
            _reverseLookup,
            TestMiddleware.Fresh,
            TestHandles.Registry,
            assemblyPath: _testAssemblyPath,
            typeName: "BaseSample",
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
            _typeAnalysis,
            Contexts,
            _reverseLookup,
            TestMiddleware.Fresh,
            TestHandles.Registry,
            assemblyPath: _testAssemblyPath,
            typeName: "NoSuchTypeXyz");

        Assert.Contains("TypeNotFound", result);
    }

    [Fact]
    public void GetTypeHierarchy_MissingAdditionalAssembly_ReturnsError()
    {
        var result = TypeAnalysisTools.GetTypeHierarchy(
            _typeAnalysis,
            Contexts,
            _reverseLookup,
            TestMiddleware.Fresh,
            TestHandles.Registry,
            assemblyPath: _testAssemblyPath,
            typeName: "BaseSample",
            additionalAssemblies: new[] { "/tmp/does-not-exist.dll" });

        Assert.Contains("AssemblyNotFound", result);
        Assert.Contains("does-not-exist", result);
    }

    [Fact]
    public void GetTypeHierarchy_Mermaid_WithScope_DrawsBaseInterfaceAndDerivedEdges()
    {
        var result = TypeAnalysisTools.GetTypeHierarchy(
            _typeAnalysis,
            Contexts,
            _reverseLookup,
            TestMiddleware.Fresh,
            TestHandles.Registry,
            assemblyPath: _testAssemblyPath,
            typeName: "BaseSample",
            additionalAssemblies: new[] { _testAssemblyPath },
            format: "mermaid");

        var data = JsonDocument.Parse(result).RootElement.GetProperty("data");
        var diagram = data.GetProperty("diagram").GetString()!;
        Assert.Equal("mermaid", data.GetProperty("format").GetString());
        Assert.StartsWith("classDiagram", diagram);
        Assert.Contains("[\"System.Object\"]", diagram);
        Assert.Contains("<<interface>>", diagram);
        Assert.Contains("<|--", diagram);
        Assert.Contains("<|..", diagram);
        Assert.Contains("DerivedSample", diagram);
        Assert.False(data.GetProperty("truncated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("note").ValueKind);
    }

    [Fact]
    public void GetTypeHierarchy_Mermaid_WithoutScope_KeepsDerivedTypesNote()
    {
        var result = TypeAnalysisTools.GetTypeHierarchy(
            _typeAnalysis,
            Contexts,
            _reverseLookup,
            TestMiddleware.Fresh,
            TestHandles.Registry,
            assemblyPath: _testAssemblyPath,
            typeName: "BaseSample",
            format: "mermaid");

        var data = JsonDocument.Parse(result).RootElement.GetProperty("data");
        Assert.DoesNotContain("DerivedSample", data.GetProperty("diagram").GetString());
        Assert.Contains("additionalAssemblies", data.GetProperty("note").GetString());
    }

    [Fact]
    public void GetTypeHierarchy_Mermaid_MaxNodes_Truncates()
    {
        var result = TypeAnalysisTools.GetTypeHierarchy(
            _typeAnalysis,
            Contexts,
            _reverseLookup,
            TestMiddleware.Fresh,
            TestHandles.Registry,
            assemblyPath: _testAssemblyPath,
            typeName: "BaseSample",
            format: "mermaid",
            maxNodes: 1);

        var data = JsonDocument.Parse(result).RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("nodeCount").GetInt32());
        Assert.Equal(0, data.GetProperty("edgeCount").GetInt32());
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.Contains("maxNodes", data.GetProperty("note").GetString());
    }

    [Fact]
    public void GetTypeHierarchy_InvalidFormat_ReturnsError()
    {
        var result = TypeAnalysisTools.GetTypeHierarchy(
            _typeAnalysis,
            Contexts,
            _reverseLookup,
            TestMiddleware.Fresh,
            TestHandles.Registry,
            assemblyPath: _testAssemblyPath,
            typeName: "BaseSample",
            format: "svg");

        Assert.Contains("InvalidFormat", result);
    }
}
