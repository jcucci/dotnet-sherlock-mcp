using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.ApiDiff;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Decompilation;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.SourceLink;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
using Sherlock.MCP.Server.Tools;
using Sherlock.MCP.Tests.IlAnalysisFixtures;

namespace Sherlock.MCP.Tests;

public class StructuredOutputTests
{
    private static readonly string TestAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private static readonly RuntimeOptions Options = new();
    private static readonly IInspectionContextProvider Contexts = new SharedInspectionContextProvider(Options);
    private static readonly ToolMiddleware Middleware = new(new InMemoryToolResponseCache(), new NoopTelemetry(), Options);
    private static readonly IReverseLookupService ReverseLookup = new ReverseLookupService();
    private static readonly ApiDiffPair ApiDiffPair = ApiDiffFixtures.EmitPair();

    private static readonly string[] StructuredTools =
    [
        nameof(SearchTools.SearchMembers),
        nameof(TypeAnalysisTools.GetTypesFromAssembly),
        nameof(TypeAnalysisTools.GetTypeInfo),
        nameof(MemberAnalysisTools.GetTypeMethods),
        nameof(MemberAnalysisTools.GetTypeMembers),
        nameof(ReflectionTools.GetAssemblyInfo),
        nameof(IlAnalysisTools.GetMethodCalls),
        nameof(DecompilationTools.DecompileMember),
        nameof(SourceTools.GetMemberSource),
        nameof(ReverseLookupTools.FindImplementationsOf),
        nameof(ReverseLookupTools.FindMethodsReturning),
        nameof(ReverseLookupTools.FindExtensionMethodsFor),
        nameof(ReverseLookupTools.FindReferencesTo),
        nameof(HandleTools.OpenAssembly),
        nameof(ApiDiffTools.CompareApiSurface)
    ];

    private static readonly IReadOnlyDictionary<string, McpServerTool> ToolsByMethodName = ToolCatalog.ByMethodName;

    public static TheoryData<string, string> ToolCalls => new()
    {
        { nameof(SearchTools.SearchMembers), "summary" },
        { nameof(TypeAnalysisTools.GetTypesFromAssembly), "summary" },
        { nameof(TypeAnalysisTools.GetTypesFromAssembly), "full" },
        { nameof(TypeAnalysisTools.GetTypeInfo), "summary" },
        { nameof(MemberAnalysisTools.GetTypeMethods), "summary" },
        { nameof(MemberAnalysisTools.GetTypeMethods), "full" },
        { nameof(MemberAnalysisTools.GetTypeMembers), "summary" },
        { nameof(MemberAnalysisTools.GetTypeMembers), "full" },
        { nameof(ReflectionTools.GetAssemblyInfo), "summary" },
        { nameof(ReflectionTools.GetAssemblyInfo), "full" },
        { nameof(IlAnalysisTools.GetMethodCalls), "summary" },
        { nameof(IlAnalysisTools.GetMethodCalls), "full" },
        { nameof(DecompilationTools.DecompileMember), "summary" },
        { nameof(SourceTools.GetMemberSource), "summary" },
        { nameof(ReverseLookupTools.FindImplementationsOf), "summary" },
        { nameof(ReverseLookupTools.FindImplementationsOf), "full" },
        { nameof(ReverseLookupTools.FindMethodsReturning), "summary" },
        { nameof(ReverseLookupTools.FindMethodsReturning), "full" },
        { nameof(ReverseLookupTools.FindExtensionMethodsFor), "summary" },
        { nameof(ReverseLookupTools.FindExtensionMethodsFor), "full" },
        { nameof(ReverseLookupTools.FindReferencesTo), "summary" },
        { nameof(ReverseLookupTools.FindReferencesTo), "full" },
        { nameof(HandleTools.OpenAssembly), "summary" },
        { nameof(ApiDiffTools.CompareApiSurface), "summary" },
        { nameof(ApiDiffTools.CompareApiSurface), "full" }
    };

    [Fact]
    public void OnlyCoreToolsPublishAnOutputSchema()
    {
        var withSchema = ToolsByMethodName
            .Where(pair => pair.Value.ProtocolTool.OutputSchema is not null)
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal);

        Assert.Equal(StructuredTools.Order(StringComparer.Ordinal), withSchema);
    }

    [Theory]
    [MemberData(nameof(StructuredToolNames))]
    public void OutputSchema_DescribesTheEnvelope(string methodName)
    {
        var schema = ToolsByMethodName[methodName].ProtocolTool.OutputSchema!.Value;

        Assert.Equal("object", schema.GetProperty("type").GetString());
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal<string>(["kind", "version", "data"], required);
    }

    public static TheoryData<string> StructuredToolNames() => new(StructuredTools);

    [Theory]
    [MemberData(nameof(ToolCalls))]
    public void StructuredContent_MatchesTextAndValidatesAgainstSchema(string methodName, string projection)
    {
        var tool = ToolsByMethodName[methodName];
        var result = StructuredOutput.Apply(Invoke(methodName, projection), tool);

        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);

        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        Assert.DoesNotContain("\n", text);
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(text).RootElement, result.StructuredContent!.Value));

        ToolCatalog.AssertMatchesOutputSchema(methodName, result.StructuredContent!.Value, $"{methodName} ({projection})");
    }

    [Fact]
    public void ErrorResult_HasNoStructuredContent()
    {
        var tool = ToolsByMethodName[nameof(SearchTools.SearchMembers)];
        var result = ToolErrorFlag.Apply(SearchTools.SearchMembers(
            new SearchService(),
            Middleware,
            Options,
            TestHandles.Registry,
            assemblyPath: "/no/such/assembly.dll",
            nameContains: "Method"));

        result = StructuredOutput.Apply(result, tool);

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
    }

    [Fact]
    public void ToolWithoutSchema_HasNoStructuredContent()
    {
        var tool = ToolsByMethodName[nameof(TypeAnalysisTools.GetNestedTypes)];
        var result = StructuredOutput.Apply(
            ToolResponse.Result(TypeAnalysisTools.GetNestedTypes(new TypeAnalysisService(), Contexts, TestMiddleware.Fresh, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: typeof(Outer).FullName!)),
            tool);

        Assert.Null(result.StructuredContent);
    }

    private static CallToolResult Invoke(string methodName, string projection) => methodName switch
    {
        nameof(SearchTools.SearchMembers) =>
            SearchTools.SearchMembers(new SearchService(), Middleware, Options, TestHandles.Registry, assemblyPath: TestAssemblyPath, nameContains: "Method", noCache: true),
        nameof(TypeAnalysisTools.GetTypesFromAssembly) =>
            TypeAnalysisTools.GetTypesFromAssembly(new TypeAnalysisService(), TestMiddleware.Fresh, TestHandles.Registry, assemblyPath: TestAssemblyPath, maxItems: 10, projection: projection),
        nameof(TypeAnalysisTools.GetTypeInfo) =>
            TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(), Contexts, TestMiddleware.Fresh, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: typeof(TestSampleClass).FullName!),
        nameof(MemberAnalysisTools.GetTypeMethods) =>
            MemberAnalysisTools.GetTypeMethods(new MemberAnalysisService(), Contexts, Middleware, Options, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: typeof(TestSampleClass).FullName!, projection: projection, noCache: true),
        nameof(MemberAnalysisTools.GetTypeMembers) =>
            MemberAnalysisTools.GetTypeMembers(new MemberAnalysisService(), Contexts, Middleware, Options, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: typeof(TestSampleClass).FullName!, projection: projection, noCache: true),
        nameof(ReflectionTools.GetAssemblyInfo) =>
            ReflectionTools.GetAssemblyInfo(Contexts, TestMiddleware.Fresh, TestHandles.Registry, assemblyPath: TestAssemblyPath, projection: projection),
        nameof(IlAnalysisTools.GetMethodCalls) =>
            IlAnalysisTools.GetMethodCalls(new IlAnalysisService(), Middleware, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: typeof(IlSampleSubject).FullName!, methodName: nameof(IlSampleSubject.DoWork), projection: projection, noCache: true),
        nameof(DecompilationTools.DecompileMember) =>
            DecompilationTools.DecompileMember(new DecompilerService(), Contexts, Middleware, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: typeof(IlSampleSubject).FullName!, memberName: nameof(IlSampleSubject.Helper), noCache: true),
        nameof(SourceTools.GetMemberSource) =>
            SourceTools.GetMemberSource(new OriginalSourceService(new SourceFetcher(Options, new HttpClient())), new DecompilerService(), Contexts, Middleware, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: typeof(IlSampleSubject).FullName!, memberName: nameof(IlSampleSubject.Helper), noCache: true).GetAwaiter().GetResult(),
        nameof(ReverseLookupTools.FindImplementationsOf) =>
            ReverseLookupTools.FindImplementationsOf(ReverseLookup, Middleware, Options, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: "ISampleEventReader", projection: projection, noCache: true),
        nameof(ReverseLookupTools.FindMethodsReturning) =>
            ReverseLookupTools.FindMethodsReturning(ReverseLookup, Middleware, Options, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: "Snapshot", projection: projection, noCache: true),
        nameof(ReverseLookupTools.FindExtensionMethodsFor) =>
            ReverseLookupTools.FindExtensionMethodsFor(ReverseLookup, Middleware, Options, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: "string", projection: projection, noCache: true),
        nameof(ReverseLookupTools.FindReferencesTo) =>
            ReverseLookupTools.FindReferencesTo(ReverseLookup, new IlAnalysisService(), Middleware, Options, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: "Snapshot", projection: projection, noCache: true),
        nameof(HandleTools.OpenAssembly) =>
            HandleTools.OpenAssembly(TestHandles.Registry, Contexts, TestAssemblyPath),
        nameof(ApiDiffTools.CompareApiSurface) =>
            ApiDiffTools.CompareApiSurface(new ApiDiffService(Contexts), new ProjectAnalysisService(), Middleware, Options, TestHandles.Registry, ApiDiffPair.LeftPath, ApiDiffPair.RightPath, projection: projection, noCache: true).GetAwaiter().GetResult(),
        _ => throw new ArgumentOutOfRangeException(nameof(methodName), methodName, null)
    };
}
