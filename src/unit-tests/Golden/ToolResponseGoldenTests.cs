using System.Text.Json;
using ModelContextProtocol.Protocol;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.ApiDiff;
using Sherlock.MCP.Runtime.Decompilation;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.SourceLink;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
using Sherlock.MCP.Server.Tools;
using static VerifyXunit.Verifier;

namespace Sherlock.MCP.Tests.Golden;

public class ToolResponseGoldenTests
{
    private const string Processor = "Golden.Shapes.OrderProcessor";
    private const string Order = "Golden.Shapes.Order";

    private static readonly RuntimeOptions Options = new()
    {
        StateDirectory = GoldenFixture.Shared.StateDirectory,
        SourceFetch = SourceFetchMode.Off
    };

    private static readonly IInspectionContextProvider Contexts = new SharedInspectionContextProvider(Options);
    private static readonly SourceFetcher SourceFetcher = new(Options, new HttpClient());

    private static readonly Dictionary<string, Func<Task<CallToolResult>>> Cases = new(StringComparer.Ordinal)
    {
        ["OpenAssembly|summary"] = () => Done(HandleTools.OpenAssembly(TestHandles.Registry, Contexts, Fixture.AssemblyPath)),
        ["OpenAssembly|error-invalid-assembly"] = () => Done(HandleTools.OpenAssembly(TestHandles.Registry, Contexts, Fixture.NotDotNetPath)),

        ["GetAssemblyInfo|summary"] = () => Done(ReflectionTools.GetAssemblyInfo(Contexts, Middleware, TestHandles.Registry, assemblyPath: Fixture.AssemblyPath)),
        ["GetAssemblyInfo|full"] = () => Done(ReflectionTools.GetAssemblyInfo(Contexts, Middleware, TestHandles.Registry, assemblyPath: Fixture.AssemblyPath, projection: "full")),
        ["GetAssemblyInfo|error-assembly-not-found"] = () => Done(ReflectionTools.GetAssemblyInfo(Contexts, Middleware, TestHandles.Registry, assemblyPath: Path.Combine(Fixture.Root, "Missing.dll"))),
        ["GetAssemblyInfo|error-unknown-handle"] = () => Done(ReflectionTools.GetAssemblyInfo(Contexts, Middleware, TestHandles.Registry, assemblyHandle: "asm_unknown")),
        ["AnalyzeAssembly|summary"] = () => Text(ReflectionTools.AnalyzeAssembly(Contexts, Middleware, Options, TestHandles.Registry, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["AnalyzeType|summary"] = () => Text(ReflectionTools.AnalyzeType(Contexts, Middleware, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["AnalyzeMethod|summary"] = () => Text(ReflectionTools.AnalyzeMethod(Contexts, Middleware, TestHandles.Registry, Processor, "Add", assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["AnalyzeMethod|error-member-not-found"] = () => Text(ReflectionTools.AnalyzeMethod(Contexts, Middleware, TestHandles.Registry, Processor, "Ad", assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["FindAssemblyByClassName|summary"] = () => Text(ReflectionTools.FindAssemblyByClassName("OrderProcessor", Fixture.Root)),
        ["FindAssemblyByFileName|summary"] = () => Text(ReflectionTools.FindAssemblyByFileName($"{GoldenFixture.AssemblyName}.dll", Fixture.Root)),
        ["FindAssemblyByNugetPackage|error-package-not-found"] = async () => ToolResponse.Result(await ReflectionTools.FindAssemblyByNugetPackage(new ProjectAnalysisService(), GoldenProject.MissingPackageId)),

        ["GetTypesFromAssembly|summary"] = () => Done(TypeAnalysisTools.GetTypesFromAssembly(new TypeAnalysisService(Contexts), Middleware, TestHandles.Registry, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetTypesFromAssembly|full"] = () => Done(TypeAnalysisTools.GetTypesFromAssembly(new TypeAnalysisService(Contexts), Middleware, TestHandles.Registry, assemblyPath: Fixture.AssemblyPath, projection: "full", noCache: true)),
        ["GetTypesFromAssembly|page-1"] = () => Done(TypesPage(continuationToken: null)),
        ["GetTypesFromAssembly|page-2"] = () => Done(TypesPage(NextToken(TypesPage(continuationToken: null)))),
        ["GetTypesFromAssembly|error-invalid-token"] = () => Done(TypesPage(continuationToken: "not-a-token")),
        ["GetTypeInfo|summary"] = () => Done(TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(Contexts), Contexts, Middleware, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetTypeInfo|error-type-not-found"] = () => Done(TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(Contexts), Contexts, Middleware, TestHandles.Registry, "Golden.Shapes.OrderProcesor", assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetTypeInfo|error-ambiguous-type-name"] = () => Done(TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(Contexts), Contexts, Middleware, TestHandles.Registry, "Order", assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetTypeHierarchy|summary"] = () => Text(TypeAnalysisTools.GetTypeHierarchy(new TypeAnalysisService(Contexts), Contexts, new ReverseLookupService(), Middleware, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetTypeHierarchy|derived-types"] = () => Text(TypeAnalysisTools.GetTypeHierarchy(new TypeAnalysisService(Contexts), Contexts, new ReverseLookupService(), Middleware, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, additionalAssemblies: [Fixture.ApiDiffLeftPath], noCache: true)),
        ["GetTypeHierarchy|mermaid"] = () => Text(TypeAnalysisTools.GetTypeHierarchy(new TypeAnalysisService(Contexts), Contexts, new ReverseLookupService(), Middleware, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, additionalAssemblies: [Fixture.ApiDiffLeftPath], format: "mermaid", noCache: true)),
        ["GetGenericTypeInfo|summary"] = () => Text(TypeAnalysisTools.GetGenericTypeInfo(new TypeAnalysisService(Contexts), Contexts, Middleware, TestHandles.Registry, "Golden.Shapes.Repository`1", assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetTypeAttributes|summary"] = () => Text(TypeAnalysisTools.GetTypeAttributes(new TypeAnalysisService(Contexts), Contexts, Middleware, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetNestedTypes|summary"] = () => Text(TypeAnalysisTools.GetNestedTypes(new TypeAnalysisService(Contexts), Contexts, Middleware, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, noCache: true)),

        ["GetTypeMembers|summary"] = () => Done(MembersPage(maxItems: null, continuationToken: null)),
        ["GetTypeMembers|full"] = () => Done(MemberAnalysisTools.GetTypeMembers(new MemberAnalysisService(), Contexts, Middleware, Options, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, projection: "full", noCache: true)),
        ["GetTypeMembers|page-1"] = () => Done(MembersPage(maxItems: 2, continuationToken: null)),
        ["GetTypeMembers|page-2"] = () => Done(MembersPage(maxItems: 2, NextToken(MembersPage(maxItems: 2, continuationToken: null)))),
        ["GetTypeMethods|summary"] = () => Done(MemberAnalysisTools.GetTypeMethods(new MemberAnalysisService(), Contexts, Middleware, Options, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetTypeMethods|full"] = () => Done(MemberAnalysisTools.GetTypeMethods(new MemberAnalysisService(), Contexts, Middleware, Options, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, projection: "full", noCache: true)),
        ["GetTypeProperties|summary"] = () => Text(MemberAnalysisTools.GetTypeProperties(new MemberAnalysisService(), Contexts, Middleware, Options, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetTypeFields|summary"] = () => Text(MemberAnalysisTools.GetTypeFields(new MemberAnalysisService(), Contexts, Middleware, Options, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetTypeEvents|summary"] = () => Text(MemberAnalysisTools.GetTypeEvents(new MemberAnalysisService(), Contexts, Middleware, Options, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetTypeConstructors|summary"] = () => Text(MemberAnalysisTools.GetTypeConstructors(new MemberAnalysisService(), Contexts, Middleware, Options, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetAllTypeMembers|summary"] = () => Text(MemberAnalysisTools.GetAllTypeMembers(new MemberAnalysisService(), Contexts, Middleware, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetMemberAttributes|summary"] = () => Text(MemberAnalysisTools.GetMemberAttributes(Contexts, Middleware, TestHandles.Registry, Processor, "method", "Add", assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetParameterAttributes|summary"] = () => Text(MemberAnalysisTools.GetParameterAttributes(Contexts, Middleware, TestHandles.Registry, Processor, "Discount", parameterIndex: 0, assemblyPath: Fixture.AssemblyPath, noCache: true)),

        ["SearchMembers|summary"] = () => Done(SearchPage(maxItems: null, continuationToken: null)),
        ["SearchMembers|page-1"] = () => Done(SearchPage(maxItems: 2, continuationToken: null)),
        ["SearchMembers|page-2"] = () => Done(SearchPage(maxItems: 2, NextToken(SearchPage(maxItems: 2, continuationToken: null)))),

        ["GetXmlDocsForType|summary"] = () => Text(XmlDocTools.GetXmlDocsForType(new XmlDocService(), Contexts, Middleware, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetXmlDocsForMember|summary"] = () => Text(XmlDocTools.GetXmlDocsForMember(new XmlDocService(), Contexts, Middleware, TestHandles.Registry, Processor, "Add", assemblyPath: Fixture.AssemblyPath, noCache: true)),

        ["FindImplementationsOf|summary"] = () => Done(ReverseLookupTools.FindImplementationsOf(new ReverseLookupService(), Middleware, Options, TestHandles.Registry, "Golden.Shapes.IOrderSource", assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["FindImplementationsOf|full"] = () => Done(ReverseLookupTools.FindImplementationsOf(new ReverseLookupService(), Middleware, Options, TestHandles.Registry, "Golden.Shapes.IOrderSource", assemblyPath: Fixture.AssemblyPath, projection: "full", noCache: true)),
        ["FindMethodsReturning|summary"] = () => Done(ReverseLookupTools.FindMethodsReturning(new ReverseLookupService(), Middleware, Options, TestHandles.Registry, Order, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["FindMethodsReturning|full"] = () => Done(ReverseLookupTools.FindMethodsReturning(new ReverseLookupService(), Middleware, Options, TestHandles.Registry, Order, assemblyPath: Fixture.AssemblyPath, projection: "full", noCache: true)),
        ["FindExtensionMethodsFor|summary"] = () => Done(ReverseLookupTools.FindExtensionMethodsFor(new ReverseLookupService(), Middleware, Options, TestHandles.Registry, Order, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["FindExtensionMethodsFor|full"] = () => Done(ReverseLookupTools.FindExtensionMethodsFor(new ReverseLookupService(), Middleware, Options, TestHandles.Registry, Order, assemblyPath: Fixture.AssemblyPath, projection: "full", noCache: true)),
        ["FindReferencesTo|summary"] = () => Done(ReverseLookupTools.FindReferencesTo(new ReverseLookupService(), IlAnalysis, Middleware, Options, TestHandles.Registry, Order, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["FindReferencesTo|full"] = () => Done(ReverseLookupTools.FindReferencesTo(new ReverseLookupService(), IlAnalysis, Middleware, Options, TestHandles.Registry, Order, assemblyPath: Fixture.AssemblyPath, projection: "full", noCache: true)),
        ["FindReferencesTo|il"] = () => Done(ReverseLookupTools.FindReferencesTo(new ReverseLookupService(), IlAnalysis, Middleware, Options, TestHandles.Registry, Order, assemblyPath: Fixture.AssemblyPath, analysisDepth: "il", noCache: true)),

        ["GetMethodCalls|summary"] = () => Done(IlAnalysisTools.GetMethodCalls(IlAnalysis, Middleware, TestHandles.Registry, Processor, "Add", assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetMethodCalls|full"] = () => Done(IlAnalysisTools.GetMethodCalls(IlAnalysis, Middleware, TestHandles.Registry, Processor, "Add", assemblyPath: Fixture.AssemblyPath, projection: "full", noCache: true)),
        ["GetMethodCalls|mermaid"] = () => Done(IlAnalysisTools.GetMethodCalls(IlAnalysis, Middleware, TestHandles.Registry, Processor, "Add", assemblyPath: Fixture.AssemblyPath, format: "mermaid", noCache: true)),
        ["GetMethodCalls|mermaid-depth"] = () => Done(IlAnalysisTools.GetMethodCalls(IlAnalysis, Middleware, TestHandles.Registry, Processor, "Add", assemblyPath: Fixture.AssemblyPath, format: "mermaid", depth: 3, noCache: true)),

        ["DecompileMember|summary"] = () => Done(DecompilePage(maxLines: SourcePager.DefaultMaxLines, continuationToken: null)),
        ["DecompileMember|page-1"] = () => Done(DecompilePage(maxLines: 4, continuationToken: null)),
        ["DecompileMember|page-2"] = () => Done(DecompilePage(maxLines: 4, NextToken(DecompilePage(maxLines: 4, continuationToken: null)))),
        ["DecompileType|summary"] = () => Text(DecompilationTools.DecompileType(new DecompilerService(), Contexts, Middleware, TestHandles.Registry, Order, assemblyPath: Fixture.AssemblyPath, noCache: true)),
        ["GetMemberSource|summary"] = async () => await SourcePage(maxLines: SourcePager.DefaultMaxLines, continuationToken: null),
        ["GetMemberSource|page-1"] = async () => await SourcePage(maxLines: 4, continuationToken: null),
        ["GetMemberSource|page-2"] = async () => await SourcePage(maxLines: 4, NextToken(await SourcePage(maxLines: 4, continuationToken: null))),

        ["CompareApiSurface|summary"] = () => CompareApiSurface("summary"),
        ["CompareApiSurface|full"] = () => CompareApiSurface("full"),

        ["AnalyzeSolution|summary"] = async () => ToolResponse.Result(await ProjectAnalysisTools.AnalyzeSolution(new ProjectAnalysisService(), Middleware, Fixture.Project.SolutionPath, noCache: true)),
        ["AnalyzeProject|summary"] = async () => ToolResponse.Result(await ProjectAnalysisTools.AnalyzeProject(new ProjectAnalysisService(), Middleware, Fixture.Project.ProjectFile, noCache: true)),
        ["AnalyzeProject|error-project-not-found"] = async () => ToolResponse.Result(await ProjectAnalysisTools.AnalyzeProject(new ProjectAnalysisService(), Middleware, Path.Combine(Fixture.Root, "Missing.csproj"), noCache: true)),
        ["GetProjectOutputPaths|summary"] = async () => ToolResponse.Result(await ProjectAnalysisTools.GetProjectOutputPaths(new ProjectAnalysisService(), Middleware, Fixture.Project.ProjectFile, noCache: true)),
        ["ResolvePackageReferences|summary"] = async () => ToolResponse.Result(await ProjectAnalysisTools.ResolvePackageReferences(new ProjectAnalysisService(), Fixture.Project.ProjectFile)),
        ["FindDepsJsonDependencies|summary"] = async () => ToolResponse.Result(await ProjectAnalysisTools.FindDepsJsonDependencies(new ProjectAnalysisService(), Middleware, Fixture.Project.ProjectFile, noCache: true)),
        ["GetPackageGraph|summary"] = () => Text(PackageGraphTools.GetPackageGraph(Middleware, Options, TestHandles.Registry, projectPath: Fixture.Project.ProjectFile, targetFramework: "net8.0", noCache: true)),
        ["GetPackageGraph|full"] = () => Text(PackageGraphTools.GetPackageGraph(Middleware, Options, TestHandles.Registry, projectPath: Fixture.Project.ProjectFile, targetFramework: "net8.0", projection: "full", noCache: true)),

        ["GetRuntimeOptions|summary"] = () => Text(ConfigTools.GetRuntimeOptions(new RuntimeOptions { SourceFetch = SourceFetchMode.KnownHosts })),
        ["UpdateRuntimeOptions|summary"] = () => Text(ConfigTools.UpdateRuntimeOptions(new RuntimeOptions { SourceFetch = SourceFetchMode.KnownHosts }, defaultMaxItems: 20, sourceFetch: "off")),
        ["UpdateRuntimeOptions|error-invalid-argument"] = () => Text(ConfigTools.UpdateRuntimeOptions(new RuntimeOptions(), sourceFetch: "sometimes"))
    };

    private static GoldenFixture Fixture => GoldenFixture.Shared;

    private static ToolMiddleware Middleware => TestMiddleware.Fresh;

    private static IlAnalysisService IlAnalysis => new();

    public static TheoryData<string, string> CaseNames()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in Cases.Keys.Order(StringComparer.Ordinal))
        {
            var separator = key.IndexOf('|', StringComparison.Ordinal);
            data.Add(key[..separator], key[(separator + 1)..]);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Response(string tool, string variant)
    {
        var result = Finish(tool, await Cases[$"{tool}|{variant}"]());

        AssertErrorMatchesVariant(result, variant);
        AssertStructuredContentMatchesSchema(tool, result, variant);

        await VerifyJson(GoldenSnapshot.Render(result, Fixture.Scrubs))
            .UseDirectory("Snapshots")
            .UseFileName($"{tool}.{variant}");
    }

    [Fact]
    public void EveryToolHasAGoldenCase()
    {
        var covered = Cases.Keys.Select(key => key[..key.IndexOf('|', StringComparison.Ordinal)]).ToHashSet(StringComparer.Ordinal);

        var missing = ToolCatalog.ByMethodName.Keys
            .Where(name => !covered.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0, $"Tools without a golden case: {string.Join(", ", missing)}. Add a case to {nameof(ToolResponseGoldenTests)}.");
    }

    [Fact]
    public void EveryProjectedToolHasSummaryAndFullCases()
    {
        var projected = ToolCatalog.ByMethodName
            .Where(pair => pair.Value.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("projection", out _))
            .Select(pair => pair.Key);

        foreach (var tool in projected)
        {
            Assert.True(Cases.ContainsKey($"{tool}|summary"), $"{tool} has no summary golden case");
            Assert.True(Cases.ContainsKey($"{tool}|full"), $"{tool} has no full golden case");
        }
    }

    private static CallToolResult Finish(string tool, CallToolResult result) =>
        StructuredOutput.Apply(ToolErrorFlag.Apply(result), ToolCatalog.ByMethodName[tool]);

    private static void AssertErrorMatchesVariant(CallToolResult result, string variant) =>
        Assert.True(
            result.IsError == true == variant.StartsWith("error-", StringComparison.Ordinal),
            $"Expected isError={variant.StartsWith("error-", StringComparison.Ordinal)} for '{variant}': {result.Text()}");

    private static void AssertStructuredContentMatchesSchema(string tool, CallToolResult result, string variant)
    {
        if (result.StructuredContent is { } structured)
            ToolCatalog.AssertMatchesOutputSchema(tool, structured, $"{tool} ({variant})");
    }

    private static Task<CallToolResult> Done(CallToolResult result) => Task.FromResult(result);

    private static Task<CallToolResult> Text(string text) => Task.FromResult(ToolResponse.Result(text));

    private static CallToolResult TypesPage(string? continuationToken) =>
        TypeAnalysisTools.GetTypesFromAssembly(new TypeAnalysisService(Contexts), Middleware, TestHandles.Registry, assemblyPath: Fixture.AssemblyPath, maxItems: 4, continuationToken: continuationToken, noCache: true);

    private static CallToolResult MembersPage(int? maxItems, string? continuationToken) =>
        MemberAnalysisTools.GetTypeMembers(new MemberAnalysisService(), Contexts, Middleware, Options, TestHandles.Registry, Processor, assemblyPath: Fixture.AssemblyPath, kinds: maxItems == null ? null : "property,field", maxItems: maxItems, continuationToken: continuationToken, noCache: true);

    private static CallToolResult SearchPage(int? maxItems, string? continuationToken) =>
        SearchTools.SearchMembers(new SearchService(), Middleware, Options, TestHandles.Registry, "Order", assemblyPath: Fixture.AssemblyPath, maxItems: maxItems, continuationToken: continuationToken, noCache: true);

    private static CallToolResult DecompilePage(int maxLines, string? continuationToken) =>
        DecompilationTools.DecompileMember(new DecompilerService(), Contexts, Middleware, TestHandles.Registry, Processor, "Add", parameterTypes: "Golden.Shapes.Order", assemblyPath: Fixture.AssemblyPath, maxLines: maxLines, continuationToken: continuationToken, noCache: true);

    private static Task<CallToolResult> SourcePage(int maxLines, string? continuationToken) =>
        SourceTools.GetMemberSource(new OriginalSourceService(SourceFetcher), new DecompilerService(), Contexts, Middleware, TestHandles.Registry, Processor, "Add", parameterTypes: "Golden.Shapes.Order", assemblyPath: Fixture.AssemblyPath, maxLines: maxLines, continuationToken: continuationToken, noCache: true);

    private static Task<CallToolResult> CompareApiSurface(string projection) =>
        ApiDiffTools.CompareApiSurface(new ApiDiffService(Contexts), new ProjectAnalysisService(), Middleware, Options, TestHandles.Registry, Fixture.ApiDiffLeftPath, Fixture.ApiDiffRightPath, projection: projection, noCache: true);

    private static string NextToken(CallToolResult result) =>
        FindToken(JsonDocument.Parse(result.Text()).RootElement)
        ?? throw new InvalidOperationException($"No continuation token in: {result.Text()}");

    private static string? FindToken(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .Select(property => property.Name is "continuationToken" or "nextToken" && property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : FindToken(property.Value))
            .FirstOrDefault(token => token != null),
        JsonValueKind.Array => element.EnumerateArray().Select(FindToken).FirstOrDefault(token => token != null),
        _ => null
    };
}
