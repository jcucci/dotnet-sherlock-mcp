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

Once installed, you can start the MCP server by running the following command:

```bash
sherlock-mcp
```

The server will start and listen for connections from LLMs.

### Example: Using the Tool with an LLM

Here's an example of how you can use Sherlock MCP for .NET with an LLM to get information about a constructor:

## Available Tools

Sherlock MCP for .NET provides the following tools for analyzing .NET assemblies:

*   `AnalyzeAssembly`: Analyzes a .NET assembly and returns information about all public types, their members, and metadata.
*   `AnalyzeType`: Gets detailed information about a specific type including all its members, methods, properties, and fields.
*   `FindAssemblyByClassName`: Searches for an assembly by its class name in common binary folders.
*   `FindAssemblyByFileName`: Searches for an assembly by its file name in common binary folders.
*   `GetAllTypeMembers`: Gets comprehensive member information for a type, including all methods, properties, fields, events, and constructors.
*   `GetTypeConstructors`: Gets detailed information about all constructors in a type.
*   `GetTypeEvents`: Gets detailed information about all events in a type.
*   `GetTypeFields`: Gets detailed information about all fields in a type.
*   `GetTypeMethods`: Gets detailed information about all methods in a type.
*   `GetTypeProperties`: Gets detailed information about all properties in a type.

## Contributing

Contributions to the project are welcome. Please follow the existing coding style and conventions. Ensure that any new code is accompanied by corresponding unit tests.

## License

Sherlock MCP for .NET is licensed under the [MIT License](LICENSE).
