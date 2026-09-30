# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- The NuGet package is now published with the `McpServer` package type and embeds `server.json` as `.mcp/server.json`, so NuGet.org lists it as an MCP server. The README documents launching it without installing anything via the .NET 10 SDK's `dnx` (`dnx Sherlock.MCP.Server@<version> --yes`), with Claude Code and VS Code config examples. The release workflow now checks that the tag, both `server.json` versions and the project version all match, and that the packed package carries the `McpServer` type and `.mcp/server.json`. (#63)
- Long-running tools can now be cancelled and report progress. Every scanning tool takes the request's `CancellationToken`, so a client's `notifications/cancelled` stops reverse lookups, IL inbound-caller scans, `search_members`, `get_method_calls`, the assembly locators and project analysis part-way through. A cancelled call is never cached and is no longer reported as an `InternalError` payload. When the client sends a `progressToken`, `find_references_to` (both phases of `analysisDepth='il'` on one scale), `find_implementations_of`, `find_methods_returning`, `find_extension_methods_for`, `get_type_hierarchy` (with `additionalAssemblies`) and `find_assembly_by_class_name` emit `notifications/progress` per assembly or file scanned, throttled to about 100 updates. The `io.modelcontextprotocol/tasks` extension was evaluated but is not available in `ModelContextProtocol` 2.1.0. (#66)
- Ambiguous inputs are resolved through elicitation (MRTR `input_required`, spec 2026-07-28). A simple type name that matches several types (e.g. `Widget` in two namespaces) used to resolve silently to the first match; every single-type tool (`get_type_info`, `get_type_hierarchy`, `get_generic_type_info`, `get_type_attributes`, `get_nested_types`, the `get_type_*` member tools, `get_all_type_members`, `get_member_attributes`, `get_parameter_attributes`, `analyze_type`, `analyze_method`, `get_method_calls` and the XML-doc tools) now asks the client which full name was meant. Clients that cannot elicit get an `AmbiguousTypeName` error whose `recommendedParams.candidates` lists the full names; the type resource template returns `InvalidParams` with the candidates. `find_assembly_by_nuget_package` asks which target framework to inspect when `tfm` is omitted and the package ships several (other clients keep the automatic pick), its success payload now includes `availableVersions` and `availableTfms`, and TFM folders without a DLL (e.g. `_._` placeholders) are no longer considered. The duplicated type-name lookups now share one resolver, `TypeNameResolver`. (#67)
- Tool errors are now flagged with MCP's `isError: true` (set centrally by a `tools/call` filter, so it also applies to cached responses) and carry guidance. `TypeNotFound` and `MemberNotFound` list the closest names in `recommendedParams.candidates` ("did you mean"), `AssemblyNotFound` points at the assembly-discovery tools and lists similarly named assemblies in the same folder, and common exceptions map to specific codes instead of `InternalError`: `InvalidAssembly` (`BadImageFormatException`), `DependencyNotFound` (unloadable reference), `AccessDenied`, `InvalidArgument`, and `ProjectNotFound` / `FileNotFound` for the project tools. `DependencyResolutionFailed` guidance now applies to every tool, not only `get_types_from_assembly`. (#59)

### Fixed

- `FindAssemblyByClassName` and `FindAssemblyByFileName` no longer scan every directory under `workingDirectory`. They now walk the tree lazily, skipping `obj/`, `ref/`, `refint/`, `node_modules/`, `packages/`, `TestResults/`, dot-directories and symlinked directories. Matches are ranked deterministically (`bin/` first, then newest, then shortest path), so a stale `obj/` or reference-assembly copy is no longer returned by chance. The response keeps `foundAssembly` and adds `candidateCount` plus up to 10 `candidates` when there are several matches. Class lookup reads type names through `System.Reflection.Metadata` instead of creating a `MetadataLoadContext` per DLL, and also matches full and nested (`Outer+Inner`) names. A missing `workingDirectory` now returns `InvalidArgument` rather than `InternalError`, not-found errors carry guidance, and both tool descriptions no longer claim to search only `bin/Debug` and `bin/Release`. (#78)

## [2.13.0] - 2026-08-06

### Added

- Behavioural annotations on all 36 tools (`readOnlyHint`, `destructiveHint`, `openWorldHint`, `idempotentHint`) alongside a human-readable title. Every tool was previously declared with a bare `[McpServerTool]`, so the SDK advertised its pessimistic defaults (`destructiveHint: true`, `readOnlyHint: false`) — inaccurate for the 35 tools that only read metadata. Assembly-discovery tools remain open-world since they scan search roots for an unbounded set of assemblies, and `UpdateRuntimeOptions` is the only mutating tool. (#56)
- Caching hints on `tools/list` (`ttlMs`, `cacheScope`), introduced by MCP revision `2026-07-28`. The tool set is scanned once at startup and holds no per-caller data, so it is advertised as publicly cacheable for one hour. Tools are also returned in a deterministic order, which keeps client caches and LLM prompt caches stable across reconnects. (#56)

### Changed

- Upgraded `ModelContextProtocol` from 1.4.0 to 2.1.0, which implements MCP specification revision `2026-07-28`. Clients now negotiate the current revision rather than falling back to an older one, and the handshake-less request flow — no `initialize` exchange, with the protocol version declared per request in `_meta` — is supported. Clients speaking earlier revisions continue to work unchanged and are not sent any of the new result fields. The server remains stdio-only. (#56)
- Updated the `server.json` registry schema reference from `2025-09-16` to `2025-12-11`. (#56)

### Removed

- `GEMINI.md`. The agent-facing guidance in `CLAUDE.md` and `README.md` remains current.

## [2.12.0] - 2026-06-21

### Added

- Transitive dependency resolution from the NuGet global packages cache. When an assembly is loaded from an isolated `lib/<tfm>` folder (e.g. `~/.nuget/packages/.../lib/net8.0/`) whose dependencies are not sitting alongside it, the loader now probes the cache for the best framework-compatible build of each dependency package. This makes type introspection work against bare NuGet assemblies (such as `Azure.ResourceManager.MachineLearning`) without needing a build output directory.
- `GetTypesFromAssembly` now accepts an optional `additionalAssemblies` parameter so callers can point at sibling dependency DLLs (e.g. a `bin/Debug/<tfm>` folder) to resolve types when automatic probing is insufficient.

### Fixed

- `GetTypesFromAssembly` no longer returns a silent empty result when an assembly's dependencies cannot be resolved. It now reports a structured `DependencyResolutionFailed` error naming the unresolved assemblies and how to supply them, instead of masking the failure behind an empty list.

## [2.11.0] - 2026-06-12

### Added

- The server now emits MCP usage instructions to connecting clients, giving agents built-in guidance on how to drive the introspection tools — token-efficient `summary`/`full` projections and the discovery → type → member workflow — without relying on external documentation. (#54)

### Changed

- Refreshed the agent-facing guidance in `CLAUDE.md`, `GEMINI.md`, `README.md`, and the runtime member-analysis docs to document the snake_case tool names exposed on the wire, the lean `projection` defaults, and the recommended analysis workflow. (#54)

## [2.10.0] - 2026-06-12

### Added

- `SearchMembers` — a single assembly-wide member search tool that scans every type in an assembly and returns methods, properties, fields, events, and constructors matching a name query, following the existing pagination, projection, and caching conventions. This avoids having to enumerate types first and then probe each one individually. (#33)
- `FindExtensionMethodsFor` — a reverse-lookup tool that discovers extension methods applicable to a given type across one or more assemblies. Scans are pre-filtered to static, non-generic classes (the only place extension methods can be declared) to keep large-assembly lookups fast. (#34)
- `GetAssemblyInfo` — an assembly-level metadata tool reporting identity, target framework, referenced assemblies, and module information. Output is projection-validated and guarded against oversized responses so large dependency graphs don't blow the token budget. (#36)
- `GetMethodCalls` plus a new `analysisDepth='il'` mode — IL-level call analysis. `GetMethodCalls` reports a method's *outbound* call and field-access targets, while `FindReferencesTo` with `analysisDepth='il'` resolves *inbound* callers from IL rather than signature matching alone. Static constructors are included and duplicate inbound hits are de-duplicated. (#35)
- `GetTypeHierarchy` now populates `DerivedTypes` when given an optional `additionalAssemblies` search scope, so subclasses defined in other assemblies are discovered instead of silently omitted. Derived-type scanning keys off the resolved `hierarchy.TypeName`. (#38)
- End-to-end MCP integration tests that launch the server over stdio and exercise the full client/server protocol round-trip, complementing the existing unit suite. (#40)

### Changed

- The build is now centralized through a root `Directory.Build.props` with warnings-as-errors and the .NET analyzers enabled across all projects, raising the code-quality baseline for every build. (#42)
- Upgraded the `ModelContextProtocol` SDK to 1.4.0 across the server and integration-test projects. (#41)

### Fixed

- `AnalyzeSolution` now parses `.slnx` (XML-format) solution files; it previously returned zero projects for the newer solution format. (#37)
- Package-version resolution now applies a deterministic tie-break when only prerelease versions are available, so repeated runs resolve to the same version. (#37)
- The release pipeline now waits for NuGet to finish indexing a freshly pushed package before attempting the MCP Registry publish, eliminating a race that could fail the publish step.

## [2.9.1] - 2026-05-05

### Fixed

- Excessive inotify watch consumption on Linux that could exhaust `fs.inotify.max_user_watches`. The host builder previously wired up the default `appsettings.json` reload pipeline, which on Linux backs `PhysicalFileProvider` with a `FileSystemWatcher` rooted at the process working directory and configured with `IncludeSubdirectories = true`. When `sherlock-mcp` was launched from a project root or `$HOME` (the typical client CWD for a stdio MCP server installed as a `dotnet tool`), this recursively allocated one inotify watch per subdirectory across the entire tree. Sherlock does not consume `IConfiguration` anywhere, so the host now uses `Host.CreateEmptyApplicationBuilder`, eliminating the watcher entirely. A new `Sherlock.MCP.IntegrationTests` project pins the contract on Linux by spawning the server in a directory of stub subdirectories and asserting the resulting inotify watch count via `/proc/<pid>/fdinfo`. (#31)

## [2.9.0] - 2026-04-19

### Added

- Three new reverse-lookup MCP tools for answering "what implements / returns / references this type?" across one or more assemblies: `FindImplementationsOf`, `FindMethodsReturning`, `FindReferencesTo`. All three follow the existing pagination / projection / caching conventions (summary vs full). `FindReferencesTo` enforces a hard scan cap (defaults to max(maxItems*4, 500)) and reports `truncated: true` when hit. A new `TypeNameMatcher` normalizes simple, full, open-generic (`Foo<>`, `Foo<T>`, `Foo\`1`), nested (`Outer+Inner` or `Outer.Inner`), array/byref/pointer, nullable (`int?`), and built-in alias (`int`, `string`) forms. (#22)
- `FindAssemblyByNugetPackage(packageId, version?, tfm?)` resolves DLLs directly from the local NuGet cache without requiring a `.csproj`. When `version` or `tfm` is omitted, the highest available version and best-matching framework are selected automatically; lookup failures return structured errors that include `availableVersions` and `availableTfms` to aid retry. `ResolvePackageReferences` and the new tool now honor the `NUGET_PACKAGES` environment variable instead of hardcoding `~/.nuget/packages`. (#23)

### Changed

- Listing tools `GetTypesFromAssembly` and `GetTypeMethods` now default to the lean `summary` projection, reducing typical response size by roughly 80% to avoid token blowouts on mid-sized assemblies. Callers that require the prior detailed payload (attributes, interfaces, generics, structured parameters) must now pass `projection: "full"` explicitly. (#21)

### Fixed

- Signature rendering polish for consumer-facing JSON output: `Nullable<T>` now renders as `T?`, default values use C# casing (`true` / `false` / `null`), interface-member signatures no longer repeat the implied `public` / `abstract` modifiers, and consumer-facing type names strip arity backticks (e.g., `` List`1 `` → `List<T>`). Internal reflection paths and XML-doc lookups are intentionally left on the canonical form. (#24)

## [2.7.2] - 2026-04-18

### Fixed

- Attribute dumping no longer crashes JSON serialization when an attribute argument is a `typeof(...)` value. `AttributeUtils` now projects `Type` arguments (including `Type[]`) to a serializable `TypeRef { FullName, AssemblyName }` contract at extraction time, so consumers of `get_member_attributes`, `get_type_methods`, and related tools can serialize results without hitting `Serialization and deserialization of 'System.RuntimeType' instances is not supported`. (#20)

## [2.7.1] - 2026-04-17

### Fixed

- Inspection tools no longer fail with `Could not load file or assembly` when inspected types reference attributes whose dependencies are absent on disk. The default inspection context now uses `System.Reflection.MetadataLoadContext` (inspection-only) instead of loading assemblies into the default `AssemblyLoadContext`, so attribute metadata is readable without resolving the attribute's runtime dependencies. Six MLC-incompatible reflection patterns (attribute lookups, `typeof()` type comparisons, `GetBaseDefinition`, `DefaultValue`) were migrated to MLC-safe equivalents. (#19)

## [2.7.0] - 2025-01-18

This is the baseline release for conventional commits adoption. Prior versions were not tracked with structured release notes.

### Features

- **28+ MCP Tools**: Comprehensive .NET assembly analysis capabilities
- **Assembly Discovery**: `AnalyzeAssembly`, `FindAssemblyByClassName`, `FindAssemblyByFileName`
- **Type Introspection**: `GetTypesFromAssembly`, `GetTypeInfo`, `GetTypeHierarchy`, `GetGenericTypeInfo`, `GetTypeAttributes`, `GetNestedTypes`, `AnalyzeType`
- **Member Analysis**: `GetTypeMethods`, `GetTypeProperties`, `GetTypeFields`, `GetTypeEvents`, `GetTypeConstructors`, `GetAllTypeMembers`, `AnalyzeMethod`
- **Attributes & Documentation**: `GetMemberAttributes`, `GetParameterAttributes`, `GetXmlDocsForType`, `GetXmlDocsForMember`
- **Project Analysis**: `AnalyzeSolution`, `AnalyzeProject`, `GetProjectOutputPaths`, `ResolvePackageReferences`, `FindDepsJsonDependencies`
- **Configuration**: `GetRuntimeOptions`, `UpdateRuntimeOptions`
- **Multi-platform Support**: .NET 8.0, 9.0, and 10.0 targets
- **Container Support**: Alpine-based Docker images
- **Performance Features**: Smart pagination, response size validation, caching layer, streaming support
- **Transitive Assembly Resolution**: Directory-based dependency resolver for complex assembly graphs

### Previous Versions

Versions prior to 2.7.0 were not tracked with conventional commits. This changelog begins with 2.7.0 as the baseline.

[2.10.0]: https://github.com/jcucci/dotnet-sherlock-mcp/releases/tag/v2.10.0
[2.9.1]: https://github.com/jcucci/dotnet-sherlock-mcp/releases/tag/v2.9.1
[2.9.0]: https://github.com/jcucci/dotnet-sherlock-mcp/releases/tag/v2.9.0
[2.7.2]: https://github.com/jcucci/dotnet-sherlock-mcp/releases/tag/v2.7.2
[2.7.1]: https://github.com/jcucci/dotnet-sherlock-mcp/releases/tag/v2.7.1
[2.7.0]: https://github.com/jcucci/dotnet-sherlock-mcp/releases/tag/v2.7.0


<a name="2.9.2"></a>
## [2.9.2](https://www.github.com/jcucci/dotnet-sherlock-mcp/releases/tag/v2.9.2) (2026-06-11)

### Bug Fixes

* **ci:** remove invalid commitParser key from .versionize config ([b298257](https://www.github.com/jcucci/dotnet-sherlock-mcp/commit/b298257f73acca9d9a14ebc153de0b485b08cb9b))

### Performance Improvements

* **runtime:** shared assembly contexts, mtime-aware caching, parallel reverse lookup ([6d0b22c](https://www.github.com/jcucci/dotnet-sherlock-mcp/commit/6d0b22c3fcb165feb1cc00e0a19dd3651abaf450))

