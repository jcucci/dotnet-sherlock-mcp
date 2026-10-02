using System.Runtime.InteropServices;
using System.Text.Json;
using Sherlock.MCP.Runtime.ProjectAssets;

namespace Sherlock.MCP.Runtime.Inspection;

public enum FrameworkResolutionKind
{
    ReferencePack,
    AppLocal,
    HostRuntime
}

public sealed record FrameworkPack(string Name, string Version, string Directory);

public sealed record FrameworkResolution(
    FrameworkResolutionKind Kind,
    string? TargetFramework,
    string CoreAssemblyName,
    IReadOnlyList<FrameworkPack> Packs,
    IReadOnlyList<string> MissingFrameworks,
    IReadOnlyList<string> SearchDirectories);

public static class FrameworkReferenceResolver
{
    public const string NetCoreApp = "Microsoft.NETCore.App";

    private const string HostCoreAssembly = "System.Private.CoreLib";
    private const string NetCoreCoreAssembly = "System.Runtime";
    private const string NetStandardCoreAssembly = "netstandard";
    private const string NetStandardLibrary = "NETStandard.Library";
    private const string NetStandardLibraryRef = "NETStandard.Library.Ref";
    private const string RefPackSuffix = ".Ref";
    private const string WindowsDesktopApp = "Microsoft.WindowsDesktop.App";

    private static readonly Dictionary<string, string> FrameworkAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [WindowsDesktopApp + ".WPF"] = WindowsDesktopApp,
        [WindowsDesktopApp + ".WindowsForms"] = WindowsDesktopApp
    };

    private static readonly AsyncLocal<string?> DotnetRootOverrideValue = new();

    internal static string? DotnetRootOverride
    {
        get => DotnetRootOverrideValue.Value;
        set => DotnetRootOverrideValue.Value = value;
    }

    public static FrameworkResolution Resolve(string assemblyPath, ProjectAssetsMatch? assets)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        var shortName = TargetFrameworkNames.ToShortName(TargetFrameworkReader.Read(fullPath));
        var directory = Path.GetDirectoryName(fullPath);

        if (directory != null && File.Exists(Path.Combine(directory, HostCoreAssembly + ".dll")))
            return new FrameworkResolution(FrameworkResolutionKind.AppLocal, shortName, HostCoreAssembly, [], [], []);

        return ParseShortName(shortName) switch
        {
            ("netcoreapp", var version) => ResolveNetCore(fullPath, shortName!, version, assets),
            ("netstandard", var version) => ResolveNetStandard(shortName!, version, PackageFolders(assets)),
            _ => HostRuntime(shortName, [NetCoreApp], missing: [])
        };
    }

    private static FrameworkResolution ResolveNetCore(string assemblyPath, string tfm, Version version, ProjectAssetsMatch? assets)
    {
        var frameworks = FrameworkNames(assemblyPath, assets);
        var pins = PinnedPackVersions(assets);
        var packageFolders = PackageFolders(assets);
        var packs = new List<FrameworkPack>();
        var missing = new List<string>();

        foreach (var framework in frameworks)
        {
            var packName = framework + RefPackSuffix;
            var pack = FindPack(
                packName, Path.Combine("ref", tfm), candidate => SameMajorMinor(candidate, version), pins.GetValueOrDefault(packName), packageFolders);
            if (pack != null) packs.Add(pack);
            else missing.Add(framework);
        }

        if (!packs.Any(pack => pack.Name.Equals(NetCoreApp + RefPackSuffix, StringComparison.OrdinalIgnoreCase)))
            return HostRuntime(tfm, frameworks, frameworks);

        var searchDirectories = packs.Select(pack => pack.Directory)
            .Concat(missing.Select(HostSharedDirectory).OfType<string>())
            .ToArray();
        return new FrameworkResolution(FrameworkResolutionKind.ReferencePack, tfm, NetCoreCoreAssembly, packs, missing, searchDirectories);
    }

    private static FrameworkResolution ResolveNetStandard(string tfm, Version version, IReadOnlyList<string> packageFolders)
    {
        var pack = version >= new Version(2, 1)
            ? FindPack(NetStandardLibraryRef, Path.Combine("ref", tfm), candidate => SameMajorMinor(candidate, version), pinnedVersion: null, packageFolders)
            : FindPack(NetStandardLibrary, Path.Combine("build", "netstandard2.0", "ref"), candidate => candidate.Major == 2, pinnedVersion: null, packageFolders);

        if (pack == null)
            return HostRuntime(tfm, [NetCoreApp], [version >= new Version(2, 1) ? NetStandardLibraryRef : NetStandardLibrary]);

        return new FrameworkResolution(
            FrameworkResolutionKind.ReferencePack, tfm, NetStandardCoreAssembly, [pack], [], [pack.Directory]);
    }

    private static FrameworkResolution HostRuntime(string? tfm, IReadOnlyList<string> frameworks, IReadOnlyList<string> missing) =>
        new(
            FrameworkResolutionKind.HostRuntime,
            tfm,
            HostCoreAssembly,
            [],
            missing,
            frameworks.Select(HostSharedDirectory).OfType<string>().Prepend(HostRuntimeDirectory()).Distinct(PathComparers.Comparer).ToArray());

    private static string[] FrameworkNames(string assemblyPath, ProjectAssetsMatch? assets)
    {
        var references = assets?.Assets.FrameworkFor(assets.Target.Alias)?.FrameworkReferences
            ?? RuntimeConfigFrameworks(assemblyPath);
        return references
            .Select(reference => FrameworkAliases.GetValueOrDefault(reference, reference))
            .Prepend(NetCoreApp)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> PackageFolders(ProjectAssetsMatch? assets) => assets?.Assets.PackageFolders ?? [];

    public static string RuntimeConfigStamp(string assemblyPath) =>
        string.Join(";", RuntimeConfigPaths(Path.GetFullPath(assemblyPath)).Select(FileStamp));

    public static bool PacksChanged(string assemblyPath, FrameworkResolution resolved) =>
        resolved.MissingFrameworks.Count > 0
        && !Resolve(assemblyPath, ProjectAssetsLocator.Locate(assemblyPath)).Packs.SequenceEqual(resolved.Packs);

    private static string[] RuntimeConfigFrameworks(string assemblyPath) =>
        RuntimeConfigPaths(assemblyPath).SelectMany(ReadRuntimeConfigFrameworks).ToArray();

    private static string[] RuntimeConfigPaths(string assemblyPath)
    {
        var ownConfig = Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");
        return File.Exists(ownConfig) ? [ownConfig] : SiblingRuntimeConfigs(assemblyPath);
    }

    private static string FileStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? $"{info.FullName}|{info.LastWriteTimeUtc.Ticks}|{info.Length}" : info.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    private static string[] SiblingRuntimeConfigs(string assemblyPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(assemblyPath);
            return directory is null ? [] : Directory.GetFiles(directory, "*.runtimeconfig.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> ReadRuntimeConfigFrameworks(string configPath)
    {
        try
        {
            using var stream = File.OpenRead(configPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("runtimeOptions", out var options)
                || options.ValueKind != JsonValueKind.Object)
                return [];

            var frameworks = new List<string>();
            if (options.TryGetProperty("framework", out var single) && FrameworkName(single) is { } name)
                frameworks.Add(name);
            if (options.TryGetProperty("frameworks", out var many) && many.ValueKind == JsonValueKind.Array)
                frameworks.AddRange(many.EnumerateArray().Select(FrameworkName).OfType<string>());
            return frameworks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static string? FrameworkName(JsonElement framework) =>
        framework.ValueKind == JsonValueKind.Object
        && framework.TryGetProperty("name", out var name)
        && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;

    private static Dictionary<string, string> PinnedPackVersions(ProjectAssetsMatch? assets)
    {
        var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dependencies = assets?.Assets.FrameworkFor(assets.Target.Alias)?.DownloadDependencies ?? [];
        foreach (var dependency in dependencies.Where(d => d.Id.EndsWith(RefPackSuffix, StringComparison.OrdinalIgnoreCase)))
        {
            var version = dependency.Range.Trim('[', ']', '(', ')', ' ').Split(',')[0].Trim();
            if (version.Length > 0) pins.TryAdd(dependency.Id, version);
        }
        return pins;
    }

    private static FrameworkPack? FindPack(
        string packName, string relativeDirectory, Func<Version, bool> accepts, string? pinnedVersion, IReadOnlyList<string> packageFolders)
    {
        var candidates = PackRoots(packName, packageFolders)
            .SelectMany(root => SafeEnumerateDirectories(root).Select(versionDirectory => (
                Version: Path.GetFileName(versionDirectory),
                Directory: Path.Combine(versionDirectory, relativeDirectory))))
            .Where(candidate => Directory.Exists(candidate.Directory))
            .ToArray();

        var pinned = pinnedVersion is null
            ? default
            : candidates.FirstOrDefault(candidate => candidate.Version.Equals(pinnedVersion, StringComparison.OrdinalIgnoreCase));
        var chosen = pinned.Directory != null
            ? pinned
            : candidates
                .Where(candidate => NuGetVersions.TryParse(candidate.Version) is { } version && accepts(version))
                .OrderBy(candidate => candidate.Version.Contains('-') ? 1 : 0)
                .ThenByDescending(candidate => candidate.Version, NuGetVersions.Comparer)
                .FirstOrDefault();

        return chosen.Directory is null ? null : new FrameworkPack(packName, chosen.Version, Path.GetFullPath(chosen.Directory));
    }

    private static IEnumerable<string> PackRoots(string packName, IReadOnlyList<string> packageFolders) =>
        DotnetRoots()
            .Select(root => Path.Combine(root, "packs", packName))
            .Concat(packageFolders.Append(NuGetCacheProbe.GetCacheRoot()).Select(folder => Path.Combine(folder, packName.ToLowerInvariant())))
            .Select(Path.GetFullPath)
            .Distinct(PathComparers.Comparer);

    private static IEnumerable<string> DotnetRoots()
    {
        if (!string.IsNullOrWhiteSpace(DotnetRootOverride))
            return [DotnetRootOverride];

        var roots = new List<string> { HostDotnetRoot() };
        var environmentRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(environmentRoot)) roots.Add(environmentRoot);
        return roots.Select(Path.GetFullPath).Distinct(PathComparers.Comparer);
    }

    public static string HostRuntimeDirectory() =>
        Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string HostDotnetRoot() => Path.GetFullPath(Path.Combine(HostRuntimeDirectory(), "..", "..", ".."));

    private static string? HostSharedDirectory(string framework)
    {
        if (framework.Equals(NetCoreApp, StringComparison.OrdinalIgnoreCase)) return HostRuntimeDirectory();

        var hostVersion = Path.GetFileName(HostRuntimeDirectory());
        var frameworkRoot = Path.Combine(HostDotnetRoot(), "shared", framework);
        var exact = Path.Combine(frameworkRoot, hostVersion);
        if (Directory.Exists(exact)) return exact;

        var hostMajor = NuGetVersions.TryParse(hostVersion)?.Major;
        return SafeEnumerateDirectories(frameworkRoot)
            .Where(directory => NuGetVersions.TryParse(Path.GetFileName(directory))?.Major == hostMajor)
            .OrderByDescending(directory => Path.GetFileName(directory), NuGetVersions.Comparer)
            .FirstOrDefault();
    }

    private static (string Family, Version Version)? ParseShortName(string? shortName)
    {
        if (shortName is null) return null;

        if (shortName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            return Version.TryParse(shortName["netstandard".Length..], out var standard) ? ("netstandard", standard) : null;
        if (shortName.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
            return Version.TryParse(shortName["netcoreapp".Length..], out var core) ? ("netcoreapp", core) : null;
        if (shortName.StartsWith("net", StringComparison.OrdinalIgnoreCase) && shortName.Contains('.'))
            return Version.TryParse(shortName["net".Length..], out var net) && net.Major >= 5 ? ("netcoreapp", net) : null;
        return null;
    }

    private static bool SameMajorMinor(Version candidate, Version target) =>
        candidate.Major == target.Major && candidate.Minor == target.Minor;

    private static string[] SafeEnumerateDirectories(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.GetDirectories(directory) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
