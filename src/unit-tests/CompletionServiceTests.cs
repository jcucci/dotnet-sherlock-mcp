using System.Reflection;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Completions;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Tests;

[Collection(nameof(EnvVarCollection))]
public class CompletionServiceTests
{
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private readonly RecentAssemblyRegistry _recent = new();
    private readonly CompletionService _completions;

    public CompletionServiceTests() =>
        _completions = new CompletionService(new SharedInspectionContextProvider(new RuntimeOptions()), new XmlDocService(), _recent);

    [Fact]
    public void CompleteTypeName_FullNamePrefix_ReturnsMatchingTypes()
    {
        var result = _completions.CompleteTypeName(_testAssemblyPath, "Sherlock.MCP.Tests.TestSample");

        Assert.Contains(typeof(TestSampleClass).FullName!, result.Values);
        Assert.All(result.Values, value => Assert.StartsWith("Sherlock.MCP.Tests.TestSample", value, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompleteTypeName_SimpleNamePrefix_RanksAboveSubstring()
    {
        var result = _completions.CompleteTypeName(_testAssemblyPath, "Outer");

        Assert.Equal("Sherlock.MCP.Tests.Outer", result.Values[0]);
        Assert.Contains("Sherlock.MCP.Tests.Outer+Inner", result.Values);
    }

    [Fact]
    public void CompleteTypeName_NestedSimpleName_Matches()
    {
        var result = _completions.CompleteTypeName(_testAssemblyPath, "Inner");

        Assert.Contains("Sherlock.MCP.Tests.Outer+Inner", result.Values);
    }

    [Fact]
    public void CompleteTypeName_LargeAssembly_CapsValuesAndReportsTotal()
    {
        var result = _completions.CompleteTypeName(typeof(string).Assembly.Location, "System.");

        Assert.Equal(CompletionService.MaxValues, result.Values.Length);
        Assert.True(result.HasMore);
        Assert.True(result.Total > CompletionService.MaxValues);
    }

    [Fact]
    public void CompleteTypeName_GenericType_ReturnsMetadataName()
    {
        var result = _completions.CompleteTypeName(_testAssemblyPath, "Sherlock.MCP.Tests.GenericHolder");

        Assert.Equal(["Sherlock.MCP.Tests.GenericHolder`2"], result.Values);
    }

    [Fact]
    public void CompleteTypeName_UnresolvedDependencies_StillListsTypeNames()
    {
        using var dir = new TempDir();
        var isolated = Path.Combine(dir.Path, Path.GetFileName(_testAssemblyPath));
        File.Copy(_testAssemblyPath, isolated);

        var result = _completions.CompleteTypeName(isolated, "Sherlock.MCP.Tests.RuntimeDerived");

        Assert.Equal([typeof(RuntimeDerivedFixture).FullName!], result.Values);
    }

    [Fact]
    public void CompleteTypeName_CachesNamesUntilAssemblyChanges()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "Cached.dll");
        File.Copy(_testAssemblyPath, path);
        using var inner = new SharedInspectionContextProvider(new RuntimeOptions());
        var counting = new CountingProvider(inner);
        var completions = new CompletionService(counting, new XmlDocService(), new RecentAssemblyRegistry());

        completions.CompleteTypeName(path, "Outer");
        completions.CompleteTypeName(path, "Inner");
        Assert.Equal(1, counting.Acquisitions);

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        completions.CompleteTypeName(path, "Outer");
        Assert.Equal(2, counting.Acquisitions);
    }

    [Fact]
    public void CompleteTypeName_MissingAssembly_ReturnsEmpty()
    {
        var result = _completions.CompleteTypeName(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.dll"), "Foo");

        Assert.Empty(result.Values);
        Assert.False(result.HasMore);
    }

    [Fact]
    public void CompleteMemberId_TypePrefix_ReturnsDocumentedIds()
    {
        var result = _completions.CompleteMemberId(_testAssemblyPath, "T:Sherlock.MCP.Tests.TestSample");

        Assert.Contains("T:Sherlock.MCP.Tests.TestSampleClass", result.Values);
    }

    [Fact]
    public void CompletePackageId_PrefixRanksAboveSubstring()
    {
        using var cache = new TempDir();
        Directory.CreateDirectory(Path.Combine(cache.Path, "newtonsoft.json"));
        Directory.CreateDirectory(Path.Combine(cache.Path, "system.text.json"));
        Directory.CreateDirectory(Path.Combine(cache.Path, "serilog"));
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var result = _completions.CompletePackageId("system");

        Assert.Equal(["system.text.json"], result.Values);
        Assert.Equal(["newtonsoft.json", "system.text.json"], _completions.CompletePackageId("json").Values);
    }

    [Fact]
    public void CompletePackageVersion_ReturnsVersionsDescendingFilteredByPrefix()
    {
        using var cache = new TempDir();
        foreach (var version in new[] { "13.0.1", "13.0.3", "12.0.2" })
            Directory.CreateDirectory(Path.Combine(cache.Path, "newtonsoft.json", version));
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        Assert.Equal(["13.0.3", "13.0.1", "12.0.2"], _completions.CompletePackageVersion("Newtonsoft.Json", "").Values);
        Assert.Equal(["13.0.3", "13.0.1"], _completions.CompletePackageVersion("Newtonsoft.Json", "13").Values);
    }

    [Fact]
    public void CompletePackageVersion_TraversalPackageId_ReturnsEmpty()
    {
        Assert.Empty(_completions.CompletePackageVersion("..", "").Values);
    }

    [Fact]
    public void CompleteAssemblyPath_RecentAssemblies_ComeFirst()
    {
        _recent.Record(_testAssemblyPath);

        var result = _completions.CompleteAssemblyPath(Path.GetFileNameWithoutExtension(_testAssemblyPath));

        Assert.Equal(Path.GetFullPath(_testAssemblyPath), result.Values[0]);
    }

    [Fact]
    public void CompleteAssemblyPath_DirectoryPrefix_ListsAssembliesAndSubdirectories()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "Alpha.dll"), "");
        File.WriteAllText(Path.Combine(dir.Path, "Alpha.xml"), "");
        File.WriteAllText(Path.Combine(dir.Path, "Beta.exe"), "");
        Directory.CreateDirectory(Path.Combine(dir.Path, "AlphaSub"));

        var result = _completions.CompleteAssemblyPath(Path.Combine(dir.Path, "Al"));

        Assert.Equal(
            [Path.Combine(dir.Path, "Alpha.dll"), Path.Combine(dir.Path, "AlphaSub") + Path.DirectorySeparatorChar],
            result.Values);
    }

    [Fact]
    public void CompleteAssemblyPath_TildePrefix_MatchesRecentAssemblyUnderHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var underHome = Path.Combine(home, $"sherlock_{Guid.NewGuid():N}", "Tilde.dll");
        _recent.Record(underHome);

        var result = _completions.CompleteAssemblyPath("~" + underHome[home.Length..]);

        Assert.Equal(underHome, result.Values[0]);
    }

    [Fact]
    public void CompleteAssemblyPath_TildeUserPrefix_IsNotExpanded()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _recent.Record(Path.Combine(home, "otheruser", "Lib.dll"));

        Assert.Empty(_completions.CompleteAssemblyPath("~otheruser/Lib").Values);
    }

    [Fact]
    public void RecentAssemblyRegistry_MovesRepeatToFrontAndEvictsOldest()
    {
        var registry = new RecentAssemblyRegistry(capacity: 2);
        var a = Path.GetFullPath("a.dll");
        var b = Path.GetFullPath("b.dll");
        var c = Path.GetFullPath("c.dll");

        registry.Record(a);
        registry.Record(b);
        registry.Record(a);
        registry.Record(c);

        Assert.Equal([c, a], registry.GetRecent());
    }

    [Fact]
    public void SharedInspectionContextProvider_Acquire_RecordsRecentAssembly()
    {
        var registry = new RecentAssemblyRegistry();
        using var provider = new SharedInspectionContextProvider(new RuntimeOptions(), registry);

        using (provider.Acquire(_testAssemblyPath)) { }

        Assert.Equal([Path.GetFullPath(_testAssemblyPath)], registry.GetRecent());
    }

    private sealed class CountingProvider(IInspectionContextProvider inner) : IInspectionContextProvider
    {
        public int Acquisitions { get; private set; }

        public InspectionContextLease Acquire(string assemblyPath, bool forceRuntimeLoad = false, IReadOnlyList<string>? additionalSearchDirectories = null)
        {
            Acquisitions++;
            return inner.Acquire(assemblyPath, forceRuntimeLoad, additionalSearchDirectories);
        }
    }
}
