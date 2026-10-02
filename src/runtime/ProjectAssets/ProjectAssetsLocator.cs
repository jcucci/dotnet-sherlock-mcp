using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Runtime.ProjectAssets;

public sealed record ProjectAssetsMatch(ProjectAssetsFile Assets, AssetsTarget Target, string AssemblyPath)
{
    public IReadOnlyList<string> ResolveDependencyPaths()
    {
        var paths = new List<string>();
        foreach (var library in Target.Libraries)
        {
            if (library.IsPackage)
                paths.AddRange(Assets.ResolvePackageAssets(library, library.CompileAssets.Concat(library.RuntimeAssets)));
            else if (library.IsProject && ProjectAssetsLocator.ResolveProjectOutput(this, library) is { } output)
                paths.Add(output);
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public static class ProjectAssetsLocator
{
    public const string AssetsFileName = "project.assets.json";

    private const string BinDirectoryName = "bin";
    private const string ObjDirectoryName = "obj";
    private const string ArtifactsDirectoryName = "artifacts";
    private const int MaxDepthBelowBin = 6;

    public static string? FindAssetsFile(string assemblyPath) =>
        FindLayout(assemblyPath) is { } layout && File.Exists(layout.AssetsPath) ? layout.AssetsPath : null;

    public static string AssetsStamp(string assemblyPath)
    {
        try
        {
            if (FindAssetsFile(assemblyPath) is not { } assetsPath) return "";
            var info = new FileInfo(assetsPath);
            return info.Exists ? $"{info.FullName}|{info.LastWriteTimeUtc.Ticks}|{info.Length}" : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "";
        }
    }

    public static string AssetsPathForProject(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        if (Path.GetFileName(fullPath).Equals(AssetsFileName, StringComparison.OrdinalIgnoreCase)) return fullPath;

        var isDirectory = Directory.Exists(fullPath);
        var projectDirectory = isDirectory ? fullPath : Path.GetDirectoryName(fullPath) ?? fullPath;
        var standard = Path.Combine(projectDirectory, ObjDirectoryName, AssetsFileName);
        if (File.Exists(standard)) return standard;

        var projectName = isDirectory ? ProjectNameInDirectory(projectDirectory) : Path.GetFileNameWithoutExtension(fullPath);
        return FindArtifactsAssets(projectDirectory, projectName) ?? standard;
    }

    private static string ProjectNameInDirectory(string directory)
    {
        var projects = Directory.EnumerateFiles(directory, "*.*proj", SearchOption.TopDirectoryOnly).Take(2).ToArray();
        return projects.Length == 1 ? Path.GetFileNameWithoutExtension(projects[0]) : Path.GetFileName(directory);
    }

    private static string? FindArtifactsAssets(string projectDirectory, string projectName)
    {
        for (var directory = new DirectoryInfo(projectDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ArtifactsDirectoryName, ObjDirectoryName, projectName, AssetsFileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static ProjectAssetsMatch? Locate(string assemblyPath)
    {
        try
        {
            if (FindLayout(assemblyPath) is not { } layout || !File.Exists(layout.AssetsPath)) return null;

            var assets = ProjectAssetsReader.Read(layout.AssetsPath);
            return SelectTarget(assets, layout.PivotSegments, assemblyPath) is { } target
                ? new ProjectAssetsMatch(assets, target, Path.GetFullPath(assemblyPath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    internal static string? ResolveProjectOutput(ProjectAssetsMatch match, AssetsLibrary library)
    {
        var fileName = library.CompileAssets.Concat(library.RuntimeAssets).Select(Path.GetFileName).FirstOrDefault(name => !string.IsNullOrEmpty(name));
        if (fileName is null || match.Assets.ResolveProjectFile(library) is not { } referencedProject) return null;
        if (FindLayout(match.AssemblyPath) is not { } layout) return null;

        var outputRoot = layout.IsArtifacts
            ? Path.Combine(layout.BinDirectory, Path.GetFileNameWithoutExtension(referencedProject))
            : Path.Combine(Path.GetDirectoryName(referencedProject) ?? "", BinDirectoryName);
        var pivot = layout.IsArtifacts ? layout.PivotSegments.Skip(1).ToArray() : layout.PivotSegments.ToArray();

        return ReferencedPivots(pivot, match.Target, library.Framework, layout.IsArtifacts)
            .Select(candidate => Path.GetFullPath(Path.Combine([outputRoot, .. candidate, fileName])))
            .FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string[]> ReferencedPivots(string[] pivot, AssetsTarget consumer, string? referencedFramework, bool isArtifacts)
    {
        var framework = referencedFramework ?? consumer.Alias;
        string[] Rewrite(Func<string, bool> keep) => pivot
            .Select(segment => string.Join('_', segment.Split('_')
                .Where(keep)
                .Select(token => token.Equals(consumer.Alias, StringComparison.OrdinalIgnoreCase) ? framework : token)))
            .Where(segment => segment.Length > 0)
            .ToArray();

        bool IsRid(string token) => consumer.RuntimeIdentifier != null && token.Equals(consumer.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase);
        bool IsTfm(string token) => token.Equals(consumer.Alias, StringComparison.OrdinalIgnoreCase);

        string[] Qualify(Func<string, bool> keep) => pivot
            .Select((segment, index) =>
            {
                var tokens = segment.Split('_').Where(keep).ToArray();
                return index == 0 && tokens.Length > 0 ? string.Join('_', [tokens[0], framework, .. tokens.Skip(1)]) : string.Join('_', tokens);
            })
            .Where(segment => segment.Length > 0)
            .ToArray();

        yield return Rewrite(_ => true);
        if (consumer.RuntimeIdentifier != null) yield return Rewrite(token => !IsRid(token));
        if (!isArtifacts) yield break;

        yield return Rewrite(token => !IsRid(token) && !IsTfm(token));
        if (pivot.SelectMany(segment => segment.Split('_')).Any(IsTfm)) yield break;

        yield return Qualify(_ => true);
        if (consumer.RuntimeIdentifier != null) yield return Qualify(token => !IsRid(token));
    }

    private static AssetsTarget? SelectTarget(ProjectAssetsFile assets, IReadOnlyList<string> pivotSegments, string assemblyPath)
    {
        var tokens = pivotSegments.SelectMany(segment => segment.Split('_')).ToArray();
        var alias = assets.Aliases.FirstOrDefault(candidate => tokens.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            ?? AliasFromAttribute(assets, assemblyPath)
            ?? (assets.Aliases.Count == 1 ? assets.Aliases[0] : null);
        if (alias is null) return null;

        var runtimeTarget = assets.Targets.FirstOrDefault(target =>
            target.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)
            && target.RuntimeIdentifier is { } rid
            && tokens.Contains(rid, StringComparer.OrdinalIgnoreCase));
        return runtimeTarget ?? assets.FindTarget(alias);
    }

    private static string? AliasFromAttribute(ProjectAssetsFile assets, string assemblyPath) =>
        TargetFrameworkNames.ToShortName(TargetFrameworkReader.Read(assemblyPath)) is { } shortName
            ? TargetFrameworkNames.BestMatch(assets.Aliases, shortName)
            : null;

    private static Layout? FindLayout(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        if (NuGetCacheProbe.TryParseCacheLayout(fullPath, out _, out _)) return null;

        var pivot = new List<string>();
        var directory = new DirectoryInfo(Path.GetDirectoryName(fullPath) ?? fullPath);
        while (directory?.Parent is { } parent && pivot.Count <= MaxDepthBelowBin)
        {
            if (directory.Name.Equals(BinDirectoryName, StringComparison.OrdinalIgnoreCase))
                return CreateLayout(directory, pivot);

            pivot.Insert(0, directory.Name);
            directory = parent;
        }
        return null;
    }

    private static Layout? CreateLayout(DirectoryInfo binDirectory, List<string> pivot)
    {
        var root = binDirectory.Parent!;
        var standard = Path.Combine(root.FullName, ObjDirectoryName, AssetsFileName);
        if (File.Exists(standard) || pivot.Count == 0) return new Layout(standard, binDirectory.FullName, pivot, IsArtifacts: false);

        var artifacts = Path.Combine(root.FullName, ObjDirectoryName, pivot[0], AssetsFileName);
        return File.Exists(artifacts)
            ? new Layout(artifacts, binDirectory.FullName, pivot, IsArtifacts: true)
            : new Layout(standard, binDirectory.FullName, pivot, IsArtifacts: false);
    }

    private sealed record Layout(string AssetsPath, string BinDirectory, IReadOnlyList<string> PivotSegments, bool IsArtifacts);
}
