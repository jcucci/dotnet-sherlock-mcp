# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET MCP (Model Context Protocol) server called "dotnet-sherlock-mcp" that uses reflection to provide LLMs with knowledge about .NET classes, their members, and their signatures. The project is currently in initial setup phase with only a Visual Studio solution file present.

## Project Structure

- `src/Sherlock.MCP.sln` - Visual Studio solution file
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

This MCP server provides LLMs with comprehensive .NET reflection capabilities through two key architectural decisions:

### Assembly Discovery First Approach
Since the MCP server runs as a separate process, it cannot access the client's loaded assemblies. Instead, it provides tools for clients to:
1. Discover assembly locations using various strategies (project analysis, standard paths, NuGet cache)
2. Load specific assemblies by path for analysis
3. Analyze types and members within those assemblies

### Tool Categories
- **Assembly Discovery**: Find assemblies containing specific types or in specific locations
- **Project Analysis**: Parse .csproj/.sln files to resolve assembly references
- **Type Analysis**: Get detailed information about types, inheritance, generics, attributes
- **Member Analysis**: Analyze methods, properties, fields, events, constructors
- **Usage & Documentation**: Generate examples and extract documentation

The server uses the `Microsoft.Extensions.Hosting` framework with MCP protocol support via `ModelContextProtocol.Server` package.