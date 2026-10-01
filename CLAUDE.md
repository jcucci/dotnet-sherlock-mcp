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
Since the MCP server runs as a separate process, it cannot access the client's loaded assemblies. Instead, it provides 38 specialized tools for clients to:
1. **Discover assemblies** using multiple strategies (project analysis, class name search, file system scanning)
2. **Load and analyze** specific assemblies by path with efficient caching
3. **Deep introspection** of types, members, attributes, and XML documentation
4. **Performance optimization** through pagination, caching, and response size validation

### Tool Categories (38 Available)
- **Assembly Discovery & Analysis** (6 tools): `OpenAssembly`, `AnalyzeAssembly`, `GetAssemblyInfo`, `FindAssemblyByClassName`, `FindAssemblyByFileName`, `FindAssemblyByNugetPackage`
- **Type Introspection** (7 tools): `GetTypesFromAssembly`, `GetTypeInfo`, `GetTypeHierarchy`, `AnalyzeType` (deprecated), etc.
- **Member Analysis** (8 tools): `GetTypeMembers` (one tool for every member kind, filtered by `kinds`), `AnalyzeMethod`, and the deprecated `GetTypeMethods` / `GetTypeProperties` / `GetTypeFields` / `GetTypeEvents` / `GetTypeConstructors` / `GetAllTypeMembers`
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
- **Memory Efficiency**: Metadata-only inspection contexts shared across calls with LRU eviction

The server uses `Microsoft.Extensions.Hosting` with dependency injection and the `ModelContextProtocol` 2.1.0 (GA) package, which implements MCP specification revision `2026-07-28`. The server is stdio-only and exposes every tool unless started with the `core` tool profile (`--profile core` or `SHERLOCK_TOOL_PROFILE=core`), which `src/server/Shared/ToolProfile.cs` resolves and `Program.cs` applies by pruning `McpServerOptions.ToolCollection` in a `PostConfigure`; it advertises behavioural annotations (`readOnlyHint`, `destructiveHint`, `openWorldHint`, `idempotentHint`) on every tool and caching hints (`ttlMs`, `cacheScope`) on `tools/list`, `resources/templates/list` and `resources/read`. Tool payloads are compact JSON serialized with `JsonHelpers.DefaultOptions`; the core browsing tools declare `UseStructuredContent` with an `OutputSchemaType` of `ToolEnvelope<…Data>` from `src/server/Schemas` (schema-only records whose property names are pinned to the wire names), and the call-tool filter in `src/server/Shared/StructuredOutput.cs` copies the envelope text into `structuredContent` for those tools on successful results; `StructuredOutputTests` validates each tool's real output against its schema. Scanning tools take the request's `CancellationToken` (bound by the SDK, not part of the schema) and must throw `OperationCanceledException` rather than return partial results, so cancelled work is never cached; tool catch-alls filter it with `when (ex is not OperationCanceledException)`. Multi-assembly scans and `find_assembly_by_class_name` report `notifications/progress` through `IProgress<ScanProgress>` (`src/runtime/ScanProgress.cs`), adapted to MCP by `src/server/Shared/ProgressAdapter.cs`. Resource templates (`sherlock://assembly/{path}/type/{fullName}`, `sherlock://assembly/{path}/docs/{memberId}`, `sherlock://nuget/{packageId}/{version}`) live in `src/server/Resources`; their variables are completed through `completion/complete` by `src/server/Completions/CompletionHandler.cs`, which delegates to `ICompletionService` in `src/runtime/Completions` (MCP can't complete tool arguments, only template variables and prompt arguments). Workflow prompts (`explore_package`, `explain_type`, `who_calls`) live in `src/server/Prompts`, reference only `core`-profile tools (enforced by `WorkflowPromptsTests`), and share their names and argument names with `CompletionHandler` through `PromptNames`; `search_members`, `get_types_from_assembly` and the `find_*` tools return `resource_link` blocks to the type resource alongside their JSON text. Single-type lookups go through `TypeNameResolver` (`src/runtime/TypeNameResolver.cs`), which reports ambiguity as `AmbiguousTypeNameException` instead of taking the first match. Tools take an optional SDK-bound `RequestContext<CallToolRequestParams>` and use `src/server/Shared/Elicitation.cs`: when the client supports MRTR and elicitation they throw `InputRequiredException` (a single-select form keyed `typeName` or `tfm`) and read the answer from `InputResponses` on the retried call; otherwise they return an `AmbiguousTypeName` error with the candidates. Ambiguity must propagate out of `ToolMiddleware.Execute` so the prompt is never cached, and the chosen name is substituted before the cache key is built. Every tool that takes `assemblyPath` also accepts `assemblyHandle`, an `asm_…` id minted by `open_assembly`: `AssemblyHandleRegistry` (`src/runtime/Handles`) derives it from the paths and file stamps of the assembly and its `additionalAssemblies` and persists it to `handles.json` under `RuntimeOptions.StateDirectory` (`SHERLOCK_STATE_DIR`, default `LocalApplicationData/sherlock`), so handles survive restarts. Every assembly- and project-backed tool (all but the `find_assembly_by_*`, config and handle tools) runs its work inside `ToolMiddleware.Execute`/`ExecuteAsync` and takes `noCache`; keys are built with `CacheKeyHelper.Build` over `FileStamp`/`ScopeStamp` (plus `XmlDocStamp` for the `.xml` sidecar and `ProjectStamp` for `obj/project.assets.json`), and argument validation stays outside the cached lambda. Tools resolve the handle or path through `AssemblyScope.ResolveTarget` before building cache keys, which therefore stay keyed by path and stamp; a rebuilt file makes the handle `StaleAssemblyHandle`.

## .NET Type Analysis (Sherlock MCP)

This project uses Sherlock MCP for comprehensive .NET assembly analysis. When working with .NET code:

> **Tool names:** the MCP client exposes these tools in `snake_case`, so the names you call are `get_type_members`, `search_members`, `find_references_to`, etc. The PascalCase names below (`GetTypeMembers`, `SearchMembers`, …) match the C# methods and the tool descriptions — map them to snake_case when invoking.

### Token-efficient by default
The enumerating tools return a lean **`summary`** payload by default (e.g. `GetTypeMembers` returns `{ kind, name, signature }` — the C# signature already carries the return type, parameters, and modifiers). Only pass `projection='full'` when you need structured access to parameters, attributes, or modifier flags, and ideally only for the specific items you've already narrowed to. Prefer filtered, paginated `GetTypeMembers` calls (narrow with `kinds` / `nameContains`) over the deprecated `GetAllTypeMembers`/`AnalyzeType` — those return everything at once and can blow the token budget. Tools carrying `projection`: `GetTypesFromAssembly`, `GetTypeMembers`, `GetTypeMethods`, `GetAssemblyInfo`, `GetMethodCalls`, `FindImplementationsOf`, `FindMethodsReturning`, `FindExtensionMethodsFor`, `FindReferencesTo`.

### Discovery & Initial Analysis
1. **Find the DLL**: `FindAssemblyByClassName` / `FindAssemblyByFileName` when you know a name but not the path; `FindAssemblyByNugetPackage` to resolve a package from the NuGet cache; `GetProjectOutputPaths` from a project file. Don't hardcode `./bin/Debug/net9.0/...` — the target framework varies.
2. **Orient cheaply**: `GetAssemblyInfo` for identity/target-framework/references (lightweight); `AnalyzeAssembly` when you want the type list with counts.
3. **Find a member without knowing its type**: `SearchMembers` searches a whole assembly by name fragment (filter with `memberKinds`).
4. **Project structure**: `AnalyzeProject` and `AnalyzeSolution` for build configuration.

### Type Analysis Workflow
1. **List types**: `GetTypesFromAssembly` (paginated, summary) to discover available types.
2. **Type details**: `GetTypeInfo` for metadata, inheritance, accessibility, and member counts (lightweight).
3. **Members**: filtered `GetTypeMembers` (use `kinds` / `nameContains` / `hasAttributeContains`); re-call with `projection='full'` for the specific members you need. The per-kind `GetType{Methods,Properties,Fields,Events,Constructors}` tools are deprecated.
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
3. GetTypeMembers (filtered by kinds/nameContains) → Narrow, then projection='full' for detail
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
- **Stale results**: pass `noCache=true` to bypass the response cache for a single call — e.g. after adding a missing dependency DLL next to an assembly, or restoring a project whose `project.assets.json` isn't under `obj/` (cache keys don't stamp those files).
- **Repeated calls on one assembly**: `OpenAssembly` once, then pass `assemblyHandle` instead of `assemblyPath` (it also carries `additionalAssemblies`). On `StaleAssemblyHandle` (the assembly was rebuilt) or `UnknownAssemblyHandle`, open it again.

### Error Handling & Troubleshooting
- Error results carry `isError: true`; the JSON payload's `suggestion`, `alternativeTools` and `recommendedParams` say how to fix the call.
- If `TypeNotFound` / `MemberNotFound`: retry with one of the near-miss names in `recommendedParams.candidates`, or use `GetTypesFromAssembly` / `SearchMembers` to find it first.
- If `InvalidAssembly`: the path is a native or non-.NET file. If `DependencyNotFound`: point at the assembly's build-output copy so its dependencies sit beside it.
- If `AmbiguousTypeName`: the simple name matched several types; retry with one of the full names in `recommendedParams.candidates`.
- If response too large: reduce `maxItems`, keep `projection='summary'`, and page with `continuationToken`.
- For nested types: use format `OuterType+InnerType` or `GetNestedTypes`.
- Missing assembly: use `FindAssemblyByClassName` / `FindAssemblyByNugetPackage` or check build output paths via `GetProjectOutputPaths`.
