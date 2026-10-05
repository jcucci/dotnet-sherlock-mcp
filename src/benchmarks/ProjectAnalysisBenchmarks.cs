using BenchmarkDotNet.Attributes;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;
using Sherlock.MCP.Runtime.ProjectEvaluation;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Benchmarks;

[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 10, invocationCount: 1)]
public class ProjectAnalysisBenchmarks : WarmBenchmark
{
    public const string Xml = "xml";
    public const string MsBuild = "msbuild";

    private ProjectAnalysisService _projects = null!;

    [Params(Xml, MsBuild)]
    public string Mode { get; set; } = Xml;

    protected override void Prime()
    {
        Graph.Options.ProjectEvaluation = Mode == MsBuild ? ProjectEvaluationMode.Auto : ProjectEvaluationMode.Off;
        _projects = new ProjectAnalysisService(Graph.Options, new DotnetCliProjectEvaluator());
        ProjectAnalysisResult result = _projects.AnalyzeProjectFileAsync(BenchmarkCorpus.ProjectPath).GetAwaiter().GetResult();
        if (result.Evaluation.Mode != Mode)
            throw new InvalidOperationException($"Expected {Mode} evaluation but got {result.Evaluation.Mode}: {result.Evaluation.Reason}");
        ProjectAnalysisTools.AnalyzeProject(_projects, Graph.Middleware, BenchmarkCorpus.ProjectPath).GetAwaiter().GetResult();
    }

    [Benchmark]
    public Task<ProjectAnalysisResult> AnalyzeProject() =>
        _projects.AnalyzeProjectFileAsync(BenchmarkCorpus.ProjectPath);

    [Benchmark]
    public Task<ProjectOutputPaths> OutputPathsOneConfiguration() =>
        _projects.GetProjectOutputPathsAsync(BenchmarkCorpus.ProjectPath, configuration: "Release");

    [Benchmark]
    public Task<string> AnalyzeProjectResponseCacheHit() =>
        ProjectAnalysisTools.AnalyzeProject(_projects, Graph.Middleware, BenchmarkCorpus.ProjectPath);
}
