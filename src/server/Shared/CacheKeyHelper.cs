using System.Security.Cryptography;
using System.Text;
using Sherlock.MCP.Runtime.ProjectAssets;

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

    public static string ScopeStamp(IEnumerable<string> paths) => string.Join(";", paths.Select(FileStamp));

    public static string AssemblyStamp(string assemblyPath) =>
        $"{FileStamp(assemblyPath)};{ProjectAssetsLocator.AssetsStamp(assemblyPath)}";

    public static string AssemblyScopeStamp(IEnumerable<string> assemblyPaths) => string.Join(";", assemblyPaths.Select(AssemblyStamp));

    public static string XmlDocStamp(string assemblyPath) =>
        $"{AssemblyStamp(assemblyPath)};{FileStamp(Path.ChangeExtension(assemblyPath, ".xml"))}";

    public static string PdbStamp(string assemblyStamp, string assemblyPath) =>
        $"{assemblyStamp};{FileStamp(Path.ChangeExtension(assemblyPath, ".pdb"))}";

    public static string ProjectStamp(string projectFilePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectFilePath)) ?? string.Empty;
        return $"{FileStamp(projectFilePath)};{FileStamp(Path.Combine(directory, "obj", "project.assets.json"))}";
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

