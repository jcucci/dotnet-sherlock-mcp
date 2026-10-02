using System.Reflection;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Tests;

// Public type deriving from a Sherlock.MCP.Runtime type, so loading this assembly in
// isolation (without Sherlock.MCP.Runtime.dll alongside) forces a dependency resolution.
public class RuntimeDerivedFixture : TypeAnalysisService { }

public class DependencyResolutionTests
{
    [Fact]
    public void IsolatedAssembly_SurfacesDependencyResolutionError()
    {
        using var isolated = new IsolatedCopy();
        var service = new TypeAnalysisService();

        var ex = Assert.Throws<DependencyResolutionException>(() => service.GetTypesFromAssembly(isolated.DllPath));

        Assert.NotEmpty(ex.UnresolvedDependencies);
    }

    [Fact]
    public void AdditionalSearchDirectories_ResolveDependencies()
    {
        using var isolated = new IsolatedCopy();
        var binDirectory = Path.GetDirectoryName(typeof(RuntimeDerivedFixture).Assembly.Location)!;

        using var ctx = new MetadataOnlyInspectionContext(isolated.DllPath, new[] { binDirectory });
        var types = ctx.GetTypes().ToArray();

        Assert.Empty(ctx.UnresolvedDependencies);
        Assert.Contains(types, t => t.Name == nameof(RuntimeDerivedFixture));
    }

    [Fact]
    public void GetTypesFromAssembly_ThrowsDependencyResolution_WhenEmptyAndUnresolved()
    {
        var service = new TypeAnalysisService(new StubProvider(["Azure.Core", "Azure.ResourceManager"]));

        var ex = Assert.Throws<DependencyResolutionException>(() => service.GetTypesFromAssembly("ignored.dll"));

        Assert.Contains("Azure.Core", ex.UnresolvedDependencies);
        Assert.Contains("Azure.ResourceManager", ex.UnresolvedDependencies);
    }

    [Fact]
    public void Acquire_WithDifferentSearchDirectories_UsesDistinctContexts()
    {
        using var provider = new SharedInspectionContextProvider(new RuntimeOptions());
        var path = typeof(RuntimeDerivedFixture).Assembly.Location;

        using var a = provider.Acquire(path);
        using var b = provider.Acquire(path);
        using var c = provider.Acquire(path, additionalSearchDirectories: new[] { Path.GetTempPath() });

        Assert.Same(a.Context, b.Context);
        Assert.NotSame(a.Context, c.Context);
    }

    private sealed class IsolatedCopy : IDisposable
    {
        public IsolatedCopy()
        {
            var source = typeof(RuntimeDerivedFixture).Assembly.Location;
            Directory = Path.Combine(Path.GetTempPath(), $"sherlock_isolated_{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Directory);
            DllPath = Path.Combine(Directory, Path.GetFileName(source));
            File.Copy(source, DllPath);
        }

        public string Directory { get; }

        public string DllPath { get; }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, recursive: true); } catch { }
        }
    }

    private sealed class StubProvider(string[] unresolved) : IInspectionContextProvider
    {
        public InspectionContextLease Acquire(string assemblyPath, IReadOnlyList<string>? additionalSearchDirectories = null) =>
            new(new StubContext(unresolved), () => { });
    }

    private sealed class StubContext(string[] unresolved) : IAssemblyInspectionContext
    {
            public Assembly Assembly => typeof(StubContext).Assembly;

        public FrameworkResolution Framework { get; } =
            new(FrameworkResolutionKind.HostRuntime, null, "System.Private.CoreLib", [], [], []);

        public IReadOnlyList<string> UnresolvedDependencies => unresolved;

        public IEnumerable<Type> GetTypes() => [];

        public MemberInfo[] GetMembers(Type type, BindingFlags flags) => [];

        public void Dispose() { }
    }
}
