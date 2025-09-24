# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a comprehensive .NET MCP (Model Context Protocol) server called "Sherlock MCP" that provides deep introspection capabilities for .NET assemblies. It uses advanced reflection techniques to give LLMs precise knowledge about .NET types, members, attributes, and documentation. The project includes a production-ready server, extensive runtime libraries, comprehensive testing, and performance optimizations.

## Project Structure

- `src/Sherlock.MCP.sln` - Visual Studio solution file
- `src/server/Sherlock.MCP.Server.csproj` - Main MCP server application
- `src/runtime/Sherlock.MCP.Runtime.csproj` - Core reflection and assembly discovery services
- `src/unit-tests/Sherlock.MCP.Tests.csproj` - Unit tests for all functionality

## Development Commands

- `dotnet build` - Build the entire solution
- `dotnet run --project src/server` - Run the MCP server with stdio transport
- `dotnet test` - Run all unit tests
- `dotnet restore` - Restore NuGet packages for all projects

## Code Style Guidelines

1. **Single-line blocks**: If the block of code is only one line and readability is not impaired, forego the braces.
2. **No inline comments**: No need to comment what the code is doing in the code. The naming conventions should be descriptive enough.
3. **Use expression methods**: Prefer expression methods when possible
3. **Use named parameters**: Use named parameters when the parameter values might be confusing

## Architecture Notes

This MCP server provides LLMs with comprehensive .NET reflection capabilities through several key architectural principles:

### Assembly-First Discovery
Since the MCP server runs as a separate process, it cannot access the client's loaded assemblies. Instead, it provides 28+ specialized tools for clients to:
1. **Discover assemblies** using multiple strategies (project analysis, class name search, file system scanning)
2. **Load and analyze** specific assemblies by path with efficient caching
3. **Deep introspection** of types, members, attributes, and XML documentation
4. **Performance optimization** through pagination, streaming, and response size validation

### Tool Categories (28+ Available)
- **Assembly Discovery & Analysis** (3 tools): `AnalyzeAssembly`, `FindAssemblyByClassName`, `FindAssemblyByFileName`
- **Type Introspection** (7 tools): `GetTypesFromAssembly`, `GetTypeInfo`, `GetTypeHierarchy`, etc.
- **Member Analysis** (8 tools): `GetTypeMethods`, `GetTypeProperties`, `GetAllTypeMembers`, etc.
- **Attributes & Metadata** (2 tools): `GetMemberAttributes`, `GetParameterAttributes`
- **XML Documentation** (2 tools): `GetXmlDocsForType`, `GetXmlDocsForMember`
- **Project Analysis** (5 tools): `AnalyzeSolution`, `AnalyzeProject`, `ResolvePackageReferences`, etc.
- **Configuration** (2 tools): `GetRuntimeOptions`, `UpdateRuntimeOptions`

### Performance & Scalability Features
- **Smart Pagination**: Token-based continuation for large result sets
- **Response Size Validation**: Prevents oversized responses that could hit token limits
- **Caching Layer**: Configurable TTL-based caching for expensive operations
- **Memory Efficiency**: Streaming and chunked processing for large assemblies

The server uses `Microsoft.Extensions.Hosting` with dependency injection and the latest `ModelContextProtocol.Server` package.

## .NET Type Analysis (Sherlock MCP)

This project uses Sherlock MCP for comprehensive .NET assembly analysis. When working with .NET code:

### Discovery & Initial Analysis
1. **Find assemblies**: Use `FindAssemblyByClassName` or `FindAssemblyByFileName` when you need to locate DLLs
2. **Assembly overview**: Start with `AnalyzeAssembly` to get all types and metadata
3. **Project structure**: Use `AnalyzeProject` and `AnalyzeSolution` for build configuration analysis

### Type Analysis Workflow
1. **List types**: `GetTypesFromAssembly` (paginated) to discover available types
2. **Type details**: `GetTypeInfo` for basic metadata, inheritance, and accessibility
3. **Deep analysis**: `AnalyzeType` for comprehensive member overview
4. **Specialized queries**:
   - `GetTypeHierarchy` for inheritance chains
   - `GetGenericTypeInfo` for generic type parameters
   - `GetNestedTypes` for inner type declarations

### Member Analysis (Use appropriate tool for your needs)
- **All members**: `GetAllTypeMembers` for comprehensive view
- **Specific categories**: `GetTypeMethods`, `GetTypeProperties`, `GetTypeFields`, `GetTypeEvents`, `GetTypeConstructors`
- **Method details**: `AnalyzeMethod` for overload analysis with parameters and attributes
- **Documentation**: `GetXmlDocsForMember` for extracted XML documentation

### Best Practices
- **Assembly paths**: Typically `./bin/Debug/net9.0/ProjectName.dll` or use discovery tools
- **Type names**: Prefer full names (`Namespace.Type`) for accuracy
- **Pagination**: Use `maxItems=50` and `continuationToken` for large results
- **Filtering**: Leverage `nameContains`, `hasAttributeContains` for targeted queries
- **Performance**: Check response sizes and use appropriate pagination for large types

### Automatic Type Analysis

**IMPORTANT**: When working with .NET code and needing to understand type structures, interfaces, or assembly details, Claude should proactively use the Sherlock MCP server tools without explicitly asking the user.

### Common Patterns & Examples

**Discovering unknown types:**
```
1. GetTypesFromAssembly → Find types by name pattern
2. GetTypeInfo → Get basic metadata and structure
3. GetTypeMethods/Properties → Analyze specific members
```

**Understanding inheritance:**
```
1. GetTypeInfo → Basic type information
2. GetTypeHierarchy → Full inheritance chain and interfaces
3. GetGenericTypeInfo → Generic constraints and parameters (if applicable)
```

**Finding specific functionality:**
```
1. GetTypeMethods with nameContains="Parse" → Find parsing methods
2. GetMemberAttributes → Check for specific attributes like [Obsolete]
3. GetXmlDocsForMember → Get documentation for found methods
```

**Project exploration:**
```
1. AnalyzeProject → Understand build configuration and dependencies
2. GetProjectOutputPaths → Find compiled assemblies
3. AnalyzeAssembly → Get overview of available types
```

### Error Handling & Troubleshooting
- If `TypeNotFound`: Try simple name instead of full name, or use `GetTypesFromAssembly` first
- If response too large: Use pagination (`maxItems=25`, `skip`, `continuationToken`)
- For nested types: Use format `OuterType+InnerType` or search via `GetNestedTypes`
- Missing assembly: Use `FindAssemblyByClassName` or check build output paths

### Performance Tips
- Cache expensive calls when possible using the built-in caching layer
- Use targeted filtering rather than retrieving all members
- Leverage continuation tokens for exploring large result sets
- Check `totalCount` fields to understand data size before requesting all results
