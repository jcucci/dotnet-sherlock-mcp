# dotnet-sherlock-mcp

`dotnet-sherlock-mcp` is a .NET-based server that provides a Model Context Protocol (MCP) interface for Language Learning Models (LLMs). It uses reflection to analyze .NET assemblies and provide detailed information about types, members, and their signatures. This enables LLMs to have a deep understanding of the code and provide more accurate and context-aware responses.

## Running the Server

You can run the server directly on your machine or using Docker.

### Local Execution

To run the MCP server directly, execute the following command:

```bash
dotnet run --project src/server/Sherlock.MCP.Server.csproj
```

### Running with Docker

For a more isolated and consistent environment, you can run the server inside a Docker container.

1.  **Build the Docker image:**

    ```bash
    docker build -t sherlock-mcp .
    ```

2.  **Run the Docker container:**

    ```bash
    docker run -p 5000:5000 sherlock-mcp
    ```

This will start the server and expose it on port 5000.

## Development

### Building the Project

To build the entire solution, run the following command from the root directory:

```bash
dotnet build src/Sherlock.MCP.sln
```

### Running Unit Tests

To run the unit tests, use the following command:

```bash
dotnet test src/unit-tests/Sherlock.MCP.Tests.csproj
```