using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

IConfig config = DefaultConfig.Instance
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(JsonExporter.Brief);

BenchmarkSwitcher.FromAssembly(typeof(Sherlock.MCP.Benchmarks.ServiceGraph).Assembly).Run(args, config);
