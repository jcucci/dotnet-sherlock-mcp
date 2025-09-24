# Sherlock MCP for .NET

**Sherlock MCP for .NET** is a comprehensive Model Context Protocol (MCP) server that provides deep introspection capabilities for .NET assemblies. It enables Language Learning Models (LLMs) to analyze and understand your .NET code with precision, delivering accurate and context-aware responses for complex development scenarios.

This tool is essential for developers who want to harness LLM capabilities for:

*   **Deep codebase analysis** - Understanding complex .NET architectures and dependencies
*   **Precise type information** - Getting detailed metadata about types, members, and their signatures
*   **Automated documentation** - Extracting and utilizing XML documentation and attributes
*   **Custom tooling** - Building sophisticated tools that interact with .NET assemblies
*   **Code generation** - Creating accurate code based on existing type structures

## Key Features

*   **Comprehensive MCP Server**: Provides 28+ specialized tools for .NET assembly analysis
*   **Advanced Assembly Introspection**: Deep reflection-based analysis of types, members, and metadata
*   **Rich Member Analysis**: Detailed inspection of methods, properties, fields, events, and constructors
*   **Smart Filtering & Pagination**: Advanced filtering by name/attributes with efficient pagination for large datasets
*   **XML Documentation Integration**: Automatic extraction of summary, parameters, returns, and remarks
*   **Performance Optimized**: Caching, streaming, and memory-efficient processing
*   **Stable JSON API**: Consistent envelopes with versioning and structured error codes
*   **.NET 9.0 Native**: Built on the latest .NET platform with modern C# features
*   **Project Integration**: Solution and project file analysis with dependency resolution

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

To automatically use Sherlock when working with .NET code, add these configurations:

### Claude Code (CLAUDE.md)

Add this to your project's `CLAUDE.md` file:

```markdown
## .NET Assembly Analysis

This project uses Sherlock MCP for .NET assembly analysis. When analyzing .NET types, methods, or assemblies:

1. Use sherlock-mcp tools instead of guessing about .NET APIs
2. For type analysis: `GetTypeInfo`, `GetTypeMethods`, `GetTypeProperties`
3. For assembly overview: `AnalyzeAssembly` or `GetTypesFromAssembly`
4. For project structure: `AnalyzeProject`, `AnalyzeSolution`
5. Assembly paths are typically: `./bin/Debug/net9.0/ProjectName.dll`

Always include assembly path, prefer full type names, and use pagination for large results.
```

### Cursor (.cursorrules)

Add this to your project's `.cursorrules` file:

```
# .NET Analysis Rules
When working with .NET code, assemblies, or types:
- Use sherlock-mcp tools for accurate type/member information
- Assembly paths: ./bin/Debug/net9.0/*.dll or ./bin/Release/net9.0/*.dll
- For unknown types: GetTypesFromAssembly -> GetTypeInfo -> GetTypeMethods/Properties
- For code analysis: AnalyzeAssembly for overview, GetTypeInfo for details
- Use pagination (maxItems=50) for large results to avoid token limits
```

### Global Configuration

For system-wide usage, add to your global Claude Code settings or Cursor configuration:

```text
For .NET development: Use sherlock-mcp tools when analyzing assemblies, types, methods, or project structure. Prefer these over guessing .NET API details.
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

## Tools Overview

### Assembly Discovery & Analysis
- **`AnalyzeAssembly`**: Complete assembly overview with public types and metadata
- **`FindAssemblyByClassName`**: Locate assemblies containing specific class names
- **`FindAssemblyByFileName`**: Find assemblies by file name in common build paths

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
* `maxItems` (int): Maximum results per request
* `continuationToken` (string): Token-based pagination for large datasets
* `sortBy` / `sortOrder` (string): Sort by name/access in asc/desc order

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

Contributions are welcome. This repo includes an `.editorconfig` with modern C# preferences (file-scoped namespaces, expression-bodied members, 4-space indentation). Please:

* Keep changes small and focused; add unit tests for new behavior.
* Follow the response envelope and error code conventions when adding tools.
* Run `dotnet build` and `dotnet test` locally before opening a PR.

## License

Sherlock MCP for .NET is licensed under the [MIT License](LICENSE).
