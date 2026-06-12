# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

