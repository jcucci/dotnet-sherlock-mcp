using System.Reflection;
using System.Text.Json;
using Json.Schema;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Inspection;
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

    private static readonly string[] StructuredTools =
    [
        nameof(SearchTools.SearchMembers),
        nameof(TypeAnalysisTools.GetTypesFromAssembly),
        nameof(TypeAnalysisTools.GetTypeInfo),
        nameof(MemberAnalysisTools.GetTypeMethods),
        nameof(MemberAnalysisTools.GetTypeMembers),
        nameof(ReflectionTools.GetAssemblyInfo),
        nameof(IlAnalysisTools.GetMethodCalls),
        nameof(ReverseLookupTools.FindImplementationsOf),
        nameof(ReverseLookupTools.FindMethodsReturning),
        nameof(ReverseLookupTools.FindExtensionMethodsFor),
        nameof(ReverseLookupTools.FindReferencesTo),
        nameof(HandleTools.OpenAssembly)
    ];

    private static readonly Dictionary<string, McpServerTool> ToolsByMethodName = typeof(ConfigTools).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
        .ToDictionary(m => m.Name, m => McpServerTool.Create(m));

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
        { nameof(ReverseLookupTools.FindImplementationsOf), "summary" },
        { nameof(ReverseLookupTools.FindImplementationsOf), "full" },
        { nameof(ReverseLookupTools.FindMethodsReturning), "summary" },
        { nameof(ReverseLookupTools.FindMethodsReturning), "full" },
        { nameof(ReverseLookupTools.FindExtensionMethodsFor), "summary" },
        { nameof(ReverseLookupTools.FindExtensionMethodsFor), "full" },
        { nameof(ReverseLookupTools.FindReferencesTo), "summary" },
        { nameof(ReverseLookupTools.FindReferencesTo), "full" },
        { nameof(HandleTools.OpenAssembly), "summary" }
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

        var schema = JsonSchema.Build(tool.ProtocolTool.OutputSchema!.Value);
        var evaluation = schema.Evaluate(result.StructuredContent!.Value, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(evaluation.IsValid, $"{methodName} ({projection}) does not match its outputSchema: {JsonSerializer.Serialize(evaluation)}");
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
            ToolResponse.Result(TypeAnalysisTools.GetNestedTypes(new TypeAnalysisService(), Contexts, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: typeof(Outer).FullName!)),
            tool);

        Assert.Null(result.StructuredContent);
    }

    private static CallToolResult Invoke(string methodName, string projection) => methodName switch
    {
        nameof(SearchTools.SearchMembers) =>
            SearchTools.SearchMembers(new SearchService(), Middleware, Options, TestHandles.Registry, assemblyPath: TestAssemblyPath, nameContains: "Method", noCache: true),
        nameof(TypeAnalysisTools.GetTypesFromAssembly) =>
            TypeAnalysisTools.GetTypesFromAssembly(new TypeAnalysisService(), TestHandles.Registry, assemblyPath: TestAssemblyPath, maxItems: 10, projection: projection),
        nameof(TypeAnalysisTools.GetTypeInfo) =>
            TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(), Contexts, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: typeof(TestSampleClass).FullName!),
        nameof(MemberAnalysisTools.GetTypeMethods) =>
            MemberAnalysisTools.GetTypeMethods(new MemberAnalysisService(), Contexts, Middleware, Options, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: typeof(TestSampleClass).FullName!, projection: projection, noCache: true),
        nameof(MemberAnalysisTools.GetTypeMembers) =>
            MemberAnalysisTools.GetTypeMembers(new MemberAnalysisService(), Contexts, Middleware, Options, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: typeof(TestSampleClass).FullName!, projection: projection, noCache: true),
        nameof(ReflectionTools.GetAssemblyInfo) =>
            ReflectionTools.GetAssemblyInfo(Contexts, TestHandles.Registry, assemblyPath: TestAssemblyPath, projection: projection),
        nameof(IlAnalysisTools.GetMethodCalls) =>
            IlAnalysisTools.GetMethodCalls(new IlAnalysisService(), Middleware, TestHandles.Registry, assemblyPath: TestAssemblyPath, typeName: typeof(IlSampleSubject).FullName!, methodName: nameof(IlSampleSubject.DoWork), projection: projection, noCache: true),
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
        _ => throw new ArgumentOutOfRangeException(nameof(methodName), methodName, null)
    };
}
