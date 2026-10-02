using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Decompilation;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.SourceLink;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Tools;
using Sherlock.MCP.Tests.DecompilationFixtures;
using Sherlock.MCP.Tests.SourceFixtures;

namespace Sherlock.MCP.Tests;

public class SourceToolsTests
{
    private static readonly string TestAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private static readonly IInspectionContextProvider Contexts = new SharedInspectionContextProvider(new RuntimeOptions());
    private static readonly string SubjectName = typeof(SourceSubject).FullName!;
    private const string WidgetName = "Acme.Widgets.Widget";
    private static readonly Guid Sha256Algorithm = new("8829d00f-11b8-4213-878b-770e8597ac16");

    [Fact]
    public async Task GetMemberSource_ReadsTheLocalFileTheAssemblyWasBuiltFrom()
    {
        var data = Data(await Source(SubjectName, nameof(SourceSubject.Add)));

        Assert.Equal(SourceOrigins.Local, data.GetProperty("origin").GetString());
        var overload = Assert.Single(data.GetProperty("overloads").EnumerateArray());
        Assert.EndsWith("SourceFixtures.cs", overload.GetProperty("document").GetString());
        Assert.True(overload.GetProperty("lines").GetProperty("start").GetInt32() > 1);
        var source = data.GetProperty("source").GetString()!;
        Assert.StartsWith("/// <summary>Adds an entry to the log.</summary>", source);
        Assert.Contains("[Description(\"source-fixture\")]", source);
        Assert.Contains("// original comment survives", source);
        Assert.EndsWith("}", source);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("note").ValueKind);
    }

    [Fact]
    public async Task GetMemberSource_ConstructorExcludesFieldInitializers()
    {
        var source = Data(await Source(SubjectName, ".ctor")).GetProperty("source").GetString()!;

        Assert.StartsWith("public SourceSubject()", source);
        Assert.Contains("constructed", source);
        Assert.DoesNotContain("seeded", source);
    }

    [Fact]
    public async Task GetMemberSource_AsyncMethodUsesTheStateMachineBody()
    {
        var data = Data(await Source(SubjectName, nameof(SourceSubject.CountAsync)));

        Assert.Equal(SourceOrigins.Local, data.GetProperty("origin").GetString());
        Assert.Contains("await Task.Yield();", data.GetProperty("source").GetString());
    }

    [Fact]
    public async Task GetMemberSource_PropertyUsesItsAccessors()
    {
        var data = Data(await Source(SubjectName, nameof(SourceSubject.Count)));

        Assert.Equal("public int Count => _log.Count;", data.GetProperty("source").GetString());
    }

    [Fact]
    public async Task GetMemberSource_FieldFallsBackToDecompiledWithNote()
    {
        var data = Data(await Source(SubjectName, "_log"));

        Assert.Equal(SourceOrigins.Decompiled, data.GetProperty("origin").GetString());
        Assert.Contains("sequence points", data.GetProperty("note").GetString());
    }

    [Fact]
    public async Task GetMemberSource_CombinesOverloadsWithSignatureHeaders()
    {
        var data = Data(await Source(typeof(DecompileSubject).FullName!, "Format"));

        Assert.Equal(3, data.GetProperty("overloads").GetArrayLength());
        Assert.All(data.GetProperty("overloads").EnumerateArray(), o => Assert.Equal(SourceOrigins.Local, o.GetProperty("origin").GetString()));
        Assert.Contains("// string Format(string value, int count)", data.GetProperty("source").GetString());
    }

    [Fact]
    public async Task GetMemberSource_ReadsSourceEmbeddedInAnEmbeddedPdb()
    {
        var assembly = PdbFixtures.Emit(PdbKind.Embedded, embedSource: true);

        var data = Data(await Source(WidgetName, "Spin", assemblyPath: assembly.AssemblyPath));

        Assert.Equal(SourceOrigins.Embedded, data.GetProperty("origin").GetString());
        Assert.Contains("// the original comment", data.GetProperty("source").GetString());
    }

    [Fact]
    public async Task GetMemberSource_FetchesFromSourceLinkOnAKnownHost()
    {
        var assembly = PdbFixtures.Emit(PdbKind.SideBySide);
        var handler = new FakeHandler(_ => Ok(assembly.DocumentBytes));

        var data = Data(await Source(WidgetName, "Spin", assemblyPath: assembly.AssemblyPath, handler: handler));

        Assert.Equal(SourceOrigins.SourceLink, data.GetProperty("origin").GetString());
        var overload = Assert.Single(data.GetProperty("overloads").EnumerateArray());
        Assert.Equal(PdbFixtures.SourceLinkUrlPrefix + "Widget.cs", overload.GetProperty("url").GetString());
        Assert.Contains("// the original comment", data.GetProperty("source").GetString());
        Assert.Equal(PdbFixtures.SourceLinkUrlPrefix + "Widget.cs", Assert.Single(handler.Requests).ToString());
    }

    [Fact]
    public async Task GetMemberSource_DecompilesWhenTheHostIsNotKnown()
    {
        var assembly = PdbFixtures.Emit(PdbKind.SideBySide, sourceLinkUrlPrefix: "https://git.example.com/acme/");
        var handler = new FakeHandler(_ => Ok(assembly.DocumentBytes));

        var data = Data(await Source(WidgetName, "Spin", assemblyPath: assembly.AssemblyPath, handler: handler));

        Assert.Equal(SourceOrigins.Decompiled, data.GetProperty("origin").GetString());
        Assert.Contains("git.example.com", data.GetProperty("note").GetString());
        Assert.Equal("https://git.example.com/acme/Widget.cs", data.GetProperty("overloads")[0].GetProperty("url").GetString());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetMemberSource_ConstructorUsesItsOwnPartialFile()
    {
        var assembly = PdbFixtures.Emit(PdbKind.Embedded, embedSource: true, documents: new Dictionary<string, string>
        {
            ["Fields.cs"] = "namespace Acme.Widgets;\n\npublic partial class Gadget\n{\n    private int _size = 42;\n}\n",
            ["Constructors.cs"] = "namespace Acme.Widgets;\n\npublic partial class Gadget\n{\n    public Gadget()\n    {\n        _size++;\n    }\n}\n"
        });

        var data = Data(await Source("Acme.Widgets.Gadget", ".ctor", assemblyPath: assembly.AssemblyPath));

        Assert.EndsWith("Constructors.cs", data.GetProperty("overloads")[0].GetProperty("document").GetString());
        Assert.StartsWith("public Gadget()", data.GetProperty("source").GetString());
    }

    [Fact]
    public async Task GetMemberSource_DoesNotCacheAFallbackCausedByANetworkFailure()
    {
        var assembly = PdbFixtures.Emit(PdbKind.SideBySide);
        var middleware = TestMiddleware.Fresh;
        var options = new RuntimeOptions { StateDirectory = TestHandles.NewStateDirectory() };
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Ok(assembly.DocumentBytes));

        var failed = Data(await Source(WidgetName, "Spin", assemblyPath: assembly.AssemblyPath, handler: handler, options: options, middleware: middleware));
        var recovered = Data(await Source(WidgetName, "Spin", assemblyPath: assembly.AssemblyPath, handler: handler, options: options, middleware: middleware));

        Assert.Equal(SourceOrigins.Decompiled, failed.GetProperty("origin").GetString());
        Assert.Equal(SourceOrigins.SourceLink, recovered.GetProperty("origin").GetString());
    }

    [Fact]
    public void ReadLocal_IgnoresDocumentsWithoutAChecksumOrAFullPath()
    {
        var file = Path.Combine(TestHandles.NewStateDirectory(), "secret.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "secret");

        OriginalSourceService.ReadLocal(new SourceDocument(file, Guid.Empty, [], null, null), out var unhashed);
        OriginalSourceService.ReadLocal(new SourceDocument("secret.txt", Guid.Empty, [], null, null), out var relative);

        Assert.Null(unhashed);
        Assert.Null(relative);
    }

    [Theory]
    [InlineData(@"\\attacker\share\a.cs")]
    [InlineData("//attacker/share/a.cs")]
    [InlineData(@"\\?\C:\a.cs")]
    public void ReadLocal_IgnoresUncAndDevicePaths(string path)
    {
        var note = OriginalSourceService.ReadLocal(new SourceDocument(path, Sha256Algorithm, new byte[32], null, null), out var content);

        Assert.Null(content);
        Assert.Null(note);
    }

    [Fact]
    public void ReadLocal_IgnoresSpecialAndOversizedFiles()
    {
        var oversized = Path.Combine(TestHandles.NewStateDirectory(), "big.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(oversized)!);
        var bytes = new byte[SourceDocument.MaxBytes + 1];
        File.WriteAllBytes(oversized, bytes);

        OriginalSourceService.ReadLocal(new SourceDocument(oversized, Sha256Algorithm, SHA256.HashData(bytes), null, null), out var big);
        Assert.Null(big);

        if (OperatingSystem.IsWindows()) return;
        OriginalSourceService.ReadLocal(new SourceDocument("/dev/zero", Sha256Algorithm, new byte[32], null, null), out var device);
        Assert.Null(device);
    }

    [Fact]
    public async Task ResolveText_SkipsEmbeddedSourceThatFailsItsChecksum()
    {
        var embedded = Encoding.UTF8.GetBytes("class Tampered { }");
        var document = new SourceDocument("/nonexistent/a.cs", Sha256Algorithm, SHA256.HashData(Encoding.UTF8.GetBytes("class Original { }")), embedded, null);
        var service = new OriginalSourceService(new SourceFetcher(new RuntimeOptions { StateDirectory = TestHandles.NewStateDirectory() }, new HttpClient()));

        var resolved = await service.ResolveTextAsync(document, default);

        Assert.Null(resolved.Text);
        Assert.Equal(SourceOrigins.Decompiled, resolved.Origin);
        Assert.Contains("embedded in the PDB does not match", resolved.Note);
    }

    [Fact]
    public void Inflate_RejectsOversizedOrMisdeclaredEmbeddedSource()
    {
        var source = Encoding.UTF8.GetBytes(new string('x', 4096));
        using var compressed = new MemoryStream();
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(source);
        var blob = compressed.ToArray();

        Assert.Equal(source, PdbSourceLocator.Inflate(blob, source.Length));
        Assert.Null(PdbSourceLocator.Inflate(blob, SourceDocument.MaxBytes + 1));
        Assert.Null(PdbSourceLocator.Inflate(blob, source.Length - 1));
        Assert.Null(PdbSourceLocator.Inflate(blob, source.Length + 1));
    }

    [Fact]
    public async Task GetMemberSource_DecompilesWithoutAPdb()
    {
        var assembly = PdbFixtures.Emit(PdbKind.None);

        var data = Data(await Source(WidgetName, "Spin", assemblyPath: assembly.AssemblyPath));

        Assert.Equal(SourceOrigins.Decompiled, data.GetProperty("origin").GetString());
        Assert.Contains("No portable PDB", data.GetProperty("note").GetString());
        Assert.Contains("return turns * 2;", data.GetProperty("source").GetString());
    }

    [Fact]
    public async Task GetMemberSource_ChangingTheFetchPolicyBypassesTheCachedFallback()
    {
        var assembly = PdbFixtures.Emit(PdbKind.SideBySide);
        var options = new RuntimeOptions { StateDirectory = TestHandles.NewStateDirectory(), SourceFetch = SourceFetchMode.Off };
        var middleware = TestMiddleware.Fresh;
        var handler = new FakeHandler(_ => Ok(assembly.DocumentBytes));

        var off = Data(await Source(WidgetName, "Spin", assemblyPath: assembly.AssemblyPath, handler: handler, options: options, middleware: middleware));
        options.SourceFetch = SourceFetchMode.KnownHosts;
        var on = Data(await Source(WidgetName, "Spin", assemblyPath: assembly.AssemblyPath, handler: handler, options: options, middleware: middleware));

        Assert.Equal(SourceOrigins.Decompiled, off.GetProperty("origin").GetString());
        Assert.Contains("sourceFetch=off", off.GetProperty("note").GetString());
        Assert.Equal(SourceOrigins.SourceLink, on.GetProperty("origin").GetString());
    }

    [Fact]
    public async Task GetMemberSource_PagesByLine()
    {
        var first = Data(await Source(SubjectName, nameof(SourceSubject.Add), maxLines: 2));

        Assert.True(first.GetProperty("truncated").GetBoolean());
        Assert.Equal(2, first.GetProperty("lineCount").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, first.GetProperty("continuationToken").ValueKind);
    }

    [Fact]
    public async Task GetMemberSource_CancellationThrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source(SubjectName, nameof(SourceSubject.Add), cancellationToken: cts.Token));
    }

    private static async Task<string> Source(
        string typeName, string memberName, string? assemblyPath = null, FakeHandler? handler = null, RuntimeOptions? options = null,
        ToolMiddleware? middleware = null, int maxLines = 400, CancellationToken cancellationToken = default)
    {
        options ??= new RuntimeOptions { StateDirectory = TestHandles.NewStateDirectory() };
        var fetcher = new SourceFetcher(options, new HttpClient(handler ?? new FakeHandler(_ => throw new HttpRequestException("offline"))));
        var result = await SourceTools.GetMemberSource(
            new OriginalSourceService(fetcher), new DecompilerService(), Contexts, middleware ?? TestMiddleware.Fresh, TestHandles.Registry,
            typeName: typeName, memberName: memberName, assemblyPath: assemblyPath ?? TestAssemblyPath, maxLines: maxLines,
            cancellationToken: cancellationToken);
        return result.Text();
    }

    private static HttpResponseMessage Ok(byte[] content) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    private static JsonElement Data(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.NotEqual("error", doc.RootElement.GetProperty("kind").GetString());
        return doc.RootElement.GetProperty("data").Clone();
    }
}

internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request.RequestUri!);
        return Task.FromResult(respond(request));
    }
}
