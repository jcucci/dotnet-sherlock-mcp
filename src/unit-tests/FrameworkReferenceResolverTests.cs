using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.ProjectAssets;

namespace Sherlock.MCP.Tests;

[Collection(nameof(EnvVarCollection))]
public sealed class FrameworkReferenceResolverTests : IDisposable
{
    private const string NetCoreRef = "Microsoft.NETCore.App.Ref";
    private const string AspNetCoreRef = "Microsoft.AspNetCore.App.Ref";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sherlock_fx_{Guid.NewGuid():N}");

    public FrameworkReferenceResolverTests()
    {
        Directory.CreateDirectory(DotnetRoot);
        Directory.CreateDirectory(NuGetRoot);
    }

    private string DotnetRoot => Path.Combine(_root, "dotnet");

    private string NuGetRoot => Path.Combine(_root, "nuget");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void NetCore_PicksHighestStablePatchOfTheTargetMajorMinor()
    {
        AddPack(NetCoreRef, "8.0.1", "net8.0");
        var expected = AddPack(NetCoreRef, "8.0.10", "net8.0");
        AddPack(NetCoreRef, "8.0.11-preview.1", "net8.0");
        AddPack(NetCoreRef, "9.0.0", "net9.0");

        var resolution = Resolve(Emit(".NETCoreApp,Version=v8.0"));

        Assert.Equal(FrameworkResolutionKind.ReferencePack, resolution.Kind);
        Assert.Equal("net8.0", resolution.TargetFramework);
        Assert.Equal("System.Runtime", resolution.CoreAssemblyName);
        var pack = Assert.Single(resolution.Packs);
        Assert.Equal(new FrameworkPack(NetCoreRef, "8.0.10", expected), pack);
        Assert.Equal([expected], resolution.SearchDirectories);
        Assert.Empty(resolution.MissingFrameworks);
    }

    [Fact]
    public void NetCore_FindsTargetingPackDownloadedToTheNuGetCache()
    {
        var expected = AddCachedPack("microsoft.netcore.app.ref", "7.0.20", "ref", "net7.0");

        var resolution = Resolve(Emit(".NETCoreApp,Version=v7.0"));

        Assert.Equal(FrameworkResolutionKind.ReferencePack, resolution.Kind);
        Assert.Equal(expected, Assert.Single(resolution.Packs).Directory);
    }

    [Fact]
    public void NetCore_NoPack_FallsBackToHostRuntime()
    {
        AddPack(NetCoreRef, "8.0.10", "net8.0");

        var resolution = Resolve(Emit(".NETCoreApp,Version=v6.0"));

        Assert.Equal(FrameworkResolutionKind.HostRuntime, resolution.Kind);
        Assert.Equal("net6.0", resolution.TargetFramework);
        Assert.Equal("System.Private.CoreLib", resolution.CoreAssemblyName);
        Assert.Empty(resolution.Packs);
        Assert.Equal(["Microsoft.NETCore.App"], resolution.MissingFrameworks);
        Assert.Equal(FrameworkReferenceResolver.HostRuntimeDirectory(), resolution.SearchDirectories[0]);
    }

    [Fact]
    public void RuntimeConfig_AddsSharedFrameworkPacks()
    {
        var netCore = AddPack(NetCoreRef, "8.0.10", "net8.0");
        var aspNetCore = AddPack(AspNetCoreRef, "8.0.10", "net8.0");
        var assembly = Emit(".NETCoreApp,Version=v8.0");
        WriteRuntimeConfig(assembly, "Microsoft.NETCore.App", "Microsoft.AspNetCore.App");

        var resolution = Resolve(assembly);

        Assert.Equal([netCore, aspNetCore], resolution.Packs.Select(pack => pack.Directory));
        Assert.Empty(resolution.MissingFrameworks);
    }

    [Fact]
    public void MissingSharedFrameworkPack_IsReportedWithoutLosingTheCorePack()
    {
        AddPack(NetCoreRef, "8.0.10", "net8.0");
        var assembly = Emit(".NETCoreApp,Version=v8.0");
        WriteRuntimeConfig(assembly, "Microsoft.AspNetCore.App");

        var resolution = Resolve(assembly);

        Assert.Equal(FrameworkResolutionKind.ReferencePack, resolution.Kind);
        Assert.Equal(NetCoreRef, Assert.Single(resolution.Packs).Name);
        Assert.Equal(["Microsoft.AspNetCore.App"], resolution.MissingFrameworks);
    }

    [Fact]
    public void ProjectAssets_FrameworkReferencesAndPinnedVersionsWin()
    {
        using var fixture = new AssetsFixture();
        var pinned = AddPack(NetCoreRef, "8.0.1", "net8.0");
        AddPack(NetCoreRef, "8.0.10", "net8.0");
        var aspNetCore = AddPack(AspNetCoreRef, "8.0.10", "net8.0");
        fixture.WriteAssets(
            targets: new JsonObject { ["net8.0"] = new JsonObject() },
            libraries: new JsonObject(),
            frameworks: new JsonObject
            {
                ["net8.0"] = new JsonObject
                {
                    ["targetAlias"] = "net8.0",
                    ["frameworkReferences"] = new JsonObject { ["Microsoft.AspNetCore.App"] = new JsonObject() },
                    ["downloadDependencies"] = new JsonArray(new JsonObject { ["name"] = NetCoreRef, ["version"] = "[8.0.1, 8.0.1]" })
                }
            });
        var assembly = Emit(".NETCoreApp,Version=v8.0", fixture.BinDirectory("Debug", "net8.0"));

        var resolution = Resolve(assembly, ProjectAssetsLocator.Locate(assembly));

        Assert.Equal([pinned, aspNetCore], resolution.Packs.Select(pack => pack.Directory));
    }

    [Fact]
    public void ProjectAssets_WindowsDesktopAliases_ResolveTheWindowsDesktopPack()
    {
        using var fixture = new AssetsFixture();
        AddPack(NetCoreRef, "8.0.10", "net8.0");
        var desktop = AddPack("Microsoft.WindowsDesktop.App.Ref", "8.0.10", "net8.0");
        WriteFrameworkAssets(fixture, new JsonObject
        {
            ["Microsoft.WindowsDesktop.App.WPF"] = new JsonObject(),
            ["Microsoft.WindowsDesktop.App.WindowsForms"] = new JsonObject()
        });
        var assembly = Emit(".NETCoreApp,Version=v8.0", fixture.BinDirectory("Debug", "net8.0"));

        var resolution = Resolve(assembly, ProjectAssetsLocator.Locate(assembly));

        Assert.Equal(desktop, Assert.Single(resolution.Packs, pack => pack.Name == "Microsoft.WindowsDesktop.App.Ref").Directory);
        Assert.Equal(2, resolution.Packs.Count);
        Assert.Empty(resolution.MissingFrameworks);
    }

    [Fact]
    public void ProjectAssets_PackRestoredToTheProjectPackageFolder_IsFound()
    {
        using var fixture = new AssetsFixture();
        var expected = Directory.CreateDirectory(Path.Combine(fixture.PackageFolder, "microsoft.netcore.app.ref", "8.0.5", "ref", "net8.0")).FullName;
        WriteFrameworkAssets(fixture, new JsonObject(), (NetCoreRef, "[8.0.5, 8.0.5]"));
        var assembly = Emit(".NETCoreApp,Version=v8.0", fixture.BinDirectory("Debug", "net8.0"));

        var resolution = Resolve(assembly, ProjectAssetsLocator.Locate(assembly));

        Assert.Equal(new FrameworkPack(NetCoreRef, "8.0.5", expected), Assert.Single(resolution.Packs));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"text\"")]
    public void NonObjectRuntimeConfig_IsIgnored(string json)
    {
        AddPack(NetCoreRef, "8.0.10", "net8.0");
        var assembly = Emit(".NETCoreApp,Version=v8.0");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(assembly)!, "Other.runtimeconfig.json"), json);

        var resolution = Resolve(assembly);

        Assert.Equal(FrameworkResolutionKind.ReferencePack, resolution.Kind);
        Assert.Equal(NetCoreRef, Assert.Single(resolution.Packs).Name);
    }

    [Fact]
    public void NetStandard21_UsesTheNetStandardReferencePack()
    {
        var expected = AddPack("NETStandard.Library.Ref", "2.1.0", "netstandard2.1");

        var resolution = Resolve(Emit(".NETStandard,Version=v2.1"));

        Assert.Equal(FrameworkResolutionKind.ReferencePack, resolution.Kind);
        Assert.Equal("netstandard", resolution.CoreAssemblyName);
        Assert.Equal(expected, Assert.Single(resolution.Packs).Directory);
    }

    [Fact]
    public void NetStandard20_UsesTheNetStandardLibraryPackageInTheNuGetCache()
    {
        AddCachedPack("netstandard.library", "2.0.1", "build", "netstandard2.0", "ref");
        var expected = AddCachedPack("netstandard.library", "2.0.3", "build", "netstandard2.0", "ref");

        var resolution = Resolve(Emit(".NETStandard,Version=v2.0"));

        Assert.Equal("netstandard", resolution.CoreAssemblyName);
        Assert.Equal(new FrameworkPack("NETStandard.Library", "2.0.3", expected), Assert.Single(resolution.Packs));
    }

    [Fact]
    public void SelfContainedOutput_ResolvesAppLocal()
    {
        AddPack(NetCoreRef, "8.0.10", "net8.0");
        var assembly = Emit(".NETCoreApp,Version=v8.0");
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(assembly)!, "System.Private.CoreLib.dll"), []);

        var resolution = Resolve(assembly);

        Assert.Equal(FrameworkResolutionKind.AppLocal, resolution.Kind);
        Assert.Equal("System.Private.CoreLib", resolution.CoreAssemblyName);
        Assert.Empty(resolution.SearchDirectories);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(".NETFramework,Version=v4.8")]
    public void UnsupportedOrMissingTargetFramework_FallsBackToHostRuntime(string? frameworkName)
    {
        AddPack(NetCoreRef, "8.0.10", "net8.0");

        var resolution = Resolve(Emit(frameworkName));

        Assert.Equal(FrameworkResolutionKind.HostRuntime, resolution.Kind);
        Assert.Empty(resolution.MissingFrameworks);
    }

    [Fact]
    public void InspectionContext_ResolvesFrameworkTypesFromTheInstalledReferencePack()
    {
        if (InstalledPack(NetCoreRef) is not { } pack) return;

        var assembly = Emit(
            FrameworkNameFor(pack.Tfm),
            source: "namespace Fx { public class Widget : System.Exception { } }",
            references: Directory.GetFiles(pack.Directory, "*.dll").Select(path => MetadataReference.CreateFromFile(path)));

        using var context = new MetadataOnlyInspectionContext(assembly);

        Assert.Equal(FrameworkResolutionKind.ReferencePack, context.Framework.Kind);
        var baseType = context.Assembly.GetType("Fx.Widget")!.BaseType!;
        Assert.Equal("System.Exception", baseType.FullName);
        Assert.Contains(context.Framework.Packs, framework => baseType.Assembly.Location.StartsWith(framework.Directory, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InspectionContext_ResolvesAspNetCoreBaseTypesThroughTheRuntimeConfig()
    {
        if (InstalledPack(NetCoreRef) is not { } netCore || InstalledPack(AspNetCoreRef, netCore.Version) is not { } aspNetCore) return;

        var references = Directory.GetFiles(netCore.Directory, "*.dll").Concat(Directory.GetFiles(aspNetCore.Directory, "*.dll"))
            .Select(path => MetadataReference.CreateFromFile(path));
        var assembly = Emit(
            FrameworkNameFor(netCore.Tfm),
            source: "namespace Fx { public class HomeController : Microsoft.AspNetCore.Mvc.ControllerBase { } }",
            references: references);
        WriteRuntimeConfig(assembly, "Microsoft.AspNetCore.App");

        using var context = new MetadataOnlyInspectionContext(assembly);

        var controller = Assert.Single(context.GetTypes(), type => type.Name == "HomeController");
        Assert.Equal("Microsoft.AspNetCore.Mvc.ControllerBase", controller.BaseType!.FullName);
        Assert.Empty(context.UnresolvedDependencies);
        Assert.Contains(context.Framework.Packs, pack => pack.Name == AspNetCoreRef);
    }

    [Fact]
    public void Resolver_RestoredPackageNewerThanTheReferencePack_Wins()
    {
        if (InstalledPack(NetCoreRef) is not { } installed) return;

        using var fixture = new AssetsFixture();
        var packReferences = Directory.GetFiles(installed.Directory, "*.dll").Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        var fakePack = AddPack(NetCoreRef, installed.Version, installed.Tfm);
        foreach (var dll in Directory.GetFiles(installed.Directory, "*.dll"))
            File.Copy(dll, Path.Combine(fakePack, Path.GetFileName(dll)));
        var sharedSource = "namespace Fx.Shared { public class Base { } }";
        Emit(null, fakePack, sharedSource, packReferences, "Fx.Shared", "1.0.0.0");
        var sharedV2 = Emit(null, Path.Combine(_root, "shared-v2"), sharedSource, packReferences, "Fx.Shared", "2.0.0.0");
        var packageAsset = $"lib/{installed.Tfm}/Fx.Shared.dll";
        fixture.AddPackageFile("Fx.Shared", "2.0.0", packageAsset, sharedV2);
        fixture.WriteAssets(
            targets: new JsonObject { [installed.Tfm] = new JsonObject { ["Fx.Shared/2.0.0"] = AssetsFixture.Package(compile: packageAsset) } },
            libraries: new JsonObject { ["Fx.Shared/2.0.0"] = AssetsFixture.Library("package", "fx.shared/2.0.0") },
            frameworks: new JsonObject { [installed.Tfm] = AssetsFixture.Framework(installed.Tfm, ("Fx.Shared", "[2.0.0, )")) });
        var assembly = Emit(
            FrameworkNameFor(installed.Tfm),
            fixture.BinDirectory("Debug", installed.Tfm),
            "namespace Fx { public class Derived : Fx.Shared.Base { } }",
            packReferences.Append(MetadataReference.CreateFromFile(sharedV2)));

        var baseType = WithFakeRoots(() =>
        {
            using var context = new MetadataOnlyInspectionContext(assembly);
            return context.Assembly.GetType("Fx.Derived")!.BaseType!.Assembly.GetName().Version;
        });

        Assert.Equal(new Version(2, 0, 0, 0), baseType);
    }

    [Fact]
    public void SharedProvider_InstallingAMissingPack_RebuildsTheContext()
    {
        AddPack(NetCoreRef, "8.0.10", "net8.0");
        var assembly = Emit(".NETCoreApp,Version=v8.0");
        WriteRuntimeConfig(assembly, "Microsoft.AspNetCore.App");
        using var provider = new SharedInspectionContextProvider(new Sherlock.MCP.Runtime.RuntimeOptions());

        var before = WithFakeRoots(() => { using var lease = provider.Acquire(assembly); return lease.Framework; });
        AddPack(AspNetCoreRef, "8.0.10", "net8.0");
        var after = WithFakeRoots(() => { using var lease = provider.Acquire(assembly); return lease.Framework; });

        Assert.Equal(["Microsoft.AspNetCore.App"], before.MissingFrameworks);
        Assert.Empty(after.MissingFrameworks);
        Assert.Contains(after.Packs, pack => pack.Name == AspNetCoreRef);
    }

    [Fact]
    public void SharedProvider_ChangedRuntimeConfig_RebuildsTheContext()
    {
        AddPack(NetCoreRef, "8.0.10", "net8.0");
        AddPack(AspNetCoreRef, "8.0.10", "net8.0");
        var assembly = Emit(".NETCoreApp,Version=v8.0");
        WriteRuntimeConfig(assembly, "Microsoft.NETCore.App");
        using var provider = new SharedInspectionContextProvider(new Sherlock.MCP.Runtime.RuntimeOptions());

        var before = WithFakeRoots(() => { using var lease = provider.Acquire(assembly); return lease.Framework; });
        WriteRuntimeConfig(assembly, "Microsoft.NETCore.App", "Microsoft.AspNetCore.App", "Changed.Framework");
        var after = WithFakeRoots(() => { using var lease = provider.Acquire(assembly); return lease.Framework; });

        Assert.Single(before.Packs);
        Assert.Equal(2, after.Packs.Count);
    }

    private FrameworkResolution Resolve(string assemblyPath, ProjectAssetsMatch? assets = null) =>
        WithFakeRoots(() => FrameworkReferenceResolver.Resolve(assemblyPath, assets));

    private T WithFakeRoots<T>(Func<T> action)
    {
        using var _ = new EnvVar("NUGET_PACKAGES", NuGetRoot);
        FrameworkReferenceResolver.DotnetRootOverride = DotnetRoot;
        try
        {
            return action();
        }
        finally
        {
            FrameworkReferenceResolver.DotnetRootOverride = null;
        }
    }

    private string AddPack(string name, string version, string tfm) =>
        Directory.CreateDirectory(Path.Combine(DotnetRoot, "packs", name, version, "ref", tfm)).FullName;

    private string AddCachedPack(string id, string version, params string[] relative) =>
        Directory.CreateDirectory(Path.Combine([NuGetRoot, id, version, .. relative])).FullName;

    private string Emit(
        string? frameworkName,
        string? directory = null,
        string? source = null,
        IEnumerable<MetadataReference>? references = null,
        string assemblyName = "Fx.App",
        string? assemblyVersion = null)
    {
        directory ??= Path.Combine(_root, "app", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var attribute = frameworkName is null ? "" : $"[assembly: System.Runtime.Versioning.TargetFramework(\"{frameworkName}\")]\n";
        if (assemblyVersion != null) attribute += $"[assembly: System.Reflection.AssemblyVersion(\"{assemblyVersion}\")]\n";
        var assemblyPath = Path.Combine(directory, $"{assemblyName}.dll");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(attribute + (source ?? "namespace Fx { public class Widget { } }"))],
            references ?? PdbFixtures.PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = File.Create(assemblyPath);
        var result = compilation.Emit(peStream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return assemblyPath;
    }

    private static void WriteFrameworkAssets(AssetsFixture fixture, JsonObject frameworkReferences, params (string Name, string Version)[] downloads) =>
        fixture.WriteAssets(
            targets: new JsonObject { ["net8.0"] = new JsonObject() },
            libraries: new JsonObject(),
            frameworks: new JsonObject
            {
                ["net8.0"] = new JsonObject
                {
                    ["targetAlias"] = "net8.0",
                    ["frameworkReferences"] = frameworkReferences,
                    ["downloadDependencies"] = new JsonArray(downloads
                        .Select(download => (JsonNode)new JsonObject { ["name"] = download.Name, ["version"] = download.Version })
                        .ToArray())
                }
            });

    private static void WriteRuntimeConfig(string assemblyPath, params string[] frameworks) =>
        File.WriteAllText(
            Path.ChangeExtension(assemblyPath, ".runtimeconfig.json"),
            new JsonObject
            {
                ["runtimeOptions"] = new JsonObject
                {
                    ["frameworks"] = new JsonArray(frameworks.Select(name => (JsonNode)new JsonObject { ["name"] = name }).ToArray())
                }
            }.ToJsonString());

    private static string FrameworkNameFor(string tfm) => $".NETCoreApp,Version=v{tfm["net".Length..]}";

    private static (string Version, string Tfm, string Directory)? InstalledPack(string name, string? version = null)
    {
        var packRoot = Path.GetFullPath(Path.Combine(FrameworkReferenceResolver.HostRuntimeDirectory(), "..", "..", "..", "packs", name));
        if (!Directory.Exists(packRoot)) return null;

        var hostMajorMinor = $"{Environment.Version.Major}.{Environment.Version.Minor}.";
        return Directory.GetDirectories(packRoot)
            .Where(directory => version is null ? Path.GetFileName(directory).StartsWith(hostMajorMinor, StringComparison.Ordinal) : Path.GetFileName(directory) == version)
            .Select(directory => Path.Combine(directory, "ref", $"net{Environment.Version.Major}.{Environment.Version.Minor}"))
            .Where(Directory.Exists)
            .Select(directory => ((string Version, string Tfm, string Directory)?)(
                Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(directory))!), Path.GetFileName(directory), directory))
            .FirstOrDefault();
    }
}
