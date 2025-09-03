# Repository Guidelines

## Project Structure & Module Organization
- `src/Sherlock.MCP.sln`: Solution file.
- `src/server`: MCP server (`Sherlock.MCP.Server`) and entry point.
- `src/runtime`: Core analysis services and contracts (`Sherlock.MCP.Runtime`).
- `src/unit-tests`: xUnit tests (e.g., `MemberAnalysisServiceTests.cs`).
- `src/example`: Minimal console example.
- `src/docs`: Roadmap and supporting docs.

## Build, Test, and Development Commands
- `dotnet restore && dotnet build src/Sherlock.MCP.sln`: Restore and build (net9.0).
- `dotnet run --project src/server/Sherlock.MCP.Server.csproj`: Run MCP server over stdio.
- `dotnet test src/unit-tests/Sherlock.MCP.Tests.csproj`: Run unit tests.
- `dotnet test --collect:"XPlat Code Coverage"`: Run tests with coverage (coverlet).
- `./tool-install.sh` / `./tool-uninstall.sh`: Pack and install/uninstall the global tool (`sherlock-mcp`).

## Coding Style & Naming Conventions
- C# 12/.NET 9, nullable enabled, implicit usings on; prefer file‑scoped namespaces.
- Indentation: 4 spaces; wrap at ~120 chars where practical.
- Naming: PascalCase for types/methods/properties; camelCase for locals/parameters; interfaces start with `I`.
- Prefer expression-bodied members when clear; use named parameters when argument meaning isn’t obvious.
- Keep code self‑documenting; avoid redundant inline comments. Braces optional for single‑line blocks if readability holds.

## Whitespace & Formatting
- Use a single blank line between the `using` block and the file‑scoped `namespace`.
- Use a single blank line after the file‑scoped `namespace` before the first type/member.
- Keep exactly one blank line between consecutive members inside a type (methods, properties, events, nested types) to separate logical units.
- Within longer methods, group related steps and separate them with a single blank line (setup, processing, return) — no multiple consecutive blank lines.
- Ensure files end with a trailing newline; avoid trailing whitespace on lines.
- Maintain one top‑level type per file; nested types are allowed within their parent file.

AI contributors: when generating or editing code, please preserve and improve whitespace per the above. Favor readability by adding minimal, consistent blank lines between logical sections while avoiding extra vertical padding.

## Testing Guidelines
- Framework: xUnit; files in `src/unit-tests` named `*Tests.cs` (e.g., `MemberAnalysisServiceTests`).
- Test naming: `MethodOrUnit_Scenario_ExpectedOutcome`.
- Aim to cover new public API surface; include edge cases (binding flags, generics, by‑ref/arrays).
- Run locally with `dotnet test`; include coverage on substantial changes.

## Commit & Pull Request Guidelines
- Messages: imperative mood; group logical changes. Conventional Commits allowed (e.g., `feat:`, `fix:`) as seen in history.
- PRs: clear description, rationale, and scope; link issues; include test updates and example usage when relevant; note breaking changes.
- Keep diffs focused; update README or docs under `src/docs` when behavior or tooling changes.

## Security & Configuration Tips
- Assembly loading analyzes paths supplied by users; avoid pointing at untrusted binaries.
- Require .NET 9 SDK. Use the global tool (`sherlock-mcp`) or `dotnet run` during development.
