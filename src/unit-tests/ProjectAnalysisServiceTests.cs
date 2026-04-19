using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;

namespace Sherlock.MCP.Tests;

public class ProjectAnalysisServiceTests
{
    private readonly IProjectAnalysisService _service = new ProjectAnalysisService();

    [Fact]
    public async Task AnalyzeSolutionFile_Parses_ManagedProjects()
    {
        using var temp = new TempDir();
        var slnPath = Path.Combine(temp.Path, "Test.sln");
        var projRel = Path.Combine("src", "App", "App.csproj");
        var projDir = Path.Combine(temp.Path, "src", "App");
        Directory.CreateDirectory(projDir);
        await File.WriteAllTextAsync(Path.Combine(projDir, "App.csproj"), MinimalCsproj());

        var sln = string.Format(
            "Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"App\", \"{0}\", \"{{D2C1C5A0-9A7F-4B8C-9E7A-111111111111}}\"\nEndProject\n",
            projRel.Replace("\\", "/"));
        await File.WriteAllTextAsync(slnPath, sln);

        var projects = await _service.AnalyzeSolutionFileAsync(slnPath);
        Assert.Single(projects);
        var p = projects[0];
        Assert.Equal("App", p.Name);
        Assert.EndsWith(projRel, p.RelativePath.Replace('\\', '/'));
        Assert.True(File.Exists(p.FullPath));
    }

    [Fact]
    public async Task AnalyzeProjectFile_Parses_Metadata_And_References()
    {
        using var temp = new TempDir();
        var projDir = Path.Combine(temp.Path, "src", "Lib");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Lib.csproj");
        var refProjDir = Path.Combine(temp.Path, "src", "Other");
        Directory.CreateDirectory(refProjDir);
        var refProjRel = Path.Combine("..", "Other", "Other.csproj");
        await File.WriteAllTextAsync(Path.Combine(refProjDir, "Other.csproj"), MinimalCsproj());

        var csproj = string.Format(
            """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <OutputType>Library</OutputType>
    <RootNamespace>MyRoot</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{0}" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
""",
            refProjRel.Replace("\\", "/"));
        await File.WriteAllTextAsync(projPath, csproj);

        var result = await _service.AnalyzeProjectFileAsync(projPath);
        Assert.Equal("Lib", result.ProjectName);
        Assert.Equal("net9.0", result.TargetFramework);
        Assert.Contains("net9.0", result.TargetFrameworks);
        Assert.Equal("Library", result.OutputType);
        Assert.Equal("Lib", result.AssemblyName);
        Assert.Equal("MyRoot", result.RootNamespace);
        Assert.Single(result.ProjectReferences);
        Assert.Contains(result.PackageReferences, p => p.Name == "Newtonsoft.Json");
    }

    [Fact]
    public async Task ResolvePackageReferences_Returns_Entry_For_Requested_Package()
    {
        using var temp = new TempDir();
        var projDir = Path.Combine(temp.Path, "src", "Lib");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "Lib.csproj");
        var csproj = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
""";
        await File.WriteAllTextAsync(projPath, csproj);

        var resolved = await _service.ResolvePackageReferencesAsync(projPath, "Newtonsoft.Json");
        Assert.Single(resolved);
        Assert.Equal("Newtonsoft.Json", resolved[0].Name);
        Assert.Equal("13.0.3", resolved[0].Version);
        // IsResolved may be true or false depending on local cache, both acceptable.
        Assert.NotNull(resolved[0].AssemblyPaths);
    }

    [Fact]
    public async Task GetProjectOutputPaths_Computes_Default_And_Custom()
    {
        using var temp = new TempDir();
        var projDir = Path.Combine(temp.Path, "src", "App");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "App.csproj");

        var csproj = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>
""";
        await File.WriteAllTextAsync(projPath, csproj);

        var defaults = await _service.GetProjectOutputPathsAsync(projPath);
        Assert.Contains(Path.Combine(projDir, "bin", "Debug", "net9.0"), defaults);
        Assert.Contains(Path.Combine(projDir, "bin", "Release", "net9.0"), defaults);

        var csprojCustom = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <OutputPath>out/custom</OutputPath>
  </PropertyGroup>
</Project>
""";
        await File.WriteAllTextAsync(projPath, csprojCustom);
        var custom = await _service.GetProjectOutputPathsAsync(projPath, "Debug");
        Assert.Single(custom);
        Assert.Equal(Path.GetFullPath(Path.Combine(projDir, "out", "custom")), custom[0]);
    }

    [Fact]
    public async Task FindAssemblyInNugetCache_Returns_ExactMatch_When_Version_And_Tfm_Provided()
    {
        using var cache = new TempDir();
        SeedPackage(cache.Path, "Sharp.Events", "41.0.1", "net9.0");
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var result = await _service.FindAssemblyInNugetCacheAsync("Sharp.Events", "41.0.1", "net9.0");

        Assert.Null(result.Failure);
        Assert.Equal("41.0.1", result.ResolvedVersion);
        Assert.Equal("net9.0", result.ResolvedTfm);
        Assert.NotNull(result.FoundAssembly);
        Assert.EndsWith(Path.Combine("lib", "net9.0", "Sharp.Events.dll"), result.FoundAssembly);
        Assert.True(File.Exists(result.FoundAssembly));
    }

    [Fact]
    public async Task FindAssemblyInNugetCache_Picks_Highest_Version_When_Omitted()
    {
        using var cache = new TempDir();
        SeedPackage(cache.Path, "Sharp.Events", "41.0.1", "net9.0");
        SeedPackage(cache.Path, "Sharp.Events", "41.2.0", "net9.0");
        SeedPackage(cache.Path, "Sharp.Events", "40.9.5", "net9.0");
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var result = await _service.FindAssemblyInNugetCacheAsync("Sharp.Events");

        Assert.Null(result.Failure);
        Assert.Equal("41.2.0", result.ResolvedVersion);
        Assert.Contains("41.2.0", result.AvailableVersions);
        Assert.Contains("41.0.1", result.AvailableVersions);
        Assert.Contains("40.9.5", result.AvailableVersions);
    }

    [Fact]
    public async Task FindAssemblyInNugetCache_Picks_Best_Tfm_When_Omitted()
    {
        using var cache = new TempDir();
        SeedPackage(cache.Path, "Some.Pkg", "1.0.0", "netstandard2.0");
        SeedPackage(cache.Path, "Some.Pkg", "1.0.0", "net8.0");
        SeedPackage(cache.Path, "Some.Pkg", "1.0.0", "net9.0");
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var result = await _service.FindAssemblyInNugetCacheAsync("Some.Pkg", "1.0.0");

        Assert.Null(result.Failure);
        Assert.Equal("net9.0", result.ResolvedTfm);
    }

    [Fact]
    public async Task FindAssemblyInNugetCache_Falls_Back_To_Compatible_Tfm()
    {
        using var cache = new TempDir();
        SeedPackage(cache.Path, "Legacy.Pkg", "1.0.0", "netstandard2.0");
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var result = await _service.FindAssemblyInNugetCacheAsync("Legacy.Pkg", "1.0.0", "net9.0");

        Assert.Null(result.Failure);
        Assert.Equal("netstandard2.0", result.ResolvedTfm);
    }

    [Fact]
    public async Task FindAssemblyInNugetCache_Reports_PackageNotFound()
    {
        using var cache = new TempDir();
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var result = await _service.FindAssemblyInNugetCacheAsync("Does.Not.Exist");

        Assert.Equal(NugetLookupFailure.PackageNotFound, result.Failure);
        Assert.Null(result.FoundAssembly);
    }

    [Fact]
    public async Task FindAssemblyInNugetCache_Reports_VersionNotFound_With_Available()
    {
        using var cache = new TempDir();
        SeedPackage(cache.Path, "Sharp.Events", "41.0.1", "net9.0");
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var result = await _service.FindAssemblyInNugetCacheAsync("Sharp.Events", "99.0.0");

        Assert.Equal(NugetLookupFailure.VersionNotFound, result.Failure);
        Assert.Contains("41.0.1", result.AvailableVersions);
    }

    [Fact]
    public async Task FindAssemblyInNugetCache_Reports_AssemblyNotFound_When_Tfm_Missing()
    {
        using var cache = new TempDir();
        SeedPackage(cache.Path, "Sharp.Events", "41.0.1", "net6.0");
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var result = await _service.FindAssemblyInNugetCacheAsync("Sharp.Events", "41.0.1", "net481");

        Assert.Equal(NugetLookupFailure.AssemblyNotFound, result.Failure);
        Assert.Contains("net6.0", result.AvailableTfms);
    }

    [Fact]
    public async Task FindAssemblyInNugetCache_Honors_NUGET_PACKAGES_Env_Var()
    {
        using var cache = new TempDir();
        SeedPackage(cache.Path, "EnvVar.Pkg", "2.0.0", "net9.0");
        using var _ = new EnvVar("NUGET_PACKAGES", cache.Path);

        var result = await _service.FindAssemblyInNugetCacheAsync("EnvVar.Pkg");

        Assert.Null(result.Failure);
        Assert.Equal(cache.Path, result.CacheRoot);
        Assert.StartsWith(cache.Path, result.FoundAssembly!);
    }

    private static void SeedPackage(string cacheRoot, string packageId, string version, string tfm)
    {
        var tfmDir = Path.Combine(cacheRoot, packageId.ToLowerInvariant(), version, "lib", tfm);
        Directory.CreateDirectory(tfmDir);
        File.WriteAllBytes(Path.Combine(tfmDir, packageId + ".dll"), new byte[] { 0x4D, 0x5A });
    }

    [Fact]
    public async Task FindDepsJson_Parses_Libraries_When_File_Exists()
    {
        using var temp = new TempDir();
        var projDir = Path.Combine(temp.Path, "proj");
        Directory.CreateDirectory(projDir);
        var projPath = Path.Combine(projDir, "App.csproj");
        await File.WriteAllTextAsync(projPath, MinimalCsproj());

        var outDir = Path.Combine(projDir, "bin", "Debug", "net9.0");
        Directory.CreateDirectory(outDir);
        var depsPath = Path.Combine(outDir, "App.deps.json");
        var depsJson = "{\n  \"libraries\": { \"Newtonsoft.Json/13.0.3\": { \"type\": \"package\", \"serviceable\": true, \"runtime\": { \"lib/netstandard2.0/Newtonsoft.Json.dll\": {} } } }\n}";
        await File.WriteAllTextAsync(depsPath, depsJson);

        var deps = await _service.FindDepsJsonFilesAsync(projPath, "Debug");
        Assert.Contains(deps, d => d.Name == "Newtonsoft.Json" && d.Version == "13.0.3");
    }

    private static string MinimalCsproj() => """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>
""";
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; }
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sherlock-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}

internal sealed class EnvVar : IDisposable
{
    private readonly string _name;
    private readonly string? _previous;
    public EnvVar(string name, string? value)
    {
        _name = name;
        _previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }
    public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
}
