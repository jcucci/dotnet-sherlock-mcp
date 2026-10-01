---
name: sherlock
description: Inspect compiled .NET assemblies with the Sherlock MCP tools. Use when a task needs facts about a .NET API - a type's members, a method's signature or overloads, which types implement an interface, who calls a method, what a NuGet package exposes - or before writing C# against a library you have not verified. Prefer this over guessing from memory or reading decompiled source.
---

# Sherlock: .NET assembly introspection

Sherlock reads real assemblies (your build output or the NuGet cache) via metadata-only reflection, so its answers match the exact version the project compiles against. Reach for it whenever correctness depends on an API's actual shape.

Tool names are snake_case; argument names are camelCase.

## 1. Locate the assembly

Every tool takes an assembly path. Find it rather than guessing:

- `find_assembly_by_class_name` - you know a type name but not which DLL holds it.
- `find_assembly_by_nuget_package` - resolve a package (and version) from the local NuGet cache.
- `get_project_output_paths` - the build output DLL of a `.csproj`.
- `find_assembly_by_file_name` - you know the DLL name.

Never hardcode `bin/Debug/<tfm>/...`; the target framework and configuration vary.

When you will make several calls against the same assembly, call `open_assembly` once and pass the returned `assemblyHandle` (a short `asm_…` id) instead of `assemblyPath`. The handle also carries any `additionalAssemblies` you opened it with.

## 2. Orient cheaply

- `get_assembly_info` - identity, target framework, references.
- `search_members` - you know a member name fragment but not its declaring type (narrow with `memberKinds`).
- `get_types_from_assembly` - browse types, paginated.

## 3. Drill in, narrow to wide

1. `get_type_info` - kind, base type, interfaces, accessibility, member counts.
2. `get_type_members` filtered by `kinds`, `nameContains` or `hasAttributeContains`. The default `summary` projection returns `{ kind, name, signature }`, and the C# signature already carries return type, parameters and modifiers.
3. Re-call with `projection='full'` only for the few members where you need structured parameters, attributes or modifier flags.
4. `analyze_method` for one method's overloads; `get_xml_docs_for_type` / `get_xml_docs_for_member` for documentation; `get_type_hierarchy` for the inheritance chain.

Start with a small `maxItems` and page with `continuationToken` instead of fetching everything.

## 4. Relationships

- `find_implementations_of` - implementers of an interface or subclasses of a base type (open generics match).
- `find_methods_returning` - factories and accessors that produce a type.
- `find_extension_methods_for` - extension methods targeting a type.
- `find_references_to` - where a type is used; add `analysisDepth='il'` to include callers found in method bodies.
- `get_method_calls` - what a method body calls and which fields it touches (use `.ctor` / `.cctor` for constructors).

Pass `additionalAssemblies` to search across several DLLs.

## 5. Recovering from errors

Failed calls return `isError: true` with JSON guidance. Follow it rather than retrying blindly:

- `TypeNotFound` / `MemberNotFound`: retry with a name from `recommendedParams.candidates`.
- `AmbiguousTypeName`: the simple name matched several types; use one of the full names offered (or answer the client's selection prompt).
- Response too large: lower `maxItems`, keep `projection='summary'`, page with `continuationToken`.
- `DependencyNotFound`: point at the build-output copy of the assembly so its dependencies sit beside it.
- Results look stale after a rebuild: pass `noCache=true`.
- `StaleAssemblyHandle` or `UnknownAssemblyHandle`: call `open_assembly` again, with the paths in `recommendedParams` when given. If the error lists `details.missingFiles`, the assembly was moved or deleted: locate it again first.

Use full type names (`Namespace.Type`, nested as `Outer+Inner`) whenever you know them.

## 6. Resource links

`search_members`, `get_types_from_assembly` and the `find_*` tools also return `sherlock://assembly/{path}/type/{fullName}` resource links. Reading one gives the same payload as `get_type_info` without another tool call.
