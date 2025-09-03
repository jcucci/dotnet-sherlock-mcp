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
*   **Rich Toolset**: Offers a comprehensive set of tools for analyzing assemblies, types, methods, properties, fields, events, and constructors.
*   **.NET 9.0 Support**: Built on the latest version of the .NET platform.
*   **Easy to Use**: Can be easily integrated into your existing workflows.

## Installation

To use Sherlock MCP for .NET, you can install it from NuGet:

```bash
dotnet tool install -g Sherlock.MCP.Server
```

## Usage

To configure Sherlock MCP as a custom tool in an IDE that supports the Model Context Protocol (such as Cursor), follow these steps:

1.  **Open your IDE's settings.**
2.  **Navigate to the MCP or custom tools section.**
3.  **Add a new server configuration.**
4.  **For the command, enter `sherlock-mcp`.**

Once configured, your IDE will be able to use Sherlock MCP for deep .NET code analysis.

## Available Tools

Sherlock MCP for .NET provides the following tools for analyzing .NET assemblies:

*   `AnalyzeAssembly`: Analyzes a .NET assembly and returns information about all public types, their members, and metadata.
*   `AnalyzeType`: Gets detailed information about a specific type including all its members, methods, properties, and fields.
*   `AnalyzeMethod`: Gets detailed information about a method and all overloads.
*   `FindAssemblyByClassName`: Searches for an assembly by its class name in common binary folders.
*   `FindAssemblyByFileName`: Searches for an assembly by its file name in common binary folders.
*   `GetAllTypeMembers`: Gets comprehensive member information for a type, including all methods, properties, fields, events, and constructors.
*   `GetTypeConstructors`: Gets detailed information about all constructors in a type.
*   `GetTypeEvents`: Gets detailed information about all events in a type.
*   `GetTypeFields`: Gets detailed information about all fields in a type.
*   `GetTypeMethods`: Gets detailed information about all methods in a type.
*   `GetTypeProperties`: Gets detailed information about all properties in a type.

Additionally, the following tools provide higher‑level project and type analysis:

*   `GetTypesFromAssembly`: Lists public types from an assembly with metadata.
*   `GetTypeInfo`: Returns rich metadata for a single type.
*   `GetTypeHierarchy`: Shows inheritance chain and implemented interfaces.
*   `GetGenericTypeInfo`: Details generic parameters, arguments, and variance.
*   `GetTypeAttributes`: Returns custom attributes applied to a type.
*   `GetNestedTypes`: Lists nested types declared on a type.
*   `AnalyzeSolution`: Parses a `.sln` and lists contained projects.
*   `AnalyzeProject`: Parses a project file and returns metadata, refs, and outputs.
*   `GetProjectOutputPaths`: Computes output folders for a given configuration.
*   `ResolvePackageReferences`: Resolves NuGet package references to local assemblies in the cache.
*   `FindDepsJsonDependencies`: Reads deps.json files to surface runtime dependencies.

## Contributing

Contributions to the project are welcome. Please follow the existing coding style and conventions. Ensure that any new code is accompanied by corresponding unit tests.

## License

Sherlock MCP for .NET is licensed under the [MIT License](LICENSE).
