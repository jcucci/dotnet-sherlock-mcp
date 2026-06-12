# Sherlock MCP for .NET

**Sherlock MCP for .NET** is a comprehensive Model Context Protocol (MCP) server that provides deep introspection capabilities for .NET assemblies. It enables Language Learning Models (LLMs) to analyze and understand your .NET code with precision, delivering accurate and context-aware responses for complex development scenarios.

This tool is essential for developers who want to harness LLM capabilities for:

*   **Deep codebase analysis** - Understanding complex .NET architectures and dependencies
*   **Precise type information** - Getting detailed metadata about types, members, and their signatures
*   **Automated documentation** - Extracting and utilizing XML documentation and attributes
*   **Custom tooling** - Building sophisticated tools that interact with .NET assemblies
*   **Code generation** - Creating accurate code based on existing type structures

## Key Features

*   **Comprehensive MCP Server**: Provides 36 specialized tools for .NET assembly analysis
*   **Advanced Assembly Introspection**: Deep reflection-based analysis of types, members, and metadata
*   **Rich Member Analysis**: Detailed inspection of methods, properties, fields, events, and constructors
*   **Smart Filtering & Pagination**: Advanced filtering by name/attributes with efficient pagination for large datasets
*   **XML Documentation Integration**: Automatic extraction of summary, parameters, returns, and remarks
*   **Performance Optimized**: Caching, streaming, and memory-efficient processing
*   **Stable JSON API**: Consistent envelopes with versioning and structured error codes
*   **.NET 9.0 Native**: Built on the latest .NET platform with modern C# features
*   **Project Integration**: Solution and project file analysis with dependency resolution
*   **Current MCP SDK**: Built on `ModelContextProtocol` 1.4.0 (GA)

## What's New in 2.10.0

- **New tools**: `SearchMembers` (assembly-wide member search), `FindExtensionMethodsFor`, `GetMethodCalls` (IL-level "what does this method call?"), and `FindAssemblyByNugetPackage`.
- **Token-efficient projections**: enumerating tools now default to a lean `summary` and accept `projection='full'` for structured detail — see [Response Shape](#response-shape-token-efficiency).
- **IL analysis**: `FindReferencesTo` accepts `analysisDepth='il'` to resolve inbound callers from method bodies.
- **Wider scope**: `GetTypeHierarchy` and reverse-lookup tools accept `additionalAssemblies` (derived types are computed only when provided).
- **SDK upgrade**: `ModelContextProtocol` `0.3.0-preview.2` → `1.4.0` (GA). See `CHANGELOG.md` for full details.

## Installation

Install the global tool from NuGet (adds `sherlock-mcp` to your PATH):

```bash
dotnet tool install -g Sherlock.MCP.Server
```

Alternatively, during development you can run the server locally:

```bash
dotnet run --project src/server/Sherlock.MCP.Server.csproj
```

## Configure Your MCP Client

Sherlock runs as a standard MCP server that communicates over stdio.

- Cursor: Settings → MCP / Custom tools → Add tool → Command: `sherlock-mcp`
- Claude Desktop / other MCP clients: Add a server entry pointing to the `sherlock-mcp` command. Example JSON entry (refer to your client’s docs for exact file location/format):

```jsonc
{
  "servers": {
    "sherlock": {
      "command": "sherlock-mcp"
    }
  }
}
```

No arguments are required. The server self-registers all tools when launched.

## Auto-Configure for .NET Projects

**You usually don't need to paste anything.** Sherlock ships its usage guidance in the MCP `instructions` field returned at initialize, and most MCP clients (including Claude Code) surface that to the agent automatically — so the guidance stays correct and versioned with the package, with no copy-paste to maintain.

The snippets below are **optional reinforcement**. Keep them short and principle-based rather than enumerating tool names and workflows: a static list pasted into your repo will drift as Sherlock's tools evolve, whereas the tools' own descriptions (and the server `instructions`) always match the version you're running.

> Tool names are exposed in `snake_case` (`get_type_methods`, `search_members`, …); argument names stay camelCase (`projection`, `nameContains`).

### Claude Code (CLAUDE.md)

Optional — a short pointer in your project's `CLAUDE.md`:

```markdown
## .NET Assembly Analysis

Use the Sherlock MCP tools (`get_type_methods`, `search_members`, …) for .NET type/assembly
questions instead of guessing. Locate DLLs with the `find_assembly_by_*` / `get_project_output_paths`
tools rather than hardcoding bin paths. Start lean — `search_members` or `get_types_from_assembly`,
then drill in — and pass `projection='full'` only when you need parameters/attributes/modifiers.
The tools' own descriptions cover the specifics.
```

### Cursor (.cursor/rules)

The single-file `.cursorrules` format is **deprecated** (and silently ignored in Cursor's Agent mode).
Add a Project Rule at `.cursor/rules/sherlock.mdc` instead:

```mdc
---
description: Use Sherlock MCP for .NET assembly/type analysis
alwaysApply: true
---

- Prefer the Sherlock MCP tools (snake_case, e.g. `get_type_methods`, `search_members`) over guessing about .NET APIs.
- Find DLLs with `find_assembly_by_*` / `get_project_output_paths`; don't hardcode `bin/Debug/<tfm>/*.dll`.
- Start lean (`search_members` / `get_types_from_assembly`); request `projection='full'` only when you need parameters/attributes/modifiers.
```

### Other agents (AGENTS.md)

For tools that follow the cross-editor `AGENTS.md` convention, the same short pointer works — drop the Claude Code snippet above into your `AGENTS.md`.

### Global configuration

For system-wide usage, add to your global agent settings:

```text
For .NET work, use the Sherlock MCP tools (snake_case) to analyze assemblies, types, and members instead of guessing. Start lean and opt into projection='full' only when you need detail.
```

## How To Prompt It

Below are compact prompt snippets you can paste into your chat to get productive fast. Adjust paths to your local DLLs.

General setup

```text
You have access to an MCP server named "sherlock" that can analyze .NET assemblies. Prefer these tools for .NET questions and include short reasoning for which tool you chose. Ask me for the assembly path if missing.
```

Enumerate members for a type

```text
Analyze: /absolute/path/to/MyLib/bin/Debug/net9.0/MyLib.dll
Type: MyNamespace.MyType
List methods, including non-public, filter name contains "Async", include attributes, return JSON.
```

Get XML docs for a member

```text
Use GetXmlDocsForMember on /abs/path/MyLib.dll, type MyNamespace.MyType, member TryParse. Summarize the summary + params.
```

Find types and drill in

```text
List types from /abs/path/MyLib.dll; then get type info for the first result and list its nested types.
```

Tune paging and filters

```text
Use GetTypeMethods on /abs/path/MyLib.dll, type MyNamespace.MyType, sortBy name, sortOrder asc, skip 0, take 25, hasAttributeContains Obsolete.
```

Browse lean, then get detail (projection)

```text
On /abs/path/MyLib.dll, run GetTypeMethods for MyNamespace.MyType with the default summary projection to see signatures. Then re-call GetTypeMethods with projection='full' only for the methods I name to get their parameters and attributes.
```

Trace relationships and call sites

```text
On /abs/path/MyLib.dll: FindImplementationsOf MyNamespace.IMyService. Then FindReferencesTo that interface with analysisDepth='il' to find callers, and GetMethodCalls on the most relevant method to see what it invokes.
```

## Tools Overview

> **Tool names:** MCP clients call these tools in `snake_case` — `GetTypeMethods` → `get_type_methods`, `SearchMembers` → `search_members`, and so on. The PascalCase names used throughout this README match the underlying C# methods and the tool descriptions your client displays.

### Assembly Discovery & Analysis
- **`AnalyzeAssembly`**: Complete assembly overview with public types and metadata
- **`GetAssemblyInfo`**: Assembly-level metadata — identity/version, target framework, and referenced assemblies (`projection=full` adds all assembly attributes)
- **`FindAssemblyByClassName`**: Locate assemblies containing specific class names
- **`FindAssemblyByFileName`**: Find assemblies by file name in common build paths
- **`FindAssemblyByNugetPackage`**: Resolve a DLL from the local NuGet cache by package id (optional `version`/`tfm`)

### Type Introspection
- **`GetTypesFromAssembly`**: List all public types with metadata (paginated)
- **`AnalyzeType`**: Comprehensive type analysis with all members
- **`GetTypeInfo`**: Detailed type metadata (accessibility, generics, nested types)
- **`GetTypeHierarchy`**: Inheritance chain and interface implementations
- **`GetGenericTypeInfo`**: Generic parameters, arguments, and variance information
- **`GetTypeAttributes`**: Custom attributes declared on types
- **`GetNestedTypes`**: Nested type declarations

### Member Analysis (Filterable & Paginated)
- **`GetAllTypeMembers`**: All members across all categories
- **`GetTypeMethods`**: Method signatures, overloads, and metadata
- **`GetTypeProperties`**: Property details including getters/setters and indexers
- **`GetTypeFields`**: Field information including constants and readonly fields
- **`GetTypeEvents`**: Event declarations with handler types
- **`GetTypeConstructors`**: Constructor signatures and parameters
- **`AnalyzeMethod`**: Deep method analysis with overloads and attributes

### Member Search
- **`SearchMembers`**: Search a whole assembly for members whose name contains a fragment — the entry point when you know a member name but not its declaring type. Filter by `memberKinds` (`method|property|field|event|type`).

### Reverse Lookup
- **`FindImplementationsOf`**: Types implementing an interface or deriving from a base class (open-generic match supported)
- **`FindMethodsReturning`**: Methods whose return type matches a given type (open-generic match supported)
- **`FindExtensionMethodsFor`**: Extension methods that extend a given type (scans static classes by `this`-parameter)
- **`FindReferencesTo`**: Broader sweep across parameters, fields, properties, events, and generic arguments; pass `analysisDepth='il'` to also resolve inbound callers from method bodies

### IL Analysis
- **`GetMethodCalls`**: Read a method's IL body to list what it calls and which fields it touches — the "what does this method do?" question signature-level tools can't answer (aggregates across overloads; use `.ctor`/`.cctor` for constructors)

### Attributes & Metadata
- **`GetMemberAttributes`**: Attributes for specific members
- **`GetParameterAttributes`**: Parameter-level attribute information

### XML Documentation
- **`GetXmlDocsForType`**: Extract type-level XML documentation
- **`GetXmlDocsForMember`**: Member-specific documentation (summary/params/returns/remarks)

### Project & Solution Analysis
- **`AnalyzeSolution`**: Parse .sln files and enumerate projects
- **`AnalyzeProject`**: Project metadata, references, and build configuration
- **`GetProjectOutputPaths`**: Resolve output directories for different configurations
- **`ResolvePackageReferences`**: Map NuGet packages to cached assemblies
- **`FindDepsJsonDependencies`**: Parse deps.json for runtime dependencies

### Configuration & Runtime
- **`GetRuntimeOptions`**: Current server configuration and defaults
- **`UpdateRuntimeOptions`**: Modify pagination, caching, and search behavior

### Advanced Filtering & Pagination

All member analysis tools support comprehensive filtering and pagination:

**Filtering Options:**
* `caseSensitive` (bool): Case-sensitive type/member matching
* `nameContains` (string): Filter by member name substring
* `hasAttributeContains` (string): Filter by attribute type substring
* `includePublic` / `includeNonPublic` (bool): Visibility filtering
* `includeStatic` / `includeInstance` (bool): Member type filtering

**Pagination:**
* `skip` / `take` (int): Standard offset pagination
* `maxItems` (int): Maximum results per request (default 50; `FindReferencesTo` defaults to 25)
* `continuationToken` (string): Token-based pagination for large datasets
* `sortBy` / `sortOrder` (string): Sort by name/access in asc/desc order

#### Response Shape (token efficiency)

Most enumerating tools default to a lean **`summary`** projection and let you opt into the heavier **`full`** payload only when you need it. Reach for `full` deliberately — `summary` is usually enough to decide your next call.

* `projection` (`summary` | `full`): supported by `GetTypesFromAssembly`, `GetTypeMethods`, `GetAssemblyInfo`, `GetMethodCalls`, `FindImplementationsOf`, `FindMethodsReturning`, `FindExtensionMethodsFor`, and `FindReferencesTo`. `summary` returns just enough to browse (e.g. `{ name, signature }` for methods); `full` adds structured fields (parameters, attributes, return type, modifiers, etc.). _Note: `GetTypeProperties/Fields/Events/Constructors` have a single fixed shape and take no `projection`._
* `analysisDepth` (`signatures` | `il`): `FindReferencesTo` only. `signatures` (default) scans member declarations; `il` additionally scans method bodies for inbound callers (slower).
* `additionalAssemblies` (string[]): widen the search scope for `GetTypeHierarchy` and the reverse-lookup tools. `GetTypeHierarchy.derivedTypes` stays `null` until you pass this.
* `noCache` (bool): bypass the response cache for a single call when you suspect stale results.

**Type Resolution:**
* Supports full names (`Namespace.Type`), simple names (`Type`), and nested types (`Outer+Inner`)
* Case sensitivity controlled by `caseSensitive` parameter
* Automatic fallback resolution for ambiguous type names

### Response Schema

All tools return a stable JSON envelope:

```jsonc
{ "kind": "type.list|member.methods|...", "version": "1.0.0", "data": { /* result */ } }
```

Errors use a consistent shape:

```jsonc
{ "kind": "error", "version": "1.0.0", "code": "AssemblyNotFound|TypeNotFound|InvalidArgument|InternalError", "message": "...", "details": { } }

Common error codes include `AssemblyNotFound`, `TypeNotFound`, `MemberNotFound`, `InvalidArgument`, and `InternalError`.
```

## Contributing

Contributions are welcome. This repo includes an `.editorconfig` with modern C# preferences (file-scoped namespaces, expression-bodied members, 4-space indentation).

### Commit Message Format

This project uses [Conventional Commits](https://www.conventionalcommits.org/) for automated changelog generation. All commits must follow this format:

```
type(scope): description
```

**Valid types:**
- `feat` - A new feature
- `fix` - A bug fix
- `docs` - Documentation only changes
- `style` - Code style changes (formatting, semicolons, etc)
- `refactor` - Code change that neither fixes a bug nor adds a feature
- `perf` - Performance improvement
- `test` - Adding or correcting tests
- `build` - Changes to build system or dependencies
- `ci` - Changes to CI configuration
- `chore` - Other changes that don't modify src or test files
- `revert` - Reverts a previous commit

**Examples:**
```bash
git commit -m "feat(tools): add new assembly analysis tool"
git commit -m "fix: resolve null reference in type loader"
git commit -m "docs(readme): update installation instructions"
```

### Development Setup

```bash
# Restore .NET tools (versionize, husky)
dotnet tool restore

# Install git hooks for commit validation
dotnet husky install
```

### Guidelines

* Keep changes small and focused; add unit tests for new behavior.
* Follow the response envelope and error code conventions when adding tools.
* Run `dotnet build` and `dotnet test` locally before opening a PR.

### Creating a Release

Maintainers can create releases using:

```bash
# Restore tools if not already done
dotnet tool restore

# Preview what will change
dotnet versionize --dry-run

# Create release (bumps version, updates changelog, creates git tag)
dotnet versionize

# Push changes and tag to trigger release workflow
git push --follow-tags
```

The release workflow will automatically:
1. Build and test the project
2. Create a GitHub Release with changelog notes
3. Publish the NuGet package
4. Update `server.json` with the new version

## MCP Registry
mcp-name: io.github.jcucci/dotnet-sherlock-mcp

## License

Sherlock MCP for .NET is licensed under the [MIT License](LICENSE).
