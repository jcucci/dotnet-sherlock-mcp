using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.ProjectAssets;

namespace Sherlock.MCP.Tests;

public sealed class ProjectAssetsTests : IDisposable
{
    private static readonly string TestAssemblyPath = typeof(RuntimeDerivedFixture).Assembly.Location;
    private static readonly string RuntimeAssemblyPath = typeof(TypeAnalysisService).Assembly.Location;

    private readonly AssetsFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Read_ParsesTargetsLibrariesAndDirectDependencies()
    {
        WriteStandardAssets();

        var assets = ProjectAssetsReader.Read(_fixture.AssetsPath);

        Assert.Equal("Sample", assets.ProjectName);
        Assert.Equal(["net8.0", "net10.0"], assets.Aliases);
        Assert.Equal(3, assets.FindTarget("net8.0")!.Libraries.Count);
        Assert.NotNull(assets.FindTarget("net8.0", "linux-x64"));
        var lib = Assert.Single(assets.FindTarget("net8.0")!.Libraries, l => l.Id == "Fake.Lib");
        Assert.Equal("1.0.0", lib.Version);
        Assert.Equal("fake.lib/1.0.0", lib.Path);
        Assert.Equal([new AssetsDependency("Fake.Dep", "1.0.0")], lib.Dependencies);
        Assert.Empty(assets.FindTarget("net8.0")!.Libraries.Single(l => l.Id == "Fake.Dep").CompileAssets);
        Assert.Equal([new AssetsDependency("Fake.Lib", "[1.0.0, )")], assets.DirectDependenciesFor("net8.0"));
    }

    [Fact]
    public void Read_LegacyFrameworkNameKeys_MapToAliases()
    {
        _fixture.WriteAssets(
            targets: new JsonObject
            {
                [".NETCoreApp,Version=v8.0"] = new JsonObject { ["Fake.Lib/1.0.0"] = AssetsFixture.Package(compile: "lib/net8.0/Fake.Lib.dll") },
                [".NETCoreApp,Version=v8.0/linux-x64"] = new JsonObject()
            },
            libraries: new JsonObject { ["Fake.Lib/1.0.0"] = AssetsFixture.Library("package", "fake.lib/1.0.0") },
            frameworks: new JsonObject { ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject() } });

        var assets = ProjectAssetsReader.Read(_fixture.AssetsPath);

        Assert.Equal(["net8.0"], assets.Aliases);
        Assert.Single(assets.FindTarget("net8.0")!.Libraries);
        Assert.NotNull(assets.FindTarget("net8.0", "linux-x64"));
    }

    [Fact]
    public void Read_PlatformTargetKeys_MapToTheirAlias()
    {
        _fixture.WriteAssets(
            targets: new JsonObject
            {
                ["net8.0"] = new JsonObject(),
                ["net8.0-windows7.0"] = new JsonObject { ["Fake.Lib/1.0.0"] = AssetsFixture.Package(compile: "lib/net8.0/Fake.Lib.dll") },
                ["net8.0-windows7.0/win-x64"] = new JsonObject()
            },
            libraries: new JsonObject { ["Fake.Lib/1.0.0"] = AssetsFixture.Library("package", "fake.lib/1.0.0") },
            frameworks: new JsonObject
            {
                ["net8.0"] = AssetsFixture.Framework("net8.0"),
                ["net8.0-windows"] = AssetsFixture.Framework("net8.0-windows", ("Fake.Lib", "[1.0.0, )"))
            });

        var assets = ProjectAssetsReader.Read(_fixture.AssetsPath);

        Assert.Equal(["net8.0", "net8.0-windows"], assets.Aliases);
        Assert.Single(assets.FindTarget("net8.0-windows")!.Libraries);
        Assert.Empty(assets.FindTarget("net8.0")!.Libraries);
        Assert.NotNull(assets.FindTarget("net8.0-windows", "win-x64"));
        Assert.Single(assets.DirectDependenciesFor("net8.0-windows"));
    }

    [Fact]
    public void Read_RewrittenFile_IsParsedAgain()
    {
        WriteStandardAssets();
        var first = ProjectAssetsReader.Read(_fixture.AssetsPath);

        _fixture.WriteAssets(new JsonObject { ["net9.0"] = new JsonObject() }, new JsonObject(), new JsonObject { ["net9.0"] = AssetsFixture.Framework("net9.0") });
        File.SetLastWriteTimeUtc(_fixture.AssetsPath, DateTime.UtcNow.AddMinutes(1));
        var second = ProjectAssetsReader.Read(_fixture.AssetsPath);

        Assert.Equal(["net8.0", "net10.0"], first.Aliases);
        Assert.Equal(["net9.0"], second.Aliases);
    }

    [Fact]
    public void Read_MalformedJson_ThrowsInvalidData()
    {
        File.WriteAllText(_fixture.AssetsPath, "{ not json");

        Assert.Throws<InvalidDataException>(() => ProjectAssetsReader.Read(_fixture.AssetsPath));
    }

    [Fact]
    public void Locate_BinLayout_PicksTargetFromOutputFolder()
    {
        WriteStandardAssets();
        var dll = WriteEmptyDll(_fixture.BinDirectory("Debug", "net10.0"));

        var match = ProjectAssetsLocator.Locate(dll);

        Assert.NotNull(match);
        Assert.Equal("net10.0", match.Target.Key);
        Assert.Equal(Path.GetFullPath(_fixture.AssetsPath), ProjectAssetsLocator.FindAssetsFile(dll));
    }

    [Fact]
    public void Locate_RuntimeIdentifierFolder_PicksRidTarget()
    {
        WriteStandardAssets();
        var dll = WriteEmptyDll(_fixture.BinDirectory("Release", "net8.0", "linux-x64"));

        var match = ProjectAssetsLocator.Locate(dll);

        Assert.Equal("net8.0/linux-x64", match!.Target.Key);
        Assert.Contains(match.ResolveDependencyPaths(), path => path.EndsWith("Fake.Native.dll", StringComparison.Ordinal));
    }

    [Fact]
    public void Locate_ArtifactsLayout_FindsAssetsUnderArtifactsObj()
    {
        WriteStandardAssets();
        var artifactsAssets = Path.Combine(_fixture.Root, "artifacts", "obj", "Sample", ProjectAssetsLocator.AssetsFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactsAssets)!);
        File.Copy(_fixture.AssetsPath, artifactsAssets);
        var binDirectory = Directory.CreateDirectory(Path.Combine(_fixture.Root, "artifacts", "bin", "Sample", "debug_net8.0")).FullName;
        var dll = WriteEmptyDll(binDirectory);

        var match = ProjectAssetsLocator.Locate(dll);

        Assert.Equal(Path.GetFullPath(artifactsAssets), match!.Assets.AssetsPath);
        Assert.Equal("net8.0", match.Target.Key);
    }

    [Fact]
    public void Locate_NoAliasInPath_UsesTargetFrameworkAttribute()
    {
        var alias = TargetFrameworkNames.ToShortName(typeof(ProjectAssetsTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName)!;
        _fixture.WriteAssets(
            targets: new JsonObject { ["netstandard2.0"] = new JsonObject(), [alias] = new JsonObject() },
            libraries: new JsonObject(),
            frameworks: new JsonObject { ["netstandard2.0"] = AssetsFixture.Framework("netstandard2.0"), [alias] = AssetsFixture.Framework(alias) });
        var dll = _fixture.CopyInto(_fixture.BinDirectory("custom"), TestAssemblyPath);

        var match = ProjectAssetsLocator.Locate(dll);

        Assert.Equal(alias, match!.Target.Key);
    }

    [Fact]
    public void Locate_NoAliasInPath_PrefersExactAliasOverPlatformAlias()
    {
        var alias = TargetFrameworkNames.ToShortName(typeof(ProjectAssetsTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName)!;
        var platformAlias = alias + "-windows";
        _fixture.WriteAssets(
            targets: new JsonObject { [platformAlias + "7.0"] = new JsonObject(), [alias] = new JsonObject() },
            libraries: new JsonObject(),
            frameworks: new JsonObject { [platformAlias] = AssetsFixture.Framework(platformAlias), [alias] = AssetsFixture.Framework(alias) });
        var dll = _fixture.CopyInto(_fixture.BinDirectory("custom"), TestAssemblyPath);

        var match = ProjectAssetsLocator.Locate(dll);

        Assert.Equal(alias, match!.Target.Key);
    }

    [Fact]
    public void ResolveDependencyPaths_ProjectReferenceWithOtherFramework_UsesItsOwnOutputFolder()
    {
        WriteStandardAssets();
        var assets = JsonNode.Parse(File.ReadAllText(_fixture.AssetsPath))!;
        assets["targets"]!["net8.0"]!["Ref/1.0.0"]!["framework"] = ".NETStandard,Version=v2.0";
        File.WriteAllText(_fixture.AssetsPath, assets.ToJsonString());
        var referencedOutput = Directory.CreateDirectory(Path.Combine(_fixture.Root, "Ref", "bin", "Debug", "netstandard2.0")).FullName;
        File.WriteAllBytes(Path.Combine(referencedOutput, "Ref.dll"), []);
        var dll = WriteEmptyDll(_fixture.BinDirectory("Debug", "net8.0"));

        var paths = ProjectAssetsLocator.Locate(dll)!.ResolveDependencyPaths();

        Assert.Contains(Path.Combine(referencedOutput, "Ref.dll"), paths);
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(_fixture.Root, "Ref", "bin", "Debug", "net8.0", "Ref.dll")), paths);
    }

    [Fact]
    public void ResolveDependencyPaths_ArtifactsProjectReference_FallsBackToConfigurationOnlyPivot()
    {
        WriteStandardAssets();
        var artifacts = Path.Combine(_fixture.Root, "artifacts");
        var artifactsAssets = Path.Combine(artifacts, "obj", "Sample", ProjectAssetsLocator.AssetsFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactsAssets)!);
        File.Copy(_fixture.AssetsPath, artifactsAssets);
        var referencedOutput = Directory.CreateDirectory(Path.Combine(artifacts, "bin", "Ref", "debug")).FullName;
        File.WriteAllBytes(Path.Combine(referencedOutput, "Ref.dll"), []);
        var dll = WriteEmptyDll(Directory.CreateDirectory(Path.Combine(artifacts, "bin", "Sample", "debug_net8.0")).FullName);

        var paths = ProjectAssetsLocator.Locate(dll)!.ResolveDependencyPaths();

        Assert.Contains(Path.Combine(referencedOutput, "Ref.dll"), paths);
    }

    [Fact]
    public void ResolveDependencyPaths_SingleTargetArtifactsConsumer_FindsFrameworkQualifiedReference()
    {
        WriteSingleTargetAssetsWithNetStandardReference();
        var artifacts = Path.Combine(_fixture.Root, "artifacts");
        var artifactsAssets = Path.Combine(artifacts, "obj", "Sample", ProjectAssetsLocator.AssetsFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactsAssets)!);
        File.Copy(_fixture.AssetsPath, artifactsAssets);
        var referencedOutput = Directory.CreateDirectory(Path.Combine(artifacts, "bin", "Ref", "debug_netstandard2.0")).FullName;
        File.WriteAllBytes(Path.Combine(referencedOutput, "Ref.dll"), []);
        var dll = WriteEmptyDll(Directory.CreateDirectory(Path.Combine(artifacts, "bin", "Sample", "debug")).FullName);

        var paths = ProjectAssetsLocator.Locate(dll)!.ResolveDependencyPaths();

        Assert.Contains(Path.Combine(referencedOutput, "Ref.dll"), paths);
    }

    [Fact]
    public void AssetsPathForProject_ArtifactsLayout_FindsArtifactsObj()
    {
        WriteStandardAssets();
        var artifactsAssets = Path.Combine(_fixture.Root, "artifacts", "obj", "Sample", ProjectAssetsLocator.AssetsFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactsAssets)!);
        File.Move(_fixture.AssetsPath, artifactsAssets);

        Assert.Equal(artifactsAssets, ProjectAssetsLocator.AssetsPathForProject(_fixture.ProjectFile));
        Assert.Equal(artifactsAssets, ProjectAssetsLocator.AssetsPathForProject(_fixture.ProjectDirectory));
    }

    [Fact]
    public void AssetsPathForProject_StandardLayout_PrefersObj()
    {
        WriteStandardAssets();

        Assert.Equal(_fixture.AssetsPath, ProjectAssetsLocator.AssetsPathForProject(_fixture.ProjectDirectory));
    }

    [Theory]
    [InlineData(new[] { "net8.0-windows", "net8.0" }, "net8.0", "net8.0")]
    [InlineData(new[] { "net8.0-windows", "net9.0" }, "net8.0", "net8.0-windows")]
    [InlineData(new[] { "net9.0" }, "net8.0", null)]
    public void BestMatch_PrefersExactAlias(string[] aliases, string shortName, string? expected) =>
        Assert.Equal(expected, TargetFrameworkNames.BestMatch(aliases, shortName));

    [Fact]
    public void Locate_AssemblyOutsideBin_IsNotMatched()
    {
        WriteStandardAssets();
        var dll = WriteEmptyDll(Directory.CreateDirectory(Path.Combine(_fixture.ProjectDirectory, "lib", "net8.0")).FullName);

        Assert.Null(ProjectAssetsLocator.Locate(dll));
        Assert.Null(ProjectAssetsLocator.FindAssetsFile(dll));
        Assert.Equal("", ProjectAssetsLocator.AssetsStamp(dll));
    }

    [Fact]
    public void ResolveDependencyPaths_UsesRestoredVersionsAndProjectOutputs()
    {
        WriteStandardAssets();
        var dll = WriteEmptyDll(_fixture.BinDirectory("Debug", "net8.0"));

        var paths = ProjectAssetsLocator.Locate(dll)!.ResolveDependencyPaths();

        Assert.Contains(paths, path => path.Contains(Path.Combine("fake.lib", "1.0.0"), StringComparison.Ordinal));
        Assert.DoesNotContain(paths, path => path.Contains(Path.Combine("fake.lib", "2.0.0"), StringComparison.Ordinal));
        Assert.Contains(Path.GetFullPath(Path.Combine(_fixture.Root, "Ref", "bin", "Debug", "net8.0", "Ref.dll")), paths);
    }

    [Fact]
    public void InspectionContext_ResolvesDependencyFromRestoredPackage()
    {
        var restored = WriteRuntimePackageAssets();
        var dll = _fixture.CopyInto(_fixture.BinDirectory("Debug", "net10.0"), TestAssemblyPath);

        using var context = new MetadataOnlyInspectionContext(dll);
        var baseType = context.Assembly.GetType(typeof(RuntimeDerivedFixture).FullName!)!.BaseType!;

        Assert.Equal(nameof(TypeAnalysisService), baseType.Name);
        Assert.Equal(restored, AssemblyLocations.Of(baseType.Assembly), ignoreCase: true);
    }

    [Fact]
    public void InspectionContext_WithoutAssetsFile_LeavesDependencyUnresolved()
    {
        WriteRuntimePackageAssets();
        File.Delete(_fixture.AssetsPath);
        var dll = _fixture.CopyInto(_fixture.BinDirectory("Debug", "net10.0"), TestAssemblyPath);

        using var context = new MetadataOnlyInspectionContext(dll);
        var type = context.Assembly.GetType(typeof(RuntimeDerivedFixture).FullName!)!;

        Assert.ThrowsAny<FileNotFoundException>(() => type.BaseType);
    }

    [Fact]
    public void SharedProvider_RestoredAgain_RetiresContext()
    {
        WriteRuntimePackageAssets();
        var dll = _fixture.CopyInto(_fixture.BinDirectory("Debug", "net10.0"), TestAssemblyPath);
        using var provider = new SharedInspectionContextProvider(new RuntimeOptions());

        IAssemblyInspectionContext first;
        using (var lease = provider.Acquire(dll)) first = lease.Context;
        File.SetLastWriteTimeUtc(_fixture.AssetsPath, DateTime.UtcNow.AddMinutes(1));
        using var second = provider.Acquire(dll);

        Assert.NotSame(first, second.Context);
    }

    private string WriteRuntimePackageAssets()
    {
        var restored = _fixture.AddPackageFile("Sherlock.MCP.Runtime", "1.0.0", "lib/net8.0/Sherlock.MCP.Runtime.dll", RuntimeAssemblyPath);
        _fixture.AddPackageFile("Sherlock.MCP.Runtime", "2.0.0", "lib/net8.0/Sherlock.MCP.Runtime.dll", RuntimeAssemblyPath);
        _fixture.WriteAssets(
            targets: new JsonObject
            {
                ["net10.0"] = new JsonObject { ["Sherlock.MCP.Runtime/1.0.0"] = AssetsFixture.Package(compile: "lib/net8.0/Sherlock.MCP.Runtime.dll") }
            },
            libraries: new JsonObject { ["Sherlock.MCP.Runtime/1.0.0"] = AssetsFixture.Library("package", "sherlock.mcp.runtime/1.0.0") },
            frameworks: new JsonObject { ["net10.0"] = AssetsFixture.Framework("net10.0", ("Sherlock.MCP.Runtime", "[1.0.0, )")) });
        return restored;
    }

    internal static void WriteStandardAssets(AssetsFixture fixture)
    {
        fixture.AddPackageFile("Fake.Lib", "1.0.0", "lib/net8.0/Fake.Lib.dll");
        fixture.AddPackageFile("Fake.Lib", "2.0.0", "lib/net8.0/Fake.Lib.dll");
        fixture.AddPackageFile("Fake.Native", "1.0.0", "runtimes/linux-x64/lib/net8.0/Fake.Native.dll");
        var referencedOutput = Directory.CreateDirectory(Path.Combine(fixture.Root, "Ref", "bin", "Debug", "net8.0")).FullName;
        File.WriteAllBytes(Path.Combine(referencedOutput, "Ref.dll"), []);

        fixture.WriteAssets(
            targets: new JsonObject
            {
                ["net8.0"] = new JsonObject
                {
                    ["Fake.Lib/1.0.0"] = AssetsFixture.Package(compile: "lib/net8.0/Fake.Lib.dll", dependencies: new JsonObject { ["Fake.Dep"] = "1.0.0" }),
                    ["Fake.Dep/1.0.0"] = AssetsFixture.Package(compile: "lib/net8.0/_._"),
                    ["Ref/1.0.0"] = new JsonObject { ["type"] = "project", ["compile"] = new JsonObject { ["bin/placeholder/Ref.dll"] = new JsonObject() } }
                },
                ["net8.0/linux-x64"] = new JsonObject
                {
                    ["Fake.Lib/1.0.0"] = AssetsFixture.Package(compile: "lib/net8.0/Fake.Lib.dll"),
                    ["Fake.Native/1.0.0"] = AssetsFixture.Package(runtime: "runtimes/linux-x64/lib/net8.0/Fake.Native.dll")
                },
                ["net10.0"] = new JsonObject { ["Fake.Lib/1.0.0"] = AssetsFixture.Package(compile: "lib/net8.0/Fake.Lib.dll") }
            },
            libraries: new JsonObject
            {
                ["Fake.Lib/1.0.0"] = AssetsFixture.Library("package", "fake.lib/1.0.0"),
                ["Fake.Dep/1.0.0"] = AssetsFixture.Library("package", "fake.dep/1.0.0"),
                ["Fake.Native/1.0.0"] = AssetsFixture.Library("package", "fake.native/1.0.0"),
                ["Ref/1.0.0"] = AssetsFixture.Library("project", "../Ref/Ref.csproj")
            },
            frameworks: new JsonObject
            {
                ["net8.0"] = AssetsFixture.Framework("net8.0", ("Fake.Lib", "[1.0.0, )")),
                ["net10.0"] = AssetsFixture.Framework("net10.0", ("Fake.Lib", "[1.0.0, )"))
            });
    }

    private void WriteSingleTargetAssetsWithNetStandardReference()
    {
        Directory.CreateDirectory(Path.Combine(_fixture.Root, "Ref"));
        var reference = new JsonObject
        {
            ["type"] = "project",
            ["framework"] = ".NETStandard,Version=v2.0",
            ["compile"] = new JsonObject { ["bin/placeholder/Ref.dll"] = new JsonObject() }
        };
        _fixture.WriteAssets(
            targets: new JsonObject { ["net8.0"] = new JsonObject { ["Ref/1.0.0"] = reference } },
            libraries: new JsonObject { ["Ref/1.0.0"] = AssetsFixture.Library("project", "../Ref/Ref.csproj") },
            frameworks: new JsonObject { ["net8.0"] = AssetsFixture.Framework("net8.0") });
    }

    private void WriteStandardAssets() => WriteStandardAssets(_fixture);

    private static string WriteEmptyDll(string directory)
    {
        var path = Path.Combine(directory, "Sample.dll");
        File.WriteAllBytes(path, []);
        return path;
    }
}

[Collection(nameof(EnvVarCollection))]
public sealed class ProjectAssetsReaderCacheTests : IDisposable
{
    private readonly AssetsFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Read_ManyAssetsFiles_KeepsCacheBounded()
    {
        for (var i = 0; i <= ProjectAssetsReader.MaxCachedFiles + 8; i++)
        {
            var assets = Path.Combine(_fixture.Root, $"P{i}", "obj", ProjectAssetsLocator.AssetsFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
            File.WriteAllText(assets, "{}");
            ProjectAssetsReader.Read(assets);
        }

        Assert.True(ProjectAssetsReader.CachedFileCount <= ProjectAssetsReader.MaxCachedFiles);
    }
}
