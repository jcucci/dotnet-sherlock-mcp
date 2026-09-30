using ModelContextProtocol.Protocol;

namespace Sherlock.MCP.Server.Shared;

public static class ResourceUris
{
    public const string TypeTemplate = "sherlock://assembly/{path}/type/{fullName}";
    public const string DocsTemplate = "sherlock://assembly/{path}/docs/{memberId}";
    public const string JsonMimeType = "application/json";

    public static string Type(string assemblyPath, string fullName) =>
        $"sherlock://assembly/{Encode(Path.GetFullPath(assemblyPath))}/type/{Encode(fullName)}";

    public static string Docs(string assemblyPath, string memberId) =>
        $"sherlock://assembly/{Encode(Path.GetFullPath(assemblyPath))}/docs/{Encode(memberId)}";

    public static ResourceLinkBlock TypeLink(string assemblyPath, string fullName) => new()
    {
        Uri = Type(assemblyPath, fullName),
        Name = fullName,
        MimeType = JsonMimeType
    };

    public static IReadOnlyList<ResourceLinkBlock> TypeLinks(IEnumerable<(string AssemblyPath, string FullName)> types) =>
        types
            .Where(t => !string.IsNullOrEmpty(t.FullName))
            .Select(t => TypeLink(t.AssemblyPath, t.FullName))
            .DistinctBy(link => link.Uri, StringComparer.Ordinal)
            .ToArray();

    private static string Encode(string value) => Uri.EscapeDataString(value);
}
