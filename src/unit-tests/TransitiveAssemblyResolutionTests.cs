using Sherlock.MCP.Runtime.Inspection;
using System.Reflection;

namespace Sherlock.MCP.Tests;

public class TransitiveAssemblyResolutionTests
{
    [Fact]
    public void IsolatedContext_Loads_Assembly_Successfully()
    {
        var testAssemblyPath = Assembly.GetExecutingAssembly().Location;

        using var ctx = new IsolatedRuntimeInspectionContext(testAssemblyPath);

        Assert.NotNull(ctx.Assembly);
        Assert.Equal(testAssemblyPath, ctx.Assembly.Location);
    }

    [Fact]
    public void IsolatedContext_GetTypes_Returns_Types()
    {
        var testAssemblyPath = Assembly.GetExecutingAssembly().Location;

        using var ctx = new IsolatedRuntimeInspectionContext(testAssemblyPath);
        var types = ctx.GetTypes().ToArray();

        Assert.True(types.Length > 0);
        Assert.Contains(types, t => t.Name == nameof(TransitiveAssemblyResolutionTests));
    }

    [Fact]
    public void MetadataOnlyContext_Loads_Assembly_Successfully()
    {
        var testAssemblyPath = Assembly.GetExecutingAssembly().Location;

        using var ctx = new MetadataOnlyInspectionContext(testAssemblyPath);

        Assert.NotNull(ctx.Assembly);
    }

    [Fact]
    public void MetadataOnlyContext_GetTypes_Returns_Types()
    {
        var testAssemblyPath = Assembly.GetExecutingAssembly().Location;

        using var ctx = new MetadataOnlyInspectionContext(testAssemblyPath);
        var types = ctx.GetTypes().ToArray();

        Assert.True(types.Length > 0);
        Assert.Contains(types, t => t.Name == nameof(TransitiveAssemblyResolutionTests));
    }

    [Fact]
    public void DependencyResolvingLoadContext_Loads_Assembly_From_Directory()
    {
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var contextName = $"test_{Guid.NewGuid():N}";

        using var alc = new DependencyResolvingLoadContext(contextName, assemblyDir);
        var testAssemblyPath = Assembly.GetExecutingAssembly().Location;
        var loaded = alc.LoadFromAssemblyPath(testAssemblyPath);

        Assert.NotNull(loaded);
    }

    [Fact]
    public void InspectionContextFactory_Creates_MetadataOnly_By_Default()
    {
        var testAssemblyPath = Assembly.GetExecutingAssembly().Location;

        using var ctx = InspectionContextFactory.Create(testAssemblyPath);

        Assert.IsType<MetadataOnlyInspectionContext>(ctx);
    }

    [Fact]
    public void InspectionContextFactory_Creates_Isolated_When_Forced()
    {
        var testAssemblyPath = Assembly.GetExecutingAssembly().Location;

        using var ctx = InspectionContextFactory.Create(testAssemblyPath, forceRuntimeLoad: true);

        Assert.IsType<IsolatedRuntimeInspectionContext>(ctx);
    }

    [Fact]
    public void IsolatedContext_GetMembers_Returns_Members()
    {
        var testAssemblyPath = Assembly.GetExecutingAssembly().Location;

        using var ctx = new IsolatedRuntimeInspectionContext(testAssemblyPath);
        var testType = ctx.GetTypes().First(t => t.Name == nameof(TransitiveAssemblyResolutionTests));
        var members = ctx.GetMembers(testType, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.True(members.Length > 0);
    }
}
