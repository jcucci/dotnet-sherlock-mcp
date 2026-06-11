using System.Reflection;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.Search;

namespace Sherlock.MCP.Tests;

public class SearchServiceTests
{
    private readonly ISearchService _svc = new SearchService();
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;

    private static SearchOptions Options(
        bool caseSensitive = false, bool includeNonPublic = false, params string[] kinds) =>
        new(CaseSensitive: caseSensitive,
            IncludeNonPublic: includeNonPublic,
            MemberKinds: kinds.Length == 0 ? null : kinds.ToHashSet(StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void SearchMembers_FindsMethodsAcrossTypes_ByNameFragment()
    {
        var result = _svc.SearchMembers(_testAssemblyPath, "Method", Options(), offset: 0, pageSize: 1000);

        var methodNames = result.Items
            .Where(h => h.MemberKind == "method")
            .Select(h => h.Name)
            .ToHashSet();

        Assert.Contains("StaticMethod", methodNames);
        Assert.Contains("VirtualMethod", methodNames);
        Assert.Contains("MethodWithParameters", methodNames);
        Assert.Contains("GenericMethod", methodNames);
        Assert.All(result.Items, h => Assert.Contains("Method", h.Name));
        Assert.All(result.Items, h => Assert.False(string.IsNullOrEmpty(h.Signature)));
    }

    [Fact]
    public void SearchMembers_KindFilter_ReturnsOnlyRequestedKind()
    {
        var result = _svc.SearchMembers(
            _testAssemblyPath, "Property", Options(kinds: "property"), offset: 0, pageSize: 1000);

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, h => Assert.Equal("property", h.MemberKind));
        Assert.Contains(result.Items, h => h.Name == "PublicProperty");
    }

    [Fact]
    public void SearchMembers_IncludeNonPublic_ChangesResults()
    {
        var publicOnly = _svc.SearchMembers(
            _testAssemblyPath, "Property", Options(kinds: "property"), offset: 0, pageSize: 1000);
        var withNonPublic = _svc.SearchMembers(
            _testAssemblyPath, "Property", Options(includeNonPublic: true, kinds: "property"), offset: 0, pageSize: 1000);

        Assert.DoesNotContain(publicOnly.Items, h => h.Name == "ProtectedVirtualProperty");
        Assert.Contains(withNonPublic.Items, h => h.Name == "ProtectedVirtualProperty");
        Assert.True(withNonPublic.Total >= publicOnly.Total);
    }

    [Fact]
    public void SearchMembers_TypeKind_FindsTypesByName()
    {
        var result = _svc.SearchMembers(
            _testAssemblyPath, "TestSampleClass", Options(kinds: "type"), offset: 0, pageSize: 1000);

        Assert.Contains(result.Items, h => h.MemberKind == "type" && h.Name == "TestSampleClass");
    }

    [Fact]
    public void SearchMembers_CaseSensitive_Filters()
    {
        var insensitive = _svc.SearchMembers(
            _testAssemblyPath, "staticmethod", Options(kinds: "method"), offset: 0, pageSize: 1000);
        var sensitive = _svc.SearchMembers(
            _testAssemblyPath, "staticmethod", Options(caseSensitive: true, kinds: "method"), offset: 0, pageSize: 1000);

        Assert.Contains(insensitive.Items, h => h.Name == "StaticMethod");
        Assert.DoesNotContain(sensitive.Items, h => h.Name == "StaticMethod");
    }

    [Fact]
    public void SearchMembers_Pagination_TotalStable_PagesDisjoint()
    {
        var first = _svc.SearchMembers(_testAssemblyPath, "Method", Options(), offset: 0, pageSize: 2);
        var second = _svc.SearchMembers(_testAssemblyPath, "Method", Options(), offset: 2, pageSize: 2);

        Assert.Equal(first.Total, second.Total);
        Assert.True(first.Items.Length <= 2);

        var firstKeys = first.Items.Select(h => $"{h.DeclaringType}.{h.Name}.{h.Signature}").ToHashSet();
        var secondKeys = second.Items.Select(h => $"{h.DeclaringType}.{h.Name}.{h.Signature}").ToHashSet();
        Assert.Empty(firstKeys.Intersect(secondKeys));
    }

    [Fact]
    public void SearchMembers_OffsetPastEnd_ReturnsEmptyButTotalIntact()
    {
        var result = _svc.SearchMembers(_testAssemblyPath, "Method", Options(), offset: 100_000, pageSize: 50);

        Assert.Empty(result.Items);
        Assert.True(result.Total > 0);
    }

    [Fact]
    public void SearchMembers_NoMatch_ReturnsEmpty()
    {
        var result = _svc.SearchMembers(
            _testAssemblyPath, "ZzzNoSuchMemberZzz", Options(), offset: 0, pageSize: 50);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }
}
