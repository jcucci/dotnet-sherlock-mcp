using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;
using Sherlock.MCP.Runtime.ProjectEvaluation;

namespace Sherlock.MCP.Tests;

public class DotnetCliProjectEvaluatorTests
{
    private static readonly IReadOnlyDictionary<string, string> NoGlobalProperties = new Dictionary<string, string>();

    [Fact]
    public void Parse_ReadsPropertiesAndItemMetadata()
    {
        var result = DotnetCliProjectEvaluator.Parse("""
            {
              "Properties": { "TargetFrameworks": "net8.0;net10.0", "AssemblyName": "App" },
              "Items": {
                "PackageReference": [ { "Identity": "Newtonsoft.Json", "Version": "13.0.3", "FullPath": "/x" } ],
                "ProjectReference": []
              }
            }
            """);

        Assert.True(result.Success);
        Assert.Equal("net8.0;net10.0", result.GetProperty("targetframeworks"));
        var package = Assert.Single(result.GetItems("PackageReference"));
        Assert.Equal("Newtonsoft.Json", package.Identity);
        Assert.Equal("13.0.3", package.GetMetadata("version"));
        Assert.Empty(result.GetItems("ProjectReference"));
        Assert.Empty(result.GetItems("PackageVersion"));
    }

    [Fact]
    public void Parse_SkipsLeadingNoiseAndTreatsEmptyPropertiesAsMissing()
    {
        var result = DotnetCliProjectEvaluator.Parse("Some banner\n{ \"Properties\": { \"TargetFramework\": \"\" } }");

        Assert.True(result.Success);
        Assert.Null(result.GetProperty("TargetFramework"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("net8.0")]
    [InlineData("{ not json")]
    public void Parse_UnreadableOutput_Fails(string output)
    {
        var result = DotnetCliProjectEvaluator.Parse(output);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task EvaluateAsync_NoDotnet_ReportsSdkNotFound()
    {
        var evaluator = new DotnetCliProjectEvaluator(locateDotnet: () => null);

        var result = await evaluator.EvaluateAsync("/nowhere/App.csproj", NoGlobalProperties, ["TargetFramework"], []);

        Assert.False(result.Success);
        Assert.Equal(DotnetCliProjectEvaluator.SdkNotFoundReason, result.FailureReason);
    }

    [Fact]
    public async Task EvaluateAsync_CancelledToken_Throws()
    {
        var evaluator = new DotnetCliProjectEvaluator();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            evaluator.EvaluateAsync("/nowhere/App.csproj", NoGlobalProperties, ["TargetFramework"], [], cancelled.Token));
    }

    [Fact]
    public async Task EvaluateAsync_MissingProject_FailsWithMsBuildError()
    {
        using var temp = new TempDir();

        var result = await new DotnetCliProjectEvaluator().EvaluateAsync(
            Path.Combine(temp.Path, "Missing.csproj"), NoGlobalProperties, ["TargetFramework"], []);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
    }

    [Fact]
    public async Task AnalyzeProject_WithRealSdk_HonoursDirectoryBuildPropsAndCentralPackageManagement()
    {
        using var temp = new TempDir();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "Directory.Build.props"), """
            <Project>
              <PropertyGroup>
                <TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>
                <AssemblyName>Evaluated.App</AssemblyName>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "Directory.Packages.props"), """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        var projectDirectory = Path.Combine(temp.Path, "src", "App");
        Directory.CreateDirectory(projectDirectory);
        var projectPath = Path.Combine(projectDirectory, "App.csproj");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" />
                <ProjectReference Include="../Lib/Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        var service = new ProjectAnalysisService(new RuntimeOptions { ProjectEvaluation = ProjectEvaluationMode.Auto }, new DotnetCliProjectEvaluator());

        var result = await service.AnalyzeProjectFileAsync(projectPath);

        Assert.Equal(ProjectEvaluationInfo.MsBuild, result.Evaluation);
        Assert.Equal("Evaluated.App", result.AssemblyName);
        Assert.Equal(["net8.0", "netstandard2.0"], result.TargetFrameworks);
        var package = Assert.Single(result.PackageReferences);
        Assert.Equal("Newtonsoft.Json", package.Name);
        Assert.Equal("13.0.3", package.Version);
        var reference = Assert.Single(result.ProjectReferences);
        Assert.Equal("Lib", reference.Name);
        Assert.EndsWith(Path.Combine("src", "Lib", "Lib.csproj"), reference.FullPath);
        Assert.Equal(4, result.OutputPaths.Length);
        Assert.Contains(result.OutputPaths, path => path.EndsWith(Path.Combine("src", "App", "bin", "Release", "netstandard2.0"), StringComparison.Ordinal));
    }
}
