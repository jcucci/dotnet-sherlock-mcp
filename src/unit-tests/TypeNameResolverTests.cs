using System.Reflection;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Tests.Ambiguity.Alpha;

namespace Sherlock.MCP.Tests;

public class TypeNameResolverTests
{
    private static readonly Assembly TestAssembly = typeof(TypeNameResolverTests).Assembly;
    private static readonly Type[] AllTypes = TestAssembly.GetTypes();

    private const string AlphaWidget = "Sherlock.MCP.Tests.Ambiguity.Alpha.DuplicateWidget";
    private const string BetaWidget = "Sherlock.MCP.Tests.Ambiguity.Beta.DuplicateWidget";

    [Theory]
    [InlineData(AlphaWidget)]
    [InlineData("Sherlock.MCP.Tests.Outer+Inner")]
    [InlineData("Sherlock.MCP.Tests.Outer/Inner")]
    [InlineData("Sherlock.MCP.Tests.Outer.Inner")]
    [InlineData("TypeNameResolverTests")]
    public void Resolve_UniqueName_ReturnsType(string typeName)
    {
        var resolution = TypeNameResolver.Resolve(TestAssembly, typeName);

        Assert.True(resolution.IsFound);
        Assert.False(resolution.IsAmbiguous);
    }

    [Fact]
    public void Resolve_FriendlyGenericName_ReturnsType()
    {
        var resolution = TypeNameResolver.Resolve(typeof(List<>).Assembly, "System.Collections.Generic.List<T>");

        Assert.Equal(typeof(List<>).FullName, resolution.Type?.FullName);
    }

    [Fact]
    public void Resolve_AmbiguousSimpleName_ReturnsSortedCandidates()
    {
        var resolution = TypeNameResolver.Resolve(TestAssembly, nameof(DuplicateWidget));

        Assert.True(resolution.IsAmbiguous);
        Assert.Null(resolution.Type);
        Assert.Equal([AlphaWidget, BetaWidget], resolution.Candidates);
    }

    [Fact]
    public void Resolve_CaseInsensitiveCollision_IsAmbiguous()
    {
        var resolution = TypeNameResolver.Resolve(TestAssembly, "Sherlock.MCP.Tests.Ambiguity.Beta.DUPLICATECASING", StringComparison.OrdinalIgnoreCase);

        Assert.True(resolution.IsAmbiguous);
    }

    [Fact]
    public void Resolve_CaseSensitive_PicksExactCasing()
    {
        var resolution = TypeNameResolver.Resolve(TestAssembly, "DuplicateCASING");

        Assert.Equal("Sherlock.MCP.Tests.Ambiguity.Beta.DuplicateCASING", resolution.Type?.FullName);
    }

    [Fact]
    public void Resolve_CaseInsensitive_ExactCasing_WinsOverCollision()
    {
        var resolution = TypeNameResolver.Resolve(TestAssembly, "Sherlock.MCP.Tests.Ambiguity.Beta.DuplicateCasing", StringComparison.OrdinalIgnoreCase);

        Assert.Equal("Sherlock.MCP.Tests.Ambiguity.Beta.DuplicateCasing", resolution.Type?.FullName);
    }

    [Fact]
    public void Resolve_FullName_DoesNotEnumerateTypes()
    {
        var resolution = TypeNameResolver.Resolve(
            TestAssembly,
            () => throw new ReflectionTypeLoadException([], []),
            AlphaWidget,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(AlphaWidget, resolution.Type?.FullName);
    }

    [Fact]
    public void FromMatches_CaseInsensitive_PrefersExactCasing()
    {
        var exact = typeof(Ambiguity.Beta.DuplicateCasing);
        var other = typeof(Ambiguity.Beta.DuplicateCASING);

        var resolution = TypeNameResolver.FromMatches([other, exact], exact.FullName!, StringComparison.OrdinalIgnoreCase);

        Assert.Same(exact, resolution.Type);
    }

    [Fact]
    public void Resolve_UnknownName_IsNotFound()
    {
        var resolution = TypeNameResolver.Resolve(TestAssembly, "NoSuchTypeAnywhere");

        Assert.False(resolution.IsFound);
        Assert.False(resolution.IsAmbiguous);
    }

    [Fact]
    public void OrThrowIfAmbiguous_Ambiguous_ThrowsWithCandidates()
    {
        var resolution = TypeNameResolver.Resolve(TestAssembly, nameof(DuplicateWidget));

        var ex = Assert.Throws<AmbiguousTypeNameException>(() => resolution.OrThrowIfAmbiguous(nameof(DuplicateWidget)));

        Assert.Equal(nameof(DuplicateWidget), ex.TypeName);
        Assert.Equal([AlphaWidget, BetaWidget], ex.Candidates);
    }

    [Fact]
    public void FromMatches_PrefersExactFullName()
    {
        var alpha = typeof(DuplicateWidget);
        var beta = typeof(Ambiguity.Beta.DuplicateWidget);

        var resolution = TypeNameResolver.FromMatches([alpha, beta], AlphaWidget, StringComparison.Ordinal);

        Assert.Same(alpha, resolution.Type);
    }
}
