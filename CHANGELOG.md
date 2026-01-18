# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[2.7.0]: https://github.com/jcucci/dotnet-sherlock-mcp/releases/tag/v2.7.0
