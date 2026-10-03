# Sherlock.MCP.Benchmarks

BenchmarkDotNet benchmarks for the scanning and lookup paths. Run them before and after a performance change, and before building anything that is only justified by measurement (for example the persistent index in #39).

## Running

```bash
dotnet run -c Release --project src/benchmarks -f net10.0 -- --list flat
dotnet run -c Release --project src/benchmarks -f net10.0 -- --filter '*Search*'
dotnet run -c Release --project src/benchmarks -f net10.0 -- --filter '*' --job dry
```

Results land in `BenchmarkDotNet.Artifacts/results` (Markdown and JSON). The `Benchmarks` GitHub workflow runs them on demand (`workflow_dispatch`) and uploads that folder; they never run on pull requests.

## Corpus

| Entry | Assembly | Source |
|---|---|---|
| `small` | `Sherlock.MCP.Runtime.dll` | build output |
| `large` | `Microsoft.CodeAnalysis.CSharp.dll` (+ `Microsoft.CodeAnalysis.dll` for multi-assembly scans) | build output |
| `nuget` | `ICSharpCode.Decompiler.dll` | NuGet global packages folder (needs `dotnet restore`) |

`LocatorBenchmarks` scans the host runtime's shared framework directory.

## Scenarios

| Class | What it measures |
|---|---|
| `TypeMembersBenchmarks` | `get_type_members kinds=method` through the tool and `ToolMiddleware`: warm contexts with `noCache`, and a response-cache hit |
| `SearchMembersBenchmarks` | `search_members` for `Parse` |
| `ReverseLookupBenchmarks` | `find_implementations_of`, `find_references_to` (signatures) and IL inbound callers (`analysisDepth='il'`) across `large` + companion + `small` |
| `LocatorBenchmarks` | `find_assembly_by_class_name` |

Each `*Benchmarks` class is **warm**: one service graph is built and primed in `GlobalSetup`, so measured calls reuse the shared inspection contexts. Each `*ColdBenchmarks` twin is **cold**: every invocation gets a fresh service graph (contexts, metadata readers, response cache), so it pays for loading and resolving the assemblies. Process-wide state such as the OS file cache and the NuGet cache index stays warm.
