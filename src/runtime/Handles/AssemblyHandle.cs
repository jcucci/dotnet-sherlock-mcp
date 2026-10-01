namespace Sherlock.MCP.Runtime.Handles;

public sealed record AssemblyFileStamp(string Path, long LastWriteTicks, long Length)
{
    public static AssemblyFileStamp? Read(string path)
    {
        try
        {
            var info = new FileInfo(System.IO.Path.GetFullPath(path));
            return info.Exists ? new AssemblyFileStamp(info.FullName, info.LastWriteTimeUtc.Ticks, info.Length) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool IsCurrent() => Read(Path) is { } current && current == this;
}

public sealed record AssemblyHandle(string Id, IReadOnlyList<AssemblyFileStamp> Files, DateTimeOffset LastUsedUtc)
{
    public string AssemblyPath => Files[0].Path;

    public IReadOnlyList<string> AdditionalAssemblies => Files.Skip(1).Select(file => file.Path).ToArray();

    public IReadOnlyList<string> ChangedFiles => Files.Where(file => !file.IsCurrent()).Select(file => file.Path).ToArray();

    public IReadOnlyList<string> MissingFiles => Files.Where(file => AssemblyFileStamp.Read(file.Path) is null).Select(file => file.Path).ToArray();
}

public enum HandleStatus
{
    Resolved,
    Unknown,
    Stale
}

public readonly record struct HandleLookup(HandleStatus Status, AssemblyHandle? Handle);
