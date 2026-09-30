using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Shared;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public sealed class ToolErrorsTests : IDisposable
{
    private static readonly string TestAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private static readonly IInspectionContextProvider Contexts = new SharedInspectionContextProvider(new RuntimeOptions());

    private readonly List<string> _tempDirectories = [];

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void IsErrorPayload_ErrorEnvelope_ReturnsTrue() =>
        Assert.True(ToolErrorFlag.IsErrorPayload(JsonHelpers.Error("TypeNotFound", "missing")));

    [Fact]
    public void IsErrorPayload_GuidanceEnvelope_ReturnsTrue() =>
        Assert.True(ToolErrorFlag.IsErrorPayload(JsonHelpers.ErrorWithGuidance("TypeNotFound", "missing", suggestion: "retry")));

    [Fact]
    public void IsErrorPayload_SuccessEnvelopeMentioningError_ReturnsFalse() =>
        Assert.False(ToolErrorFlag.IsErrorPayload(JsonHelpers.Envelope("x", new { kind = "error", note = "error" })));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain text error")]
    [InlineData("{ \"kind\": \"error\"")]
    public void IsErrorPayload_NonEnvelopeText_ReturnsFalse(string? text) =>
        Assert.False(ToolErrorFlag.IsErrorPayload(text));

    [Fact]
    public void Apply_ErrorText_SetsIsError()
    {
        var result = ToolErrorFlag.Apply(ToolResponse.Result(JsonHelpers.Error("InternalError", "boom")));

        Assert.True(result.IsError);
    }

    [Fact]
    public void Apply_SuccessText_LeavesIsErrorUnset()
    {
        var result = ToolErrorFlag.Apply(ToolResponse.Result(JsonHelpers.Envelope("type.list", new { })));

        Assert.NotEqual(true, result.IsError);
    }

    [Fact]
    public void Apply_NoTextContent_LeavesIsErrorUnset()
    {
        var result = ToolErrorFlag.Apply(new CallToolResult { Content = [] });

        Assert.NotEqual(true, result.IsError);
    }

    [Fact]
    public void AssemblyNotFound_SuggestsDiscoveryToolsAndSimilarFiles()
    {
        var misspelled = Path.Combine(Path.GetDirectoryName(TestAssemblyPath)!, "Sherlock.MCP.Test.dll");

        var error = Parse(ToolErrors.AssemblyNotFound(misspelled));

        Assert.Equal("AssemblyNotFound", error.GetProperty("code").GetString());
        Assert.Contains("find_assembly_by_class_name", Strings(error.GetProperty("alternativeTools")));
        Assert.Contains(TestAssemblyPath, Strings(error.GetProperty("recommendedParams").GetProperty("similarFiles")));
    }

    [Fact]
    public void AssemblyNotFound_MissingDirectory_OmitsSimilarFiles()
    {
        var error = Parse(ToolErrors.AssemblyNotFound("/no/such/dir/Thing.dll"));

        Assert.False(error.TryGetProperty("recommendedParams", out _));
        Assert.True(error.TryGetProperty("alternativeTools", out _));
    }

    [Fact]
    public void AssemblyNotFound_DllAndExeWithSameName_PrefersDll()
    {
        var directory = CreateTempDirectory("App.dll", "App.exe");

        var error = Parse(ToolErrors.AssemblyNotFound(Path.Combine(directory, "Ap.dll")));

        Assert.Equal(Path.Combine(directory, "App.dll"), Assert.Single(SimilarFiles(error)));
    }

    [Fact]
    public void AssemblyNotFound_TypoInMiddleSegment_RanksIntendedFileFirst()
    {
        var directory = CreateTempDirectory(
            "Microsoft.Extensions.Logging.Abstractions.dll",
            "Microsoft.Extensions.Hosting.Abstractions.dll",
            "Microsoft.Extensions.Options.Abstractions.dll",
            "Microsoft.Extensions.Primitives.Abstractions.dll",
            "Microsoft.Extensions.FileProviders.Abstractions.dll",
            "Microsoft.Extensions.Configuration.Abstractions.dll",
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
            "System.Abstractions.dll");

        var error = Parse(ToolErrors.AssemblyNotFound(Path.Combine(directory, "Microsoft.Extensions.Loging.Abstractions.dll")));

        Assert.Equal(Path.Combine(directory, "Microsoft.Extensions.Logging.Abstractions.dll"), SimilarFiles(error)[0]);
        Assert.DoesNotContain(Path.Combine(directory, "System.Abstractions.dll"), SimilarFiles(error));
    }

    [Fact]
    public void TypeNotFound_NoCandidatesAndUnresolvedDependencies_ReportsDependencyResolutionFailed()
    {
        var error = Parse(ToolErrors.TypeNotFound(new StubContext([], ["Azure.Core"]), "Azure.Widget"));

        Assert.Equal("DependencyResolutionFailed", error.GetProperty("code").GetString());
        Assert.Contains("Azure.Core", Strings(error.GetProperty("details").GetProperty("unresolvedDependencies")));
    }

    [Fact]
    public void TypeNotFound_CandidatesAndUnresolvedDependencies_KeepsCandidatesAndMentionsDependencies()
    {
        var error = Parse(ToolErrors.TypeNotFound(new StubContext([typeof(TestSampleClass)], ["Azure.Core"]), "TestSampleClas"));

        Assert.Equal("TypeNotFound", error.GetProperty("code").GetString());
        Assert.Contains(typeof(TestSampleClass).FullName, Candidates(error));
        Assert.Contains("Azure.Core", error.GetProperty("suggestion").GetString());
        Assert.Contains("Azure.Core", Strings(error.GetProperty("details").GetProperty("unresolvedDependencies")));
    }

    [Fact]
    public void TypeNotFound_NoUnresolvedDependencies_OmitsDetails()
    {
        var error = Parse(ToolErrors.TypeNotFound(new StubContext([typeof(TestSampleClass)], []), "TestSampleClas"));

        Assert.False(error.TryGetProperty("details", out _));
    }

    [Theory]
    [InlineData("method", "get_type_methods")]
    [InlineData("property", "get_type_properties")]
    [InlineData("field", "get_type_fields")]
    [InlineData("event", "get_type_events")]
    [InlineData("constructor", "get_type_constructors")]
    public void MemberNotFound_SuggestsToolForMemberKind(string memberKind, string expectedTool)
    {
        var error = Parse(ToolErrors.MemberNotFound(typeof(TestSampleClass), "Missing", memberKind));

        var tools = Strings(error.GetProperty("alternativeTools"));
        Assert.Contains(expectedTool, tools);
        Assert.DoesNotContain(tools, tool => tool!.StartsWith("get_type_", StringComparison.Ordinal) && tool != expectedTool);
    }

    [Fact]
    public void DependencyGuidance_QualifiesAdditionalAssemblies()
    {
        var error = Parse(ToolErrors.FromException(new DependencyResolutionException("/x/App.dll", ["Azure.Core"]), "get type info"));

        Assert.Contains("tools that accept additionalAssemblies", error.GetProperty("suggestion").GetString());
    }

    [Theory]
    [InlineData(typeof(BadImageFormatException), "InvalidAssembly")]
    [InlineData(typeof(FileNotFoundException), "DependencyNotFound")]
    [InlineData(typeof(FileLoadException), "DependencyNotFound")]
    [InlineData(typeof(UnauthorizedAccessException), "AccessDenied")]
    [InlineData(typeof(ArgumentException), "InvalidArgument")]
    [InlineData(typeof(InvalidOperationException), "InternalError")]
    public void FromException_MapsExceptionToCode(Type exceptionType, string expectedCode)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        var error = Parse(ToolErrors.FromException(ex, "do the thing"));

        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
        Assert.Equal("Failed to do the thing: boom", error.GetProperty("message").GetString());
    }

    [Fact]
    public void FromException_FileNotFoundWithOverride_UsesOverrideCode()
    {
        var error = Parse(ToolErrors.FromException(new FileNotFoundException("gone"), "analyze project", fileNotFoundCode: "ProjectNotFound"));

        Assert.Equal("ProjectNotFound", error.GetProperty("code").GetString());
    }

    [Fact]
    public void FromException_DependencyResolution_KeepsGuidance()
    {
        var ex = new DependencyResolutionException("/x/App.dll", ["Azure.Core"]);

        var error = Parse(ToolErrors.FromException(ex, "get types"));

        Assert.Equal("DependencyResolutionFailed", error.GetProperty("code").GetString());
        Assert.Contains("Azure.Core", error.GetProperty("suggestion").GetString());
    }

    [Fact]
    public void GetTypeInfo_MisspelledType_SuggestsClosestType()
    {
        var error = Parse(TypeAnalysisTools.GetTypeInfo(new TypeAnalysisService(), Contexts, TestAssemblyPath, "TestSampleClas"));

        Assert.Equal("TypeNotFound", error.GetProperty("code").GetString());
        Assert.Contains(typeof(TestSampleClass).FullName, Candidates(error));
        Assert.Contains("search_members", Strings(error.GetProperty("alternativeTools")));
    }

    [Fact]
    public void AnalyzeMethod_MisspelledMethod_SuggestsClosestMember()
    {
        var error = Parse(ReflectionTools.AnalyzeMethod(Contexts, TestAssemblyPath, typeof(TestSampleClass).FullName!, "VirtualMetod"));

        Assert.Equal("MemberNotFound", error.GetProperty("code").GetString());
        Assert.Contains("VirtualMethod", Candidates(error));
    }

    [Fact]
    public void AnalyzeMethod_MisspelledPrivateMethod_DoesNotSuggestUnreachableName()
    {
        var error = Parse(ReflectionTools.AnalyzeMethod(Contexts, TestAssemblyPath, typeof(SuggestionFixture).FullName!, "ParseCor"));

        Assert.Equal("MemberNotFound", error.GetProperty("code").GetString());
        Assert.DoesNotContain("ParseCore", Candidates(error));
        Assert.Contains("ParseCorePublic", Candidates(error));
    }

    [Fact]
    public void GetTypeMethods_MisspelledType_SuggestsClosestType()
    {
        var middleware = new Sherlock.MCP.Server.Middleware.ToolMiddleware(
            new Sherlock.MCP.Runtime.Caching.InMemoryToolResponseCache(),
            new Sherlock.MCP.Runtime.Telemetry.NoopTelemetry(),
            new RuntimeOptions());

        var error = Parse(MemberAnalysisTools.GetTypeMethods(
            new MemberAnalysisService(), Contexts, middleware, new RuntimeOptions(), TestAssemblyPath, "testsampleclass2", noCache: true));

        Assert.Equal("TypeNotFound", error.GetProperty("code").GetString());
        Assert.Contains(typeof(TestSampleClass).FullName, Candidates(error));
    }

    private string CreateTempDirectory(params string[] fileNames)
    {
        var directory = Directory.CreateTempSubdirectory("sherlock-errors-").FullName;
        _tempDirectories.Add(directory);
        foreach (var fileName in fileNames)
            File.WriteAllBytes(Path.Combine(directory, fileName), []);
        return directory;
    }

    private static string?[] SimilarFiles(JsonElement error) =>
        Strings(error.GetProperty("recommendedParams").GetProperty("similarFiles"));

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string?[] Candidates(JsonElement error) =>
        Strings(error.GetProperty("recommendedParams").GetProperty("candidates"));

    private static string?[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(item => item.GetString()).ToArray();
}

public class SuggestionFixture
{
    public void ParseCorePublic() { }

    private void ParseCore() { }
}

internal sealed class StubContext(Type[] types, string[] unresolved) : IAssemblyInspectionContext
{
    public Assembly Assembly => typeof(StubContext).Assembly;

    public IReadOnlyList<string> UnresolvedDependencies => unresolved;

    public IEnumerable<Type> GetTypes() => types;

    public MemberInfo[] GetMembers(Type type, BindingFlags flags) => [];

    public void Dispose() { }
}
