using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public sealed class ResponseCachingTests : IDisposable
{
    private static readonly string TestAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private static readonly string SampleType = typeof(TestSampleClass).FullName!;
    private static readonly RuntimeOptions Options = new();
    private static readonly IInspectionContextProvider Contexts = new SharedInspectionContextProvider(Options);

    private readonly string _workDirectory = TestHandles.NewStateDirectory();

    public ResponseCachingTests() => Directory.CreateDirectory(_workDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_workDirectory, recursive: true); } catch (IOException) { }
    }

    public delegate Task<string> ToolCall(ToolMiddleware middleware, string assemblyPath, bool noCache);

    private static readonly Dictionary<string, ToolCall> AssemblyTools = new()
    {
        ["get_types_from_assembly"] = (m, path, noCache) => Task.FromResult(TypeAnalysisTools.GetTypesFromAssembly(new TypeAnalysisService(), m, TestHandles.Registry, assemblyPath: path, maxItems: 5, noCache: noCache).Text()),
        ["get_type_info"] = (m, path, noCache) => Task.FromResult(TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(), Contexts, m, TestHandles.Registry, typeName: SampleType, assemblyPath: path, noCache: noCache).Text()),
        ["get_type_hierarchy"] = (m, path, noCache) => Task.FromResult(TypeAnalysisTools.GetTypeHierarchy(new TypeAnalysisService(), Contexts, new ReverseLookupService(), m, TestHandles.Registry, typeName: SampleType, assemblyPath: path, noCache: noCache)),
        ["get_generic_type_info"] = (m, path, noCache) => Task.FromResult(TypeAnalysisTools.GetGenericTypeInfo(new TypeAnalysisService(), Contexts, m, TestHandles.Registry, typeName: typeof(GenericHolder<,>).FullName!, assemblyPath: path, noCache: noCache)),
        ["get_type_attributes"] = (m, path, noCache) => Task.FromResult(TypeAnalysisTools.GetTypeAttributes(new TypeAnalysisService(), Contexts, m, TestHandles.Registry, typeName: SampleType, assemblyPath: path, noCache: noCache)),
        ["get_nested_types"] = (m, path, noCache) => Task.FromResult(TypeAnalysisTools.GetNestedTypes(new TypeAnalysisService(), Contexts, m, TestHandles.Registry, typeName: SampleType, assemblyPath: path, noCache: noCache)),
        ["analyze_assembly"] = (m, path, noCache) => Task.FromResult(ReflectionTools.AnalyzeAssembly(Contexts, m, Options, TestHandles.Registry, assemblyPath: path, maxItems: 5, noCache: noCache)),
        ["get_assembly_info"] = (m, path, noCache) => Task.FromResult(ReflectionTools.GetAssemblyInfo(Contexts, m, TestHandles.Registry, assemblyPath: path, noCache: noCache).Text()),
        ["analyze_type"] = (m, path, noCache) => Task.FromResult(ReflectionTools.AnalyzeType(Contexts, m, TestHandles.Registry, typeName: SampleType, assemblyPath: path, maxItems: 5, noCache: noCache)),
        ["analyze_method"] = (m, path, noCache) => Task.FromResult(ReflectionTools.AnalyzeMethod(Contexts, m, TestHandles.Registry, typeName: SampleType, methodName: nameof(TestSampleClass.MethodWithParameters), assemblyPath: path, noCache: noCache)),
        ["get_member_attributes"] = (m, path, noCache) => Task.FromResult(MemberAnalysisTools.GetMemberAttributes(Contexts, m, TestHandles.Registry, typeName: SampleType, memberKind: "method", memberName: nameof(TestSampleClass.MethodWithParameters), assemblyPath: path, noCache: noCache)),
        ["get_parameter_attributes"] = (m, path, noCache) => Task.FromResult(MemberAnalysisTools.GetParameterAttributes(Contexts, m, TestHandles.Registry, typeName: SampleType, methodName: nameof(TestSampleClass.MethodWithParameters), parameterIndex: 0, assemblyPath: path, noCache: noCache)),
        ["get_xml_docs_for_type"] = (m, path, noCache) => Task.FromResult(XmlDocTools.GetXmlDocsForType(new XmlDocService(), Contexts, m, TestHandles.Registry, typeName: SampleType, assemblyPath: path, noCache: noCache)),
        ["get_xml_docs_for_member"] = (m, path, noCache) => Task.FromResult(XmlDocTools.GetXmlDocsForMember(new XmlDocService(), Contexts, m, TestHandles.Registry, typeName: SampleType, memberName: nameof(TestSampleClass.MethodWithParameters), assemblyPath: path, noCache: noCache)),
    };

    public static TheoryData<string> AssemblyToolNames() => new(AssemblyTools.Keys);

    [Theory]
    [MemberData(nameof(AssemblyToolNames))]
    public async Task AssemblyTool_RepeatedCall_IsServedFromCache(string tool)
    {
        var (middleware, cache) = Recording();

        var first = await AssemblyTools[tool](middleware, TestAssemblyPath, false);
        var second = await AssemblyTools[tool](middleware, TestAssemblyPath, false);

        Assert.NotEqual("error", Kind(first));
        Assert.Equal(first, second);
        Assert.Equal(1, cache.Sets);
        Assert.Equal(1, cache.Hits);
    }

    [Theory]
    [MemberData(nameof(AssemblyToolNames))]
    public async Task AssemblyTool_NoCache_BypassesCache(string tool)
    {
        var (middleware, cache) = Recording();

        await AssemblyTools[tool](middleware, TestAssemblyPath, true);
        await AssemblyTools[tool](middleware, TestAssemblyPath, true);

        Assert.Equal(0, cache.Sets);
        Assert.Equal(0, cache.Hits);
    }

    [Theory]
    [MemberData(nameof(AssemblyToolNames))]
    public async Task AssemblyTool_RebuiltAssembly_MissesCache(string tool)
    {
        var (middleware, cache) = Recording();
        var copy = CopyWithDocs("Rebuilt");

        await AssemblyTools[tool](middleware, copy, false);
        File.SetLastWriteTimeUtc(copy, File.GetLastWriteTimeUtc(copy).AddMinutes(1));
        await AssemblyTools[tool](middleware, copy, false);

        Assert.Equal(2, cache.Sets);
        Assert.Equal(0, cache.Hits);
    }

    [Theory]
    [InlineData("get_xml_docs_for_type")]
    [InlineData("get_xml_docs_for_member")]
    public async Task XmlDocTool_ChangedDocFile_MissesCache(string tool)
    {
        var (middleware, cache) = Recording();
        var copy = CopyWithDocs("Docs");
        var xml = Path.ChangeExtension(copy, ".xml");

        await AssemblyTools[tool](middleware, copy, false);
        File.SetLastWriteTimeUtc(xml, File.GetLastWriteTimeUtc(xml).AddMinutes(1));
        await AssemblyTools[tool](middleware, copy, false);

        Assert.Equal(2, cache.Sets);
        Assert.Equal(0, cache.Hits);
    }

    [Fact]
    public void GetTypeInfo_HandleAndPath_ShareCacheEntry()
    {
        var (middleware, cache) = Recording();
        var handle = JsonDocument.Parse(HandleTools.OpenAssembly(TestHandles.Registry, Contexts, TestAssemblyPath).Text())
            .RootElement.GetProperty("data").GetProperty("handle").GetString();

        var byHandle = TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(), Contexts, middleware, TestHandles.Registry, typeName: SampleType, assemblyHandle: handle).Text();
        var byPath = TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(), Contexts, middleware, TestHandles.Registry, typeName: SampleType, assemblyPath: TestAssemblyPath).Text();

        Assert.Equal(byHandle, byPath);
        Assert.Equal(1, cache.Hits);
    }

    public static TheoryData<string> AmbiguousTools() => new("get_type_info", "analyze_method", "get_xml_docs_for_type", "get_type_hierarchy");

    [Theory]
    [MemberData(nameof(AmbiguousTools))]
    public void AmbiguousTypeName_IsNotCached(string tool)
    {
        var (middleware, cache) = Recording();

        var first = Ambiguous(tool, middleware);
        var second = Ambiguous(tool, middleware);

        Assert.Equal("AmbiguousTypeName", Code(first));
        Assert.Equal("AmbiguousTypeName", Code(second));
        Assert.Equal(0, cache.Sets);
    }

    [Fact]
    public async Task AnalyzeProject_ChangedAssetsFile_MissesCache()
    {
        var (middleware, cache) = Recording();
        var project = WriteProject();
        var assets = Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");

        var first = await ProjectAnalysisTools.AnalyzeProject(new ProjectAnalysisService(), middleware, project);
        await ProjectAnalysisTools.AnalyzeProject(new ProjectAnalysisService(), middleware, project);
        File.SetLastWriteTimeUtc(assets, File.GetLastWriteTimeUtc(assets).AddMinutes(1));
        await ProjectAnalysisTools.AnalyzeProject(new ProjectAnalysisService(), middleware, project);

        Assert.Equal("project.project", Kind(first));
        Assert.Equal(1, cache.Hits);
        Assert.Equal(2, cache.Sets);
    }

    [Fact]
    public async Task ProjectTools_RepeatedCall_AreServedFromCacheUnlessNoCache()
    {
        var (middleware, cache) = Recording();
        var project = WriteProject();
        var analysis = new ProjectAnalysisService();
        var solution = Path.Combine(_workDirectory, "Sample.sln");
        await File.WriteAllTextAsync(solution, "Microsoft Visual Studio Solution File, Format Version 12.00\n");

        Func<bool, Task<string>>[] calls =
        [
            noCache => ProjectAnalysisTools.AnalyzeSolution(analysis, middleware, solution, noCache),
            noCache => ProjectAnalysisTools.GetProjectOutputPaths(analysis, middleware, project, noCache: noCache),
            noCache => ProjectAnalysisTools.FindDepsJsonDependencies(analysis, middleware, project, noCache: noCache),
        ];

        foreach (var call in calls)
        {
            await call(true);
            await call(false);
            await call(false);
        }

        Assert.Equal(calls.Length, cache.Sets);
        Assert.Equal(calls.Length, cache.Hits);
    }

    [Fact]
    public void ResolvePackageReferences_ReadsNuGetCache_IsNotCached() =>
        Assert.DoesNotContain(
            typeof(ProjectAnalysisTools).GetMethod(nameof(ProjectAnalysisTools.ResolvePackageReferences))!.GetParameters(),
            p => p.ParameterType == typeof(ToolMiddleware));

    [Fact]
    public async Task FindDepsJsonDependencies_NewDepsFile_MissesCache()
    {
        var (middleware, cache) = Recording();
        var project = WriteProject();
        var analysis = new ProjectAnalysisService();

        await ProjectAnalysisTools.FindDepsJsonDependencies(analysis, middleware, project);
        var output = Path.Combine(_workDirectory, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "Sample.deps.json"), "{}");
        await ProjectAnalysisTools.FindDepsJsonDependencies(analysis, middleware, project);

        Assert.Equal(2, cache.Sets);
        Assert.Equal(0, cache.Hits);
    }

    [Fact]
    public async Task ExecuteAsync_FailingAction_IsNotCached()
    {
        var (middleware, cache) = Recording();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.ExecuteAsync("key", () => throw new InvalidOperationException()));

        Assert.Equal(0, cache.Sets);
    }

    private static string Ambiguous(string tool, ToolMiddleware middleware) => tool switch
    {
        "get_type_info" => TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(), Contexts, middleware, TestHandles.Registry, typeName: "DuplicateWidget", assemblyPath: TestAssemblyPath).Text(),
        "analyze_method" => ReflectionTools.AnalyzeMethod(Contexts, middleware, TestHandles.Registry, typeName: "DuplicateWidget", methodName: "Spin", assemblyPath: TestAssemblyPath),
        "get_xml_docs_for_type" => XmlDocTools.GetXmlDocsForType(new XmlDocService(), Contexts, middleware, TestHandles.Registry, typeName: "DuplicateWidget", assemblyPath: TestAssemblyPath),
        "get_type_hierarchy" => TypeAnalysisTools.GetTypeHierarchy(new TypeAnalysisService(), Contexts, new ReverseLookupService(), middleware, TestHandles.Registry, typeName: "DuplicateWidget", assemblyPath: TestAssemblyPath),
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };

    private string CopyWithDocs(string name)
    {
        var destination = Path.Combine(_workDirectory, $"{name}.dll");
        File.Copy(TestAssemblyPath, destination, overwrite: true);
        File.Copy(Path.ChangeExtension(TestAssemblyPath, ".xml"), Path.ChangeExtension(destination, ".xml"), overwrite: true);
        foreach (var reference in Assembly.GetExecutingAssembly().GetReferencedAssemblies())
        {
            var sibling = Path.Combine(Path.GetDirectoryName(TestAssemblyPath)!, $"{reference.Name}.dll");
            if (File.Exists(sibling))
                File.Copy(sibling, Path.Combine(_workDirectory, Path.GetFileName(sibling)), overwrite: true);
        }
        return destination;
    }

    private string WriteProject()
    {
        var project = Path.Combine(_workDirectory, "Sample.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        var obj = Path.Combine(_workDirectory, "obj");
        Directory.CreateDirectory(obj);
        File.WriteAllText(Path.Combine(obj, "project.assets.json"), "{}");
        return project;
    }

    private static (ToolMiddleware Middleware, RecordingCache Cache) Recording()
    {
        var cache = new RecordingCache();
        return (new ToolMiddleware(cache, new NoopTelemetry(), Options), cache);
    }

    private static string? Code(string json) => JsonDocument.Parse(json).RootElement.GetProperty("code").GetString();

    private static string? Kind(string json) => JsonDocument.Parse(json).RootElement.GetProperty("kind").GetString();

    private sealed class RecordingCache : IToolResponseCache
    {
        private readonly InMemoryToolResponseCache _inner = new();

        public int Hits { get; private set; }

        public int Sets { get; private set; }

        public bool TryGet(string key, out string? payload)
        {
            var found = _inner.TryGet(key, out payload);
            if (found) Hits++;
            return found;
        }

        public void Set(string key, string payload, TimeSpan ttl)
        {
            Sets++;
            _inner.Set(key, payload, ttl);
        }
    }
}
