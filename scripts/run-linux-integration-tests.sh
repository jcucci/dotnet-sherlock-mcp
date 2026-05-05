#!/usr/bin/env bash
# Runs the Sherlock.MCP.IntegrationTests project inside a Linux container so the
# tests can exercise OS-specific behaviour (e.g. /proc/<pid>/fdinfo for inotify).
# Works with either docker or podman; auto-detects whichever is on PATH.
set -euo pipefail

if command -v docker >/dev/null 2>&1; then
    runtime=docker
elif command -v podman >/dev/null 2>&1; then
    runtime=podman
else
    echo "error: neither docker nor podman is installed." >&2
    exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

framework="${TEST_TFM:-net9.0}"
case "$framework" in
    net8.0) image="mcr.microsoft.com/dotnet/sdk:8.0" ;;
    net9.0) image="mcr.microsoft.com/dotnet/sdk:9.0" ;;
    net10.0) image="mcr.microsoft.com/dotnet/sdk:10.0" ;;
    *) echo "error: unsupported TFM '$framework'." >&2; exit 1 ;;
esac

echo ">> $runtime ($image) :: integration tests on $framework"

# /p:TargetFrameworks=$framework collapses the multi-TFM <TargetFrameworks>
# list to a single entry so the SDK image (which only carries one runtime /
# targeting pack) can restore and build the whole graph.
exec "$runtime" run --rm \
    -v "$repo_root":/src:ro \
    -e DOTNET_NOLOGO=1 \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    "$image" \
    bash -c "
        set -e
        mkdir -p /work
        cp -R /src/. /work
        cd /work
        dotnet test src/integration-tests/Sherlock.MCP.IntegrationTests.csproj \
            --configuration Release \
            --framework $framework \
            -p:TargetFrameworks=$framework \
            --logger 'console;verbosity=normal'
    "
