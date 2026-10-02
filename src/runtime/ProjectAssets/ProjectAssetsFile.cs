namespace Sherlock.MCP.Runtime.ProjectAssets;

public sealed record AssetsDependency(string Id, string Range);

public sealed record AssetsLibrary(
    string Id,
    string Version,
    string Type,
    string? Path,
    IReadOnlyList<AssetsDependency> Dependencies,
    IReadOnlyList<string> CompileAssets,
    IReadOnlyList<string> RuntimeAssets,
    string? Framework = null)
{
    public bool IsPackage => Type.Equals("package", StringComparison.OrdinalIgnoreCase);

    public bool IsProject => Type.Equals("project", StringComparison.OrdinalIgnoreCase);
}

public sealed record AssetsTarget(string Alias, string? RuntimeIdentifier, IReadOnlyList<AssetsLibrary> Libraries)
{
    public string Key => RuntimeIdentifier is null ? Alias : $"{Alias}/{RuntimeIdentifier}";
}

public sealed record ProjectAssetsFile(
    string AssetsPath,
    string? ProjectName,
    string? ProjectPath,
    IReadOnlyList<string> PackageFolders,
    IReadOnlyList<AssetsTarget> Targets,
    IReadOnlyDictionary<string, IReadOnlyList<AssetsDependency>> DirectDependencies)
{
    public IReadOnlyList<string> Aliases =>
        Targets.Select(target => target.Alias).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public AssetsTarget? FindTarget(string alias, string? runtimeIdentifier = null) =>
        Targets.FirstOrDefault(target =>
            target.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.RuntimeIdentifier, runtimeIdentifier, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<AssetsDependency> DirectDependenciesFor(string alias) =>
        DirectDependencies.TryGetValue(alias, out var dependencies) ? dependencies : [];

    public IReadOnlyList<string> ResolvePackageAssets(AssetsLibrary library, IEnumerable<string> assets)
    {
        if (!library.IsPackage || string.IsNullOrWhiteSpace(library.Path)) return [];

        var resolved = new List<string>();
        foreach (var asset in assets)
        {
            var match = PackageFolders
                .Select(folder => System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, library.Path, asset)))
                .FirstOrDefault(File.Exists);
            if (match != null) resolved.Add(match);
        }
        return resolved;
    }

    public string? ResolveProjectFile(AssetsLibrary library)
    {
        if (!library.IsProject || string.IsNullOrWhiteSpace(library.Path)) return null;

        var baseDirectory = System.IO.Path.GetDirectoryName(ProjectPath)
            ?? System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(AssetsPath));
        return baseDirectory is null ? null : System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, library.Path));
    }
}
