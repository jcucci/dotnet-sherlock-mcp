using System.Reflection;

namespace Sherlock.MCP.Runtime.Inspection;

internal sealed class InMemoryAssemblyResolver : MetadataAssemblyResolver
{
    private readonly Dictionary<string, List<string>> _pathsByName = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryAssemblyResolver(IEnumerable<string> assemblyPaths)
    {
        foreach (var path in assemblyPaths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!_pathsByName.TryGetValue(name, out var paths))
                _pathsByName[name] = paths = [];
            paths.Add(path);
        }
    }

    public override Assembly? Resolve(MetadataLoadContext context, AssemblyName assemblyName)
    {
        if (assemblyName.Name is not { } simpleName || !_pathsByName.TryGetValue(simpleName, out var paths))
            return null;

        var winner = SelectCandidate(assemblyName, paths);
        return winner == null ? null : AssemblyLocations.LoadInMemory(context, winner);
    }

    private static string? SelectCandidate(AssemblyName requested, IEnumerable<string> paths)
    {
        var requestedToken = requested.GetPublicKeyToken() ?? [];
        (string Path, Version? Version)? sameToken = null;
        (string Path, Version? Version)? ignoringToken = null;

        foreach (var path in paths)
        {
            if (TryReadName(path) is not { } candidate) continue;
            if (!string.Equals(candidate.Name, requested.Name, StringComparison.OrdinalIgnoreCase)) continue;

            var candidateToken = candidate.GetPublicKeyToken() ?? [];
            if (requestedToken.AsSpan().SequenceEqual(candidateToken))
            {
                if (sameToken == null || candidate.Version > sameToken.Value.Version)
                    sameToken = (path, candidate.Version);
            }
            else if (sameToken == null && requestedToken.Length == 0)
            {
                if (ignoringToken == null || candidate.Version > ignoringToken.Value.Version)
                    ignoringToken = (path, candidate.Version);
            }
        }

        return (sameToken ?? ignoringToken)?.Path;
    }

    private static AssemblyName? TryReadName(string path)
    {
        try { return AssemblyName.GetAssemblyName(path); }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException) { return null; }
    }
}
