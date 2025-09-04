# Sherlock MCP for .NET — Roadmap

This roadmap outlines high‑impact features and tools to deepen LLM understanding of .NET codebases, improve ergonomics, and expand project/source awareness.

## Goals
- Richer semantic context for types and members (attributes, docs, generics, inheritance).
- Safe, scalable inspection across many assemblies and projects.
- Source‑level reasoning (symbols, references, snippets, diagnostics).
- LLM‑friendly responses (stable schemas, paging, summarization, streaming).

## Advanced Understanding & Graphs
- Call graph (static approximation)
  - Build a conservative call graph from Roslyn semantic model; tools to fetch callers/callees.
- API diffing
  - Compare two assemblies/versions and return breaking/behavioral changes; tool: `CompareApiSurface`.
- Summaries & explanations
  - `ExplainType`, `ExplainMember` – generate concise natural‑language summaries using metadata + XML docs.
- Code metrics
  - Cyclomatic complexity, lines of code, member counts; tool: `GetMetrics` (opt‑in; Roslyn‑based).
- Pattern detectors
  - ASP.NET Core endpoints, DI registrations, EF DbContexts/entities, attribute‑driven patterns (e.g., MediatR handlers).

## Project & NuGet Improvements
- Project evaluation
  - Read evaluated MSBuild properties/items (configuration, TFMs, conditions) without executing builds.
- NuGet resolution
  - Parse `project.assets.json` to resolve transitive and RID‑specific assets; include ref/lib distinctions.
  - Expose `GetPackageGraph` with versions, target frameworks, and asset paths.

## LLM Ergonomics
- Consistent schemas
  - Publish JSON schemas for every response; include a `schema` URI and `version` in results.
- Request shaping
  - Options for `maxItems`, `include` (fields), `omit` (heavy fields) to reduce payload size.
- Summarization
  - Provide compact summaries alongside raw data to fit token budgets.

## MCP UX & Ops
- Tool discovery
  - `ListCapabilities` with tool names, parameters, and schema versions.
- Configuration
  - Global settings tool: defaults for paging, includeNonPublic, search roots, caching TTL.
- Telemetry (optional)
  - Simple counters for tool durations and cache hit rates (opt‑in, no PII).

## Testing & Quality
- Golden tests
  - Snapshot tests for signatures, friendly names, and schema outputs across language features.
- Coverage focus
  - Edge cases: explicit interface implementations, default interface methods, ref structs/readonly structs, function pointers, required members, records, tuple element names, generic constraints/variance.

## Risks & Considerations
- Performance: reflection and Roslyn can be heavy; mitigate with caching, paging, and streaming.
- Safety: prefer `MetadataLoadContext` to avoid executing user code during analysis.
- Cross‑platform paths and case sensitivity; handle Windows/Linux/Mac differences.

## Open Questions
- Should symbol/source tools be optional behind a feature flag due to Roslyn/MSBuild dependency size?
- How far to go on call‑graph precision vs. performance?
- Provide pluggable filters/predicates for custom org patterns?


