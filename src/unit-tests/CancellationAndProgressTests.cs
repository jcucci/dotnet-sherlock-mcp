using System.Reflection;
using ModelContextProtocol;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Contracts.Il;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Runtime.Contracts.Search;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Shared;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class CancellationAndProgressTests
{
    private static readonly string TestAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private static readonly string[] ScanScope =
    [
        TestAssemblyPath,
        typeof(ReverseLookupService).Assembly.Location,
        typeof(ToolMiddleware).Assembly.Location
    ];

    private readonly IReverseLookupService _reverseLookup = new ReverseLookupService();
    private readonly IIlAnalysisService _ilAnalysis = new IlAnalysisService();
    private readonly RuntimeOptions _runtimeOptions = new();
    private readonly ToolMiddleware _middleware;

    public CancellationAndProgressTests() =>
        _middleware = new ToolMiddleware(new InMemoryToolResponseCache(), new NoopTelemetry(), _runtimeOptions);

    private static CancellationToken Cancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts.Token;
    }

    [Fact]
    public void ReverseLookup_WithCancelledToken_ThrowsOperationCanceled()
    {
        var token = Cancelled();
        var options = new ReverseLookupOptions();

        Assert.Throws<OperationCanceledException>(() => _reverseLookup.FindImplementations(ScanScope, "IDisposable", options, cancellationToken: token));
        Assert.Throws<OperationCanceledException>(() => _reverseLookup.FindMethodsReturning(ScanScope, "String", options, cancellationToken: token));
        Assert.Throws<OperationCanceledException>(() => _reverseLookup.FindExtensionMethodsFor(ScanScope, "String", options, cancellationToken: token));
        Assert.Throws<OperationCanceledException>(() => _reverseLookup.FindReferences(ScanScope, "String", options, cancellationToken: token));
    }

    [Fact]
    public void IlAnalysis_WithCancelledToken_ThrowsOperationCanceled()
    {
        var token = Cancelled();

        Assert.Throws<OperationCanceledException>(() => _ilAnalysis.FindInboundCallers(ScanScope, "String", new ReverseLookupOptions(), cancellationToken: token));
        Assert.Throws<OperationCanceledException>(() => _ilAnalysis.GetMethodCalls(TestAssemblyPath, nameof(CancellationAndProgressTests), nameof(Cancelled), new IlAnalysisOptions(), token));
    }

    [Fact]
    public void Search_WithCancelledToken_ThrowsOperationCanceled() =>
        Assert.Throws<OperationCanceledException>(() =>
            new SearchService().SearchMembers(TestAssemblyPath, "Cancel", new SearchOptions(), offset: 0, pageSize: 10, Cancelled()));

    [Fact]
    public void AssemblyLocator_WithCancelledToken_ThrowsOperationCanceled()
    {
        var root = Path.GetDirectoryName(TestAssemblyPath)!;

        Assert.Throws<OperationCanceledException>(() => AssemblyLocator.FindByClassName(root, nameof(CancellationAndProgressTests), cancellationToken: Cancelled()));
        Assert.Throws<OperationCanceledException>(() => AssemblyLocator.FindByFileName(root, "*.dll", Cancelled()));
    }

    [Fact]
    public async Task ProjectAnalysis_WithCancelledToken_ThrowsOperationCanceled()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), $"sherlock_cancel_{Guid.NewGuid():N}.csproj");
        await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new ProjectAnalysisService().AnalyzeProjectFileAsync(projectPath, Cancelled()));
        }
        finally
        {
            File.Delete(projectPath);
        }
    }

    [Fact]
    public void ReverseLookup_ReportsMonotonicProgressPerAssembly()
    {
        var progress = new CollectingProgress<ScanProgress>();

        _reverseLookup.FindImplementations(ScanScope, "IDisposable", new ReverseLookupOptions(), progress);

        AssertMonotonicAndComplete(progress.Reports, expectedTotal: ScanScope.Length);
    }

    [Fact]
    public void IlAnalysis_ReportsMonotonicProgressPerAssembly()
    {
        var progress = new CollectingProgress<ScanProgress>();

        _ilAnalysis.FindInboundCallers(ScanScope, "String", new ReverseLookupOptions(), progress);

        AssertMonotonicAndComplete(progress.Reports, expectedTotal: ScanScope.Length);
    }

    [Fact]
    public void AssemblyLocator_ReportsProgressPerInspectedFile()
    {
        var root = Path.GetDirectoryName(TestAssemblyPath)!;
        var progress = new CollectingProgress<ScanProgress>();

        var matches = AssemblyLocator.FindByClassName(root, nameof(CancellationAndProgressTests), progress);

        Assert.NotEmpty(matches);
        Assert.NotEmpty(progress.Reports);
        AssertMonotonicAndComplete(progress.Reports, expectedTotal: progress.Reports[^1].Total);
    }

    [Fact]
    public void ProgressCounter_ThrottlesToAboutOneHundredReports()
    {
        var progress = new CollectingProgress<ScanProgress>();
        var counter = new ProgressCounter(progress, total: 1000);

        for (var i = 0; i < 1000; i++)
            counter.Increment();

        Assert.Equal(100, progress.Reports.Count);
        Assert.Equal(1000, progress.Reports[^1].Completed);
    }

    [Fact]
    public void ProgressCounter_NeverExceedsOneHundredReportsJustAboveTheThreshold()
    {
        var progress = new CollectingProgress<ScanProgress>();
        var counter = new ProgressCounter(progress, total: 150);

        for (var i = 0; i < 150; i++)
            counter.Increment();

        Assert.True(progress.Reports.Count <= 100);
        Assert.Equal(150, progress.Reports[^1].Completed);
    }

    [Fact]
    public void ProgressAdapter_MapsPhasesOntoOneIncreasingScale()
    {
        var sink = new CollectingProgress<ProgressNotificationValue>();

        ProgressAdapter.ForPhase(sink, phase: 0, phaseCount: 2)!.Report(new ScanProgress(3, 3, "a.dll"));
        ProgressAdapter.ForPhase(sink, phase: 1, phaseCount: 2)!.Report(new ScanProgress(1, 3));

        Assert.Equal(3f, sink.Reports[0].Progress);
        Assert.Equal(6f, sink.Reports[0].Total);
        Assert.Equal("Scanned a.dll", sink.Reports[0].Message);
        Assert.Equal(4f, sink.Reports[1].Progress);
        Assert.Null(sink.Reports[1].Message);
        Assert.Null(ProgressAdapter.ForPhase(null));
    }

    [Fact]
    public void FindReferencesTo_IlDepth_ReportsBothPhasesToClient()
    {
        var sink = new CollectingProgress<ProgressNotificationValue>();

        ReverseLookupTools.FindReferencesTo(
            _reverseLookup,
            _ilAnalysis,
            _middleware,
            _runtimeOptions,
            TestHandles.Registry,
            assemblyPath: TestAssemblyPath,
            typeName: "String",
            additionalAssemblies: ScanScope[1..],
            analysisDepth: "il",
            noCache: true,
            progress: sink);

        Assert.All(sink.Reports, r => Assert.Equal(ScanScope.Length * 2, r.Total));
        Assert.Equal(ScanScope.Length * 2, sink.Reports[^1].Progress);
    }

    [Fact]
    public void Tools_WithCancelledToken_RethrowInsteadOfReturningError()
    {
        var token = Cancelled();

        Assert.Throws<OperationCanceledException>(() => ReverseLookupTools.FindImplementationsOf(
            _reverseLookup,
            _middleware,
            _runtimeOptions,
            TestHandles.Registry,
            assemblyPath: TestAssemblyPath,
            typeName: "IDisposable",
            noCache: true,
            cancellationToken: token));
        Assert.Throws<OperationCanceledException>(() => ReverseLookupTools.FindReferencesTo(
            _reverseLookup,
            _ilAnalysis,
            _middleware,
            _runtimeOptions,
            TestHandles.Registry,
            assemblyPath: TestAssemblyPath,
            typeName: "String",
            analysisDepth: "il",
            noCache: true,
            cancellationToken: token));
        Assert.Throws<OperationCanceledException>(() => TypeAnalysisTools.GetTypeHierarchy(
            new TypeAnalysisService(),
            new SharedInspectionContextProvider(new RuntimeOptions()),
            _reverseLookup,
            TestMiddleware.Fresh,
            TestHandles.Registry,
            assemblyPath: TestAssemblyPath,
            typeName: typeof(CancellationAndProgressTests).FullName!,
            additionalAssemblies: ScanScope[1..],
            cancellationToken: token));
        Assert.Throws<OperationCanceledException>(() => ReflectionTools.FindAssemblyByClassName(
            nameof(CancellationAndProgressTests), Path.GetDirectoryName(TestAssemblyPath)!, cancellationToken: token));
    }

    [Fact]
    public void Middleware_DoesNotCacheCancelledExecution()
    {
        var calls = 0;
        Func<string> cancelled = () =>
        {
            calls++;
            throw new OperationCanceledException();
        };

        Assert.Throws<OperationCanceledException>(() => _middleware.Execute("cancel-key", cancelled));

        var result = _middleware.Execute("cancel-key", () =>
        {
            calls++;
            return "fresh";
        });

        Assert.Equal("fresh", result);
        Assert.Equal(2, calls);
    }

    private static void AssertMonotonicAndComplete(IReadOnlyList<ScanProgress> reports, int expectedTotal)
    {
        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.Equal(expectedTotal, r.Total));
        for (var i = 1; i < reports.Count; i++)
            Assert.True(reports[i].Completed > reports[i - 1].Completed);
        Assert.Equal(expectedTotal, reports[^1].Completed);
    }

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        private readonly object _gate = new();
        private readonly List<T> _reports = [];

        public List<T> Reports
        {
            get { lock (_gate) return [.. _reports]; }
        }

        public void Report(T value)
        {
            lock (_gate) _reports.Add(value);
        }
    }
}
