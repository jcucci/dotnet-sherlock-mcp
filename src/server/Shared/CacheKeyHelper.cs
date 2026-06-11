using System.Security.Cryptography;
using System.Text;

namespace Sherlock.MCP.Server.Shared;

public static class CacheKeyHelper
{
    public static string Build(string kind, params object?[] parts)
    {
        var normalized = string.Join('|', parts.Select(NormalizePart));
        var baseKey = $"{kind}|{JsonHelpers.SchemaVersion}|{normalized}";
        var hash = Sha256(baseKey);
        return $"{baseKey}:{hash}";
    }

    public static string FileStamp(string path)
    {
        try
        {
            var info = new FileInfo(Path.GetFullPath(path));
            return info.Exists ? $"{info.FullName}|{info.LastWriteTimeUtc.Ticks}|{info.Length}" : info.FullName;
        }
        catch
        {
            return path;
        }
    }

    public static string Sha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static string NormalizePart(object? part)
    {
        return part switch
        {
            null => "",
            bool b => b ? "true" : "false",
            string s => s,
            _ => Convert.ToString(part, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
        };
    }
}

