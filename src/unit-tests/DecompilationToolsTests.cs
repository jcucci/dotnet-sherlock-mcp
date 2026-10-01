using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Decompilation;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Shared;
using Sherlock.MCP.Server.Tools;
using Sherlock.MCP.Tests.DecompilationFixtures;

namespace Sherlock.MCP.Tests;

public class DecompilationToolsTests
{
    private static readonly string TestAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private static readonly IInspectionContextProvider Contexts = new SharedInspectionContextProvider(new RuntimeOptions());
    private static readonly string SubjectName = typeof(DecompileSubject).FullName!;

    [Fact]
    public void DecompileMember_ReturnsEveryOverloadWithSignatureHeaders()
    {
        var data = Data(Member(memberName: "Format"));

        Assert.Equal(SubjectName, data.GetProperty("typeName").GetString());
        Assert.Equal(3, data.GetProperty("overloads").GetArrayLength());
        var source = data.GetProperty("source").GetString()!;
        Assert.Contains("// string Format(string value, int count)", source);
        Assert.False(data.GetProperty("truncated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("continuationToken").ValueKind);
    }

    [Fact]
    public void DecompileMember_ParameterTypesSelectsOneOverload()
    {
        var data = Data(Member(memberName: "Format", parameterTypes: "string,int"));

        var overload = Assert.Single(data.GetProperty("overloads").EnumerateArray());
        Assert.Equal("string Format(string value, int count)", overload.GetProperty("signature").GetString());
        Assert.DoesNotContain("// ", data.GetProperty("source").GetString());
    }

    [Fact]
    public void DecompileMember_UnmatchedParameterTypesListsOverloads()
    {
        using var doc = JsonDocument.Parse(Member(memberName: "Format", parameterTypes: "bool"));

        Assert.Equal("OverloadNotFound", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("recommendedParams").GetProperty("overloads").GetArrayLength());
    }

    [Fact]
    public void DecompileMember_UnknownMemberReturnsCandidates()
    {
        using var doc = JsonDocument.Parse(Member(memberName: "Formatt"));

        Assert.Equal("MemberNotFound", doc.RootElement.GetProperty("code").GetString());
        Assert.Contains("Format", doc.RootElement.GetProperty("recommendedParams").GetProperty("candidates").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public void DecompileMember_UnknownTypeReturnsTypeNotFound()
    {
        using var doc = JsonDocument.Parse(Member(typeName: "NoSuchDecompileSubject", memberName: "Format"));

        Assert.Equal("TypeNotFound", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void DecompileMember_AmbiguousTypeReturnsCandidates()
    {
        using var doc = JsonDocument.Parse(Member(typeName: "DuplicateWidget", memberName: ".ctor"));

        Assert.Equal("AmbiguousTypeName", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void DecompileMember_AcceptsAssemblyHandle()
    {
        var handle = TestHandles.Registry.Open(TestAssemblyPath).Id;

        var json = DecompilationTools.DecompileMember(
            new DecompilerService(), Contexts, TestMiddleware.Fresh, TestHandles.Registry,
            typeName: SubjectName, memberName: nameof(DecompileSubject.Total), assemblyHandle: handle).Text();

        Assert.Contains("foreach", Data(json).GetProperty("source").GetString());
    }

    [Fact]
    public void DecompileMember_OmittedAndEmptyParameterTypesAreCachedSeparately()
    {
        var middleware = TestMiddleware.Fresh;

        var all = Data(Member(".ctor", middleware: middleware));
        var parameterless = Data(Member(".ctor", parameterTypes: "", middleware: middleware));

        Assert.Equal(2, all.GetProperty("overloads").GetArrayLength());
        var overload = Assert.Single(parameterless.GetProperty("overloads").EnumerateArray());
        Assert.Equal("DecompileSubject()", overload.GetProperty("signature").GetString());
    }

    [Fact]
    public void DecompileType_ForwardedTypeReturnsTypeForwarded()
    {
        var facade = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");

        using var doc = JsonDocument.Parse(DecompilationTools.DecompileType(
            new DecompilerService(), Contexts, TestMiddleware.Fresh, TestHandles.Registry,
            typeName: "System.String", assemblyPath: facade));

        Assert.Equal("TypeForwarded", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("System.Private.CoreLib", doc.RootElement.GetProperty("recommendedParams").GetProperty("assemblyName").GetString());
    }

    [Fact]
    public void SourcePager_KeepsPagesWithinTheResponseBudget()
    {
        var lines = Enumerable.Range(0, 200).Select(i => $"{i:D3}:".PadRight(SourcePager.MaxLineLength, 'x')).ToArray();
        var envelope = JsonHelpers.Envelope("decompile.type", new { typeName = "T", source = string.Join('\n', lines) });

        var pages = new List<string>();
        var offset = 0;
        while (true)
        {
            var json = SourcePager.Page(envelope, offset, maxLines: SourcePager.MaxLinesLimit, salt: "s", toolName: "decompile_type");
            Assert.True(json.Length <= ResponseSizeHelper.MaxResponseSize, $"page of {json.Length} characters");
            var data = Data(json);
            Assert.True(data.GetProperty("lineCount").GetInt32() > 0);
            pages.Add(data.GetProperty("source").GetString()!);
            if (!data.GetProperty("truncated").GetBoolean()) break;
            offset += data.GetProperty("lineCount").GetInt32();
        }

        Assert.True(pages.Count > 1);
        Assert.Equal(string.Join('\n', lines), string.Join('\n', pages));
    }

    [Fact]
    public void DecompileMember_UsesAdditionalAssembliesAsDependencyScope()
    {
        var dependency = typeof(JsonDocument).Assembly.Location;

        var json = DecompilationTools.DecompileMember(
            new DecompilerService(), Contexts, TestMiddleware.Fresh, TestHandles.Registry,
            typeName: SubjectName, memberName: nameof(DecompileSubject.Total), assemblyPath: TestAssemblyPath,
            additionalAssemblies: [dependency]).Text();

        Assert.Contains("foreach", Data(json).GetProperty("source").GetString());
    }

    [Fact]
    public void DecompileType_MissingAdditionalAssemblyReturnsAssemblyNotFound()
    {
        using var doc = JsonDocument.Parse(DecompilationTools.DecompileType(
            new DecompilerService(), Contexts, TestMiddleware.Fresh, TestHandles.Registry,
            typeName: SubjectName, assemblyPath: TestAssemblyPath, additionalAssemblies: ["/no/such/dependency.dll"]));

        Assert.Equal("AssemblyNotFound", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void SourcePager_ClipsLinesLongerThanTheLimit()
    {
        var longLine = new string('x', SourcePager.MaxLineLength * 100);
        var envelope = JsonHelpers.Envelope("decompile.type", new { typeName = "T", source = $"{longLine}\nshort" });

        using var doc = JsonDocument.Parse(SourcePager.Page(envelope, offset: 0, maxLines: 1, salt: "s", toolName: "decompile_type"));

        var data = doc.RootElement.GetProperty("data");
        var source = data.GetProperty("source").GetString()!;
        Assert.StartsWith(new string('x', SourcePager.MaxLineLength), source);
        Assert.EndsWith($"/* {SourcePager.MaxLineLength * 99} more characters clipped */", source);
        Assert.Equal(1, data.GetProperty("clippedLines").GetInt32());
        Assert.True(data.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void DecompileType_PagesConcatenateToTheFullSource()
    {
        var middleware = TestMiddleware.Fresh;
        var full = Data(Type(middleware, maxLines: 5000));
        var totalLines = full.GetProperty("totalLines").GetInt32();

        var pages = new List<string>();
        string? token = null;
        do
        {
            var page = Data(Type(middleware, maxLines: 7, continuationToken: token));
            pages.Add(page.GetProperty("source").GetString()!);
            Assert.Equal(page.GetProperty("truncated").GetBoolean(), page.GetProperty("continuationToken").ValueKind == JsonValueKind.String);
            token = page.GetProperty("continuationToken").GetString();
        }
        while (token != null);

        Assert.True(pages.Count > 1);
        Assert.Equal((totalLines + 6) / 7, pages.Count);
        Assert.Equal(full.GetProperty("source").GetString(), string.Join('\n', pages));
    }

    [Fact]
    public void DecompileType_RejectsTokenFromAnotherQuery()
    {
        var page = Data(DecompilationTools.DecompileType(
            new DecompilerService(), Contexts, TestMiddleware.Fresh, TestHandles.Registry,
            typeName: typeof(DecompileSubject.Inner).FullName!, assemblyPath: TestAssemblyPath, maxLines: 1));
        var token = page.GetProperty("continuationToken").GetString();

        using var doc = JsonDocument.Parse(Type(TestMiddleware.Fresh, maxLines: 1, continuationToken: token));

        Assert.Equal("InvalidContinuationToken", doc.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public void DecompileType_RejectsOutOfRangeMaxLines(int maxLines)
    {
        using var doc = JsonDocument.Parse(Type(TestMiddleware.Fresh, maxLines: maxLines));

        Assert.Equal("InvalidArgument", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void DecompileMember_ThrowsWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => DecompilationTools.DecompileMember(
            new DecompilerService(), Contexts, TestMiddleware.Fresh, TestHandles.Registry,
            typeName: SubjectName, memberName: nameof(DecompileSubject.Total), assemblyPath: TestAssemblyPath,
            cancellationToken: cts.Token));
    }

    private static string Member(
        string memberName, string? typeName = null, string? parameterTypes = null, Server.Middleware.ToolMiddleware? middleware = null) =>
        DecompilationTools.DecompileMember(
            new DecompilerService(), Contexts, middleware ?? TestMiddleware.Fresh, TestHandles.Registry,
            typeName: typeName ?? SubjectName, memberName: memberName, parameterTypes: parameterTypes,
            assemblyPath: TestAssemblyPath).Text();

    private static string Type(Server.Middleware.ToolMiddleware middleware, int maxLines, string? continuationToken = null) =>
        DecompilationTools.DecompileType(
            new DecompilerService(), Contexts, middleware, TestHandles.Registry,
            typeName: SubjectName, assemblyPath: TestAssemblyPath, maxLines: maxLines, continuationToken: continuationToken);

    private static JsonElement Data(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.NotEqual("error", doc.RootElement.GetProperty("kind").GetString());
        return doc.RootElement.GetProperty("data").Clone();
    }
}
