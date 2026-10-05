using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;
using Sherlock.MCP.Runtime.ProjectEvaluation;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public sealed class ProjectEvaluationServiceTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _projectPath;

    public ProjectEvaluationServiceTests()
    {
        _projectPath = Path.Combine(_temp.Path, "App.csproj");
        File.WriteAllText(_projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Xml.Only" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task AnalyzeProject_MapsEvaluatedPropertiesAndItems()
    {
        var evaluator = new FakeProjectEvaluator(call => call.Properties.Contains("TargetDir")
            ? Output(call)
            : Result(
                properties: new()
                {
                    ["AssemblyName"] = "Custom.Name",
                    ["RootNamespace"] = "Custom.Root",
                    ["OutputType"] = "Exe",
                    ["TargetFramework"] = "",
                    ["TargetFrameworks"] = "net8.0; net10.0"
                },
                items: new()
                {
                    ["PackageReference"] =
                    [
                        Item("Central.Package"),
                        Item("Pinned.Package", ("Version", "2.0.0")),
                        Item("Overridden.Package", ("VersionOverride", "3.1.0")),
                        Item("NETStandard.Library", ("Version", "2.0.3"), ("IsImplicitlyDefined", "true"))
                    ],
                    ["PackageVersion"] =
                    [
                        Item("central.package", ("Version", "1.2.3")),
                        Item("Overridden.Package", ("Version", "3.0.0"))
                    ],
                    ["ProjectReference"] =
                    [
                        Item("..\\Lib\\Lib.csproj", ("FullPath", "/repo/Lib/Lib.csproj"))
                    ]
                }));

        var result = await Service(evaluator).AnalyzeProjectFileAsync(_projectPath);

        Assert.Equal(ProjectEvaluationInfo.MsBuild, result.Evaluation);
        Assert.Equal("Custom.Name", result.AssemblyName);
        Assert.Equal("Custom.Root", result.RootNamespace);
        Assert.Equal("Exe", result.OutputType);
        Assert.Equal("net8.0", result.TargetFramework);
        Assert.Equal(["net8.0", "net10.0"], result.TargetFrameworks);
        Assert.Equal(
            ["Central.Package@1.2.3", "Pinned.Package@2.0.0", "Overridden.Package@3.1.0"],
            result.PackageReferences.Select(p => $"{p.Name}@{p.Version}"));
        var reference = Assert.Single(result.ProjectReferences);
        Assert.Equal("Lib", reference.Name);
        Assert.Equal("/repo/Lib/Lib.csproj", reference.FullPath);
        Assert.Contains(Path.Combine(_temp.Path, "bin", "Release", "net10.0"), result.OutputPaths);
    }

    [Fact]
    public async Task GetProjectOutputPaths_EvaluatesEachConfigurationAndTargetFramework()
    {
        var evaluator = new FakeProjectEvaluator(call => call.Properties.Contains("TargetDir")
            ? Output(call)
            : Result(properties: new() { ["TargetFramework"] = "", ["TargetFrameworks"] = "net8.0;net10.0" }));

        var outputs = await Service(evaluator).GetProjectOutputPathsAsync(_projectPath);

        Assert.Equal(ProjectEvaluationInfo.MsBuild, outputs.Evaluation);
        Assert.Equal(
            [
                Path.Combine(_temp.Path, "bin", "Debug", "net8.0"),
                Path.Combine(_temp.Path, "bin", "Debug", "net10.0"),
                Path.Combine(_temp.Path, "bin", "Release", "net8.0"),
                Path.Combine(_temp.Path, "bin", "Release", "net10.0")
            ],
            outputs.Paths);
        Assert.Equal(5, evaluator.Calls.Count);
    }

    [Fact]
    public async Task GetProjectOutputPaths_SingleTarget_DoesNotPinTheTargetFramework()
    {
        var evaluator = new FakeProjectEvaluator(call => call.Properties.Contains("TargetDir")
            ? Result(properties: new() { ["TargetDir"] = Path.Combine(_temp.Path, "out") + Path.DirectorySeparatorChar })
            : Result(properties: new() { ["TargetFramework"] = "net8.0", ["TargetFrameworks"] = "" }));

        var outputs = await Service(evaluator).GetProjectOutputPathsAsync(_projectPath, configuration: "Debug");

        Assert.Equal([Path.Combine(_temp.Path, "out")], outputs.Paths);
        var outputCall = Assert.Single(evaluator.Calls, call => call.Properties.Contains("TargetDir"));
        Assert.Equal(new Dictionary<string, string> { ["Configuration"] = "Debug" }, outputCall.GlobalProperties);
    }

    [Fact]
    public async Task GetProjectOutputPaths_SingleEntryTargetFrameworks_PinsTheTargetFramework()
    {
        var evaluator = new FakeProjectEvaluator(call => call.Properties.Contains("TargetDir")
            ? Output(call)
            : Result(properties: new() { ["TargetFramework"] = "", ["TargetFrameworks"] = "net8.0" }));

        var outputs = await Service(evaluator).GetProjectOutputPathsAsync(_projectPath, configuration: "Debug");

        Assert.Equal([Path.Combine(_temp.Path, "bin", "Debug", "net8.0")], outputs.Paths);
        var outputCall = Assert.Single(evaluator.Calls, call => call.Properties.Contains("TargetDir"));
        Assert.Equal("net8.0", outputCall.GlobalProperties["TargetFramework"]);
    }

    [Fact]
    public async Task GetProjectOutputPaths_NoTargetDir_FallsBackToXml()
    {
        var evaluator = new FakeProjectEvaluator(call => call.Properties.Contains("TargetDir")
            ? Result(properties: new() { ["TargetDir"] = "" })
            : Result(properties: new() { ["TargetFramework"] = "net8.0" }));

        var outputs = await Service(evaluator).GetProjectOutputPathsAsync(_projectPath, configuration: "Debug");

        Assert.Equal(ProjectEvaluationInfo.XmlMode, outputs.Evaluation.Mode);
        Assert.Equal([Path.Combine(_temp.Path, "bin", "Debug", "net8.0")], outputs.Paths);
    }

    [Fact]
    public async Task AnalyzeProject_EvaluationFailure_FallsBackToXmlWithReason()
    {
        var evaluator = new FakeProjectEvaluator(_ => ProjectEvaluationResult.Failed("error MSB4236: SDK not found"));

        var result = await Service(evaluator).AnalyzeProjectFileAsync(_projectPath);

        Assert.Equal(ProjectEvaluationInfo.Xml("error MSB4236: SDK not found"), result.Evaluation);
        Assert.Equal("net8.0", result.TargetFramework);
        Assert.Contains(result.PackageReferences, p => p.Name == "Xml.Only");
    }

    [Fact]
    public async Task AnalyzeProject_OutputEvaluationFailure_FallsBackToXml()
    {
        var evaluator = new FakeProjectEvaluator(call => call.Properties.Contains("TargetDir")
            ? ProjectEvaluationResult.Failed("timed out")
            : Result(properties: new() { ["AssemblyName"] = "Evaluated" }));

        var result = await Service(evaluator).AnalyzeProjectFileAsync(_projectPath);

        Assert.Equal(ProjectEvaluationInfo.Xml("timed out"), result.Evaluation);
        Assert.Equal("App", result.AssemblyName);
    }

    [Fact]
    public async Task ProjectEvaluationOff_NeverCallsTheEvaluator()
    {
        var evaluator = new FakeProjectEvaluator(_ => throw new InvalidOperationException("should not evaluate"));
        var service = new ProjectAnalysisService(new RuntimeOptions { ProjectEvaluation = ProjectEvaluationMode.Off }, evaluator);

        var result = await service.AnalyzeProjectFileAsync(_projectPath);
        var outputs = await service.GetProjectOutputPathsAsync(_projectPath);

        Assert.Empty(evaluator.Calls);
        Assert.Equal(ProjectEvaluationInfo.Xml(ProjectEvaluationInfo.DisabledReason), result.Evaluation);
        Assert.Equal(ProjectEvaluationInfo.Xml(ProjectEvaluationInfo.DisabledReason), outputs.Evaluation);
        Assert.Equal(ProjectEvaluationInfo.XmlMode, service.EvaluationMode);
    }

    [Fact]
    public void EvaluationMode_ReflectsTheEvaluatorAndOption()
    {
        var options = new RuntimeOptions { ProjectEvaluation = ProjectEvaluationMode.Auto };
        var service = new ProjectAnalysisService(options, new FakeProjectEvaluator(_ => Result()));

        Assert.Equal(ProjectEvaluationInfo.MsBuildMode, service.EvaluationMode);
        Assert.Equal(ProjectEvaluationInfo.XmlMode, new ProjectAnalysisService().EvaluationMode);

        options.ProjectEvaluation = ProjectEvaluationMode.Off;
        Assert.Equal(ProjectEvaluationInfo.XmlMode, service.EvaluationMode);
    }

    [Theory]
    [InlineData("auto", ProjectEvaluationMode.Auto)]
    [InlineData("off", ProjectEvaluationMode.Off)]
    public void UpdateRuntimeOptions_SetsProjectEvaluation(string value, ProjectEvaluationMode expected)
    {
        var options = new RuntimeOptions();

        using var doc = JsonDocument.Parse(ConfigTools.UpdateRuntimeOptions(options, projectEvaluation: value));

        Assert.Equal(expected, options.ProjectEvaluation);
        Assert.Equal(value, doc.RootElement.GetProperty("data").GetProperty("projectEvaluation").GetString());
    }

    [Fact]
    public void UpdateRuntimeOptions_InvalidProjectEvaluation_LeavesEveryOptionUnchanged()
    {
        var options = new RuntimeOptions();
        var sourceFetch = options.SourceFetch;
        var projectEvaluation = options.ProjectEvaluation;

        ConfigTools.UpdateRuntimeOptions(options, sourceFetch: "off", projectEvaluation: "sometimes");

        Assert.Equal(sourceFetch, options.SourceFetch);
        Assert.Equal(projectEvaluation, options.ProjectEvaluation);
    }

    [Fact]
    public void UpdateRuntimeOptions_RejectsAnUnknownProjectEvaluationMode()
    {
        using var doc = JsonDocument.Parse(ConfigTools.UpdateRuntimeOptions(new RuntimeOptions(), projectEvaluation: "sometimes"));

        Assert.Equal("InvalidArgument", doc.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("auto", ProjectEvaluationMode.Auto)]
    [InlineData(" ON ", ProjectEvaluationMode.Auto)]
    [InlineData("msbuild", ProjectEvaluationMode.Auto)]
    [InlineData("off", ProjectEvaluationMode.Off)]
    [InlineData("xml", ProjectEvaluationMode.Off)]
    [InlineData("0", ProjectEvaluationMode.Off)]
    public void TryParseProjectEvaluation_AcceptsAliases(string value, ProjectEvaluationMode expected)
    {
        Assert.True(RuntimeOptions.TryParseProjectEvaluation(value, out var mode));
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sometimes")]
    public void TryParseProjectEvaluation_RejectsUnknownValues(string? value) =>
        Assert.False(RuntimeOptions.TryParseProjectEvaluation(value, out _));

    private static ProjectAnalysisService Service(IProjectEvaluator evaluator) =>
        new(new RuntimeOptions { ProjectEvaluation = ProjectEvaluationMode.Auto }, evaluator);

    private ProjectEvaluationResult Output(EvaluationCall call)
    {
        var configuration = call.GlobalProperties["Configuration"];
        var framework = call.GlobalProperties.GetValueOrDefault("TargetFramework", "net8.0");
        var targetDir = Path.Combine(_temp.Path, "bin", configuration, framework) + Path.DirectorySeparatorChar;
        return Result(properties: new() { ["TargetDir"] = targetDir });
    }

    private static ProjectEvaluationResult Result(
        Dictionary<string, string>? properties = null,
        Dictionary<string, EvaluatedItem[]>? items = null) =>
        new(true, null,
            new Dictionary<string, string>(properties ?? [], StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, EvaluatedItem[]>(items ?? [], StringComparer.OrdinalIgnoreCase));

    private static EvaluatedItem Item(string identity, params (string Name, string Value)[] metadata) =>
        new(identity, metadata.ToDictionary(m => m.Name, m => m.Value, StringComparer.OrdinalIgnoreCase));
}

internal sealed record EvaluationCall(
    string ProjectPath,
    IReadOnlyDictionary<string, string> GlobalProperties,
    IReadOnlyList<string> Properties,
    IReadOnlyList<string> Items);

internal sealed class FakeProjectEvaluator(Func<EvaluationCall, ProjectEvaluationResult> respond) : IProjectEvaluator
{
    private readonly List<EvaluationCall> _calls = [];

    public IReadOnlyList<EvaluationCall> Calls
    {
        get { lock (_calls) return _calls.ToArray(); }
    }

    public Task<ProjectEvaluationResult> EvaluateAsync(
        string projectPath,
        IReadOnlyDictionary<string, string> globalProperties,
        IReadOnlyList<string> properties,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken = default)
    {
        var call = new EvaluationCall(projectPath, globalProperties, properties, items);
        lock (_calls) _calls.Add(call);
        return Task.FromResult(respond(call));
    }
}
