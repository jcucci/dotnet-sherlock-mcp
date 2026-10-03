namespace Sherlock.MCP.Tests.Golden;

internal sealed class GoldenProject
{
    public const string MissingPackageId = "Sherlock.Golden.NotInCache";

    private const string ProjectXml = $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <OutputType>Library</OutputType>
            <AssemblyName>Sample</AssemblyName>
            <RootNamespace>Golden.Sample</RootNamespace>
            <Nullable>enable</Nullable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="{MissingPackageId}" Version="1.2.3" />
          </ItemGroup>
          <ItemGroup>
            <ProjectReference Include="../Ref/Ref.csproj" />
          </ItemGroup>
        </Project>
        """;

    private const string SolutionXml = """
        <Solution>
          <Project Path="Sample/Sample.csproj" />
          <Project Path="Ref/Ref.csproj" />
        </Solution>
        """;

    private const string DepsJson = """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0" },
          "targets": {},
          "libraries": {
            "Sample/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
            "Fake.Lib/1.0.0": { "type": "package", "serviceable": true, "sha512": "sha512-golden", "path": "fake.lib/1.0.0" }
          }
        }
        """;

    private GoldenProject(AssetsFixture assets, string solutionPath)
    {
        Assets = assets;
        SolutionPath = solutionPath;
    }

    public AssetsFixture Assets { get; }

    public string ProjectFile => Assets.ProjectFile;

    public string SolutionPath { get; }

    public static GoldenProject Create(string root)
    {
        var assets = new AssetsFixture(root);
        ProjectAssetsTests.WriteStandardAssets(assets);
        File.WriteAllText(assets.ProjectFile, ProjectXml);
        File.WriteAllText(Path.Combine(assets.Root, "Ref", "Ref.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(assets.BinDirectory("Debug", "net8.0"), "Sample.deps.json"), DepsJson);
        var solutionPath = Path.Combine(assets.Root, "Golden.slnx");
        File.WriteAllText(solutionPath, SolutionXml);
        return new GoldenProject(assets, solutionPath);
    }
}
