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

    private async Task<ResolvedText> ResolveTextAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        if (document.EmbeddedText != null)
            return new ResolvedText(document.EmbeddedText, SourceOrigins.Embedded, null, false);

        var localNote = ReadLocal(document, out var local);
        if (local != null)
            return new ResolvedText(SourceSlicer.Decode(local), SourceOrigins.Local, null, false);

        var fetched = await _fetcher.FetchAsync(document, cancellationToken);
        return fetched.Content != null
            ? new ResolvedText(SourceSlicer.Decode(fetched.Content), SourceOrigins.SourceLink, localNote, false)
            : new ResolvedText(null, SourceOrigins.Decompiled, string.Join(" ", new[] { localNote, fetched.Failure }.OfType<string>()), fetched.Transient);
    }

    private sealed record ResolvedText(string? Text, string Origin, string? Note, bool Transient);

    internal static string? ReadLocal(SourceDocument document, out byte[]? content)
    {
        content = null;
        if (!document.HasChecksum || !Path.IsPathFullyQualified(document.Path) || !File.Exists(document.Path)) return null;
        try
        {
            content = document.Verify(File.ReadAllBytes(document.Path));
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
