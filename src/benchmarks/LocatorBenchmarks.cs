using BenchmarkDotNet.Attributes;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Benchmarks;

public class LocatorBenchmarks
{
    [Params("System.Text.Json.JsonSerializer")]
    public string ClassName { get; set; } = "System.Text.Json.JsonSerializer";

    [Benchmark]
    public IReadOnlyList<string> FindByClassName() =>
        AssemblyLocator.FindByClassName(BenchmarkCorpus.FrameworkDirectory, ClassName);
}
