# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a comprehensive .NET MCP (Model Context Protocol) server called "Sherlock MCP" that provides deep introspection capabilities for .NET assemblies. It uses advanced reflection techniques to give LLMs precise knowledge about .NET types, members, attributes, and documentation. The project includes a production-ready server, extensive runtime libraries, comprehensive testing, and performance optimizations.

## Project Structure

- `src/Sherlock.MCP.slnx` - Visual Studio solution file (XML format)
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
Since the MCP server runs as a separate process, it cannot access the client's loaded assemblies. Instead, it provides 36 specialized tools for clients to:
1. **Discover assemblies** using multiple strategies (project analysis, class name search, file system scanning)
2. **Load and analyze** specific assemblies by path with efficient caching
3. **Deep introspection** of types, members, attributes, and XML documentation
4. **Performance optimization** through pagination, streaming, and response size validation

### Tool Categories (36 Available)
- **Assembly Discovery & Analysis** (5 tools): `AnalyzeAssembly`, `GetAssemblyInfo`, `FindAssemblyByClassName`, `FindAssemblyByFileName`, `FindAssemblyByNugetPackage`
- **Type Introspection** (7 tools): `GetTypesFromAssembly`, `GetTypeInfo`, `GetTypeHierarchy`, `AnalyzeType`, etc.
- **Member Analysis** (7 tools): `GetTypeMethods`, `GetTypeProperties`, `GetAllTypeMembers`, `AnalyzeMethod`, etc.
- **Member Search** (1 tool): `SearchMembers`
- **Reverse Lookup & IL Analysis** (5 tools): `FindImplementationsOf`, `FindMethodsReturning`, `FindExtensionMethodsFor`, `FindReferencesTo` (supports `analysisDepth='il'` for inbound callers), `GetMethodCalls` (outbound IL call/field analysis)
- **Attributes & Metadata** (2 tools): `GetMemberAttributes`, `GetParameterAttributes`
- **XML Documentation** (2 tools): `GetXmlDocsForType`, `GetXmlDocsForMember`
- **Project Analysis** (5 tools): `AnalyzeSolution`, `AnalyzeProject`, `ResolvePackageReferences`, etc.
- **Configuration** (2 tools): `GetRuntimeOptions`, `UpdateRuntimeOptions`

### Performance & Scalability Features
- **Smart Pagination**: Token-based continuation for large result sets
- **Response Size Validation**: Prevents oversized responses that could hit token limits
- **Caching Layer**: Configurable TTL-based caching for expensive operations
- **Memory Efficiency**: Streaming and chunked processing for large assemblies

The server uses `Microsoft.Extensions.Hosting` with dependency injection and the `ModelContextProtocol` 1.4.0 (GA) package.

## .NET Type Analysis (Sherlock MCP)

This project uses Sherlock MCP for comprehensive .NET assembly analysis. When working with .NET code:

> **Tool names:** the MCP client exposes these tools in `snake_case`, so the names you call are `get_type_methods`, `search_members`, `find_references_to`, etc. The PascalCase names below (`GetTypeMethods`, `SearchMembers`, …) match the C# methods and the tool descriptions — map them to snake_case when invoking.

### Token-efficient by default
The enumerating tools return a lean **`summary`** payload by default (e.g. `GetTypeMethods` returns `{ name, signature }` — the C# signature already carries the return type, parameters, and modifiers). Only pass `projection='full'` when you need structured access to parameters, attributes, or modifier flags, and ideally only for the specific items you've already narrowed to. Prefer filtered, paginated `GetType*` calls over `GetAllTypeMembers`/`AnalyzeType` on large types — those return everything at once and can blow the token budget. Tools carrying `projection`: `GetTypesFromAssembly`, `GetTypeMethods`, `GetAssemblyInfo`, `GetMethodCalls`, `FindImplementationsOf`, `FindMethodsReturning`, `FindExtensionMethodsFor`, `FindReferencesTo`.

### Discovery & Initial Analysis
1. **Find the DLL**: `FindAssemblyByClassName` / `FindAssemblyByFileName` when you know a name but not the path; `FindAssemblyByNugetPackage` to resolve a package from the NuGet cache; `GetProjectOutputPaths` from a project file. Don't hardcode `./bin/Debug/net9.0/...` — the target framework varies.
2. **Orient cheaply**: `GetAssemblyInfo` for identity/target-framework/references (lightweight); `AnalyzeAssembly` when you want the type list with counts.
3. **Find a member without knowing its type**: `SearchMembers` searches a whole assembly by name fragment (filter with `memberKinds`).
4. **Project structure**: `AnalyzeProject` and `AnalyzeSolution` for build configuration.

### Type Analysis Workflow
1. **List types**: `GetTypesFromAssembly` (paginated, summary) to discover available types.
2. **Type details**: `GetTypeInfo` for metadata, inheritance, accessibility, and member counts (lightweight).
3. **Members**: filtered `GetTypeMethods` / `GetTypeProperties` / `GetTypeFields` / `GetTypeEvents` / `GetTypeConstructors` (use `nameContains` / `hasAttributeContains`); re-call with `projection='full'` for the specific members you need.
4. **Specialized queries**:
   - `GetTypeHierarchy` for inheritance chains and interfaces — pass `additionalAssemblies` to populate `derivedTypes` (otherwise it returns `null` with a note).
   - `GetGenericTypeInfo` for generic type parameters and constraints.
   - `GetNestedTypes` for inner type declarations.
   - `AnalyzeMethod` for a single method's overloads, parameters, and attributes.
   - `GetXmlDocsForType` / `GetXmlDocsForMember` for extracted XML documentation.

### Relationships & Call Analysis
- **Who implements / derives**: `FindImplementationsOf` (open-generic match supported).
- **What returns a type**: `FindMethodsReturning`.
- **Extension methods for a type**: `FindExtensionMethodsFor`.
- **Where a type is used**: `FindReferencesTo`; add `analysisDepth='il'` to also resolve inbound callers from method bodies.
- **What a method calls**: `GetMethodCalls` reads the IL body to list calls and field accesses (use `.ctor`/`.cctor` for constructors).
- Reverse-lookup tools accept `additionalAssemblies` to widen the search scope across multiple DLLs.

### Automatic Type Analysis

**IMPORTANT**: When working with .NET code and needing to understand type structures, interfaces, or assembly details, Claude should proactively use the Sherlock MCP server tools without explicitly asking the user.

### Common Patterns & Examples

**Discovering unknown types:**
```
1. GetTypesFromAssembly → Browse types (summary)
2. GetTypeInfo → Basic metadata and member counts
3. GetTypeMethods/Properties (filtered) → Narrow, then projection='full' for detail
```

**Find a member when you don't know the type:**
```
1. SearchMembers nameContains="Parse" memberKinds="method" → Locate the declaring type
2. GetTypeInfo → Confirm the type
3. AnalyzeMethod → Inspect overloads and parameters
```

**Understanding inheritance & usage:**
```
1. GetTypeHierarchy (+ additionalAssemblies) → Inheritance chain, interfaces, derived types
2. FindImplementationsOf → Concrete implementers
3. FindReferencesTo analysisDepth='il' → Inbound callers
```

**Project exploration:**
```
1. AnalyzeProject → Build configuration and dependencies
2. GetProjectOutputPaths → Find compiled assemblies
3. GetAssemblyInfo / AnalyzeAssembly → Orient on the assembly
```

### Best Practices
- **Type names**: Prefer full names (`Namespace.Type`) for accuracy.
- **Start lean**: small `maxItems` + summary projection first; widen `maxItems` or switch to `projection='full'` only when needed. Use `continuationToken` to page large result sets.
- **Filtering beats fetching**: `nameContains` / `hasAttributeContains` / `memberKinds` rather than retrieving all members.
- **Stale results**: pass `noCache=true` to bypass the response cache for a single call.

### Error Handling & Troubleshooting
- If `TypeNotFound`: try the simple name instead of the full name, or `GetTypesFromAssembly` / `SearchMembers` to find it first.
- If response too large: reduce `maxItems`, keep `projection='summary'`, and page with `continuationToken`.
- For nested types: use format `OuterType+InnerType` or `GetNestedTypes`.
- Missing assembly: use `FindAssemblyByClassName` / `FindAssemblyByNugetPackage` or check build output paths via `GetProjectOutputPaths`.
