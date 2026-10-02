using System.Collections.Concurrent;
using System.Text.Json;

namespace Sherlock.MCP.Runtime.ProjectAssets;

public static class ProjectAssetsReader
{
    private const string Placeholder = "_._";

    private static readonly ConcurrentDictionary<string, CachedAssets> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ProjectAssetsFile Read(string assetsPath)
    {
        var fullPath = Path.GetFullPath(assetsPath);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException($"Assets file not found: {fullPath}", fullPath);

        var stamp = (info.LastWriteTimeUtc.Ticks, info.Length);
        if (Cache.TryGetValue(fullPath, out var cached) && cached.Stamp == stamp) return cached.File;

        var file = Parse(fullPath);
        Cache[fullPath] = new CachedAssets(stamp, file);
        return file;
    }

    internal static void ResetCache() => Cache.Clear();

    private static ProjectAssetsFile Parse(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            using var document = JsonDocument.Parse(stream);
            return Parse(fullPath, document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"'{fullPath}' is not a valid project.assets.json file: {ex.Message}", ex);
        }
    }

    private static ProjectAssetsFile Parse(string fullPath, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"'{fullPath}' is not a valid project.assets.json file: the root is not an object.");

        var project = Property(root, "project");
        var restore = Property(project, "restore");
        var frameworks = Property(project, "frameworks");
        var aliasesByShortName = AliasesByShortName(frameworks);
        var libraryPaths = LibraryPaths(Property(root, "libraries"));

        return new ProjectAssetsFile(
            AssetsPath: fullPath,
            ProjectName: Text(restore, "projectName"),
            ProjectPath: Text(restore, "projectPath"),
            PackageFolders: Names(Property(root, "packageFolders")),
            Targets: Objects(Property(root, "targets"))
                .Select(target => ParseTarget(target.Name, target.Value, aliasesByShortName, libraryPaths))
                .ToArray(),
            DirectDependencies: Objects(frameworks).ToDictionary(
                framework => AliasOf(framework.Name, framework.Value),
                framework => (IReadOnlyList<AssetsDependency>)ParseDirectDependencies(framework.Value),
                StringComparer.OrdinalIgnoreCase));
    }

    private static AssetsTarget ParseTarget(
        string key,
        JsonElement target,
        Dictionary<string, string> aliasesByShortName,
        Dictionary<string, string> libraryPaths)
    {
        var separator = key.IndexOf('/');
        var framework = separator < 0 ? key : key[..separator];
        var runtimeIdentifier = separator < 0 ? null : key[(separator + 1)..];
        var shortName = TargetFrameworkNames.ToShortName(framework) ?? framework;
        var alias = aliasesByShortName.TryGetValue(shortName, out var mapped)
            ? mapped
            : TargetFrameworkNames.AliasForTargetKey(aliasesByShortName.Values.Distinct(StringComparer.OrdinalIgnoreCase), shortName) ?? framework;

        var libraries = Objects(target)
            .Select(library => ParseLibrary(library.Name, library.Value, libraryPaths))
            .OfType<AssetsLibrary>()
            .ToArray();
        return new AssetsTarget(alias, runtimeIdentifier, libraries);
    }

    private static AssetsLibrary? ParseLibrary(string key, JsonElement library, Dictionary<string, string> libraryPaths)
    {
        var separator = key.IndexOf('/');
        if (separator <= 0) return null;

        return new AssetsLibrary(
            Id: key[..separator],
            Version: key[(separator + 1)..],
            Type: Text(library, "type") ?? "package",
            Path: libraryPaths.GetValueOrDefault(key),
            Dependencies: Objects(Property(library, "dependencies"))
                .Select(dependency => new AssetsDependency(dependency.Name, dependency.Value.ValueKind == JsonValueKind.String ? dependency.Value.GetString() ?? "" : ""))
                .ToArray(),
            CompileAssets: Assets(Property(library, "compile")),
            RuntimeAssets: Assets(Property(library, "runtime")),
            Framework: Text(library, "framework") is { } framework ? TargetFrameworkNames.ToShortName(framework) ?? framework : null);
    }

    private static AssetsDependency[] ParseDirectDependencies(JsonElement framework) =>
        Objects(Property(framework, "dependencies"))
            .Where(dependency => !string.Equals(Text(dependency.Value, "target"), "Reference", StringComparison.OrdinalIgnoreCase))
            .Select(dependency => new AssetsDependency(dependency.Name, Text(dependency.Value, "version") ?? ""))
            .ToArray();

    private static Dictionary<string, string> AliasesByShortName(JsonElement frameworks)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var framework in Objects(frameworks))
        {
            var alias = AliasOf(framework.Name, framework.Value);
            var shortName = TargetFrameworkNames.ToShortName(Text(framework.Value, "framework")) ?? framework.Name;
            aliases.TryAdd(shortName, alias);
            aliases.TryAdd(alias, alias);
        }
        return aliases;
    }

    private static string AliasOf(string name, JsonElement framework) => Text(framework, "targetAlias") ?? name;

    private static Dictionary<string, string> LibraryPaths(JsonElement libraries) =>
        Objects(libraries)
            .Select(library => (library.Name, Path: Text(library.Value, "path")))
            .Where(library => library.Path != null)
            .ToDictionary(library => library.Name, library => library.Path!, StringComparer.OrdinalIgnoreCase);

    private static string[] Assets(JsonElement assets) =>
        Names(assets).Where(asset => !Path.GetFileName(asset).Equals(Placeholder, StringComparison.Ordinal)).ToArray();

    private static string[] Names(JsonElement element) => Objects(element).Select(property => property.Name).ToArray();

    private static JsonProperty[] Objects(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object ? element.EnumerateObject().ToArray() : [];

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string? Text(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private sealed record CachedAssets((long Ticks, long Length) Stamp, ProjectAssetsFile File);
}
