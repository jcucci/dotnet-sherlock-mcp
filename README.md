# Sherlock MCP for .NET

**Sherlock MCP for .NET** is a comprehensive Model Context Protocol (MCP) server that provides deep introspection capabilities for .NET assemblies. It enables Language Learning Models (LLMs) to analyze and understand your .NET code with precision, delivering accurate and context-aware responses for complex development scenarios.

This tool is essential for developers who want to harness LLM capabilities for:

*   **Deep codebase analysis** - Understanding complex .NET architectures and dependencies
*   **Precise type information** - Getting detailed metadata about types, members, and their signatures
*   **Automated documentation** - Extracting and utilizing XML documentation and attributes
*   **Custom tooling** - Building sophisticated tools that interact with .NET assemblies
*   **Code generation** - Creating accurate code based on existing type structures

## Key Features

*   **Comprehensive MCP Server**: Provides 47 specialized tools for .NET assembly analysis, with an optional 24-tool `core` profile whose other tools load on demand in groups
*   **Advanced Assembly Introspection**: Deep reflection-based analysis of types, members, and metadata
*   **Rich Member Analysis**: Detailed inspection of methods, properties, fields, events, and constructors
*   **Smart Filtering & Pagination**: Advanced filtering by name/attributes with efficient pagination for large datasets
*   **XML Documentation Integration**: Automatic extraction of summary, parameters, returns, and remarks
*   **Performance Optimized**: Caching, pagination, and memory-efficient processing
*   **Stable JSON API**: Consistent envelopes with versioning and structured error codes
*   **.NET 9.0 Native**: Built on the latest .NET platform with modern C# features
*   **Project Integration**: Solution and project file analysis with dependency resolution
*   **Workflow Prompts**: `explore_package`, `explain_type` and `who_calls` prompts give one-click entry points into common analysis workflows
*   **Current MCP SDK**: Built on `ModelContextProtocol` 2.1.0 (GA)
*   **Current MCP Specification**: Speaks protocol revision `2026-07-28`, and negotiates down automatically for clients on earlier revisions

## What's New in 2.14.0

- **Claude Code plugin**: `/plugin marketplace add jcucci/dotnet-sherlock-mcp` and `/plugin install sherlock@dotnet-sherlock-mcp` install the server plus a skill that teaches agents the Sherlock workflow. See [Claude Code plugin](#claude-code-plugin).
- **`get_type_members` and tool profiles**: one paginated, filterable tool lists every member kind, and `--profile core` / `SHERLOCK_TOOL_PROFILE=core` trims the surface to the essential tools, and agents load the rest by group with `load_tools`. The per-kind member tools are deprecated.
- **Structured output and error guidance**: the core browsing tools publish an `outputSchema` and return `structuredContent`, and failed calls carry `isError: true` with did-you-mean candidates and fix-it suggestions.
- **Cancellation, progress and elicitation**: long scans can be cancelled and report progress, and an ambiguous simple type name prompts the client to choose. See `CHANGELOG.md` for full details.

## Installation

### Run with dnx (.NET 10 SDK)

With the .NET 10 SDK, `dnx` downloads the package from NuGet and runs it directly — no install step:

```bash
dnx -v q --yes Sherlock.MCP.Server@2.14.0
```

`--yes` skips the interactive confirmation prompt, which an MCP client launching the server over stdio can't answer. `-v q` stops `dnx` from printing a "Skipping NuGet package signature verification." notice to stdout the first time it downloads a version, which would otherwise corrupt the MCP stream and fail that first connection. Both are `dnx` options, so they must come before the package id; anything after it is passed to the server. Pinning the version keeps launches reproducible; bump it when you want to upgrade. The package is published with the `McpServer` package type, so it is also listed as an MCP server on NuGet.org.

### Install as a global tool

On any supported SDK (.NET 8 or later), install the global tool from NuGet (adds `sherlock-mcp` to your PATH):

```bash
dotnet tool install -g Sherlock.MCP.Server
```

Alternatively, during development you can run the server locally:

```bash
dotnet run --project src/server/Sherlock.MCP.Server.csproj
```

## Configure Your MCP Client

Sherlock runs as a standard MCP server that communicates over stdio.

### Claude Code plugin

This repository is also a Claude Code plugin marketplace. The `sherlock` plugin bundles the server (launched with `dnx`, so it needs the .NET 10 SDK) and a skill that teaches the agent the locate → orient → drill-in → relationships workflow:

```text
/plugin marketplace add jcucci/dotnet-sherlock-mcp
/plugin install sherlock@dotnet-sherlock-mcp
```

The plugin starts the server with the `core` [tool profile](#tool-profiles), which matches the tools the skill covers. The agent can add the other tools by group with `load_tools` when it needs them (see [Tool groups](#tool-groups)). To expose every tool from the start, set `SHERLOCK_TOOL_PROFILE=full` in the environment Claude Code is launched from.

Each user runs these commands once; restart Claude Code (or run `/reload-plugins`) afterwards. To pick up a new release, run `/plugin marketplace update dotnet-sherlock-mcp`. If you previously registered Sherlock with `claude mcp add`, remove that entry (`claude mcp remove sherlock`) so the tools aren't loaded twice.

#### Team setup

To offer the plugin to everyone working in a repository, commit the following to that repository's `.claude/settings.json`. Claude Code prompts each teammate to install it when they trust the folder, so nobody has to type the commands above:

```json
{
  "extraKnownMarketplaces": {
    "dotnet-sherlock-mcp": {
      "source": {
        "source": "github",
        "repo": "jcucci/dotnet-sherlock-mcp"
      }
    }
  },
  "enabledPlugins": {
    "sherlock@dotnet-sherlock-mcp": true
  }
}
```

Teammates still need the .NET 10 SDK on their `PATH` for `dnx`.

### Using dnx

- Claude Code:

```bash
claude mcp add sherlock -- dnx -v q --yes Sherlock.MCP.Server@2.14.0
```

- VS Code (`.vscode/mcp.json`):

```json
{
  "servers": {
    "sherlock": {
      "type": "stdio",
      "command": "dnx",
      "args": ["-v", "q", "--yes", "Sherlock.MCP.Server@2.14.0"]
    }
  }
}
```

### Using the global tool

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

### Tool profiles

Large tool lists cost agents context and discoverability (Claude Code switches to deferred Tool Search once tool descriptions grow past roughly 10% of the context window). Sherlock can start with a smaller surface:

| Profile | Tools | Contents |
|---|---|---|
| `full` (default) | 47 | Every tool, including the deprecated per-kind member tools |
| `core` | 24 | `load_tools` and `invoke_tool` (see [Tool groups](#tool-groups)), discovery (`find_assembly_by_class_name`, `find_assembly_by_file_name`, `find_assembly_by_nuget_package`, `get_project_output_paths`, `open_assembly`), orientation (`get_assembly_info`, `get_types_from_assembly`, `get_type_info`, `get_type_hierarchy`), members and docs (`get_type_members`, `search_members`, `analyze_method`, `get_xml_docs_for_type`, `get_xml_docs_for_member`) and relationships (`find_implementations_of`, `find_methods_returning`, `find_extension_methods_for`, `find_references_to`, `get_method_calls`) source (`get_member_source`, `decompile_member`) and API diffs (`compare_api_surface`) |

Select a profile with the `--profile` argument or the `SHERLOCK_TOOL_PROFILE` environment variable (the argument wins). An unknown profile name stops the server with an error.

```jsonc
{
  "servers": {
    "sherlock": {
      "command": "sherlock-mcp",
      "args": ["--profile", "core"]
      // or: "env": { "SHERLOCK_TOOL_PROFILE": "core" }
    }
  }
}
```

### Tool groups

Under the `core` profile every other tool belongs to a group that the agent can load when it needs it:

| Group | Tools |
|---|---|
| `frameworks` | `find_endpoints`, `find_service_registrations`, `find_ef_entities`, `find_handlers` |
| `project` | `analyze_solution`, `analyze_project`, `resolve_package_references`, `get_package_graph`, `find_deps_json_dependencies` |
| `metadata` | `analyze_assembly`, `get_generic_type_info`, `get_nested_types`, `get_type_attributes`, `get_member_attributes`, `get_parameter_attributes` |
| `decompile` | `decompile_type` |
| `config` | `get_runtime_options`, `update_runtime_options` |
| `legacy` | the deprecated `analyze_type`, `get_all_type_members` and `get_type_{methods,properties,fields,events,constructors}` |

- **`load_tools`**: with no arguments, lists every group, its tools and whether it is loaded. With `groups=['frameworks', …]`, it adds those tools to the server's tool list and returns each loaded tool's name, description and input schema. The server then sends `notifications/tools/list_changed`. Under protocol revision 2026-07-28 that only reaches clients that asked for it through `subscriptions/listen`.
- **`invoke_tool`**: calls a loaded tool by `name` with its `arguments` object and returns that tool's own result. Clients that never refresh their tool list can still use loaded groups this way. Calling a tool whose group isn't loaded fails with `ToolNotLoaded` and names the group.
- **Discovery hints**: core results can carry `hints[]`, each `{ tool, reason, group }`, pointing at a related tool. Examples:
  - `get_type_info` on a type with nested types or generic parameters, or one that is a `DbContext`, controller or MediatR handler;
  - `find_implementations_of` / `find_references_to` / `find_extension_methods_for` on those framework types or `IServiceCollection`;
  - `decompile_member` → `decompile_type`;
  - `get_project_output_paths` → `analyze_project` and `get_package_graph`.

  Dependency errors suggest `get_package_graph`. `group` is null for core tools.
- **Tool list caching**: under `core`, `tools/list` is marked `cacheScope: private`, since a session's tool list grows as groups load. Under `full`, it stays `public`.

The `full` profile lists every tool and omits `load_tools` and `invoke_tool`.

## Auto-Configure for .NET Projects

**You usually don't need to paste anything.** Sherlock ships its usage guidance in the MCP `instructions` field returned at initialize, and most MCP clients (including Claude Code) surface that to the agent automatically — so the guidance stays correct and versioned with the package, with no copy-paste to maintain.

The snippets below are **optional reinforcement**. Keep them short and principle-based rather than enumerating tool names and workflows: a static list pasted into your repo will drift as Sherlock's tools evolve, whereas the tools' own descriptions (and the server `instructions`) always match the version you're running.

> Tool names are exposed in `snake_case` (`get_type_members`, `search_members`, …); argument names stay camelCase (`projection`, `nameContains`).

### Claude Code (CLAUDE.md)

If you installed the [Claude Code plugin](#claude-code-plugin), its skill already covers this. Otherwise, you can add a short, optional pointer to your project's `CLAUDE.md`:

```markdown
## .NET Assembly Analysis

Use the Sherlock MCP tools (`get_type_members`, `search_members`, …) for .NET type/assembly
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

- Prefer the Sherlock MCP tools (snake_case, e.g. `get_type_members`, `search_members`) over guessing about .NET APIs.
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
Use GetTypeMembers on /abs/path/MyLib.dll, type MyNamespace.MyType, kinds method, sortBy name, sortOrder asc, skip 0, take 25, hasAttributeContains Obsolete.
```

Browse lean, then get detail (projection)

```text
On /abs/path/MyLib.dll, run GetTypeMembers for MyNamespace.MyType with the default summary projection to see signatures. Then re-call GetTypeMembers with kinds method, nameContains and projection='full' only for the methods I name to get their parameters and attributes.
```

Trace relationships and call sites

```text
On /abs/path/MyLib.dll: FindImplementationsOf MyNamespace.IMyService. Then FindReferencesTo that interface with analysisDepth='il' to find callers, and GetMethodCalls on the most relevant method to see what it invokes.
```

## Tools Overview

> **Tool names:** MCP clients call these tools in `snake_case` — `GetTypeMembers` → `get_type_members`, `SearchMembers` → `search_members`, and so on. The PascalCase names used throughout this README match the underlying C# methods and the tool descriptions your client displays.

### Assembly Discovery & Analysis
- **`OpenAssembly`**: Returns a short `asm_…` handle to pass as `assemblyHandle` instead of `assemblyPath` on any tool that takes an assembly. The handle carries `additionalAssemblies` too, persists across server restarts (in `handles.json` under `SHERLOCK_STATE_DIR`, default the user's local app-data `sherlock` folder) and is pinned to the current build: after a rebuild, calls fail with `StaleAssemblyHandle` until it is reopened
- **`AnalyzeAssembly`**: Complete assembly overview with public types and metadata
- **`GetAssemblyInfo`**: Assembly-level metadata — identity/version, target framework, and referenced assemblies (`projection=full` adds all assembly attributes); `frameworkResolution` reports what framework types were resolved against: the target framework's reference pack (`referencePack`, e.g. `Microsoft.NETCore.App.Ref/8.0.x/ref/net8.0`, plus `Microsoft.AspNetCore.App.Ref` and other packs named by the project's `FrameworkReference`s or the app's `runtimeconfig.json`), the assembly's own folder for self-contained output (`appLocal`), or the server's runtime when no matching pack is installed or the target is .NET Framework (`hostRuntime`, with the unmatched frameworks in `missingFrameworks`)
- **`FindAssemblyByClassName`**: Locate assemblies declaring a public type by simple, full or nested name; skips `obj/`, `ref/`, `refint/`, `node_modules/`, `packages/`, `TestResults/` and dot-directories, and ranks `bin/` matches first
- **`FindAssemblyByFileName`**: Find assemblies by file name under a root directory, with the same exclusions and ranking
- **`FindAssemblyByNugetPackage`**: Resolve a DLL from the local NuGet cache by package id (optional `version`/`tfm`)

### Type Introspection
- **`GetTypesFromAssembly`**: List all public types with metadata (paginated)
- **`AnalyzeType`** _(deprecated)_: Type metadata plus all members; use `GetTypeInfo` + `GetTypeMembers projection=full`
- **`GetTypeInfo`**: Detailed type metadata (accessibility, generics, nested types)
- **`GetTypeHierarchy`**: Inheritance chain and interface implementations; `format='mermaid'` returns a Mermaid `classDiagram`
- **`GetGenericTypeInfo`**: Generic parameters, arguments, and variance information
- **`GetTypeAttributes`**: Custom attributes declared on types
- **`GetNestedTypes`**: Nested type declarations

### Member Analysis (Filterable & Paginated)
- **`GetTypeMembers`**: Methods, properties, fields, events and constructors of a type in one paginated list. Narrow with `kinds` (`method|property|field|event|constructor`), `nameContains` and `hasAttributeContains`; `summary` items are `{ kind, name, signature }`, `projection=full` adds each kind's structured fields
- **`AnalyzeMethod`**: Deep method analysis with overloads and attributes
- _Deprecated, kept for a release or two:_ `GetTypeMethods`, `GetTypeProperties`, `GetTypeFields`, `GetTypeEvents`, `GetTypeConstructors` (use `GetTypeMembers` with `kinds`) and `GetAllTypeMembers` (use `GetTypeMembers projection=full`)

### Member Search
- **`SearchMembers`**: Search a whole assembly for members whose name contains a fragment — the entry point when you know a member name but not its declaring type. Filter by `memberKinds` (`method|property|field|event|type`).

### Reverse Lookup
- **`FindImplementationsOf`**: Types implementing an interface or deriving from a base class (open-generic match supported)
- **`FindMethodsReturning`**: Methods whose return type matches a given type (open-generic match supported)
- **`FindExtensionMethodsFor`**: Extension methods that extend a given type (scans static classes by `this`-parameter)
- **`FindReferencesTo`**: Broader sweep across parameters, fields, properties, events, and generic arguments; pass `analysisDepth='il'` to also resolve inbound callers from method bodies

### Framework Patterns
In the `frameworks` [tool group](#tool-groups) (always listed under `full`). Each tool scans one or more assemblies (`additionalAssemblies`) and pages with `maxItems` / `continuationToken`. The default `summary` projection is lean, and `projection='full'` adds the assembly path and detail fields.
- **`FindEndpoints`**: ASP.NET Core endpoints. Controller actions are read from `[Route]` / `[Http*]` / `[AcceptVerbs]` attribute routes, with `[controller]` / `[action]` substituted. Minimal APIs come from `Map{Get,Post,Put,Delete,Patch,Methods,Group,Fallback}` calls read from IL. Filter with `routeContains` and `httpMethod`. Minimal-API routes and handlers are taken from the string literal and method reference next to each call, so a route held in a variable or field comes back as `null`, and `MapGroup` prefixes are listed as separate `minimalApiGroup` entries rather than combined
- **`FindServiceRegistrations`**: `Microsoft.Extensions.DependencyInjection` registrations, read from IL calls to `Add{Singleton,Scoped,Transient}`, `TryAdd*`, `AddKeyed*`, `AddHostedService` and the `ServiceDescriptor` factories. Lifetime, service and implementation come from generic arguments or `typeof(...)` operands, and factory and instance registrations report `implementation: null`. Filter with `serviceType`, `implementationType` and `lifetime`
- **`FindEfEntities`**: The `DbSet<T>` properties, declared or inherited, of every `DbContext` subclass. Filter with `contextType` and `entityType`
- **`FindHandlers`**: MediatR-style handlers: concrete implementations of `IRequestHandler<,>` / `IRequestHandler<>`, `INotificationHandler<>`, `IStreamRequestHandler<,>` and `IPipelineBehavior<,>` from the `MediatR` or `Mediator` namespaces. Filter with `messageType` and `kind`

### IL Analysis
- **`GetMethodCalls`**: Read a method's IL body to list what it calls and which fields it touches — the "what does this method do?" question signature-level tools can't answer (aggregates across overloads; use `.ctor`/`.cctor` for constructors). `format='mermaid'` returns a Mermaid flowchart call graph, and `depth` (up to 5) follows callees defined in the same assembly

### API Diff
- **`CompareApiSurface`**: Diff the public API of two versions of an assembly. `left` (old) and `right` (new) each take an assembly path, an `asm_…` handle, or a NuGet package as `Package.Id@1.2.3` resolved from the local cache (omit the version for the highest cached one; `tfm` picks the target framework; by default both sides use one target framework both packages ship, with a warning when they share none). Public types and members, plus protected ones on inheritable types, are compared. Breaking changes are flagged with reasons: removed types or members, changed return/field/property types, reduced accessibility, classes that became sealed, abstract or static, removed base types or interfaces, new generic constraints, abstract members added to interfaces or inheritable classes, members that became static/instance, abstract or non-virtual, fields that became readonly, removed or less accessible property accessors, setters that became `init`, changed parameter modifiers, parameters that are no longer optional or `params`, and changed enum values. Additions and compatible changes (parameter renames, new default values, changed constants, new `params`, members moved to a base type) are listed without the flag. The default `summary` returns `counts` and the breaking changes; `projection='full'` lists every change with `leftSignature`, `rightSignature` and `reasons[]` (narrow with `breakingOnly` and `namespaceFilter`). Paged with `maxItems` / `continuationToken`

### Original Source & Decompilation
- **`GetMemberSource`**: The member's original source, with comments and real names, read through the assembly's portable PDB (embedded in the DLL or beside it). It uses source embedded in the PDB, then the local file the assembly was built from, then the file at the PDB's Source Link URL. Each candidate, embedded source included, must match the SHA-1/SHA-256 checksum the PDB recorded (CRLF/LF differences are tolerated); otherwise that overload falls back to decompiled C#. Local files are read only from absolute, non-UNC paths and only up to 5 MB. The result's `origin` is `embedded`, `local`, `sourcelink`, `decompiled` or `mixed`, each overload reports its `document`, `url` and `lines`, and `note` explains any fallback. The slice covers the member's `///` docs, attributes, signature and body
- **`DecompileMember`**: Decompile one member (method, property, field, event or constructor) to C# with ICSharpCode.Decompiler. Returns every overload of the name, or one overload when `parameterTypes` is given (e.g. `string,int`; an empty string selects the parameterless overload). Use `.ctor`/`.cctor` for constructors
- **`DecompileType`**: Decompile a whole type to C#. In the `decompile` [tool group](#tool-groups); prefer `DecompileMember`

All three tools page their source by line: `maxLines` (default 400, max 5000) caps the page size, a page also stops early once it reaches about 90,000 characters so it always fits the response limit, and each page reports `startLine`, `lineCount`, `totalLines`, `truncated` and a `continuationToken` for the next page. Lines over 2,000 characters are clipped with a `/* … more characters clipped */` marker and counted in `clippedLines`. Pass `additionalAssemblies` (or use an `assemblyHandle` opened with them) when dependencies live outside the assembly's folder; their folders are searched when resolving referenced types and their file stamps are part of the cache key. The full decompilation is cached by file stamp, so later pages are cheap. A type the assembly only forwards (e.g. `System.String` in a `System.Runtime.dll` facade) returns `TypeForwarded` with the defining assembly in `recommendedParams`.

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
- **`GetPackageGraph`**: Restored NuGet graph from `obj/project.assets.json` (exact direct and transitive versions per target framework and RID)
- **`FindDepsJsonDependencies`**: Parse deps.json for runtime dependencies

### Configuration & Runtime
- **`GetRuntimeOptions`**: Current server configuration and defaults
- **`UpdateRuntimeOptions`**: Modify pagination, caching, search and Source Link fetch behavior

`get_member_source` fetches Source Link files over HTTPS only from known source hosts by default (`raw.githubusercontent.com`, `gitlab.com`, `bitbucket.org`, `api.bitbucket.org`, `dev.azure.com`, `*.visualstudio.com`), because the URL comes from the PDB and could otherwise point anywhere. Set `SHERLOCK_SOURCE_FETCH` to `off`, `known-hosts` (default) or `any`, or change it at runtime with `update_runtime_options sourceFetch=…` and `addSourceFetchHosts` / `removeSourceFetchHosts`. A host gets 5 seconds to respond and 30 seconds to deliver the file, files over 5 MB are refused, redirects are followed manually (at most 3) and each hop must pass the same HTTPS and host checks, a host that can't be connected to is not retried for the rest of the session, a fallback caused by a network failure is not cached, and verified files are cached under `SHERLOCK_STATE_DIR/sources`.

### Resources
Three resource templates let clients fetch a single type, doc entry or cached package without another tool call. Every variable is percent-encoded.
- **`sherlock://assembly/{path}/type/{fullName}`**: Type metadata, the same payload as `get_type_info`
- **`sherlock://assembly/{path}/docs/{memberId}`**: XML documentation for a documentation id such as `T:Ns.Type` or `M:Ns.Type.Method(System.String)`
- **`sherlock://nuget/{packageId}/{version}`**: The assembly a cached NuGet package version resolves to, the same payload as `find_assembly_by_nuget_package`

`search_members`, `get_types_from_assembly` and the `find_*` reverse-lookup tools return a `resource_link` to the type resource for each distinct type on the page, after the JSON text block. `resources/read` carries private caching hints; a URI whose assembly, type, doc id or package doesn't exist fails with `-32602`.

Template variables support `completion/complete`, returning at most 100 values:
- `path`: recently loaded assemblies, then `.dll`/`.exe` files and subfolders of the folder being typed
- `fullName`: type names (metadata form, e.g. ``List`1``) from the assembly in the `path` context argument
- `memberId`: documentation ids from the XML file next to the `path` assembly
- `packageId` / `version`: package folders and their versions (newest first) in the local NuGet cache

### Prompts
Three prompts encode common tool sequences as one-click entry points (Claude Code lists them as `/mcp__sherlock__<name>` slash commands). Each returns a single user message that walks the agent through the tools; they only call tools in the `core` profile, so they work under either profile.
- **`explore_package`** (`packageId`, `version?`): Resolves the package from the local NuGet cache (highest cached version when `version` is omitted), orients on the assembly, and summarizes its main types and entry points
- **`explain_type`** (`assemblyPath`, `typeName`): Hierarchy, members, XML docs and usages of one type
- **`who_calls`** (`assemblyPath`, `typeName`, `memberName`, `additionalAssemblies?`): Inbound callers of a member from IL, optionally across a comma-separated list of other assemblies

`assemblyPath`, `typeName` (with `assemblyPath` in the completion context), `packageId` and `version` (with `packageId` in the context) support `completion/complete` the same way as the resource template variables. `prompts/list` carries the same public caching hints as `tools/list`.

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

* `projection` (`summary` | `full`): supported by `GetTypesFromAssembly`, `GetTypeMembers`, `GetTypeMethods`, `GetAssemblyInfo`, `GetMethodCalls`, `FindImplementationsOf`, `FindMethodsReturning`, `FindExtensionMethodsFor`, `FindReferencesTo`, `FindEndpoints`, `FindServiceRegistrations`, `FindEfEntities`, and `FindHandlers`. `summary` returns just enough to browse (e.g. `{ kind, name, signature }` for members); `full` adds structured fields (parameters, attributes, return type, modifiers, etc.). _Note: the deprecated `GetTypeProperties/Fields/Events/Constructors` have a single fixed shape and take no `projection`._
* `analysisDepth` (`signatures` | `il`): `FindReferencesTo` only. `signatures` (default) scans member declarations; `il` additionally scans method bodies for inbound callers (slower).
* `format` (`json` | `mermaid`): supported by `GetTypeHierarchy` (`classDiagram`) and `GetMethodCalls` (`flowchart`, with `depth` for transitive calls). Mermaid results return `{ diagram, nodeCount, edgeCount, truncated, note }`, bounded by `maxNodes` (default 50, max 200).
* `additionalAssemblies` (string[]): widen the search scope for `GetTypeHierarchy` and the reverse-lookup tools. `GetTypeHierarchy.derivedTypes` stays `null` until you pass this.
* `noCache` (bool): bypass the response cache for a single call when you suspect stale results. Every tool that reads an assembly or project caches its response, keyed by the file stamps of its inputs (the assembly and any `additionalAssemblies`, the `project.assets.json` of the project whose `bin` output holds the assembly, the app's `runtimeconfig.json`, the XML doc file beside it, or the project file and `obj/project.assets.json`), so a rebuild or restore invalidates it automatically. The `find_assembly_by_*` lookups and `resolve_package_references` (which read the shared NuGet package cache), configuration and handle tools are not cached. Two cases the stamps don't catch, where `noCache=true` is the fix: a dependency DLL dropped in beside an assembly that previously resolved only partially (keys stamp the assembly, not its siblings), and a restore for a project whose `project.assets.json` lives outside `obj/` or `artifacts/obj/<project>/` (e.g. a custom `BaseIntermediateOutputPath`), and a targeting pack installed after an assembly was first resolved against the host runtime (a `noCache` call notices the new pack and rebuilds the assembly's inspection context).
* `assemblyHandle` (string): accepted by every tool that takes `assemblyPath`, as an alternative to it (pass one, not both). Get one from `open_assembly`; it also supplies the `additionalAssemblies` it was opened with, and any you pass explicitly are added to them.

**Type Resolution:**
* Supports full names (`Namespace.Type`), simple names (`Type`), and nested types (`Outer+Inner`)
* Case sensitivity controlled by `caseSensitive` parameter
* Automatic fallback resolution for ambiguous type names

### Response Schema

All tools return a stable JSON envelope:

```jsonc
{ "kind": "type.list|member.methods|...", "version": "1.0.0", "data": { /* result */ }, "hints": [ /* optional */ ] }
```

The envelope is serialized as compact (unindented) JSON in the tool's text content block. The core browsing tools also advertise an MCP `outputSchema` and return the same envelope as `structuredContent`, so clients can validate and consume results without parsing text: `search_members`, `get_types_from_assembly`, `get_type_info`, `get_type_members`, `get_type_methods`, `get_assembly_info`, `get_method_calls`, `decompile_member`, `get_member_source`, `compare_api_surface`, `find_implementations_of`, `find_methods_returning`, `find_extension_methods_for` and `find_references_to`. Their schemas describe the default `summary` projection; `projection='full'` items add fields on top of it. Error results never carry `structuredContent`.

Error results are flagged with MCP's `isError: true`, so clients can tell a failure from a result without parsing the text. Errors use a consistent shape. Every error carries `kind`, `version`, `code`, and `message`; some add `details`, and guided errors add a `suggestion`, `alternativeTools`, or `recommendedParams` to point the agent at a next step:

```jsonc
{
  "kind": "error",
  "version": "1.0.0",
  "code": "MethodNotFound",
  "message": "No method named 'Parse' was found on type 'MyApp.Config' in MyApp.dll.",
  "suggestion": "Verify the type and method names. Use get_type_members with kinds=method to list available methods, or set includeNonPublic=true for private methods.",
  "alternativeTools": ["get_type_members", "analyze_method"]
}
```

Error codes:

* **Not found:** `AssemblyNotFound` (`recommendedParams.similarFiles` lists near-miss assemblies in the same folder), `TypeNotFound` / `MemberNotFound` (`recommendedParams.candidates` lists the closest names), `MethodNotFound`, `PackageNotFound`, `VersionNotFound`, `XmlNotFound`, `ProjectNotFound`, `FileNotFound`
* **Bad input:** `AmbiguousTypeName` (only for clients that can't elicit; `recommendedParams.candidates` lists the matching full names), `InvalidArgument`, `InvalidProjection`, `InvalidAnalysisDepth`, `InvalidContinuationToken`
* **Loading:** `InvalidAssembly` (not a managed assembly), `DependencyNotFound` (a referenced assembly couldn't be loaded), `DependencyResolutionFailed`, `AccessDenied`
* **Limits & internal:** `ResponseTooLarge`, `InternalError`

## Roadmap

Shipped features and planned work are summarized in [`src/docs/roadmap.md`](src/docs/roadmap.md); the live checklist is tracked in [#87](https://github.com/jcucci/dotnet-sherlock-mcp/issues/87).

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

`versionize` only bumps the project version. Before pushing, bump both `version` fields in `server.json`, the Claude Code plugin's `version` in `plugins/sherlock/.claude-plugin/plugin.json` and `.claude-plugin/marketplace.json`, and the pinned `dnx` version in `plugins/sherlock/.mcp.json` (and in this README) in the same release commit, then re-point the tag at it. `server.json` is packed into the NuGet package as `.mcp/server.json`.

The release workflow will automatically:
1. Verify that the tag, `server.json`, the project version and the Claude Code plugin versions all match
2. Build and test the project
3. Create a GitHub Release with changelog notes
4. Publish the NuGet package and the MCP Registry entry

## MCP Registry
mcp-name: io.github.jcucci/dotnet-sherlock-mcp

## License

Sherlock MCP for .NET is licensed under the [MIT License](LICENSE).
