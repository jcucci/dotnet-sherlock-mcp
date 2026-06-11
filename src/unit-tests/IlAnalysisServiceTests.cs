using System.Reflection;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.Il;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Tests.IlAnalysisFixtures;

namespace Sherlock.MCP.Tests;

public class IlAnalysisServiceTests
{
    private readonly IIlAnalysisService _svc = new IlAnalysisService();
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private readonly IlAnalysisOptions _options = new();

    private static readonly string SubjectFullName = typeof(IlSampleSubject).FullName!;
    private const string ListFullName = "System.Collections.Generic.List`1";

    [Fact]
    public void GetMethodCalls_Summary_IncludesLocalBclAndGenericTargets()
    {
        var result = _svc.GetMethodCalls(_testAssemblyPath, "IlSampleSubject", "DoWork", _options);

        Assert.NotNull(result);
        var targets = result!.Calls.Select(c => c.Target).ToArray();

        Assert.Contains("System.Console.WriteLine", targets);
        Assert.Contains($"{SubjectFullName}.Helper", targets);
        Assert.Contains($"{SubjectFullName}.Compute", targets);
        Assert.Contains($"{ListFullName}..ctor", targets);
        Assert.Contains($"{ListFullName}.Add", targets);
    }

    [Fact]
    public void GetMethodCalls_TracksFieldReadAndWrite()
    {
        var result = _svc.GetMethodCalls(_testAssemblyPath, "IlSampleSubject", "DoWork", _options);

        Assert.NotNull(result);
        var counterField = $"{SubjectFullName}._counter";
        var labelField = $"{SubjectFullName}._label";

        Assert.Contains(result!.FieldAccesses, f => f.Target == counterField && f.Access == "read");
        Assert.Contains(result.FieldAccesses, f => f.Target == counterField && f.Access == "write");
        Assert.Contains(result.FieldAccesses, f => f.Target == labelField && f.Access == "write");
    }

    [Fact]
    public void GetMethodCalls_NewObj_HasNewObjKind()
    {
        var result = _svc.GetMethodCalls(_testAssemblyPath, "IlSampleSubject", "DoWork", _options);

        Assert.NotNull(result);
        Assert.Contains(result!.Calls, c => c.Target == $"{ListFullName}..ctor" && c.Kind == "newobj");
    }

    [Fact]
    public void GetMethodCalls_AggregatesAllOverloads()
    {
        var result = _svc.GetMethodCalls(_testAssemblyPath, "IlSampleSubject", "Helper", _options);

        Assert.NotNull(result);
        Assert.Equal(2, result!.MatchedOverloads);
        Assert.False(result.AnyBodyless);
    }

    [Fact]
    public void GetMethodCalls_AbstractMethod_ReportsBodyless()
    {
        var result = _svc.GetMethodCalls(_testAssemblyPath, "Bodyless", "Nothing", _options);

        Assert.NotNull(result);
        Assert.Equal(1, result!.MatchedOverloads);
        Assert.True(result.AnyBodyless);
        Assert.Empty(result.Calls);
    }

    [Fact]
    public void GetMethodCalls_StaticConstructor_AnalyzesTypeInitializerBody()
    {
        var result = _svc.GetMethodCalls(_testAssemblyPath, "StaticCtorSubject", ".cctor", _options);

        Assert.NotNull(result);
        Assert.Equal(1, result!.MatchedOverloads);
        Assert.False(result.AnyBodyless);
        Assert.Contains(result.Calls, c => c.Target == $"{typeof(StaticCtorSubject).FullName}.CreateShared");
    }

    [Fact]
    public void GetMethodCalls_UnknownMethod_ReturnsNull()
    {
        Assert.Null(_svc.GetMethodCalls(_testAssemblyPath, "IlSampleSubject", "NoSuchMethod", _options));
        Assert.Null(_svc.GetMethodCalls(_testAssemblyPath, "NoSuchType", "DoWork", _options));
    }

    [Fact]
    public void FindInboundCallers_FindsCallerOfExternalType()
    {
        var hits = _svc.FindInboundCallers([_testAssemblyPath], "Console", new ReverseLookupOptions());

        Assert.Contains(hits, h =>
            h.CallerTypeFullName == SubjectFullName &&
            h.CallerMethod == "DoWork" &&
            h.ReferenceKind == "ilCall");
    }

    [Fact]
    public void FindInboundCallers_DeduplicatesRepeatedCallsToSameTarget()
    {
        // DoWork calls Console.WriteLine twice; the inbound hit must appear only once.
        var hits = _svc.FindInboundCallers([_testAssemblyPath], "Console", new ReverseLookupOptions());

        var writeLineFromDoWork = hits.Count(h =>
            h.CallerTypeFullName == SubjectFullName &&
            h.CallerMethod == "DoWork" &&
            h.TargetMember == "System.Console.WriteLine" &&
            h.ReferenceKind == "ilCall");

        Assert.Equal(1, writeLineFromDoWork);
    }

    [Fact]
    public void FindInboundCallers_FindsCallerOfLocalType()
    {
        var hits = _svc.FindInboundCallers([_testAssemblyPath], "IlSampleSubject", new ReverseLookupOptions());

        Assert.Contains(hits, h =>
            h.CallerTypeFullName == SubjectFullName &&
            h.CallerMethod == "DoWork" &&
            h.TargetMember == $"{SubjectFullName}.Compute");
    }
}
