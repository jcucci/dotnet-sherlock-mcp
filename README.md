# Sherlock MCP for .NET

**Sherlock MCP for .NET** is a powerful tool that provides a Model Context Protocol (MCP) server for .NET assemblies. It allows Language Learning Models (LLMs) to analyze and understand your .NET code, enabling more accurate and context-aware responses.

This tool is ideal for developers who want to leverage the power of LLMs to:

*   Understand complex .NET codebases.
*   Get detailed information about types, members, and their signatures.
*   Automate code analysis and documentation.
*   Build custom tools that interact with .NET assemblies.

## Key Features

*   **MCP Server**: Provides a standardized interface for LLMs to interact with .NET assemblies.
*   **Assembly Analysis**: Uses reflection to extract detailed information about types, members, and their signatures.
*   **Rich Toolset**: Analyze assemblies, types, methods, properties, fields, events, and constructors.
*   **Attributes & XML Docs**: Surface member/parameter attributes and adjacent XML documentation (summary/params/returns/remarks).
*   **Filtering & Paging**: Name/attribute filters, sorting, and paging for large result sets.
*   **Stable Schemas**: Standard JSON envelopes with `kind` and `version`; consistent error codes.
*   **.NET 9.0 Support**: Built on the latest version of the .NET platform.
*   **Easy to Use**: Can be easily integrated into your existing workflows.

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

Reflection (quick scans)

- AnalyzeAssembly: Assembly overview (public types + metadata), returns `reflection.assembly`.
- AnalyzeType: Type members snapshot (constructors/methods/properties/fields), returns `reflection.type`.
- AnalyzeMethod: Method overloads, params, attributes, returns `reflection.method`.
- FindAssemblyByClassName / FindAssemblyByFileName: Locate local assemblies.

Member analysis (rich, filterable)

- GetAllTypeMembers: Comprehensive members for a type (methods/properties/fields/events/constructors).
- GetTypeMethods / GetTypeProperties / GetTypeFields / GetTypeEvents / GetTypeConstructors.
- GetMemberAttributes: Attributes for a single member.
- GetParameterAttributes: Attributes for a specific parameter.

Type analysis

- GetTypesFromAssembly: Public types with metadata.
- GetTypeInfo: Detailed type metadata (kind, accessibility, generics, nested types).
- GetTypeHierarchy: Base types + interfaces.
- GetGenericTypeInfo: Generic parameters/arguments/variance.
- GetTypeAttributes: Custom attributes declared on a type.
- GetNestedTypes: Nested types declared by a type.

XML documentation

- GetXmlDocsForType: Summary/remarks/returns/params from adjacent XML.
- GetXmlDocsForMember: Docs for methods/properties/fields/events/ctors.

Project analysis

- AnalyzeSolution: Parse `.sln`, list projects.
- AnalyzeProject: Project metadata, references, outputs.
- GetProjectOutputPaths: Output folders for a configuration.
- ResolvePackageReferences: Resolve NuGet packages to cached assemblies.
- FindDepsJsonDependencies: Read deps.json runtime dependencies.

Runtime/config

- GetRuntimeOptions / UpdateRuntimeOptions: Page size, caching, search roots, defaults.

### Member Listing Options

All member listing tools (methods/properties/fields/events/constructors) accept:

* `caseSensitive` (bool): Case-sensitive type/member matching (default false in tools).
* `nameContains` (string): Filter members by substring.
* `hasAttributeContains` (string): Filter by attribute type substring.
* `skip` / `take` (int): Paging over results.
* `sortBy` / `sortOrder`: Sort by name/access; asc/desc.

Type lookup supports full names, simple names, and nested types (normalized `Outer+Inner`); `caseSensitive` controls comparison.

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
