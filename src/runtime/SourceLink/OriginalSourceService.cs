using System.Reflection;

namespace Sherlock.MCP.Runtime.SourceLink;

public static class SourceOrigins
{
    public const string SourceLink = "sourcelink";
    public const string Embedded = "embedded";
    public const string Local = "local";
    public const string Decompiled = "decompiled";
}

public sealed record OriginalSource(
    string? Text, string Origin, string? Document, string? Url, int? StartLine, int? EndLine, string? Note, bool Transient = false);

public interface IOriginalSourceService
{
    string PolicyStamp { get; }

    Task<IReadOnlyList<OriginalSource>> GetSourcesAsync(
        string assemblyPath, IReadOnlyList<MemberInfo> members, CancellationToken cancellationToken = default);
}

public sealed class OriginalSourceService : IOriginalSourceService
{
    private const string NoPdb = "No portable PDB is embedded in the assembly or sits beside it.";
    private const string NoSequencePoints = "The member has no sequence points in the PDB (a field, or an abstract or extern member).";

    private readonly ISourceFetcher _fetcher;

    public OriginalSourceService(ISourceFetcher fetcher) => _fetcher = fetcher;

    public string PolicyStamp => _fetcher.PolicyStamp;

    public async Task<IReadOnlyList<OriginalSource>> GetSourcesAsync(
        string assemblyPath, IReadOnlyList<MemberInfo> members, CancellationToken cancellationToken = default)
    {
        var lookup = PdbSourceLocator.Locate(assemblyPath, members, cancellationToken);
        var texts = new Dictionary<SourceDocument, Task<ResolvedText>>(ReferenceEqualityComparer.Instance);
        var results = new List<OriginalSource>(members.Count);
        foreach (var location in lookup.Locations)
        {
            if (location == null)
            {
                results.Add(Fallback(document: null, lookup.HasPdb ? NoSequencePoints : NoPdb));
                continue;
            }

            if (!texts.TryGetValue(location.Document, out var pending))
            {
                pending = ResolveTextAsync(location.Document, cancellationToken);
                texts[location.Document] = pending;
            }

            var (text, origin, note, transient) = await pending;
            if (text == null)
            {
                results.Add(Fallback(location.Document, note) with { Transient = transient });
                continue;
            }

            var slice = SourceSlicer.Slice(text, location);
            results.Add(new OriginalSource(
                slice.Text, origin, location.Document.Path, location.Document.Url, slice.StartLine, slice.EndLine, note));
        }
        return results;
    }

    internal async Task<ResolvedText> ResolveTextAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        string? embeddedNote = null;
        if (document.EmbeddedSource != null)
        {
            if (document.Verify(document.EmbeddedSource) is { } embedded)
                return new ResolvedText(SourceSlicer.Decode(embedded), SourceOrigins.Embedded, null, false);
            embeddedNote = "The source embedded in the PDB does not match the checksum recorded for it.";
        }

        var localNote = ReadLocal(document, out var local);
        if (local != null)
            return new ResolvedText(SourceSlicer.Decode(local), SourceOrigins.Local, embeddedNote, false);

        var fetched = await _fetcher.FetchAsync(document, cancellationToken);
        var notes = string.Join(" ", new[] { embeddedNote, localNote, fetched.Failure }.OfType<string>());
        return fetched.Content != null
            ? new ResolvedText(SourceSlicer.Decode(fetched.Content), SourceOrigins.SourceLink, NullIfEmpty(string.Join(" ", new[] { embeddedNote, localNote }.OfType<string>())), false)
            : new ResolvedText(null, SourceOrigins.Decompiled, NullIfEmpty(notes), fetched.Transient);
    }

    internal sealed record ResolvedText(string? Text, string Origin, string? Note, bool Transient);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static bool IsLocalFilePath(string path) =>
        Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal);

    internal static string? ReadLocal(SourceDocument document, out byte[]? content)
    {
        content = null;
        if (!document.HasChecksum || !IsLocalFilePath(document.Path) || !File.Exists(document.Path)) return null;
        try
        {
            var length = new FileInfo(document.Path).Length;
            if (length is 0 or > SourceDocument.MaxBytes) return null;
            using var stream = new FileStream(document.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (BoundedReader.ReadAll(stream, SourceDocument.MaxBytes) is not { } bytes) return null;
            content = document.Verify(bytes);
            return content == null ? $"The local file {document.Path} differs from the one the assembly was built from." : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static OriginalSource Fallback(SourceDocument? document, string? note) =>
        new(null, SourceOrigins.Decompiled, document?.Path, document?.Url, null, null, note);
}
