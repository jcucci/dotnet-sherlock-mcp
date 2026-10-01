using System.Reflection;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Handles;
using Sherlock.MCP.Runtime.Telemetry;

namespace Sherlock.MCP.Tests;

public sealed class AssemblyHandleRegistryTests : IDisposable
{
    private static readonly string TestAssemblyPath = Assembly.GetExecutingAssembly().Location;

    private readonly string _stateDirectory = TestHandles.NewStateDirectory();
    private readonly string _workDirectory = TestHandles.NewStateDirectory();

    public AssemblyHandleRegistryTests() => Directory.CreateDirectory(_workDirectory);

    public void Dispose()
    {
        foreach (var directory in new[] { _stateDirectory, _workDirectory })
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Open_ReturnsShortPrefixedHandle()
    {
        var handle = Registry().Open(TestAssemblyPath);

        Assert.Matches("^asm_[0-9a-f]{12}$", handle.Id);
        Assert.Equal(Path.GetFullPath(TestAssemblyPath), handle.AssemblyPath);
        Assert.Empty(handle.AdditionalAssemblies);
    }

    [Fact]
    public void Open_SameBuild_ReturnsSameHandle()
    {
        var copy = CopyAssembly("Same.dll");

        var first = Registry().Open(copy);
        var second = Registry().Open(copy);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void Open_AdditionalAssembliesInAnyOrder_ReturnsSameHandle()
    {
        var primary = CopyAssembly("Primary.dll");
        var a = CopyAssembly("A.dll");
        var b = CopyAssembly("B.dll");
        var registry = Registry();

        var forward = registry.Open(primary, [a, b]);
        var reversed = registry.Open(primary, [b, a, primary, a]);

        Assert.Equal(forward.Id, reversed.Id);
        Assert.Equal(2, reversed.AdditionalAssemblies.Count);
        Assert.NotEqual(forward.Id, registry.Open(primary).Id);
    }

    [Fact]
    public void Resolve_OpenedHandle_ReturnsResolved()
    {
        var registry = Registry();
        var handle = registry.Open(TestAssemblyPath);

        var lookup = registry.Resolve(handle.Id);

        Assert.Equal(HandleStatus.Resolved, lookup.Status);
        Assert.Equal(handle.AssemblyPath, lookup.Handle!.AssemblyPath);
    }

    [Fact]
    public void Resolve_NeverIssued_ReturnsUnknown() =>
        Assert.Equal(HandleStatus.Unknown, Registry().Resolve("asm_000000000000").Status);

    [Fact]
    public void Resolve_AfterRebuild_ReturnsStaleAndReopenGivesNewHandle()
    {
        var copy = CopyAssembly("Rebuilt.dll");
        var registry = Registry();
        var handle = registry.Open(copy);

        File.SetLastWriteTimeUtc(copy, File.GetLastWriteTimeUtc(copy).AddMinutes(1));
        var lookup = registry.Resolve(handle.Id);

        Assert.Equal(HandleStatus.Stale, lookup.Status);
        Assert.Equal([Path.GetFullPath(copy)], lookup.Handle!.ChangedFiles);
        Assert.NotEqual(handle.Id, registry.Open(copy).Id);
    }

    [Fact]
    public void Resolve_AdditionalAssemblyDeleted_ReturnsStale()
    {
        var primary = CopyAssembly("Primary.dll");
        var dependency = CopyAssembly("Dependency.dll");
        var registry = Registry();
        var handle = registry.Open(primary, [dependency]);

        File.Delete(dependency);

        Assert.Equal(HandleStatus.Stale, registry.Resolve(handle.Id).Status);
    }

    [Fact]
    public void Resolve_FromNewRegistryInstance_SurvivesRestart()
    {
        var handle = Registry().Open(TestAssemblyPath);

        var lookup = Registry().Resolve(handle.Id);

        Assert.Equal(HandleStatus.Resolved, lookup.Status);
    }

    [Fact]
    public void Resolve_HandleOpenedByAnotherProcessAfterLoad_IsFound()
    {
        var reader = Registry();
        Assert.Equal(HandleStatus.Unknown, reader.Resolve("asm_000000000000").Status);

        var handle = Registry().Open(TestAssemblyPath);

        Assert.Equal(HandleStatus.Resolved, reader.Resolve(handle.Id).Status);
    }

    [Fact]
    public void Open_PreservesHandlesWrittenByOtherInstances()
    {
        var first = Registry().Open(CopyAssembly("First.dll"));
        var second = Registry().Open(CopyAssembly("Second.dll"));

        var fresh = Registry();

        Assert.Equal(HandleStatus.Resolved, fresh.Resolve(first.Id).Status);
        Assert.Equal(HandleStatus.Resolved, fresh.Resolve(second.Id).Status);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":99,\"handles\":[]}")]
    [InlineData("{\"version\":1,\"handles\":[{\"id\":\"asm_x\"}]}")]
    [InlineData("{\"version\":1,\"handles\":[null,{\"id\":\"asm_x\",\"files\":[null]}]}")]
    [InlineData("{\"version\":1,\"handles\":[{\"id\":\"asm_x\",\"files\":[{\"lastWriteTicks\":1,\"length\":1}]}]}")]
    public void CorruptStore_IsTreatedAsEmpty(string contents)
    {
        Directory.CreateDirectory(_stateDirectory);
        File.WriteAllText(Path.Combine(_stateDirectory, "handles.json"), contents);
        var registry = Registry();

        Assert.Equal(HandleStatus.Unknown, registry.Resolve("asm_x").Status);
        var handle = registry.Open(TestAssemblyPath);
        Assert.Equal(HandleStatus.Resolved, Registry().Resolve(handle.Id).Status);
    }

    [Fact]
    public void Open_OverCapacity_EvictsLeastRecentlyUsed()
    {
        var time = new ManualTimeProvider();
        var options = new RuntimeOptions { StateDirectory = _stateDirectory, MaxAssemblyHandles = 2 };
        var registry = new AssemblyHandleRegistry(options, new NoopTelemetry(), time);

        var oldest = registry.Open(CopyAssembly("One.dll"));
        time.Advance();
        var used = registry.Open(CopyAssembly("Two.dll"));
        time.Advance();
        registry.Resolve(oldest.Id);
        time.Advance();
        registry.Resolve(used.Id);
        time.Advance();
        var newest = registry.Open(CopyAssembly("Three.dll"));

        var fresh = Registry();
        Assert.Equal(HandleStatus.Unknown, fresh.Resolve(oldest.Id).Status);
        Assert.Equal(HandleStatus.Resolved, fresh.Resolve(used.Id).Status);
        Assert.Equal(HandleStatus.Resolved, fresh.Resolve(newest.Id).Status);
    }

    [Fact]
    public void Resolve_InvalidStoredPath_ReturnsStale()
    {
        Directory.CreateDirectory(_stateDirectory);
        File.WriteAllText(
            Path.Combine(_stateDirectory, "handles.json"),
            "{\"version\":1,\"handles\":[{\"id\":\"asm_x\",\"lastUsedUtc\":\"2026-01-01T00:00:00Z\",\"files\":[{\"path\":\"bad\\u0000path\",\"lastWriteTicks\":1,\"length\":1}]}]}");

        Assert.Equal(HandleStatus.Stale, Registry().Resolve("asm_x").Status);
    }

    [Fact]
    public void Resolve_HandleInUse_IsPersistedSoOtherInstancesKeepIt()
    {
        var time = new ManualTimeProvider();
        var options = new RuntimeOptions { StateDirectory = _stateDirectory, MaxAssemblyHandles = 2 };
        var opener = new AssemblyHandleRegistry(options, new NoopTelemetry(), time);
        var inUse = opener.Open(CopyAssembly("InUse.dll"));
        time.Advance();
        var idle = opener.Open(CopyAssembly("Idle.dll"));

        time.Advance(TimeSpan.FromHours(2));
        new AssemblyHandleRegistry(options, new NoopTelemetry(), time).Resolve(inUse.Id);
        time.Advance();
        var newest = new AssemblyHandleRegistry(options, new NoopTelemetry(), time).Open(CopyAssembly("Newest.dll"));

        var fresh = Registry();
        Assert.Equal(HandleStatus.Resolved, fresh.Resolve(inUse.Id).Status);
        Assert.Equal(HandleStatus.Unknown, fresh.Resolve(idle.Id).Status);
        Assert.Equal(HandleStatus.Resolved, fresh.Resolve(newest.Id).Status);
    }

    [Fact]
    public void Resolve_PersistingUsage_EnforcesLoweredCapAndKeepsResolvedHandle()
    {
        var time = new ManualTimeProvider();
        var options = new RuntimeOptions { StateDirectory = _stateDirectory };
        var registry = new AssemblyHandleRegistry(options, new NoopTelemetry(), time);
        var oldest = registry.Open(CopyAssembly("Oldest.dll"));
        time.Advance();
        var middle = registry.Open(CopyAssembly("Middle.dll"));
        time.Advance();
        var newest = registry.Open(CopyAssembly("Newest.dll"));

        options.MaxAssemblyHandles = 2;
        time.Advance(TimeSpan.FromHours(2));
        new AssemblyHandleRegistry(options, new NoopTelemetry(), time).Resolve(oldest.Id);

        var fresh = Registry();
        Assert.Equal(HandleStatus.Resolved, fresh.Resolve(oldest.Id).Status);
        Assert.Equal(HandleStatus.Unknown, fresh.Resolve(middle.Id).Status);
        Assert.Equal(HandleStatus.Resolved, fresh.Resolve(newest.Id).Status);
    }

    [Fact]
    public void Open_MissingFile_Throws() =>
        Assert.Throws<FileNotFoundException>(() => Registry().Open(Path.Combine(_workDirectory, "missing.dll")));

    [Fact]
    public void Open_UnwritableStateDirectory_StillReturnsUsableHandle()
    {
        var blocker = Path.Combine(_workDirectory, "blocker");
        File.WriteAllText(blocker, "");
        var registry = new AssemblyHandleRegistry(
            new RuntimeOptions { StateDirectory = Path.Combine(blocker, "state") }, new NoopTelemetry());

        var handle = registry.Open(TestAssemblyPath);

        Assert.Equal(HandleStatus.Resolved, registry.Resolve(handle.Id).Status);
    }

    private AssemblyHandleRegistry Registry() => TestHandles.Create(_stateDirectory);

    private string CopyAssembly(string fileName)
    {
        var destination = Path.Combine(_workDirectory, fileName);
        File.Copy(TestAssemblyPath, destination, overwrite: true);
        return destination;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance() => Advance(TimeSpan.FromMinutes(1));

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
