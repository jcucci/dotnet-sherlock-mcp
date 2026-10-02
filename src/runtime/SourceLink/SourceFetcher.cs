using System.Collections.Concurrent;

namespace Sherlock.MCP.Runtime.SourceLink;

public sealed record FetchResult(byte[]? Content, string? Failure, bool Transient = false)
{
    public static FetchResult Failed(string failure) => new(null, failure);

    public static FetchResult TransientFailure(string failure) => new(null, failure, Transient: true);
}

public interface ISourceFetcher
{
    string PolicyStamp { get; }

    Task<FetchResult> FetchAsync(SourceDocument document, CancellationToken cancellationToken);
}

public sealed class SourceFetcher : ISourceFetcher
{
    public const int MaxDocumentBytes = 5 * 1024 * 1024;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromSeconds(30);

    private readonly RuntimeOptions _options;
    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _downloadTimeout;
    private readonly ConcurrentDictionary<string, byte> _unreachableHosts = new(StringComparer.OrdinalIgnoreCase);

    public SourceFetcher(RuntimeOptions options, HttpClient http, TimeSpan? timeout = null, TimeSpan? downloadTimeout = null)
    {
        _options = options;
        _http = http;
        _timeout = timeout ?? DefaultTimeout;
        _downloadTimeout = downloadTimeout ?? DefaultDownloadTimeout;
    }

    public string PolicyStamp => $"{_options.SourceFetch}|{string.Join(',', _options.SourceFetchHosts)}";

    public async Task<FetchResult> FetchAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        if (document.Url is null)
            return FetchResult.Failed("The PDB has no Source Link entry for this document.");
        if (!Uri.TryCreate(document.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return FetchResult.Failed($"Source Link URL '{document.Url}' is not an https URL.");
        if (PolicyRefusal(uri) is { } refusal)
            return FetchResult.Failed(refusal);
        if (ReadCached(document) is { } cached)
            return new FetchResult(cached, null);
        if (_unreachableHosts.ContainsKey(uri.Host))
            return FetchResult.TransientFailure($"Host '{uri.Host}' was unreachable earlier this session.");

        var content = await DownloadAsync(uri, cancellationToken);
        if (content.Content is not { } bytes) return content;
        if (document.Verify(bytes) is not { } verified)
            return FetchResult.Failed($"Content at {document.Url} does not match the checksum recorded in the PDB.");

        WriteCached(document, verified);
        return new FetchResult(verified, null);
    }

    private string? PolicyRefusal(Uri uri) => _options.SourceFetch switch
    {
        SourceFetchMode.Off => "Source Link fetching is off (sourceFetch=off).",
        SourceFetchMode.KnownHosts when !IsKnownHost(uri.Host) =>
            $"Host '{uri.Host}' is not in sourceFetchHosts; add it with update_runtime_options addSourceFetchHosts, or set sourceFetch=any.",
        _ => null
    };

    private bool IsKnownHost(string host) => _options.SourceFetchHosts.Any(pattern => HostMatches(host, pattern));

    private static bool HostMatches(string host, string pattern) =>
        pattern.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)
            : string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase);

    private async Task<FetchResult> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("sherlock-mcp");
        using var response = await SendAsync(request, cancellationToken);
        if (response.Failure is { } failure) return failure;

        var message = response.Message!;
        if (!message.IsSuccessStatusCode)
        {
            var status = $"{uri} returned HTTP {(int)message.StatusCode} ({message.StatusCode}).";
            return (int)message.StatusCode >= 500 ? FetchResult.TransientFailure(status) : FetchResult.Failed(status);
        }
        if (message.Content.Headers.ContentLength > MaxDocumentBytes)
            return FetchResult.Failed($"{uri} is larger than {MaxDocumentBytes / (1024 * 1024)} MB.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_downloadTimeout);
        try
        {
            return await ReadCappedAsync(uri, message.Content, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FetchResult.TransientFailure($"Downloading {uri} timed out.");
        }
        catch (IOException ex)
        {
            return FetchResult.TransientFailure($"Downloading {uri} failed ({ex.Message}).");
        }
    }

    private async Task<SendResult> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            return new SendResult(await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SendResult(null, Unreachable(request.RequestUri!, "timed out"));
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            return new SendResult(null, Unreachable(request.RequestUri!, ex.Message));
        }
    }

    private sealed record SendResult(HttpResponseMessage? Message, FetchResult? Failure) : IDisposable
    {
        public void Dispose() => Message?.Dispose();
    }

    private static async Task<FetchResult> ReadCappedAsync(Uri uri, HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxDocumentBytes)
                return FetchResult.Failed($"{uri} is larger than {MaxDocumentBytes / (1024 * 1024)} MB.");
            buffer.Write(chunk, 0, read);
        }
        return new FetchResult(buffer.ToArray(), null);
    }

    private FetchResult Unreachable(Uri uri, string reason)
    {
        _unreachableHosts[uri.Host] = 0;
        return FetchResult.TransientFailure($"Host '{uri.Host}' is unreachable ({reason}); it won't be retried this session.");
    }

    private string? CachePath(SourceDocument document) =>
        document.HasChecksum ? Path.Combine(_options.StateDirectory, "sources", document.ChecksumHex) : null;

    private byte[]? ReadCached(SourceDocument document)
    {
        if (CachePath(document) is not { } path || !File.Exists(path)) return null;
        try
        {
            return document.Verify(File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void WriteCached(SourceDocument document, byte[] content)
    {
        if (CachePath(document) is not { } path) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
