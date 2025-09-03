# Gemini Code Assistant Project Overview

This document provides a comprehensive overview of the `dotnet-sherlock-mcp` project, designed to facilitate effective collaboration with the Gemini Code Assistant.

## Project Purpose

The `dotnet-sherlock-mcp` project is a .NET-based server that provides a Model Context Protocol (MCP) interface for Language Learning Models (LLMs). It uses reflection to analyze .NET assemblies and provide detailed information about types, members, and their signatures. This enables LLMs to have a deep understanding of the code and provide more accurate and context-aware responses.

## Project Structure

The project is organized into the following components:

- **`Sherlock.MCP.Server`**: The main executable project that hosts the MCP server. It utilizes the `Microsoft.Extensions.Hosting` library for robust server management and the `ModelContextProtocol` library for MCP communication.
- **`Sherlock.MCP.Runtime`**: A class library that contains the core logic for assembly analysis. It uses `System.Reflection` and `AssemblyLoadContext` for safe, controlled inspection of assemblies.
- **`Sherlock.MCP.Tests`**: The unit testing project, which uses `xunit` as the testing framework. It ensures the reliability and correctness of the `Sherlock.MCP.Runtime` components.
- **`Sherlock.MCP.Example`**: An example console application that demonstrates how to use the `Sherlock.MCP.Runtime` library.

## Key Technologies

- **.NET 9.0**: The project is built on the latest version of the .NET platform.
- **C#**: The primary programming language used.
- **xUnit**: The framework used for unit tests.
- **Microsoft.Extensions.Hosting**: Used for hosting the server.
- **ModelContextProtocol**: The library used for MCP communication.
- **System.Reflection / AssemblyLoadContext**: Used for assembly inspection and controlled loading.

## Development Workflow

### Building the Project

To build the entire solution, run the following command from the root directory:

```bash
dotnet build src/Sherlock.MCP.sln
```

### Running the Server

To run the MCP server, execute the following command:

```bash
dotnet run --project src/server/Sherlock.MCP.Server.csproj
```

### Running Unit Tests

To run the unit tests, use the following command:

```bash
dotnet test src/unit-tests/Sherlock.MCP.Tests.csproj
```

## How to Contribute

Contributions to the project are welcome. Please follow the existing coding style and conventions. Ensure that any new code is accompanied by corresponding unit tests.

