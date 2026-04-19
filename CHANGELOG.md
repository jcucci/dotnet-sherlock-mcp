# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[2.9.0]: https://github.com/jcucci/dotnet-sherlock-mcp/releases/tag/v2.9.0
[2.7.2]: https://github.com/jcucci/dotnet-sherlock-mcp/releases/tag/v2.7.2
[2.7.1]: https://github.com/jcucci/dotnet-sherlock-mcp/releases/tag/v2.7.1
[2.7.0]: https://github.com/jcucci/dotnet-sherlock-mcp/releases/tag/v2.7.0
