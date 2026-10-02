using System.Security.Cryptography;

namespace Sherlock.MCP.Runtime.SourceLink;

public sealed record SourceDocument(string Path, Guid HashAlgorithm, byte[] Hash, byte[]? EmbeddedSource, string? Url)
{
    public const int MaxBytes = 5 * 1024 * 1024;

    private static readonly Guid Sha1 = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid Sha256 = new("8829d00f-11b8-4213-878b-770e8597ac16");

    public bool HasChecksum => Hash.Length > 0 && (HashAlgorithm == Sha1 || HashAlgorithm == Sha256);

    public string ChecksumHex => Convert.ToHexString(Hash).ToLowerInvariant();

    public byte[]? Verify(byte[] content)
    {
        if (!HasChecksum) return null;
        return new[] { content, WithLineEndings(content, crlf: true), WithLineEndings(content, crlf: false) }
            .FirstOrDefault(HashMatches);
    }

    private bool HashMatches(byte[] content)
    {
#pragma warning disable CA5350 // SHA-1 is what older PDBs record; it only checks content identity
        var actual = HashAlgorithm == Sha256 ? SHA256.HashData(content) : SHA1.HashData(content);
#pragma warning restore CA5350
        return actual.AsSpan().SequenceEqual(Hash);
    }

    private static byte[] WithLineEndings(byte[] content, bool crlf)
    {
        var output = new List<byte>(content.Length + (crlf ? content.Length / 16 : 0));
        for (var i = 0; i < content.Length; i++)
        {
            var isCrBeforeLf = content[i] == '\r' && i + 1 < content.Length && content[i + 1] == '\n';
            if (isCrBeforeLf) continue;
            if (content[i] == '\n' && crlf) output.Add((byte)'\r');
            output.Add(content[i]);
        }
        return output.ToArray();
    }
}

public static class BoundedReader
{
    public static byte[]? ReadAll(Stream stream, int maxBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}

public sealed record MemberSourceLocation(SourceDocument Document, int StartLine, int EndLine, int AnchorLine);
