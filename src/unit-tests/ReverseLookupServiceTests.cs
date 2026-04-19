using System.Reflection;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Tests.ReverseLookupFixtures;

namespace Sherlock.MCP.Tests;

public class ReverseLookupServiceTests
{
    private readonly IReverseLookupService _svc = new ReverseLookupService();
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private readonly ReverseLookupOptions _defaultOptions = new();

    [Fact]
    public void FindImplementations_FindsInterfaceImplementers()
    {
        var hits = _svc.FindImplementations(
            [_testAssemblyPath],
            "ISampleEventReader",
            _defaultOptions);

        Assert.Contains(hits, h => h.TypeFullName == typeof(ConcreteReader).FullName);
        Assert.Contains(hits, h => h.TypeFullName == typeof(TypedReader).FullName);
        Assert.All(hits.Where(h => h.TypeFullName == typeof(ConcreteReader).FullName),
            h => Assert.Equal("interface", h.Kind));
    }

    [Fact]
    public void FindImplementations_TransitiveThroughBaseClass()
    {
        var hits = _svc.FindImplementations(
            [_testAssemblyPath],
            "ISampleEventReader",
            _defaultOptions);

        Assert.Contains(hits, h => h.TypeFullName == typeof(FakeEventReader).FullName);
    }

    [Fact]
    public void FindImplementations_FindsBaseTypeDescendants()
    {
        var hits = _svc.FindImplementations(
            [_testAssemblyPath],
            "ConcreteReader",
            _defaultOptions);

        Assert.Contains(hits, h => h.TypeFullName == typeof(FakeEventReader).FullName && h.Kind == "baseType");
    }

    [Fact]
    public void FindImplementations_OpenGenericInterface_MatchesClosedForm()
    {
        var hits = _svc.FindImplementations(
            [_testAssemblyPath],
            "ISampleEventReader<>",
            _defaultOptions);

        Assert.Contains(hits, h => h.TypeFullName == typeof(TypedReader).FullName);
    }

    [Fact]
    public void FindMethodsReturning_ClosedGeneric_MatchesClosedReturn()
    {
        var hits = _svc.FindMethodsReturning(
            [_testAssemblyPath],
            "Snapshot",
            _defaultOptions);

        Assert.Contains(hits, h => h.MethodName == nameof(SnapshotFactory.CreateIntSnapshot));
        Assert.Contains(hits, h => h.MethodName == nameof(SnapshotFactory.CreateEventSnapshot));
    }

    [Fact]
    public void FindMethodsReturning_OpenGeneric_MatchesClosedReturn()
    {
        var hits = _svc.FindMethodsReturning(
            [_testAssemblyPath],
            "Snapshot<>",
            _defaultOptions);

        Assert.Contains(hits, h => h.MethodName == nameof(SnapshotFactory.CreateIntSnapshot));
    }

    [Fact]
    public void FindMethodsReturning_BuiltInAlias_MatchesPrimitiveReturn()
    {
        var hits = _svc.FindMethodsReturning(
            [_testAssemblyPath],
            "int",
            _defaultOptions);

        Assert.Contains(hits, h => h.MethodName == nameof(SnapshotFactory.GetCount));
    }

    [Fact]
    public void FindMethodsReturning_IncludesSignatureAndFriendlyName()
    {
        var hits = _svc.FindMethodsReturning(
            [_testAssemblyPath],
            "Snapshot",
            _defaultOptions);

        var hit = hits.First(h => h.MethodName == nameof(SnapshotFactory.CreateIntSnapshot));
        Assert.Contains("Snapshot", hit.Signature);
        Assert.Contains("CreateIntSnapshot", hit.Signature);
        Assert.Contains("Snapshot", hit.ReturnTypeFriendlyName);
    }

    [Fact]
    public void FindReferences_EmitsAllReferenceKinds()
    {
        var result = _svc.FindReferences(
            [_testAssemblyPath],
            "RecordedEvent",
            _defaultOptions);

        var declaring = typeof(EventStore).FullName!;
        var hits = result.Hits.Where(h => h.DeclaringTypeFullName == declaring).ToArray();

        Assert.Contains(hits, h => h.ReferenceKind == "field" && h.MemberName == nameof(EventStore.LastEvent));
        Assert.Contains(hits, h => h.ReferenceKind == "property" && h.MemberName == nameof(EventStore.All));
        Assert.Contains(hits, h => h.ReferenceKind == "property" && h.MemberName == nameof(EventStore.CurrentSnapshot));
        Assert.Contains(hits, h => h.ReferenceKind == "parameter" && h.MemberName == nameof(EventStore.Append));
        Assert.Contains(hits, h => h.ReferenceKind == "return" && h.MemberName == nameof(EventStore.GetAt));
        Assert.Contains(hits, h => h.ReferenceKind == "event" && h.MemberName == nameof(EventStore.OnAppended));
    }

    [Fact]
    public void FindReferences_IncludesInterfaceAndBaseTypeHits()
    {
        var result = _svc.FindReferences(
            [_testAssemblyPath],
            "ISampleEventReader",
            _defaultOptions);

        Assert.Contains(result.Hits, h =>
            h.DeclaringTypeFullName == typeof(ConcreteReader).FullName && h.ReferenceKind == "interface");
    }

    [Fact]
    public void FindReferences_RecursesThroughGenericArguments()
    {
        var result = _svc.FindReferences(
            [_testAssemblyPath],
            "RecordedEvent",
            _defaultOptions);

        Assert.Contains(result.Hits, h =>
            h.DeclaringTypeFullName == typeof(EventStore).FullName
            && h.ReferenceKind == "property"
            && h.MemberName == nameof(EventStore.All));
        Assert.Contains(result.Hits, h =>
            h.DeclaringTypeFullName == typeof(EventStore).FullName
            && h.ReferenceKind == "property"
            && h.MemberName == nameof(EventStore.CurrentSnapshot));
        Assert.Contains(result.Hits, h =>
            h.DeclaringTypeFullName == typeof(EventStore).FullName
            && h.ReferenceKind == "event"
            && h.MemberName == nameof(EventStore.OnAppended));
    }

    [Fact]
    public void FindReferences_HonorsHardCap()
    {
        var opts = new ReverseLookupOptions(HardCap: 2);
        var result = _svc.FindReferences(
            [_testAssemblyPath],
            "RecordedEvent",
            opts);

        Assert.True(result.Hits.Length <= 2);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void FindReferences_DedupeKeyIsStable()
    {
        var result = _svc.FindReferences(
            [_testAssemblyPath],
            "RecordedEvent",
            _defaultOptions);

        var keys = result.Hits.Select(h => h.DedupeKey).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void MissingAssemblyPath_IsSkippedSilently()
    {
        var hits = _svc.FindImplementations(
            ["/tmp/does-not-exist.dll"],
            "IDisposable",
            _defaultOptions);

        Assert.Empty(hits);
    }
}
