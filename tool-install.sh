 #!/usr/bin/env sh
dotnet pack src/server/Sherlock.MCP.Server.csproj -c Release && \
dotnet tool install --global --add-source ./src/server/bin/Release Sherlock.MCP.Server