using Sherlock.MCP.Runtime.Handles;

namespace Sherlock.MCP.Server.Shared;

internal static class AssemblyScope
{
    internal readonly struct ScopeResult
    {
        public string[] Paths { get; init; }
        public string? Error { get; init; }
    }

    internal readonly struct TargetResult
    {
        public string Path { get; init; }
        public string[]? AdditionalAssemblies { get; init; }
        public string? Error { get; init; }
    }

    public static TargetResult ResolveTarget(
        IAssemblyHandleRegistry handles,
        string? assemblyPath,
        string? assemblyHandle,
        string[]? additionalAssemblies = null)
    {
        var hasPath = !string.IsNullOrWhiteSpace(assemblyPath);
        var hasHandle = !string.IsNullOrWhiteSpace(assemblyHandle);
        if (hasPath && hasHandle)
            return Failed(JsonHelpers.Error("InvalidArgument", "Pass either assemblyPath or assemblyHandle, not both"));
        if (hasHandle)
            return FromHandle(handles.Resolve(assemblyHandle!), assemblyHandle!.Trim(), additionalAssemblies);
        if (!hasPath)
            return Failed(JsonHelpers.Error("InvalidArgument", "assemblyPath or assemblyHandle is required"));
        if (!File.Exists(assemblyPath))
            return Failed(ToolErrors.AssemblyNotFound(assemblyPath!));
        return new TargetResult { Path = assemblyPath!, AdditionalAssemblies = additionalAssemblies };
    }

    private static TargetResult FromHandle(HandleLookup lookup, string handleId, string[]? additionalAssemblies) => lookup switch
    {
        { Status: HandleStatus.Resolved, Handle: { } handle } => new TargetResult
        {
            Path = handle.AssemblyPath,
            AdditionalAssemblies = MergeAdditional(handle.AdditionalAssemblies, additionalAssemblies)
        },
        { Status: HandleStatus.Stale, Handle: { } handle } => Failed(ToolErrors.StaleAssemblyHandle(handle)),
        _ => Failed(ToolErrors.UnknownAssemblyHandle(handleId))
    };

    private static string[]? MergeAdditional(IReadOnlyList<string> fromHandle, string[]? explicitPaths)
    {
        var merged = fromHandle.Concat(explicitPaths ?? []).ToArray();
        return merged.Length > 0 ? merged : null;
    }

    private static TargetResult Failed(string error) => new() { Path = "", Error = error };

    public static ScopeResult BuildAndValidate(string assemblyPath, string[]? additionalAssemblies)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return new ScopeResult { Error = JsonHelpers.Error("InvalidArgument", "assemblyPath is required") };
        if (!File.Exists(assemblyPath))
            return new ScopeResult { Error = ToolErrors.AssemblyNotFound(assemblyPath) };

        var pathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seen = new HashSet<string>(pathComparer);
        var paths = new List<string>();

        var normalizedPrimary = Path.GetFullPath(assemblyPath);
        if (seen.Add(normalizedPrimary)) paths.Add(normalizedPrimary);

        if (additionalAssemblies != null)
        {
            foreach (var extra in additionalAssemblies)
            {
                if (string.IsNullOrWhiteSpace(extra)) continue;
                if (!File.Exists(extra))
                    return new ScopeResult { Error = ToolErrors.AssemblyNotFound(extra) };
                var normalizedExtra = Path.GetFullPath(extra);
                if (seen.Add(normalizedExtra)) paths.Add(normalizedExtra);
            }
        }
        return new ScopeResult { Paths = paths.ToArray() };
    }
}
