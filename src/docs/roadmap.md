# Sherlock MCP for .NET — Roadmap

This roadmap summarizes what has shipped and what is planned. The live, ordered checklist is the tracking issue [#87](https://github.com/jcucci/dotnet-sherlock-mcp/issues/87); each planned item below links to its own issue.

## Goals
- Richer semantic context for types and members (attributes, docs, generics, inheritance, call relationships).
- Safe, scalable inspection across many assemblies and projects.
- Source‑level reasoning (decompiled or original source, API diffs, framework patterns).
- LLM‑friendly responses (stable schemas, lean projections, paging, actionable errors).

## Shipped
- **Metadata-only inspection**: the tool surface runs on `MetadataLoadContext`, so no user code executes during analysis ([#19](https://github.com/jcucci/dotnet-sherlock-mcp/issues/19)).
- **Summary/full projections** on listing tools, with `maxItems` and continuation-token paging ([#21](https://github.com/jcucci/dotnet-sherlock-mcp/issues/21)).
- **Reverse lookup**: `find_implementations_of`, `find_methods_returning`, `find_references_to` ([#22](https://github.com/jcucci/dotnet-sherlock-mcp/issues/22)).
- **NuGet package lookup**: `find_assembly_by_nuget_package` ([#23](https://github.com/jcucci/dotnet-sherlock-mcp/issues/23)).
- **Assembly-wide member search**: `search_members` ([#33](https://github.com/jcucci/dotnet-sherlock-mcp/issues/33)).
- **Extension method discovery**: `find_extension_methods_for` ([#34](https://github.com/jcucci/dotnet-sherlock-mcp/issues/34)).
- **IL call analysis**: `get_method_calls` for outbound calls and field accesses, and `analysisDepth='il'` on `find_references_to` for inbound callers ([#35](https://github.com/jcucci/dotnet-sherlock-mcp/issues/35)).
- **Assembly identity**: `get_assembly_info` for attributes, target framework, and references ([#36](https://github.com/jcucci/dotnet-sherlock-mcp/issues/36)).
- **`.slnx` solutions** in `analyze_solution` ([#37](https://github.com/jcucci/dotnet-sherlock-mcp/issues/37)).
- **Derived types** in `get_type_hierarchy` via `additionalAssemblies` ([#38](https://github.com/jcucci/dotnet-sherlock-mcp/issues/38)).
- **End-to-end stdio integration tests** ([#40](https://github.com/jcucci/dotnet-sherlock-mcp/issues/40)).
- **ModelContextProtocol 2.1.0 GA**, with behavioural annotations on every tool and caching hints on `tools/list` ([#41](https://github.com/jcucci/dotnet-sherlock-mcp/issues/41)).
- **Runtime configuration**: `get_runtime_options` / `update_runtime_options` for paging, caching, and search defaults.
- **Assembly locator fixes**: pruned, ranked, and cheaper file-system searches ([#78](https://github.com/jcucci/dotnet-sherlock-mcp/issues/78)).

## Planned

### Agent ergonomics & MCP protocol
- Structured tool output (`outputSchema` / `structuredContent`) and compact JSON ([#58](https://github.com/jcucci/dotnet-sherlock-mcp/issues/58))
- `isError` on tool errors, with guidance for common failures ([#59](https://github.com/jcucci/dotnet-sherlock-mcp/issues/59))
- Consolidated member-listing tools and optional tool profiles ([#60](https://github.com/jcucci/dotnet-sherlock-mcp/issues/60))
- snake_case wire names in tool descriptions and server instructions ([#61](https://github.com/jcucci/dotnet-sherlock-mcp/issues/61))
- Claude Code plugin bundling the server and a Sherlock skill ([#62](https://github.com/jcucci/dotnet-sherlock-mcp/issues/62))
- NuGet `McpServer` package type and `dnx` launch ([#63](https://github.com/jcucci/dotnet-sherlock-mcp/issues/63))
- Resource templates and `resource_link` results ([#64](https://github.com/jcucci/dotnet-sherlock-mcp/issues/64))
- Completions for type names, assembly paths, and package IDs ([#65](https://github.com/jcucci/dotnet-sherlock-mcp/issues/65))
- Cancellation and progress notifications for long scans ([#66](https://github.com/jcucci/dotnet-sherlock-mcp/issues/66))
- Elicitation for ambiguous inputs ([#67](https://github.com/jcucci/dotnet-sherlock-mcp/issues/67))
- Assembly handles for stateless cross-call context ([#68](https://github.com/jcucci/dotnet-sherlock-mcp/issues/68))
- MCP prompts for common workflows ([#69](https://github.com/jcucci/dotnet-sherlock-mcp/issues/69))

### New capabilities
- Decompile types and members to C# ([#70](https://github.com/jcucci/dotnet-sherlock-mcp/issues/70))
- Original source via Source Link and PDBs ([#71](https://github.com/jcucci/dotnet-sherlock-mcp/issues/71))
- API diff and breaking-change detection between versions ([#72](https://github.com/jcucci/dotnet-sherlock-mcp/issues/72))
- Dependency resolution from `project.assets.json` ([#73](https://github.com/jcucci/dotnet-sherlock-mcp/issues/73))
- Type resolution against target-framework reference packs ([#74](https://github.com/jcucci/dotnet-sherlock-mcp/issues/74))
- MSBuild evaluation for project analysis ([#75](https://github.com/jcucci/dotnet-sherlock-mcp/issues/75))
- Framework pattern detectors: ASP.NET endpoints, DI registrations, EF models, MediatR handlers ([#76](https://github.com/jcucci/dotnet-sherlock-mcp/issues/76))
- Mermaid output for type hierarchies and call graphs ([#77](https://github.com/jcucci/dotnet-sherlock-mcp/issues/77))

### Performance & quality
- Memoized NuGet cache enumeration ([#79](https://github.com/jcucci/dotnet-sherlock-mcp/issues/79))
- Reused `PEReader`/`MetadataReader` across IL analysis calls ([#80](https://github.com/jcucci/dotnet-sherlock-mcp/issues/80))
- BenchmarkDotNet benchmarks ([#81](https://github.com/jcucci/dotnet-sherlock-mcp/issues/81))
- Response caching for type, reflection, XML-doc, and project tools ([#82](https://github.com/jcucci/dotnet-sherlock-mcp/issues/82))
- Dead-code removal ([#83](https://github.com/jcucci/dotnet-sherlock-mcp/issues/83))
- CI on Windows and macOS ([#84](https://github.com/jcucci/dotnet-sherlock-mcp/issues/84))
- Golden-file tests for tool response shapes ([#85](https://github.com/jcucci/dotnet-sherlock-mcp/issues/85))
- Persistent on-disk index for reverse lookup ([#39](https://github.com/jcucci/dotnet-sherlock-mcp/issues/39))

## Ideas (not yet tracked)
- `ExplainType` / `ExplainMember`: concise natural-language summaries from metadata and XML docs.
- Code metrics (complexity, member counts) via an opt-in tool.
- `ListCapabilities`: tool names, parameters, and schema versions in one call.
- Opt-in telemetry: tool durations and cache hit rates, no PII.
- Source-level call graph via Roslyn. The IL analysis above covers most caller/callee questions against compiled assemblies.

## Risks & Considerations
- Performance: reflection, IL walking, and decompilation can be heavy; mitigate with caching, paging, and lean projections.
- Safety: keep analysis on `MetadataLoadContext` so user code never executes.
- Cross‑platform paths and case sensitivity differ across Windows, Linux, and macOS ([#84](https://github.com/jcucci/dotnet-sherlock-mcp/issues/84)).
- Dependency weight: decompiler, Roslyn, and MSBuild packages grow the tool's install size.

## Open Questions
- Should decompilation, source, and MSBuild-backed tools sit behind a feature flag or tool profile ([#60](https://github.com/jcucci/dotnet-sherlock-mcp/issues/60)) given their dependency size?
- How much call-graph precision is worth the cost beyond single-method IL analysis?
- Should there be pluggable filters or predicates for organization-specific patterns?
