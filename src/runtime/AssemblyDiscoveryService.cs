using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
namespace Sherlock.MCP.Runtime;
public interface IAssemblyDiscoveryService
{
    string[] FindAssemblyByTypeName(string typeName, string? hintPath = null, bool? isNuGetPackage = null);
    string[] GetFrameworkAssemblyLocations();
    string[] FindAssembliesInDirectory(string directory, bool recursive = true);
    string[] GetNuGetPackageAssemblies(string packageName);
    string[] GetCommonAssemblyLocations();
}
public class AssemblyDiscoveryService : IAssemblyDiscoveryService
{
    private readonly ILogger<AssemblyDiscoveryService>? _logger;
    public AssemblyDiscoveryService(ILogger<AssemblyDiscoveryService>? logger = null)
    {
        _logger = logger;
    }
    public  string[] FindAssemblyByTypeName(string typeName, string? hintPath = null, bool? isNuGetPackage = null)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return [];
        var foundAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!string.IsNullOrWhiteSpace(hintPath))
                SearchInHintPath(hintPath, typeName, foundAssemblies);
            if (isNuGetPackage == true)
                SearchInNuGetPackages(typeName, foundAssemblies);
            if (isNuGetPackage != true)
                foreach (var assemblyPath in GetFrameworkAssemblyLocations())
                    if (ContainsType(assemblyPath, typeName))
                        foundAssemblies.Add(assemblyPath);
            if (foundAssemblies.Count != 0 && (hintPath != null || isNuGetPackage == true))
              return foundAssemblies.ToArray();
            foreach (var location in GetCommonAssemblyLocations())
            {
                if (!Directory.Exists(location))
                    continue;
                foreach (var assemblyPath in FindAssembliesInDirectory(location, recursive: true))
                    if (ContainsType(assemblyPath, typeName))
                        foundAssemblies.Add(assemblyPath);
            }
            return foundAssemblies.ToArray();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error searching for assemblies containing type {TypeName}", typeName);
            return [];
        }
    }
    public string[] GetFrameworkAssemblyLocations()
    {
        _logger?.LogInformation("Getting framework assembly locations");
        var frameworkAssemblies = new List<string>();
        try
        {
            var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            if (!string.IsNullOrEmpty(runtimeDirectory) && Directory.Exists(runtimeDirectory))
            {
                _logger?.LogDebug("Runtime directory: {RuntimeDirectory}", runtimeDirectory);
                var patterns = new[] { "System.*.dll", "Microsoft.*.dll", "mscorlib.dll", "netstandard.dll" };
                foreach (var pattern in patterns)
                {
                    var files = Directory.GetFiles(runtimeDirectory, pattern, SearchOption.TopDirectoryOnly);
                    frameworkAssemblies.AddRange(files);
                }
            }
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                        continue;
                    var assemblyName = assembly.GetName().Name;
                    if (assemblyName != null && 
                        (assemblyName.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                        assemblyName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)))
                    {
                        frameworkAssemblies.Add(assembly.Location);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to get location for assembly: {AssemblyName}", assembly.FullName);
                }
            }
            var dotnetRoot = GetDotNetRoot();
            if (!string.IsNullOrEmpty(dotnetRoot))
            {
                var sharedFrameworkPath = Path.Combine(dotnetRoot, "shared");
                if (Directory.Exists(sharedFrameworkPath))
                {
                    var frameworkDirs = Directory.GetDirectories(sharedFrameworkPath, "*", SearchOption.TopDirectoryOnly);
                    foreach (var frameworkDir in frameworkDirs)
                    {
                        var versionDirs = Directory.GetDirectories(frameworkDir, "*", SearchOption.TopDirectoryOnly);
                        foreach (var versionDir in versionDirs)
                        {
                            var assemblies = Directory.GetFiles(versionDir, "*.dll", SearchOption.TopDirectoryOnly);
                            frameworkAssemblies.AddRange(assemblies);
                        }
                    }
                }
            }
            var uniqueAssemblies = frameworkAssemblies.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _logger?.LogInformation("Found {Count} framework assemblies", uniqueAssemblies.Length);
            return uniqueAssemblies;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting framework assembly locations");
            return [];
        }
    }
    public string[] FindAssembliesInDirectory(string directory, bool recursive = true)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            _logger?.LogWarning("Directory path is null or empty");
            return [];
        }
        if (!Directory.Exists(directory))
        {
            _logger?.LogWarning("Directory does not exist: {Directory}", directory);
            return [];
        }
        _logger?.LogInformation("Searching for assemblies in directory: {Directory} (recursive: {Recursive})", directory, recursive);
        try
        {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var assemblies = new List<string>();
            var dllFiles = Directory.GetFiles(directory, "*.dll", searchOption);
            assemblies.AddRange(dllFiles);
            var exeFiles = Directory.GetFiles(directory, "*.exe", searchOption);
            assemblies.AddRange(exeFiles);
            var managedAssemblies = assemblies.Where(IsManagedAssembly).Select(Path.GetFullPath).ToArray();
            _logger?.LogInformation("Found {Count} managed assemblies in directory {Directory}", managedAssemblies.Length, directory);
            return managedAssemblies;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error searching for assemblies in directory: {Directory}", directory);
            return [];
        }
    }
    public string[] GetNuGetPackageAssemblies(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            _logger?.LogWarning("Package name is null or empty");
            return [];
        }
        _logger?.LogInformation("Searching for NuGet package assemblies: {PackageName}", packageName);
        try
        {
            var packageAssemblies = new List<string>();
            var nugetCachePaths = GetNuGetCachePaths();
            foreach (var cachePath in nugetCachePaths)
            {
                if (!Directory.Exists(cachePath))
                    continue;
                _logger?.LogDebug("Searching NuGet cache: {CachePath}", cachePath);
                var packageDirs = Directory.GetDirectories(cachePath, "*", SearchOption.TopDirectoryOnly)
                    .Where(dir => string.Equals(Path.GetFileName(dir), packageName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                foreach (var packageDir in packageDirs)
                {
                    var versionDirs = Directory.GetDirectories(packageDir, "*", SearchOption.TopDirectoryOnly);
                    foreach (var versionDir in versionDirs)
                    {
                        var libDir = Path.Combine(versionDir, "lib");
                        if (Directory.Exists(libDir))
                        {
                            var assemblies = FindAssembliesInDirectory(libDir, recursive: true);
                            packageAssemblies.AddRange(assemblies);
                        }
                        var refDir = Path.Combine(versionDir, "ref");
                        if (Directory.Exists(refDir))
                        {
                            var assemblies = FindAssembliesInDirectory(refDir, recursive: true);
                            packageAssemblies.AddRange(assemblies);
                        }
                        var runtimesDir = Path.Combine(versionDir, "runtimes");
                        if (Directory.Exists(runtimesDir))
                        {
                            var assemblies = FindAssembliesInDirectory(runtimesDir, recursive: true);
                            packageAssemblies.AddRange(assemblies);
                        }
                    }
                }
            }
            var uniqueAssemblies = packageAssemblies.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _logger?.LogInformation("Found {Count} assemblies for package {PackageName}", uniqueAssemblies.Length, packageName);
            return uniqueAssemblies;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error searching for NuGet package assemblies: {PackageName}", packageName);
            return [];
        }
    }
    public string[] GetCommonAssemblyLocations()
    {
        _logger?.LogInformation("Getting common assembly search locations");
        try
        {
            var locations = new List<string>
            {
              AppDomain.CurrentDomain.BaseDirectory,
              RuntimeEnvironment.GetRuntimeDirectory(),
            };
            locations.AddRange(GetNuGetCachePaths());
            locations.AddRange(GetPlatformSpecificProgramDirectories());
            var dotnetRoot = GetDotNetRoot();
            if (!string.IsNullOrEmpty(dotnetRoot))
            {
                locations.Add(dotnetRoot);
                locations.Add(Path.Combine(dotnetRoot, "shared"));
                locations.Add(Path.Combine(dotnetRoot, "packs"));
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                locations.AddRange(GetWindowsGacPaths());
            var uniqueLocations = locations
                .Where(Directory.Exists)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _logger?.LogInformation("Found {Count} common assembly search locations", uniqueLocations.Length);
            return uniqueLocations;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting common assembly locations");
            return [];
        }
    }
    private void SearchInHintPath(string hintPath, string typeName, HashSet<string> foundAssemblies)
    {
        try
        {
            if (Directory.Exists(hintPath))
            {
                var assembliesInHint = FindAssembliesInDirectory(hintPath, recursive: true);
                foreach (var assemblyPath in assembliesInHint)
                {
                    if (ContainsType(assemblyPath, typeName))
                        foundAssemblies.Add(assemblyPath);
                }
            }
            else if (File.Exists(hintPath) && (hintPath.EndsWith(".dll") || hintPath.EndsWith(".exe")))
            {
                if (ContainsType(hintPath, typeName))
                    foundAssemblies.Add(hintPath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to search hint path {HintPath}", hintPath);
        }
    }
    private void SearchInNuGetPackages(string typeName, HashSet<string> foundAssemblies)
    {
        try
        {
            var packageName = ExtractPackageNameFromType(typeName);
            if (!string.IsNullOrEmpty(packageName))
                foreach (var assemblyPath in GetNuGetPackageAssemblies(packageName))
                    if (ContainsType(assemblyPath, typeName))
                        foundAssemblies.Add(assemblyPath);
            foreach (var assemblyPath in GetNuGetCachePaths().Where(Directory.Exists).SelectMany(path => FindAssembliesInDirectory(path, recursive: true)))
                if (ContainsType(assemblyPath, typeName))
                    foundAssemblies.Add(assemblyPath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to search NuGet packages for type {TypeName}", typeName);
        }
    }
    private static string? ExtractPackageNameFromType(string typeName)
    {
        var commonMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HttpClient"] = "System.Net.Http",
            ["JsonSerializer"] = "System.Text.Json",
            ["Entity"] = "Microsoft.EntityFrameworkCore",
            ["Controller"] = "Microsoft.AspNetCore.Mvc",
            ["DbContext"] = "Microsoft.EntityFrameworkCore",
            ["ILogger"] = "Microsoft.Extensions.Logging",
            ["IConfiguration"] = "Microsoft.Extensions.Configuration"
        };
        return commonMappings.GetValueOrDefault(typeName);
    }
    private bool ContainsType(string assemblyPath, string typeName)
    {
        try
        {
            using var fs = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var peReader = new PEReader(fs);
            if (!peReader.HasMetadata)
                return false;
            var metadataReader = peReader.GetMetadataReader();
            foreach (var typeDefHandle in metadataReader.TypeDefinitions)
            {
                var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                var typeDefName = metadataReader.GetString(typeDef.Name);
                var typeDefNamespace = metadataReader.GetString(typeDef.Namespace);
                if (string.Equals(typeDefName, typeName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals($"{typeDefNamespace}.{typeDefName}", typeName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to inspect assembly for type {TypeName}: {AssemblyPath}", typeName, assemblyPath);
            return false;
        }
    }
    private bool IsManagedAssembly(string filePath)
    {
        try
        {
            var assemblyPaths = new List<string> { filePath };
            assemblyPaths.AddRange(GetFrameworkAssemblyLocations());
            assemblyPaths.AddRange(GetNuGetCachePaths());
            var resolver = new PathAssemblyResolver(assemblyPaths.Distinct(StringComparer.OrdinalIgnoreCase));
            using var metadataContext = new MetadataLoadContext(resolver);
            metadataContext.LoadFromAssemblyPath(filePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
    private static string? GetDotNetRoot()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot) && Directory.Exists(dotnetRoot))
        {
            return dotnetRoot;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var dotnetPath = Path.Combine(programFiles, "dotnet");
            if (Directory.Exists(dotnetPath))
                return dotnetPath;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var paths = new[] { "/usr/share/dotnet", "/usr/lib/dotnet", "/opt/dotnet" };
            return paths.FirstOrDefault(Directory.Exists);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var paths = new[] { "/usr/local/share/dotnet", "/usr/local/lib/dotnet" };
            return paths.FirstOrDefault(Directory.Exists);
        }
        return null;
    }
    private static string[] GetNuGetCachePaths()
    {
        var cachePaths = new List<string>();
        var globalCache = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrEmpty(globalCache))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            globalCache = Path.Combine(userProfile, ".nuget", "packages");
        }
        if (!string.IsNullOrEmpty(globalCache))
            cachePaths.Add(globalCache);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            cachePaths.Add(Path.Combine(localAppData, "NuGet", "v3-cache"));
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            cachePaths.Add(Path.Combine(home, ".local", "share", "NuGet", "v3-cache"));
            cachePaths.Add(Path.Combine(home, ".nuget", "packages"));
        }
        return cachePaths.ToArray();
    }
    private static string[] GetWindowsGacPaths()
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(windowsDir))
            return [];
        return
        [
          Path.Combine(windowsDir, "assembly", "GAC_MSIL"),
          Path.Combine(windowsDir, "assembly", "GAC_32"),
          Path.Combine(windowsDir, "assembly", "GAC_64"),
          Path.Combine(windowsDir, "assembly", "GAC"),
          Path.Combine(windowsDir, "Microsoft.NET", "assembly", "GAC_MSIL"),
          Path.Combine(windowsDir, "Microsoft.NET", "assembly", "GAC_32"),
          Path.Combine(windowsDir, "Microsoft.NET", "assembly", "GAC_64"),
        ];
    }
    private static string[] GetPlatformSpecificProgramDirectories()
    {
        var directories = new List<string>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles))
                directories.Add(programFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFilesX86))
                directories.Add(programFilesX86);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            directories.AddRange(["/usr/lib", "/usr/local/lib", "/opt"]);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            directories.AddRange(["/usr/local/lib", "/Library/Frameworks", "/System/Library/Frameworks"]);
        }
        return directories.ToArray();
    }
}
