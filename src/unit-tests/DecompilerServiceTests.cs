using System.Reflection;
using Sherlock.MCP.Runtime.Decompilation;
using Sherlock.MCP.Tests.DecompilationFixtures;

namespace Sherlock.MCP.Tests;

public class DecompilerServiceTests
{
    private static readonly string TestAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private readonly IDecompilerService _decompiler = new DecompilerService();

    [Fact]
    public void DecompileMembers_ReturnsMethodBody()
    {
        var method = typeof(DecompileSubject).GetMethod(nameof(DecompileSubject.Total))!;

        var source = Assert.Single(_decompiler.DecompileMembers(TestAssemblyPath, [method.MetadataToken]));

        Assert.Contains("foreach", source);
        Assert.Contains("Scale(", source);
    }

    [Fact]
    public void DecompileMembers_ReturnsOneSourcePerToken()
    {
        var tokens = typeof(DecompileSubject).GetMethods()
            .Where(m => m.Name == nameof(DecompileSubject.Format))
            .Select(m => m.MetadataToken)
            .ToArray();

        var sources = _decompiler.DecompileMembers(TestAssemblyPath, tokens);

        Assert.Equal(3, sources.Count);
        Assert.All(sources, source => Assert.Contains("Format(", source));
    }

    [Fact]
    public void DecompileMembers_HandlesPropertiesAndStaticConstructors()
    {
        var property = typeof(DecompileSubject).GetProperty(nameof(DecompileSubject.Seed))!;
        var cctor = typeof(DecompileSubject).TypeInitializer!;

        var sources = _decompiler.DecompileMembers(TestAssemblyPath, [property.MetadataToken, cctor.MetadataToken]);

        Assert.Contains("* 3", sources[0]);
        Assert.Contains("hello-from-cctor", sources[1]);
    }

    [Fact]
    public void DecompileType_IncludesEveryMemberAndNestedType()
    {
        var source = _decompiler.DecompileType(TestAssemblyPath, typeof(DecompileSubject).MetadataToken);

        Assert.Contains("class DecompileSubject", source);
        Assert.Contains("1234", source);
        Assert.Contains("inner-describe", source);
    }

    [Fact]
    public void DecompileMembers_ThrowsWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var method = typeof(DecompileSubject).GetMethod(nameof(DecompileSubject.Total))!;

        Assert.ThrowsAny<OperationCanceledException>(() =>
            _decompiler.DecompileMembers(TestAssemblyPath, [method.MetadataToken], cancellationToken: cts.Token));
    }

    [Theory]
    [InlineData("string", 1)]
    [InlineData("System.String", 1)]
    [InlineData("string, int", 1)]
    [InlineData("Dictionary<string,int>", 1)]
    [InlineData("System.Collections.Generic.Dictionary<System.String, System.Int32>", 1)]
    [InlineData("bool", 0)]
    [InlineData(null, 3)]
    public void SelectMembers_FiltersOverloadsByParameterTypes(string? parameterTypes, int expected)
    {
        var members = DecompilationTargets.SelectMembers(
            typeof(DecompileSubject), "format", parameterTypes, StringComparison.OrdinalIgnoreCase, includeNonPublic: true);

        Assert.Equal(expected, members.Count);
    }

    [Fact]
    public void SelectMembers_EmptyParameterTypesSelectsParameterlessConstructor()
    {
        var members = DecompilationTargets.SelectMembers(
            typeof(DecompileSubject), ".ctor", "", StringComparison.Ordinal, includeNonPublic: true);

        var ctor = Assert.IsAssignableFrom<ConstructorInfo>(Assert.Single(members));
        Assert.Empty(ctor.GetParameters());
    }

    [Fact]
    public void SelectMembers_ResolvesStaticConstructorAndHidesNonPublicWhenAsked()
    {
        Assert.Single(DecompilationTargets.SelectMembers(
            typeof(DecompileSubject), ".cctor", null, StringComparison.Ordinal, includeNonPublic: true));
        Assert.Empty(DecompilationTargets.SelectMembers(
            typeof(DecompileSubject), ".cctor", null, StringComparison.Ordinal, includeNonPublic: false));
        Assert.Empty(DecompilationTargets.SelectMembers(
            typeof(DecompileSubject), "Scale", null, StringComparison.Ordinal, includeNonPublic: false));
    }

    [Fact]
    public void FormatSignature_RendersFriendlyTypes()
    {
        var method = typeof(DecompileSubject).GetMethod(nameof(DecompileSubject.Format), [typeof(string), typeof(int)])!;

        Assert.Equal("string Format(string value, int count)", DecompilationTargets.FormatSignature(method));
    }
}
