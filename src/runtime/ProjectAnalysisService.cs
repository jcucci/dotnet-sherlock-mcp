using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.ProjectEvaluation;

namespace Sherlock.MCP.Runtime;

public class ProjectAnalysisService : IProjectAnalysisService
{
    private static readonly Regex SolutionProjectRegex = new(
        @"Project\(""\{(?<TypeGuid>[A-F0-9\-]+)\}""\)\s*=\s*""(?<Name>[^""]+)""\s*,\s*""(?<Path>[^""]+)""\s*,\s*""\{(?<Guid>[A-F0-9\-]+)\}""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );
    private static readonly string[] SupportedProjectExtensions = { ".csproj", ".vbproj", ".fsproj" };
    private static readonly IReadOnlyDictionary<string, string> NoGlobalProperties = new Dictionary<string, string>();
    private static readonly string[] ProjectProperties = ["AssemblyName", "RootNamespace", "OutputType", "TargetFramework", "TargetFrameworks"];
    private static readonly string[] ProjectItems = ["PackageReference", "PackageVersion", "ProjectReference"];
    private static readonly string[] FrameworkProperties = ["TargetFramework", "TargetFrameworks"];
    private static readonly string[] OutputProperties = ["TargetDir", "TargetPath"];

    private readonly RuntimeOptions? _options;
    private readonly IProjectEvaluator? _evaluator;

    public ProjectAnalysisService(RuntimeOptions? options = null, IProjectEvaluator? evaluator = null)
    {
        _options = options;
        _evaluator = evaluator;
    }

    public async Task<ProjectInfo[]> AnalyzeSolutionFileAsync(string solutionFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(solutionFilePath))
        {
            throw new FileNotFoundException($"Solution file not found: {solutionFilePath}");
        }
        var solutionDirectory = Path.GetDirectoryName(solutionFilePath) ?? string.Empty;
        var content = await File.ReadAllTextAsync(solutionFilePath, cancellationToken);
        return Path.GetExtension(solutionFilePath).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ParseSlnxProjects(content, solutionDirectory)
            : ParseSlnProjects(content, solutionDirectory);
    }

    private static ProjectInfo[] ParseSlnProjects(string content, string solutionDirectory)
    {
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

    private static ProjectInfo[] ParseSlnxProjects(string content, string solutionDirectory)
    {
        var doc = XDocument.Parse(content);
        var projects = new List<ProjectInfo>();
        foreach (var element in doc.Descendants("Project"))
        {
            var relativePath = element.Attribute("Path")?.Value;
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;
            if (!SupportedProjectExtensions.Any(ext => relativePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                continue;
            var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, normalized));
            projects.Add(new ProjectInfo(
                Path.GetFileNameWithoutExtension(normalized),
                relativePath,
                fullPath,
                string.Empty,
                string.Empty
            ));
        }
        return projects.ToArray();
    }

    public async Task<ProjectAnalysisResult> AnalyzeProjectFileAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        EnsureProjectExists(projectFilePath);
        var (evaluation, evaluationInfo) = await EvaluateAsync(projectFilePath, NoGlobalProperties, ProjectProperties, ProjectItems, cancellationToken);
        if (evaluation is null)
            return await AnalyzeFromXmlAsync(projectFilePath, evaluationInfo, cancellationToken);

        var analysis = AnalyzeFromEvaluation(projectFilePath, evaluation);
        var outputs = await EvaluateOutputPathsAsync(projectFilePath, Configurations(null), OutputFrameworks(evaluation), cancellationToken);
        return outputs.Paths is null
            ? await AnalyzeFromXmlAsync(projectFilePath, ProjectEvaluationInfo.Xml(outputs.FailureReason!), cancellationToken)
            : analysis with { OutputPaths = outputs.Paths, Evaluation = ProjectEvaluationInfo.MsBuild };
    }

    public async Task<ProjectOutputPaths> GetProjectOutputPathsAsync(string projectFilePath, string? configuration = null, CancellationToken cancellationToken = default)
    {
        EnsureProjectExists(projectFilePath);
        var configurations = Configurations(configuration);
        var (evaluation, evaluationInfo) = await EvaluateAsync(projectFilePath, NoGlobalProperties, FrameworkProperties, [], cancellationToken);
        if (evaluation is null)
            return new ProjectOutputPaths(await XmlOutputPathsAsync(projectFilePath, configurations, cancellationToken), evaluationInfo);

        var outputs = await EvaluateOutputPathsAsync(projectFilePath, configurations, OutputFrameworks(evaluation), cancellationToken);
        return outputs.Paths is null
            ? new ProjectOutputPaths(await XmlOutputPathsAsync(projectFilePath, configurations, cancellationToken), ProjectEvaluationInfo.Xml(outputs.FailureReason!))
            : new ProjectOutputPaths(outputs.Paths, ProjectEvaluationInfo.MsBuild);
    }

    public async Task<ResolvedPackageReferences> ResolvePackageReferencesAsync(string projectFilePath, string? packageName = null, CancellationToken cancellationToken = default)
    {
        var analysisResult = await AnalyzeProjectFileAsync(projectFilePath, cancellationToken);
        var resolvedPackages = new List<PackageReference>();
        var packagesToResolve = packageName != null
            ? analysisResult.PackageReferences.Where(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase))
            : analysisResult.PackageReferences;
        foreach (var package in packagesToResolve)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assemblyPaths = await ResolvePackageAssemblyPathsAsync(package, analysisResult.TargetFrameworks);
            resolvedPackages.Add(package with
            {
                AssemblyPaths = assemblyPaths,
                IsResolved = assemblyPaths.Length > 0
            });
        }
        return new ResolvedPackageReferences(resolvedPackages.ToArray(), analysisResult.Evaluation);
    }

    public async Task<RuntimeDependency[]> FindDepsJsonFilesAsync(string projectFilePath, string configuration = "Debug", CancellationToken cancellationToken = default)
    {
        var outputs = await GetProjectOutputPathsAsync(projectFilePath, configuration, cancellationToken);
        return await ReadDepsJsonFilesAsync(projectFilePath, outputs.Paths, cancellationToken);
    }

    public async Task<RuntimeDependency[]> ReadDepsJsonFilesAsync(string projectFilePath, IReadOnlyList<string> outputPaths, CancellationToken cancellationToken = default)
    {
        var dependencies = new List<RuntimeDependency>();
        foreach (var outputPath in outputPaths)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(projectFilePath);
            var depsJsonPath = Path.Combine(outputPath, $"{assemblyName}.deps.json");
            if (File.Exists(depsJsonPath))
            {
                var deps = await ParseDepsJsonFileAsync(depsJsonPath, cancellationToken);
                dependencies.AddRange(deps);
            }
        }
        return dependencies.ToArray();
    }

    private async Task<(ProjectEvaluationResult? Result, ProjectEvaluationInfo Info)> EvaluateAsync(
        string projectFilePath,
        IReadOnlyDictionary<string, string> globalProperties,
        IReadOnlyList<string> properties,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken)
    {
        if (!EvaluationEnabled)
            return (null, ProjectEvaluationInfo.Xml(ProjectEvaluationInfo.DisabledReason));

        var result = await _evaluator!.EvaluateAsync(projectFilePath, globalProperties, properties, items, cancellationToken);
        return result.Success
            ? (result, ProjectEvaluationInfo.MsBuild)
            : (null, ProjectEvaluationInfo.Xml(result.FailureReason ?? "MSBuild evaluation failed"));
    }

    public string EvaluationMode => EvaluationEnabled ? ProjectEvaluationInfo.MsBuildMode : ProjectEvaluationInfo.XmlMode;

    private bool EvaluationEnabled =>
        _evaluator is not null && _options?.ProjectEvaluation != ProjectEvaluationMode.Off;

    private async Task<(string[]? Paths, string? FailureReason)> EvaluateOutputPathsAsync(
        string projectFilePath,
        string[] configurations,
        string?[] frameworks,
        CancellationToken cancellationToken)
    {
        var evaluations = configurations
            .SelectMany(configuration => frameworks.Select(framework => OutputGlobalProperties(configuration, framework)))
            .Select(globalProperties => _evaluator!.EvaluateAsync(projectFilePath, globalProperties, OutputProperties, [], cancellationToken));
        var results = await Task.WhenAll(evaluations);
        var failure = results.FirstOrDefault(result => !result.Success);
        if (failure is not null)
            return (null, failure.FailureReason ?? "MSBuild evaluation failed");

        var paths = results
            .Select(result => result.GetProperty("TargetDir"))
            .Where(targetDir => targetDir is not null)
            .Select(targetDir => Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDir!, Path.GetDirectoryName(Path.GetFullPath(projectFilePath))!)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return paths.Length > 0 ? (paths, null) : (null, "MSBuild evaluation reported no TargetDir");
    }

    private static Dictionary<string, string> OutputGlobalProperties(string configuration, string? targetFramework)
    {
        var globalProperties = new Dictionary<string, string> { ["Configuration"] = configuration };
        if (targetFramework is not null)
            globalProperties["TargetFramework"] = targetFramework;
        return globalProperties;
    }

    private static ProjectAnalysisResult AnalyzeFromEvaluation(string projectFilePath, ProjectEvaluationResult evaluation)
    {
        var targetFrameworks = TargetFrameworksOf(evaluation);
        var targetFramework = evaluation.GetProperty("TargetFramework") ?? targetFrameworks.FirstOrDefault() ?? string.Empty;
        var assemblyName = evaluation.GetProperty("AssemblyName") ?? Path.GetFileNameWithoutExtension(projectFilePath);
        return new ProjectAnalysisResult(
            assemblyName,
            targetFramework,
            targetFrameworks.Length > 0 ? targetFrameworks : [targetFramework],
            evaluation.GetProperty("OutputType") ?? "Library",
            assemblyName,
            evaluation.GetProperty("RootNamespace") ?? assemblyName,
            EvaluatedProjectReferences(projectFilePath, evaluation),
            EvaluatedPackageReferences(evaluation),
            [],
            ProjectEvaluationInfo.MsBuild);
    }

    private static string?[] OutputFrameworks(ProjectEvaluationResult evaluation) =>
        SplitList(evaluation.GetProperty("TargetFrameworks")) is { Length: > 0 } frameworks ? [.. frameworks] : [null];

    private static string[] TargetFrameworksOf(ProjectEvaluationResult evaluation) =>
        SplitList(evaluation.GetProperty("TargetFrameworks")) is { Length: > 0 } frameworks
            ? frameworks
            : SplitList(evaluation.GetProperty("TargetFramework"));

    private static string[] SplitList(string? value) =>
        value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    private static ProjectReference[] EvaluatedProjectReferences(string projectFilePath, ProjectEvaluationResult evaluation)
    {
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectFilePath)) ?? string.Empty;
        return evaluation.GetItems("ProjectReference")
            .Select(item => new ProjectReference(
                Path.GetFileNameWithoutExtension(item.Identity.Replace('\\', '/')),
                item.Identity,
                item.GetMetadata("FullPath") ?? Path.GetFullPath(Path.Combine(projectDirectory, item.Identity))))
            .ToArray();
    }

    private static PackageReference[] EvaluatedPackageReferences(ProjectEvaluationResult evaluation)
    {
        var centralVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packageVersion in evaluation.GetItems("PackageVersion"))
            if (packageVersion.GetMetadata("Version") is { } version)
                centralVersions[packageVersion.Identity] = version;

        return evaluation.GetItems("PackageReference")
            .Where(item => !string.Equals(item.GetMetadata("IsImplicitlyDefined"), "true", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(item => item.Identity, StringComparer.OrdinalIgnoreCase)
            .Select(item => new PackageReference(
                item.Identity,
                item.GetMetadata("Version") ?? item.GetMetadata("VersionOverride") ?? centralVersions.GetValueOrDefault(item.Identity) ?? string.Empty,
                Array.Empty<string>(),
                false))
            .ToArray();
    }

    private static async Task<ProjectAnalysisResult> AnalyzeFromXmlAsync(string projectFilePath, ProjectEvaluationInfo evaluationInfo, CancellationToken cancellationToken)
    {
        var projectDirectory = Path.GetDirectoryName(projectFilePath) ?? string.Empty;
        var content = await File.ReadAllTextAsync(projectFilePath, cancellationToken);
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
        var outputPaths = XmlOutputPaths(doc, projectDirectory, Configurations(null));
        return new ProjectAnalysisResult(
            assemblyName,
            targetFramework,
            targetFrameworks,
            outputType,
            assemblyName,
            rootNamespace,
            projectReferences,
            packageReferences,
            outputPaths,
            evaluationInfo
        );
    }

    private static async Task<string[]> XmlOutputPathsAsync(string projectFilePath, string[] configurations, CancellationToken cancellationToken)
    {
        var projectDirectory = Path.GetDirectoryName(projectFilePath) ?? string.Empty;
        var content = await File.ReadAllTextAsync(projectFilePath, cancellationToken);
        return XmlOutputPaths(XDocument.Parse(content), projectDirectory, configurations);
    }

    private static string[] XmlOutputPaths(XDocument doc, string projectDirectory, string[] configurations)
    {
        var outputPaths = new List<string>();
        var propertyGroups = doc.Descendants("PropertyGroup");
        var targetFramework = GetPropertyValue(propertyGroups, "TargetFramework");
        var targetFrameworks = GetPropertyValue(propertyGroups, "TargetFrameworks")?.Split(';') ??
                              (targetFramework != null ? new[] { targetFramework } : new[] { "net9.0" });
        var customOutputPath = GetPropertyValue(propertyGroups, "OutputPath");
        foreach (var config in configurations)
        {
            foreach (var framework in targetFrameworks)
            {
                var outputPath = !string.IsNullOrEmpty(customOutputPath)
                    ? Path.GetFullPath(Path.Combine(projectDirectory, customOutputPath))
                    : Path.GetFullPath(Path.Combine(projectDirectory, "bin", config, framework));
                if (!outputPaths.Contains(outputPath))
                    outputPaths.Add(outputPath);
            }
        }
        return outputPaths.ToArray();
    }

    private static string[] Configurations(string? configuration) =>
        configuration != null ? [configuration] : ["Debug", "Release"];

    private static void EnsureProjectExists(string projectFilePath)
    {
        if (!File.Exists(projectFilePath))
            throw new FileNotFoundException($"Project file not found: {projectFilePath}");
    }

    public Task<NugetAssemblyLookup> FindAssemblyInNugetCacheAsync(string packageId, string? version = null, string? tfm = null)
    {
        ValidatePackageId(packageId);
        ValidateOptionalPathSegment(version, nameof(version));
        ValidateOptionalPathSegment(tfm, nameof(tfm));
        var cacheRoot = NuGetCacheProbe.GetCacheRoot();
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
                    AvailableVersions: NuGetVersions.SortDescending(availableVersions),
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
                AvailableVersions: NuGetVersions.SortDescending(availableVersions),
                AvailableTfms: Array.Empty<string>(),
                Failure: NugetLookupFailure.AssemblyNotFound));
        }

        var availableTfms = Directory.GetDirectories(libDir)
            .Where(d => Directory.EnumerateFiles(d, "*.dll", SearchOption.TopDirectoryOnly).Any())
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
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
                AvailableVersions: NuGetVersions.SortDescending(availableVersions),
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
            AvailableVersions: NuGetVersions.SortDescending(availableVersions),
            AvailableTfms: availableTfms,
            Failure: foundAssembly is null ? NugetLookupFailure.AssemblyNotFound : null));
    }

    private static readonly char[] PathSeparators = { '/', '\\' };

    private static void ValidatePackageId(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("packageId must be provided.", nameof(packageId));
        ValidatePathSegment(packageId, nameof(packageId));
        foreach (var ch in packageId)
            if (!IsValidNugetIdChar(ch))
                throw new ArgumentException($"packageId contains invalid character '{ch}'. NuGet ids allow letters, digits, '.', '_', and '-'.", nameof(packageId));
    }

    private static void ValidateOptionalPathSegment(string? value, string paramName)
    {
        if (value is null)
            return;
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} must not be whitespace.", paramName);
        ValidatePathSegment(value, paramName);
    }

    private static void ValidatePathSegment(string value, string paramName)
    {
        if (Path.IsPathRooted(value))
            throw new ArgumentException($"{paramName} must not be a rooted path.", paramName);
        if (value.IndexOfAny(PathSeparators) >= 0)
            throw new ArgumentException($"{paramName} must not contain path separators.", paramName);
        if (value == ".." || value == "." || value.Contains(".."))
            throw new ArgumentException($"{paramName} must not contain parent-directory segments.", paramName);
        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"{paramName} contains invalid path characters.", paramName);
    }

    private static bool IsValidNugetIdChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-';

    private static string? PickHighestVersion(string[] versions)
    {
        if (versions.Length == 0)
            return null;
        var parsed = versions
            .Select(v => (raw: v, parsed: NuGetVersions.TryParse(v), isStable: !v.Contains('-')))
            .Where(p => p.parsed is not null)
            .ToArray();
        if (parsed.Length > 0)
            return parsed
                .OrderByDescending(p => p.isStable)
                .ThenByDescending(p => p.parsed!)
                .ThenByDescending(p => p.raw, StringComparer.OrdinalIgnoreCase)
                .First().raw;
        return versions.OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase).First();
    }

    public static string? PickBestTargetFramework(IEnumerable<string> availableTfms) => PickBestTfm(availableTfms.ToArray());

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
        if (lower.StartsWith("net", StringComparison.Ordinal) && !lower.StartsWith("netstandard", StringComparison.Ordinal) && !lower.StartsWith("netcoreapp", StringComparison.Ordinal) && !lower.Contains("framework"))
        {
            var rest = lower[3..];
            if (IsFrameworkStyleTfm(rest))
                return (3, ParseFrameworkStyleVersion(rest));
            return (0, NuGetVersions.TryParse(rest) ?? new Version(0, 0));
        }
        if (lower.StartsWith("netcoreapp", StringComparison.Ordinal))
            return (1, NuGetVersions.TryParse(lower[10..]) ?? new Version(0, 0));
        if (lower.StartsWith("netstandard", StringComparison.Ordinal))
            return (2, NuGetVersions.TryParse(lower[11..]) ?? new Version(0, 0));
        return (4, new Version(0, 0));
    }

    private static bool IsFrameworkStyleTfm(string rest)
    {
        if (rest.Length is not (2 or 3))
            return false;
        foreach (var ch in rest)
            if (!char.IsDigit(ch))
                return false;
        return rest[0] is >= '1' and <= '4';
    }

    private static Version ParseFrameworkStyleVersion(string rest)
    {
        var major = rest[0] - '0';
        var minor = rest[1] - '0';
        if (rest.Length == 2)
            return new Version(major, minor);
        var patch = rest[2] - '0';
        return new Version(major, minor, patch);
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

    private static async Task<string[]> ResolvePackageAssemblyPathsAsync(PackageReference package, string[] targetFrameworks)
    {
        var assemblyPaths = new List<string>();
        var nugetCachePath = NuGetCacheProbe.GetCacheRoot();
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
        if (targetFramework.StartsWith("net", StringComparison.Ordinal) && !targetFramework.Contains("framework"))
        {
            if (availableFramework.Equals("netstandard2.0", StringComparison.OrdinalIgnoreCase) ||
                availableFramework.Equals("netstandard2.1", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task<RuntimeDependency[]> ParseDepsJsonFileAsync(string depsJsonPath, CancellationToken cancellationToken)
    {
        var dependencies = new List<RuntimeDependency>();
        try
        {
            var json = await File.ReadAllTextAsync(depsJsonPath, cancellationToken);
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
