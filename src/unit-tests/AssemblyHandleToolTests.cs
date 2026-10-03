using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public sealed class AssemblyHandleToolTests : IDisposable
{
    private static readonly string TestAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private static readonly RuntimeOptions Options = new();
    private static readonly IInspectionContextProvider Contexts = new SharedInspectionContextProvider(Options);

    private readonly string _stateDirectory = TestHandles.NewStateDirectory();
    private readonly string _workDirectory = TestHandles.NewStateDirectory();
    private readonly Sherlock.MCP.Runtime.Handles.AssemblyHandleRegistry _handles;

    public AssemblyHandleToolTests()
    {
        Directory.CreateDirectory(_workDirectory);
        _handles = TestHandles.Create(_stateDirectory);
    }

    public void Dispose()
    {
        foreach (var directory in new[] { _stateDirectory, _workDirectory })
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void OpenAssembly_ReturnsHandleAndIdentity()
    {
        var data = Data(HandleTools.OpenAssembly(_handles, Contexts, TestAssemblyPath).Text());

        Assert.Matches("^asm_[0-9a-f]{12}$", data.GetProperty("handle").GetString());
        Assert.Equal(Assembly.GetExecutingAssembly().GetName().Name, data.GetProperty("name").GetString());
        Assert.StartsWith(".NETCoreApp", data.GetProperty("targetFramework").GetString());
        Assert.Equal(Path.GetFullPath(TestAssemblyPath), data.GetProperty("assemblyPath").GetString());
    }

    [Fact]
    public void OpenAssembly_MissingFile_ReturnsAssemblyNotFound() =>
        Assert.Equal("AssemblyNotFound", Code(HandleTools.OpenAssembly(_handles, Contexts, "/no/such/assembly.dll").Text()));

    [Fact]
    public void GetTypeInfo_ViaHandle_MatchesViaPath()
    {
        var handle = Open(TestAssemblyPath);
        var typeName = typeof(TestSampleClass).FullName!;

        var viaPath = TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(), Contexts, TestMiddleware.Fresh, _handles, typeName: typeName, assemblyPath: TestAssemblyPath).Text();
        var viaHandle = TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(), Contexts, TestMiddleware.Fresh, _handles, typeName: typeName, assemblyHandle: handle).Text();

        Assert.Equal(viaPath, viaHandle);
    }

    [Fact]
    public void FindImplementationsOf_ViaHandle_UsesHandleScope()
    {
        var dependency = CopyAssembly("Extra.dll");
        var handle = Open(TestAssemblyPath, dependency);
        var middleware = new ToolMiddleware(new InMemoryToolResponseCache(), new NoopTelemetry(), Options);

        var viaPath = ReverseLookupTools.FindImplementationsOf(
            new ReverseLookupService(), middleware, Options, _handles,
            typeName: "ISampleEventReader",
            assemblyPath: TestAssemblyPath,
            additionalAssemblies: [dependency],
            noCache: true).Text();
        var viaHandle = ReverseLookupTools.FindImplementationsOf(
            new ReverseLookupService(), middleware, Options, _handles,
            typeName: "ISampleEventReader",
            assemblyHandle: handle,
            noCache: true).Text();

        Assert.Equal(viaPath, viaHandle);
        Assert.Equal(2, Data(viaHandle).GetProperty("scope").GetArrayLength());
    }

    [Fact]
    public void Tool_UnknownHandle_ReturnsGuidance()
    {
        var result = TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(), Contexts, TestMiddleware.Fresh, _handles, typeName: "X", assemblyHandle: "asm_000000000000").Text();

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("UnknownAssemblyHandle", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(["open_assembly"], doc.RootElement.GetProperty("alternativeTools").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Tool_StaleHandle_RecommendsReopenParams()
    {
        var primary = CopyAssembly("Primary.dll");
        var dependency = CopyAssembly("Dependency.dll");
        var handle = Open(primary, dependency);
        File.SetLastWriteTimeUtc(primary, File.GetLastWriteTimeUtc(primary).AddMinutes(1));

        var result = TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(), Contexts, TestMiddleware.Fresh, _handles, typeName: "X", assemblyHandle: handle).Text();

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("StaleAssemblyHandle", doc.RootElement.GetProperty("code").GetString());
        var recommended = doc.RootElement.GetProperty("recommendedParams");
        Assert.Equal(Path.GetFullPath(primary), recommended.GetProperty("assemblyPath").GetString());
        Assert.Equal([Path.GetFullPath(dependency)], recommended.GetProperty("additionalAssemblies").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Tool_HandleWithDeletedFile_ListsMissingFilesInsteadOfReopenParams()
    {
        var primary = CopyAssembly("Primary.dll");
        var dependency = CopyAssembly("Dependency.dll");
        var handle = Open(primary, dependency);
        File.Delete(dependency);

        var result = TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(), Contexts, TestMiddleware.Fresh, _handles, typeName: "X", assemblyHandle: handle).Text();

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("StaleAssemblyHandle", doc.RootElement.GetProperty("code").GetString());
        Assert.False(doc.RootElement.TryGetProperty("recommendedParams", out _));
        Assert.Equal([Path.GetFullPath(dependency)], doc.RootElement.GetProperty("details").GetProperty("missingFiles").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("find_assembly_by_file_name", doc.RootElement.GetProperty("alternativeTools").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", "")]
    [InlineData("/some/path.dll", "asm_000000000000")]
    public void Tool_NeitherOrBothTargets_ReturnsInvalidArgument(string? assemblyPath, string? assemblyHandle) =>
        Assert.Equal("InvalidArgument", Code(TypeAnalysisTools.GetTypeInfo(
            new TypeAnalysisService(), Contexts, TestMiddleware.Fresh, _handles, typeName: "X", assemblyPath: assemblyPath, assemblyHandle: assemblyHandle).Text()));

    [Fact]
    public void FindImplementationsOf_HandlePlusExplicitAdditional_MergesScopes()
    {
        var fromHandle = CopyAssembly("FromHandle.dll");
        var explicitPath = CopyAssembly("Explicit.dll");
        var handle = Open(TestAssemblyPath, fromHandle);
        var middleware = new ToolMiddleware(new InMemoryToolResponseCache(), new NoopTelemetry(), Options);

        var result = ReverseLookupTools.FindImplementationsOf(
            new ReverseLookupService(), middleware, Options, _handles,
            typeName: "ISampleEventReader",
            assemblyHandle: handle,
            additionalAssemblies: [explicitPath],
            noCache: true).Text();

        var scope = Data(result).GetProperty("scope").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal([Path.GetFullPath(TestAssemblyPath), Path.GetFullPath(fromHandle), Path.GetFullPath(explicitPath)], scope);
    }

    private string Open(string assemblyPath, params string[] additionalAssemblies) =>
        Data(HandleTools.OpenAssembly(_handles, Contexts, assemblyPath, additionalAssemblies).Text()).GetProperty("handle").GetString()!;

    private string CopyAssembly(string fileName)
    {
        var destination = Path.Combine(_workDirectory, fileName);
        File.Copy(TestAssemblyPath, destination, overwrite: true);
        return destination;
    }

    private static JsonElement Data(string json) => JsonDocument.Parse(json).RootElement.GetProperty("data").Clone();

    private static string? Code(string json) => JsonDocument.Parse(json).RootElement.GetProperty("code").GetString();
}
