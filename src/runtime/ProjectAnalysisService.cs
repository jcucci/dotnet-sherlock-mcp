using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

namespace Sherlock.MCP.Runtime;

public class ProjectAnalysisService : IProjectAnalysisService
{
    private static readonly Regex SolutionProjectRegex = new(
        @"Project\(""\{(?<TypeGuid>[A-F0-9\-]+)\}""\)\s*=\s*""(?<Name>[^""]+)""\s*,\s*""(?<Path>[^""]+)""\s*,\s*""\{(?<Guid>[A-F0-9\-]+)\}""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );
    private static readonly string[] SupportedProjectExtensions = { ".csproj", ".vbproj", ".fsproj" };

    public async Task<ProjectInfo[]> AnalyzeSolutionFileAsync(string solutionFilePath)
    {
        if (!File.Exists(solutionFilePath))
        {
            throw new FileNotFoundException($"Solution file not found: {solutionFilePath}");
        }
        var solutionDirectory = Path.GetDirectoryName(solutionFilePath) ?? string.Empty;
        var content = await File.ReadAllTextAsync(solutionFilePath);
        var projects = new List<ProjectInfo>();
        var matches = SolutionProjectRegex.Matches(content);
        foreach (Match match in matches)
        {
            var name = match.Groups["Name"].Value;
            var relativePath = match.Groups["Path"].Value;
            var projectGuid = match.Groups["Guid"].Value;
            var projectTypeGuid = match.Groups["TypeGuid"].Value;
            if (!SupportedProjectExtensions.Any(ext => relativePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, relativePath));
            projects.Add(new ProjectInfo(
                name,
                relativePath,
                fullPath,
                projectGuid,
                projectTypeGuid
            ));
        }
        return projects.ToArray();
    }

    public async Task<ProjectAnalysisResult> AnalyzeProjectFileAsync(string projectFilePath)
    {
        if (!File.Exists(projectFilePath))
        {
            throw new FileNotFoundException($"Project file not found: {projectFilePath}");
        }
        var projectDirectory = Path.GetDirectoryName(projectFilePath) ?? string.Empty;
        var content = await File.ReadAllTextAsync(projectFilePath);
        var doc = XDocument.Parse(content);
        var propertyGroups = doc.Descendants("PropertyGroup");
        var targetFramework = GetPropertyValue(propertyGroups, "TargetFramework") ?? "net9.0";
        var targetFrameworks = GetPropertyValue(propertyGroups, "TargetFrameworks")?.Split(';') ?? new[] { targetFramework };
        var outputType = GetPropertyValue(propertyGroups, "OutputType") ?? "Library";
        var assemblyName = GetPropertyValue(propertyGroups, "AssemblyName") ?? Path.GetFileNameWithoutExtension(projectFilePath);
        var rootNamespace = GetPropertyValue(propertyGroups, "RootNamespace") ?? assemblyName;
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(pr =>
            {
                var includePath = pr.Attribute("Include")?.Value ?? string.Empty;
                var fullPath = Path.GetFullPath(Path.Combine(projectDirectory, includePath));
                var name = Path.GetFileNameWithoutExtension(includePath);
                return new ProjectReference(name, includePath, fullPath);
            })
            .ToArray();
        var packageReferences = doc.Descendants("PackageReference")
            .Select(pr => new PackageReference(
                pr.Attribute("Include")?.Value ?? string.Empty,
                pr.Attribute("Version")?.Value ?? GetChildElementValue(pr, "Version") ?? string.Empty,
                Array.Empty<string>(),
                false
            ))
            .ToArray();
        var outputPaths = await GetProjectOutputPathsAsync(projectFilePath);
        return new ProjectAnalysisResult(
            assemblyName,
            targetFramework,
            targetFrameworks,
            outputType,
            assemblyName,
            rootNamespace,
            projectReferences,
            packageReferences,
            outputPaths
        );
    }

    public async Task<string[]> GetProjectOutputPathsAsync(string projectFilePath, string? configuration = null)
    {
        if (!File.Exists(projectFilePath))
        {
            throw new FileNotFoundException($"Project file not found: {projectFilePath}");
        }
        var projectDirectory = Path.GetDirectoryName(projectFilePath) ?? string.Empty;
        var content = await File.ReadAllTextAsync(projectFilePath);
        var doc = XDocument.Parse(content);
        var outputPaths = new List<string>();
        var configurations = configuration != null ? new[] { configuration } : new[] { "Debug", "Release" };
        var propertyGroups = doc.Descendants("PropertyGroup");
        var targetFramework = GetPropertyValue(propertyGroups, "TargetFramework");
        var targetFrameworks = GetPropertyValue(propertyGroups, "TargetFrameworks")?.Split(';') ??
                              (targetFramework != null ? new[] { targetFramework } : new[] { "net9.0" });
        var customOutputPath = GetPropertyValue(propertyGroups, "OutputPath");
        foreach (var config in configurations)
        {
            foreach (var framework in targetFrameworks)
            {
                string outputPath;
                if (!string.IsNullOrEmpty(customOutputPath))
                {
                    outputPath = Path.GetFullPath(Path.Combine(projectDirectory, customOutputPath));
                }
                else
                {
                    outputPath = Path.Combine(projectDirectory, "bin", config, framework);
                    outputPath = Path.GetFullPath(outputPath);
                }
                if (!outputPaths.Contains(outputPath))
                {
                    outputPaths.Add(outputPath);
                }
            }
        }
        return outputPaths.ToArray();
    }

    public async Task<PackageReference[]> ResolvePackageReferencesAsync(string projectFilePath, string? packageName = null)
    {
        var analysisResult = await AnalyzeProjectFileAsync(projectFilePath);
        var resolvedPackages = new List<PackageReference>();
        var packagesToResolve = packageName != null
            ? analysisResult.PackageReferences.Where(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase))
            : analysisResult.PackageReferences;
        foreach (var package in packagesToResolve)
        {
            var assemblyPaths = await ResolvePackageAssemblyPathsAsync(package, analysisResult.TargetFrameworks);
            resolvedPackages.Add(package with
            {
                AssemblyPaths = assemblyPaths,
                IsResolved = assemblyPaths.Length > 0
            });
        }
        return resolvedPackages.ToArray();
    }

    public async Task<RuntimeDependency[]> FindDepsJsonFilesAsync(string projectFilePath, string configuration = "Debug")
    {
        var outputPaths = await GetProjectOutputPathsAsync(projectFilePath, configuration);
        var dependencies = new List<RuntimeDependency>();
        foreach (var outputPath in outputPaths)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(projectFilePath);
            var depsJsonPath = Path.Combine(outputPath, $"{assemblyName}.deps.json");
            if (File.Exists(depsJsonPath))
            {
                var deps = await ParseDepsJsonFileAsync(depsJsonPath);
                dependencies.AddRange(deps);
            }
        }
        return dependencies.ToArray();
    }

    public Task<NugetAssemblyLookup> FindAssemblyInNugetCacheAsync(string packageId, string? version = null, string? tfm = null)
    {
        var cacheRoot = GetNugetCacheRoot();
        var packageDir = Path.Combine(cacheRoot, packageId.ToLowerInvariant());
        if (!Directory.Exists(packageDir))
        {
            return Task.FromResult(new NugetAssemblyLookup(
                packageId,
                version,
                tfm,
                ResolvedVersion: null,
                ResolvedTfm: null,
                cacheRoot,
                FoundAssembly: null,
                AvailableVersions: Array.Empty<string>(),
                AvailableTfms: Array.Empty<string>(),
                Failure: NugetLookupFailure.PackageNotFound));
        }

        var availableVersions = Directory.GetDirectories(packageDir)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .ToArray();

        string? resolvedVersion;
        if (version is not null)
        {
            resolvedVersion = availableVersions
                .FirstOrDefault(v => v.Equals(version, StringComparison.OrdinalIgnoreCase));
            if (resolvedVersion is null)
            {
                return Task.FromResult(new NugetAssemblyLookup(
                    packageId,
                    version,
                    tfm,
                    ResolvedVersion: null,
                    ResolvedTfm: null,
                    cacheRoot,
                    FoundAssembly: null,
                    AvailableVersions: SortVersionsDescending(availableVersions),
                    AvailableTfms: Array.Empty<string>(),
                    Failure: NugetLookupFailure.VersionNotFound));
            }
        }
        else
        {
            resolvedVersion = PickHighestVersion(availableVersions);
            if (resolvedVersion is null)
            {
                return Task.FromResult(new NugetAssemblyLookup(
                    packageId,
                    version,
                    tfm,
                    ResolvedVersion: null,
                    ResolvedTfm: null,
                    cacheRoot,
                    FoundAssembly: null,
                    AvailableVersions: Array.Empty<string>(),
                    AvailableTfms: Array.Empty<string>(),
                    Failure: NugetLookupFailure.VersionNotFound));
            }
        }

        var libDir = Path.Combine(packageDir, resolvedVersion, "lib");
        if (!Directory.Exists(libDir))
        {
            return Task.FromResult(new NugetAssemblyLookup(
                packageId,
                version,
                tfm,
                resolvedVersion,
                ResolvedTfm: null,
                cacheRoot,
                FoundAssembly: null,
                AvailableVersions: SortVersionsDescending(availableVersions),
                AvailableTfms: Array.Empty<string>(),
                Failure: NugetLookupFailure.AssemblyNotFound));
        }

        var availableTfms = Directory.GetDirectories(libDir)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .ToArray();

        var resolvedTfm = tfm is not null
            ? PickCompatibleTfm(availableTfms, tfm)
            : PickBestTfm(availableTfms);

        if (resolvedTfm is null)
        {
            return Task.FromResult(new NugetAssemblyLookup(
                packageId,
                version,
                tfm,
                resolvedVersion,
                ResolvedTfm: null,
                cacheRoot,
                FoundAssembly: null,
                AvailableVersions: SortVersionsDescending(availableVersions),
                AvailableTfms: availableTfms,
                Failure: NugetLookupFailure.AssemblyNotFound));
        }

        var tfmDir = Path.Combine(libDir, resolvedTfm);
        var dlls = Directory.GetFiles(tfmDir, "*.dll", SearchOption.TopDirectoryOnly);
        var foundAssembly = PickAssemblyForPackage(dlls, packageId);

        return Task.FromResult(new NugetAssemblyLookup(
            packageId,
            version,
            tfm,
            resolvedVersion,
            resolvedTfm,
            cacheRoot,
            foundAssembly,
            AvailableVersions: SortVersionsDescending(availableVersions),
            AvailableTfms: availableTfms,
            Failure: foundAssembly is null ? NugetLookupFailure.AssemblyNotFound : null));
    }

    private static string GetNugetCacheRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".nuget", "packages");
    }

    private static string? PickHighestVersion(string[] versions)
    {
        if (versions.Length == 0)
            return null;
        var parsed = versions
            .Select(v => (raw: v, parsed: TryParseVersion(v)))
            .ToArray();
        var withParsed = parsed.Where(p => p.parsed is not null).ToArray();
        if (withParsed.Length > 0)
            return withParsed.OrderByDescending(p => p.parsed!).First().raw;
        return versions.OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase).First();
    }

    private static Version? TryParseVersion(string raw)
    {
        var dash = raw.IndexOf('-');
        var core = dash >= 0 ? raw[..dash] : raw;
        return Version.TryParse(core, out var v) ? v : null;
    }

    private static string[] SortVersionsDescending(string[] versions)
    {
        return versions
            .Select(v => (raw: v, parsed: TryParseVersion(v)))
            .OrderByDescending(p => p.parsed ?? new Version(0, 0))
            .ThenByDescending(p => p.raw, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.raw)
            .ToArray();
    }

    private static string? PickBestTfm(string[] availableTfms)
    {
        if (availableTfms.Length == 0)
            return null;
        var ranked = availableTfms
            .Select(t => (raw: t, rank: RankTfm(t)))
            .OrderBy(t => t.rank.family)
            .ThenByDescending(t => t.rank.version)
            .ThenBy(t => t.raw, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return ranked[0].raw;
    }

    private static string? PickCompatibleTfm(string[] availableTfms, string requested)
    {
        var exact = availableTfms.FirstOrDefault(t => t.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;
        var compatible = availableTfms
            .Where(t => IsCompatibleFramework(t, requested))
            .OrderByDescending(t => t, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return compatible;
    }

    private static (int family, Version version) RankTfm(string tfm)
    {
        var lower = tfm.ToLowerInvariant();
        if (lower.StartsWith("net") && !lower.StartsWith("netstandard") && !lower.StartsWith("netcoreapp") && !lower.Contains("framework"))
        {
            var rest = lower[3..];
            return (0, TryParseVersion(rest) ?? new Version(0, 0));
        }
        if (lower.StartsWith("netcoreapp"))
            return (1, TryParseVersion(lower[10..]) ?? new Version(0, 0));
        if (lower.StartsWith("netstandard"))
            return (2, TryParseVersion(lower[11..]) ?? new Version(0, 0));
        return (3, new Version(0, 0));
    }

    private static string? PickAssemblyForPackage(string[] dlls, string packageId)
    {
        if (dlls.Length == 0)
            return null;
        var preferredName = packageId + ".dll";
        var preferred = dlls.FirstOrDefault(d =>
            Path.GetFileName(d).Equals(preferredName, StringComparison.OrdinalIgnoreCase));
        return preferred ?? dlls.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).First();
    }

    private static string? GetPropertyValue(IEnumerable<XElement> propertyGroups, string propertyName)
    {
        return propertyGroups
            .SelectMany(pg => pg.Elements(propertyName))
            .FirstOrDefault()?.Value;
    }

    private static string? GetChildElementValue(XElement parent, string childName)
    {
        return parent.Element(childName)?.Value;
    }

    private async Task<string[]> ResolvePackageAssemblyPathsAsync(PackageReference package, string[] targetFrameworks)
    {
        var assemblyPaths = new List<string>();
        var nugetCachePath = GetNugetCacheRoot();
        if (Directory.Exists(nugetCachePath))
        {
            var packagePath = Path.Combine(nugetCachePath, package.Name.ToLowerInvariant(), package.Version);
            if (Directory.Exists(packagePath))
            {
                var libPath = Path.Combine(packagePath, "lib");
                if (Directory.Exists(libPath))
                {
                    foreach (var framework in targetFrameworks)
                    {
                        var frameworkPaths = await FindBestMatchingFrameworkPathAsync(libPath, framework);
                        assemblyPaths.AddRange(frameworkPaths);
                    }
                }
                var refPath = Path.Combine(packagePath, "ref");
                if (Directory.Exists(refPath))
                {
                    foreach (var framework in targetFrameworks)
                    {
                        var frameworkPaths = await FindBestMatchingFrameworkPathAsync(refPath, framework);
                        assemblyPaths.AddRange(frameworkPaths);
                    }
                }
            }
        }
        return assemblyPaths.Distinct().ToArray();
    }

    private static Task<string[]> FindBestMatchingFrameworkPathAsync(string basePath, string targetFramework)
    {
        if (!Directory.Exists(basePath))
            return Task.FromResult(Array.Empty<string>());
        var assemblies = new List<string>();
        var frameworks = Directory.GetDirectories(basePath).Select(Path.GetFileName).Where(f => f != null).Cast<string>();
        var exactMatch = frameworks.FirstOrDefault(f => f.Equals(targetFramework, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null)
        {
            var exactPath = Path.Combine(basePath, exactMatch);
            assemblies.AddRange(Directory.GetFiles(exactPath, "*.dll", SearchOption.TopDirectoryOnly));
            return Task.FromResult(assemblies.ToArray());
        }
        var compatibleFrameworks = frameworks
            .Where(f => IsCompatibleFramework(f, targetFramework))
            .OrderByDescending(f => f)
            .ToArray();
        foreach (var framework in compatibleFrameworks)
        {
            var frameworkPath = Path.Combine(basePath, framework);
            assemblies.AddRange(Directory.GetFiles(frameworkPath, "*.dll", SearchOption.TopDirectoryOnly));
            if (assemblies.Count > 0)
                break;
        }
        return Task.FromResult(assemblies.ToArray());
    }

    private static bool IsCompatibleFramework(string availableFramework, string targetFramework)
    {
        if (availableFramework.Equals(targetFramework, StringComparison.OrdinalIgnoreCase))
            return true;
        if (targetFramework.StartsWith("net") && !targetFramework.Contains("framework"))
        {
            if (availableFramework.Equals("netstandard2.0", StringComparison.OrdinalIgnoreCase) ||
                availableFramework.Equals("netstandard2.1", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private async Task<RuntimeDependency[]> ParseDepsJsonFileAsync(string depsJsonPath)
    {
        var dependencies = new List<RuntimeDependency>();
        try
        {
            var json = await File.ReadAllTextAsync(depsJsonPath);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("libraries", out var libraries))
            {
                foreach (var library in libraries.EnumerateObject())
                {
                    var libraryName = library.Name;
                    var libraryInfo = library.Value;
                    if (libraryInfo.TryGetProperty("type", out var typeElement) &&
                        libraryInfo.TryGetProperty("serviceable", out var serviceableElement))
                    {
                        var type = typeElement.GetString() ?? "unknown";
                        var parts = libraryName.Split('/');
                        var name = parts.Length > 0 ? parts[0] : libraryName;
                        var version = parts.Length > 1 ? parts[1] : "unknown";
                        var assemblyPath = string.Empty;
                        var deps = Array.Empty<string>();
                        if (libraryInfo.TryGetProperty("runtime", out var runtime))
                        {
                            var firstRuntime = runtime.EnumerateObject().FirstOrDefault();
                            if (firstRuntime.Value.ValueKind == JsonValueKind.Object)
                            {
                                assemblyPath = firstRuntime.Name;
                            }
                        }
                        dependencies.Add(new RuntimeDependency(
                            name,
                            version,
                            type,
                            assemblyPath,
                            deps
                        ));
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Error parsing deps.json file {depsJsonPath}: {ex.Message}");
        }
        return dependencies.ToArray();
    }
}
