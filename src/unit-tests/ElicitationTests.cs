using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Contracts.Il;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class ElicitationTests
{
    private static readonly string TestAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private static readonly string[] Candidates =
    [
        "Sherlock.MCP.Tests.Ambiguity.Alpha.DuplicateWidget",
        "Sherlock.MCP.Tests.Ambiguity.Beta.DuplicateWidget"
    ];

    private static readonly AmbiguousTypeNameException Ambiguity = new("DuplicateWidget", Candidates);
    private static readonly IInspectionContextProvider Contexts = new SharedInspectionContextProvider(new RuntimeOptions());

    [Fact]
    public void AmbiguousType_ClientCanElicit_ThrowsInputRequiredWithCandidates()
    {
        var elicitation = new ElicitationContext(CanElicit: true, Responses: null);

        var ex = Assert.Throws<InputRequiredException>(() => Elicitation.AmbiguousType(elicitation, Ambiguity));

        var request = ex.Result.InputRequests![Elicitation.TypeNameKey].ElicitationParams!;
        var schema = Assert.IsType<ElicitRequestParams.UntitledSingleSelectEnumSchema>(request.RequestedSchema!.Properties[Elicitation.TypeNameKey]);
        Assert.Equal(Candidates, schema.Enum);
        Assert.Equal([Elicitation.TypeNameKey], request.RequestedSchema.Required);
        Assert.Equal(Elicitation.TypeNameKey, ex.Result.RequestState);
    }

    [Fact]
    public void AmbiguousType_ClientCannotElicit_ReturnsErrorWithCandidates()
    {
        var json = Elicitation.AmbiguousType(ElicitationContext.None, Ambiguity);

        AssertAmbiguousError(json);
    }

    [Fact]
    public void AmbiguousType_DeclinedRetry_ReturnsErrorInsteadOfAskingAgain()
    {
        var elicitation = Retry(Elicitation.TypeNameKey, new ElicitResult { Action = "decline" });

        var json = Elicitation.AmbiguousType(elicitation, Ambiguity);

        AssertAmbiguousError(json);
    }

    [Fact]
    public void ApplyTypeChoice_AcceptedRetry_ReturnsChosenName()
    {
        var elicitation = Retry(Elicitation.TypeNameKey, Accepted(Elicitation.TypeNameKey, Candidates[1]));

        Assert.Equal(Candidates[1], Elicitation.ApplyTypeChoice(elicitation, "DuplicateWidget"));
    }

    [Fact]
    public void ApplyTypeChoice_NoResponse_KeepsOriginalName()
    {
        Assert.Equal("DuplicateWidget", Elicitation.ApplyTypeChoice(ElicitationContext.None, "DuplicateWidget"));
    }

    [Fact]
    public void GetTypeInfo_AmbiguousName_WithoutContext_ReturnsAmbiguousTypeName()
    {
        var json = TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(), Contexts, TestMiddleware.Fresh, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: "DuplicateWidget").Text();

        AssertAmbiguousError(json);
    }

    [Fact]
    public void GetTypeMembers_AmbiguousName_IsNotCached()
    {
        var cache = new InMemoryToolResponseCache();
        var middleware = new ToolMiddleware(cache, new NoopTelemetry(), new RuntimeOptions());
        var memberAnalysis = new MemberAnalysisService();

        AssertAmbiguousError(MemberAnalysisTools.GetTypeMembers(memberAnalysis, Contexts, middleware, new RuntimeOptions(), TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: "DuplicateWidget").Text());
        AssertAmbiguousError(MemberAnalysisTools.GetTypeMembers(memberAnalysis, Contexts, middleware, new RuntimeOptions(), TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: "DuplicateWidget").Text());

        var resolved = JsonDocument.Parse(MemberAnalysisTools.GetTypeMembers(memberAnalysis, Contexts, middleware, new RuntimeOptions(), TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: Candidates[0]).Text());
        Assert.NotEqual("error", resolved.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public void GetTypeMethods_AmbiguousName_IsNotCached()
    {
        var cache = new InMemoryToolResponseCache();
        var middleware = new ToolMiddleware(cache, new NoopTelemetry(), new RuntimeOptions());
        var memberAnalysis = new MemberAnalysisService();

        AssertAmbiguousError(MemberAnalysisTools.GetTypeMethods(memberAnalysis, Contexts, middleware, new RuntimeOptions(), TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: "DuplicateWidget").Text());
        AssertAmbiguousError(MemberAnalysisTools.GetTypeMethods(memberAnalysis, Contexts, middleware, new RuntimeOptions(), TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: "DuplicateWidget").Text());

        var resolved = JsonDocument.Parse(MemberAnalysisTools.GetTypeMethods(memberAnalysis, Contexts, middleware, new RuntimeOptions(), TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: Candidates[0]).Text());
        Assert.NotEqual("error", resolved.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public void GetMethodCalls_AmbiguousName_ReturnsAmbiguousTypeName()
    {
        var middleware = new ToolMiddleware(new InMemoryToolResponseCache(), new NoopTelemetry(), new RuntimeOptions());

        var json = IlAnalysisTools.GetMethodCalls(new IlAnalysisService(), middleware, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: "DuplicateWidget", methodName: "Spin").Text();

        AssertAmbiguousError(json);
    }

    [Fact]
    public void XmlDocs_AmbiguousName_ReturnsAmbiguousTypeName()
    {
        var contexts = new SharedInspectionContextProvider(new RuntimeOptions());

        var json = XmlDocTools.GetXmlDocsForType(new XmlDocService(), contexts, TestMiddleware.Fresh, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: "DuplicateWidget");

        AssertAmbiguousError(json);
    }

    private static void AssertAmbiguousError(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal("error", root.GetProperty("kind").GetString());
        Assert.Equal("AmbiguousTypeName", root.GetProperty("code").GetString());
        var candidates = root.GetProperty("recommendedParams").GetProperty("candidates").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(Candidates, candidates);
    }

    private static ElicitationContext Retry(string key, ElicitResult result) =>
        new(CanElicit: true, Responses: new Dictionary<string, InputResponse> { [key] = InputResponse.FromElicitResult(result) });

    private static ElicitResult Accepted(string key, string value) => new()
    {
        Action = "accept",
        Content = new Dictionary<string, JsonElement> { [key] = JsonSerializer.SerializeToElement(value) }
    };
}
