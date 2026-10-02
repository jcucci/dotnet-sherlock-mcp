namespace Sherlock.MCP.Server.Shared;

internal sealed record DependencyScope(string Stamp, string[]? SearchDirectories, string? Error)
{
    public static DependencyScope Build(string assemblyPath, string[]? additionalAssemblies)
    {
        if (additionalAssemblies is not { Length: > 0 })
            return new DependencyScope(CacheKeyHelper.FileStamp(assemblyPath), null, null);

        var scope = AssemblyScope.BuildAndValidate(assemblyPath, additionalAssemblies);
        if (scope.Error != null)
            return new DependencyScope("", null, scope.Error);

        var directories = scope.Paths
            .Skip(1)
            .Select(Path.GetDirectoryName)
            .OfType<string>()
            .Where(directory => directory.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new DependencyScope(CacheKeyHelper.ScopeStamp(scope.Paths), directories, null);
    }
}
