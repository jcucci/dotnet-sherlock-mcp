# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- API diff and breaking-change detection. `compare_api_surface(left, right)` compares the public API of two versions of an assembly, where each side is an assembly path, an `asm_…` handle, or a NuGet package as `Package.Id@1.2.3` resolved from the local NuGet cache (the version may be omitted for the highest cached one). For packages, `tfm` picks the target framework; by default both sides are compared on the best target framework both packages ship, and `warnings` says so when they share none. Public types and members, plus protected members of inheritable types, are compared, and each added, removed or changed type or member carries `breaking` and its reasons: removed types or members, changed return/field/property types, reduced accessibility, classes that became sealed, abstract or static, removed base types or interfaces, new generic constraints, abstract members added to interfaces or inheritable classes, members that became static/instance, abstract or non-virtual, fields that became readonly or changed const-ness, removed or less accessible property accessors, setters that became `init`, changed parameter modifiers, parameters that are no longer optional or `params`, and changed enum values. Parameter renames, changed default values, changed constants, new `params` and members that moved to a base type (still inherited) are reported as non-breaking; generic constraints are matched by position, so renaming a type parameter is not a change. The default `summary` projection returns `counts` and the breaking changes; `projection='full'` lists every change with `leftSignature`, `rightSignature` and `reasons[]`, filterable with `breakingOnly` and `namespaceFilter`. Results are paged (`maxItems`, default 100, and `continuationToken`) and cached by both files' stamps. Types whose members can't be read because a dependency is missing are listed in `warnings` instead of being reported as removed. The tool is in the `core` profile, which now has 22 tools, and publishes an `outputSchema`. (#72)
- Original source via portable PDBs and Source Link. `get_member_source(typeName, memberName, parameterTypes?)` takes the same arguments as `decompile_member` and returns each overload's original source, with comments and real names, sliced to the member's `///` docs, attributes, signature and body. It reads the PDB embedded in the assembly or beside it and tries, in order, source embedded in the PDB, the local file the assembly was built from, and the file at the PDB's Source Link URL. Every candidate, embedded source included, must match the SHA-1/SHA-256 checksum recorded in the PDB (content that differs only in CRLF/LF line endings is accepted); overloads without a match, fields, and assemblies without a portable PDB fall back to decompiled C#. The result's `origin` is `embedded`, `local`, `sourcelink`, `decompiled` or `mixed`, each overload carries `document`, `url` and `lines`, and `note` says why a fallback happened. Fetching is on by default but limited to HTTPS on known source hosts (GitHub raw, GitLab, Bitbucket, Azure DevOps); `SHERLOCK_SOURCE_FETCH` (`known-hosts`, `any`, `off`) and the new `sourceFetch`, `addSourceFetchHosts` and `removeSourceFetchHosts` options of `update_runtime_options` change that. A host gets 5 seconds to respond and 30 seconds to deliver the file, downloads are capped at 5 MB and cached on disk under `SHERLOCK_STATE_DIR/sources` once verified, a host that couldn't be connected to is not retried for the rest of the session, and decompiled fallbacks caused by network failures are not cached. Redirects are followed manually, at most 3 hops, and each hop must pass the same HTTPS and host checks. A local file is read only when the PDB records its checksum, from an absolute, non-UNC path, and only up to 5 MB. The tool is in the `core` profile, which now has 21 tools, publishes an `outputSchema`, and is marked `openWorldHint: true` because it can reach the network. (#71)
- Decompilation to C# through ICSharpCode.Decompiler. `decompile_member(typeName, memberName, parameterTypes?)` returns the source of every overload of a method, property, field, event or constructor (`.ctor` / `.cctor`), or of the single overload whose parameters match `parameterTypes` (e.g. `string,int`; unmatched lists fail with `OverloadNotFound` and the available signatures). `decompile_type(typeName)` decompiles a whole type. Both page the source by line with `maxLines` (default 400, an upper bound; a page also stops at about 90,000 characters) and `continuationToken`, take `additionalAssemblies` (merged with an `assemblyHandle`'s) to resolve dependencies outside the assembly's folder, report `startLine`, `lineCount`, `totalLines` and `truncated`, and cache the full decompilation by file stamp. Lines longer than 2,000 characters (large array initializers, long constants) are clipped with a marker and counted in `clippedLines`, so every page fits the response limit. A type that the given assembly only type-forwards fails with `TypeForwarded`, naming the assembly that defines it. `decompile_member` is in the `core` profile, which now has 20 tools, and publishes an `outputSchema`; `decompile_type` is full-profile only. (#70)
- Workflow prompts. `explore_package` (`packageId`, `version?`), `explain_type` (`assemblyPath`, `typeName`) and `who_calls` (`assemblyPath`, `typeName`, `memberName`, `additionalAssemblies?`) each return a user message that walks the agent through the right tool sequence, using only `core`-profile tools. Their `assemblyPath`, `typeName`, `packageId` and `version` arguments support `completion/complete`, and `prompts/list` carries public caching hints. An `upgrade_check` prompt is deferred until the API-diff tool (#72) exists. (#69)
- Assembly handles. `open_assembly(assemblyPath, additionalAssemblies?)` returns `{ handle, name, version, targetFramework, assemblyPath, additionalAssemblies }`, where `handle` is a short `asm_…` id, and every tool that took `assemblyPath` now also accepts `assemblyHandle` instead (pass exactly one; explicit `additionalAssemblies` are added to the handle's). The id is derived from the paths and file stamps of the assembly and its additional assemblies, so reopening the same build returns the same handle, and handles are persisted to `handles.json` under `SHERLOCK_STATE_DIR` (default: the user's local app-data `sherlock` folder) so they survive server restarts. A handle whose files were rebuilt, moved or deleted fails with `StaleAssemblyHandle`, and one that was never issued or has been evicted fails with `UnknownAssemblyHandle`; both point back at `open_assembly`, and when the files were rebuilt in place the stale error's `recommendedParams` carry the paths to reopen; when a file was moved or deleted it lists `details.missingFiles` and points at the assembly-discovery tools instead. The registry keeps the 256 most recently used handles (`maxAssemblyHandles` in `update_runtime_options`). `open_assembly` is in the `core` profile, which now has 19 tools, and publishes an `outputSchema`. (#68)

### Changed

- `assemblyPath` is no longer a required argument on the 27 tools that take it, because `assemblyHandle` can be passed instead; calling with neither returns `InvalidArgument`. In the C# tool methods, `assemblyPath` and `assemblyHandle` now follow the required parameters such as `typeName`, so code that calls the methods with positional arguments has to switch to named ones. MCP clients pass arguments by name and are unaffected.

### Removed

- The unused `AssemblyValidator` helper.

### Fixed

- The first launch of a new version through the Claude Code plugin no longer fails to connect. On a fresh download `dnx` printed "Skipping NuGet package signature verification." to stdout, corrupting the MCP stream, so the client dropped the connection until a manual reconnect. The plugin now runs `dnx -v q --yes Sherlock.MCP.Server@<version>` (options before the package id, since later arguments are forwarded to the server), and the README's `dnx` examples do the same.

## [2.14.0] - 2026-10-01

### Added

- A Claude Code plugin. This repository is now a plugin marketplace (`/plugin marketplace add jcucci/dotnet-sherlock-mcp`, then `/plugin install sherlock@dotnet-sherlock-mcp`). The `sherlock` plugin launches the server through `dnx` with the `core` tool profile (override with `SHERLOCK_TOOL_PROFILE`) and bundles a skill that teaches the locate → orient → drill-in → relationships workflow, replacing the CLAUDE.md snippet users previously copied. The README documents per-user and team (`.claude/settings.json`) setup, and the release workflow now checks that the plugin's versions and `dnx` pin match the tag. (#62)
- `get_type_members` lists a type's methods, properties, fields, events and constructors in one paginated call. `kinds` (csv: `method|property|field|event|constructor`) narrows the kinds, alongside the usual `nameContains` / `hasAttributeContains` / sort / paging parameters. Members are grouped by kind and sorted within each kind, and `countsByKind` reports the per-kind totals. The default `summary` projection returns `{ kind, name, signature }`, and `projection='full'` adds each kind's structured fields (constructors now also carry `name` and `attributes`). The tool publishes an `outputSchema` and returns `structuredContent`. Error guidance (`MemberNotFound`, `MethodNotFound`, oversized responses) now points at `get_type_members`. (#60)
- Tool profiles: start the server with `--profile core` or `SHERLOCK_TOOL_PROFILE=core` to expose an 18-tool core set (discovery, orientation, `get_type_members`, `search_members`, `analyze_method`, XML docs, `find_implementations_of`, `find_methods_returning`, `find_extension_methods_for`, `find_references_to`, `get_method_calls`) instead of all 37. `full` stays the default, and an unknown profile stops the server with an error. `server.json` declares the environment variable. (#60)
- The NuGet package is now published with the `McpServer` package type and embeds `server.json` as `.mcp/server.json`, so NuGet.org lists it as an MCP server. The README documents launching it without installing anything via the .NET 10 SDK's `dnx` (`dnx Sherlock.MCP.Server@<version> --yes`), with Claude Code and VS Code config examples. The release workflow now checks that the tag, both `server.json` versions and the project version all match, and that the packed package carries the `McpServer` type and `.mcp/server.json`. (#63)
- Long-running tools can now be cancelled and report progress. Every scanning tool takes the request's `CancellationToken`, so a client's `notifications/cancelled` stops reverse lookups, IL inbound-caller scans, `search_members`, `get_method_calls`, the assembly locators and project analysis part-way through. A cancelled call is never cached and is no longer reported as an `InternalError` payload. When the client sends a `progressToken`, `find_references_to` (both phases of `analysisDepth='il'` on one scale), `find_implementations_of`, `find_methods_returning`, `find_extension_methods_for`, `get_type_hierarchy` (with `additionalAssemblies`) and `find_assembly_by_class_name` emit `notifications/progress` per assembly or file scanned, throttled to about 100 updates. The `io.modelcontextprotocol/tasks` extension was evaluated but is not available in `ModelContextProtocol` 2.1.0. (#66)
- Ambiguous inputs are resolved through elicitation (MRTR `input_required`, spec 2026-07-28). A simple type name that matches several types (e.g. `Widget` in two namespaces) used to resolve silently to the first match; every single-type tool (`get_type_info`, `get_type_hierarchy`, `get_generic_type_info`, `get_type_attributes`, `get_nested_types`, the `get_type_*` member tools, `get_all_type_members`, `get_member_attributes`, `get_parameter_attributes`, `analyze_type`, `analyze_method`, `get_method_calls` and the XML-doc tools) now asks the client which full name was meant. Clients that cannot elicit get an `AmbiguousTypeName` error whose `recommendedParams.candidates` lists the full names; the type resource template returns `InvalidParams` with the candidates. `find_assembly_by_nuget_package` asks which target framework to inspect when `tfm` is omitted and the package ships several (other clients keep the automatic pick), its success payload now includes `availableVersions` and `availableTfms`, and TFM folders without a DLL (e.g. `_._` placeholders) are no longer considered. The duplicated type-name lookups now share one resolver, `TypeNameResolver`. (#67)
- Tool errors are now flagged with MCP's `isError: true` (set centrally by a `tools/call` filter, so it also applies to cached responses) and carry guidance. `TypeNotFound` and `MemberNotFound` list the closest names in `recommendedParams.candidates` ("did you mean"), `AssemblyNotFound` points at the assembly-discovery tools and lists similarly named assemblies in the same folder, and common exceptions map to specific codes instead of `InternalError`: `InvalidAssembly` (`BadImageFormatException`), `DependencyNotFound` (unloadable reference), `AccessDenied`, `InvalidArgument`, and `ProjectNotFound` / `FileNotFound` for the project tools. `DependencyResolutionFailed` guidance now applies to every tool, not only `get_types_from_assembly`, and a type lookup that fails while the assembly has unresolved dependencies reports them instead of a bare `TypeNotFound`. (#59)

### Deprecated

- `get_type_methods`, `get_type_properties`, `get_type_fields`, `get_type_events`, `get_type_constructors`, `get_all_type_members` and `analyze_type` still work unchanged, but their descriptions now point at `get_type_members` (and `get_type_info` for type metadata). They are left out of the `core` profile and will be removed in a future release. (#60)

### Removed

- The no-op `enableStreaming` option is gone from `get_runtime_options` and `update_runtime_options`. Unused internals were removed along with it: the `forceRuntimeLoad` isolated load-context path, the placeholder assembly-index service, and `TypeAnalysisService.LoadAssembly` with its never-released pinned leases. (#83)

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

