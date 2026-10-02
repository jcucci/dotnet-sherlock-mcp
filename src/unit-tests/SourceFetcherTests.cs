using System.Net;
using System.Security.Cryptography;
using System.Text;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.SourceLink;

namespace Sherlock.MCP.Tests;

public class SourceFetcherTests
{
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("class C { }");
    private static readonly Guid Sha256Algorithm = new("8829d00f-11b8-4213-878b-770e8597ac16");

    [Fact]
    public async Task Fetch_ReturnsVerifiedContentAndCachesItOnDisk()
    {
        var options = Options();
        var handler = new FakeHandler(_ => Ok(Content));

        var first = await new SourceFetcher(options, new HttpClient(handler)).FetchAsync(Document(), default);
        var second = await new SourceFetcher(options, new HttpClient(handler)).FetchAsync(Document(), default);

        Assert.Equal(Content, first.Content);
        Assert.Equal(Content, second.Content);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Fetch_RejectsContentThatDoesNotMatchTheChecksum()
    {
        var fetcher = new SourceFetcher(Options(), new HttpClient(new FakeHandler(_ => Ok(Encoding.UTF8.GetBytes("tampered")))));

        var result = await fetcher.FetchAsync(Document(), default);

        Assert.Null(result.Content);
        Assert.Contains("checksum", result.Failure);
    }

    [Theory]
    [InlineData("line one\r\nline two\r\n", "line one\nline two\n")]
    [InlineData("line one\nline two\n", "line one\r\nline two\r\n")]
    public async Task Fetch_AcceptsContentThatDiffersOnlyInLineEndings(string built, string served)
    {
        var builtBytes = Encoding.UTF8.GetBytes(built);
        var document = new SourceDocument("/nonexistent/a.cs", Sha256Algorithm, SHA256.HashData(builtBytes), null, "https://raw.githubusercontent.com/a.cs");
        var fetcher = new SourceFetcher(Options(), new HttpClient(new FakeHandler(_ => Ok(Encoding.UTF8.GetBytes(served)))));

        var result = await fetcher.FetchAsync(document, default);

        Assert.Equal(builtBytes, result.Content);
    }

    [Fact]
    public async Task Fetch_MarksAnUnreachableHostForTheSession()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("connection refused"));
        var fetcher = new SourceFetcher(Options(), new HttpClient(handler));

        var first = await fetcher.FetchAsync(Document(), default);
        var second = await fetcher.FetchAsync(Document("https://raw.githubusercontent.com/acme/other.cs"), default);

        Assert.Contains("unreachable", first.Failure);
        Assert.Contains("earlier this session", second.Failure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Fetch_TimeoutMarksTheHostUnreachable()
    {
        var handler = new DelayHandler();
        var fetcher = new SourceFetcher(Options(), new HttpClient(handler), timeout: TimeSpan.FromMilliseconds(50));

        var result = await fetcher.FetchAsync(Document(), default);

        Assert.Contains("timed out", result.Failure);
    }

    [Fact]
    public async Task Fetch_BodyReadFailureFallsBackWithoutMarkingTheHost()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream()) });
        var fetcher = new SourceFetcher(Options(), new HttpClient(handler));

        var first = await fetcher.FetchAsync(Document(), default);
        await fetcher.FetchAsync(Document(), default);

        Assert.True(first.Transient);
        Assert.Contains("connection reset", first.Failure);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Fetch_SlowBodyTimesOutWithoutMarkingTheHost()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) });
        var fetcher = new SourceFetcher(Options(), new HttpClient(handler), downloadTimeout: TimeSpan.FromMilliseconds(50));

        var first = await fetcher.FetchAsync(Document(), default);
        await fetcher.FetchAsync(Document(), default);

        Assert.Contains("Downloading", first.Failure);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Fetch_BodyReadHttpRequestExceptionFallsBackAsTransient()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HttpFailingStream()) });

        var result = await new SourceFetcher(Options(), new HttpClient(handler)).FetchAsync(Document(), default);

        Assert.True(result.Transient);
        Assert.Contains("response ended early", result.Failure);
    }

    [Fact]
    public async Task Fetch_FollowsARedirectToAnAllowedHost()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath.StartsWith("/moved", StringComparison.Ordinal)
            ? Ok(Content)
            : Redirect("https://raw.githubusercontent.com/moved/a.cs"));

        var result = await new SourceFetcher(Options(), new HttpClient(handler)).FetchAsync(Document(), default);

        Assert.Equal(Content, result.Content);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("https://evil.example.com/a.cs", "not in sourceFetchHosts")]
    [InlineData("http://raw.githubusercontent.com/a.cs", "not an https URL")]
    public async Task Fetch_RefusesARedirectThatLeavesThePolicy(string location, string reason)
    {
        var handler = new FakeHandler(_ => Redirect(location));

        var result = await new SourceFetcher(Options(), new HttpClient(handler)).FetchAsync(Document(), default);

        Assert.Contains(reason, result.Failure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Fetch_StopsFollowingRedirectLoops()
    {
        var handler = new FakeHandler(_ => Redirect("https://raw.githubusercontent.com/acme/widgets/sha/a.cs"));

        var result = await new SourceFetcher(Options(), new HttpClient(handler)).FetchAsync(Document(), default);

        Assert.Contains("redirected more than", result.Failure);
        Assert.Equal(SourceFetcher.MaxRedirects + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task Fetch_RejectsADocumentWithoutAChecksum()
    {
        var document = new SourceDocument("/src/a.cs", Guid.Empty, [], null, "https://raw.githubusercontent.com/a.cs");

        var result = await new SourceFetcher(Options(), new HttpClient(new FakeHandler(_ => Ok(Content)))).FetchAsync(document, default);

        Assert.Null(result.Content);
        Assert.Contains("checksum", result.Failure);
    }

    [Fact]
    public async Task Fetch_HttpErrorDoesNotMarkTheHostUnreachable()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var fetcher = new SourceFetcher(Options(), new HttpClient(handler));

        var first = await fetcher.FetchAsync(Document(), default);
        await fetcher.FetchAsync(Document(), default);

        Assert.Contains("404", first.Failure);
        Assert.False(first.Transient);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(SourceFetchMode.Off, "https://raw.githubusercontent.com/a.cs", "sourceFetch=off")]
    [InlineData(SourceFetchMode.KnownHosts, "https://git.example.com/a.cs", "not in sourceFetchHosts")]
    [InlineData(SourceFetchMode.AnyHost, "http://raw.githubusercontent.com/a.cs", "not an https URL")]
    public async Task Fetch_RefusesWithoutSendingARequest(SourceFetchMode mode, string url, string reason)
    {
        var options = Options();
        options.SourceFetch = mode;
        var handler = new FakeHandler(_ => Ok(Content));

        var result = await new SourceFetcher(options, new HttpClient(handler)).FetchAsync(Document(url), default);

        Assert.Contains(reason, result.Failure);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("https://myorg.visualstudio.com/_apis/a.cs", SourceFetchMode.KnownHosts)]
    [InlineData("https://git.example.com/a.cs", SourceFetchMode.AnyHost)]
    public async Task Fetch_AllowsWildcardKnownHostsAndAnyHost(string url, SourceFetchMode mode)
    {
        var options = Options();
        options.SourceFetch = mode;

        var result = await new SourceFetcher(options, new HttpClient(new FakeHandler(_ => Ok(Content)))).FetchAsync(Document(url), default);

        Assert.Equal(Content, result.Content);
    }

    [Fact]
    public async Task Fetch_RejectsOversizedContent()
    {
        var big = new byte[SourceFetcher.MaxDocumentBytes + 1];
        var document = new SourceDocument("/src/a.cs", Guid.Empty, [], null, "https://raw.githubusercontent.com/a.cs");

        var result = await new SourceFetcher(Options(), new HttpClient(new FakeHandler(_ => Ok(big)))).FetchAsync(document, default);

        Assert.Contains("larger than", result.Failure);
    }

    [Theory]
    [InlineData("/build/src/Foo/Bar.cs", "https://raw.githubusercontent.com/acme/repo/sha/Foo/Bar.cs")]
    [InlineData("/build/src/gen/X.cs", "https://example.com/generated/X.cs")]
    [InlineData("C:\\build\\src\\Win\\Y.cs", "https://raw.githubusercontent.com/acme/repo/sha/Win/Y.cs")]
    [InlineData("/build/exact.cs", "https://example.com/exact.cs")]
    [InlineData("/elsewhere/Z.cs", null)]
    public void SourceLinkMap_MapsLongestPrefixAndExactEntries(string path, string? expected)
    {
        var map = SourceLinkMap.Parse("""
            {"documents":{
              "/build/src/*":"https://raw.githubusercontent.com/acme/repo/sha/*",
              "/build/src/gen/*":"https://example.com/generated/*",
              "C:\\build\\src\\*":"https://raw.githubusercontent.com/acme/repo/sha/*",
              "/build/exact.cs":"https://example.com/exact.cs"
            }}
            """)!;

        Assert.Equal(expected, map.GetUrl(path));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"other\":1}")]
    public void SourceLinkMap_RejectsMalformedJson(string json) => Assert.Null(SourceLinkMap.Parse(json));

    private static RuntimeOptions Options() => new() { StateDirectory = TestHandles.NewStateDirectory(), SourceFetch = SourceFetchMode.KnownHosts };

    private static SourceDocument Document(string url = "https://raw.githubusercontent.com/acme/widgets/sha/a.cs") =>
        new("/nonexistent/a.cs", Sha256Algorithm, SHA256.HashData(Content), null, url);

    private static HttpResponseMessage Ok(byte[] content) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } };

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("connection reset");
    }

    private sealed class HttpFailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("response ended early");
    }

    private sealed class StallingStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    private sealed class DelayHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}
