using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.Telemetry;

namespace Sherlock.MCP.Runtime.Handles;

public sealed class AssemblyHandleRegistry : IAssemblyHandleRegistry
{
    public const string HandlePrefix = "asm_";
    private const string FileName = "handles.json";
    private const int StoreVersion = 1;
    private const int IdHexLength = 12;
    private static readonly TimeSpan UsagePersistInterval = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions StoreJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly RuntimeOptions _options;
    private readonly ITelemetry _telemetry;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, AssemblyHandle> _handles = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _loaded;

    public AssemblyHandleRegistry(RuntimeOptions options, ITelemetry telemetry)
        : this(options, telemetry, TimeProvider.System)
    {
    }

    public AssemblyHandleRegistry(RuntimeOptions options, ITelemetry telemetry, TimeProvider time)
    {
        _options = options;
        _telemetry = telemetry;
        _time = time;
    }

    public string StorePath => Path.Combine(_options.StateDirectory, FileName);

    // _gate only serializes this process. Two servers sharing StateDirectory can race between reading and
    // replacing handles.json and drop each other's newest entry from disk; the opener keeps it in memory, and
    // because ids are deterministic a later open_assembly restores the same handle.
    public AssemblyHandle Open(string assemblyPath, IReadOnlyList<string>? additionalAssemblies = null)
    {
        var files = StampScope(assemblyPath, additionalAssemblies ?? []);
        var handle = new AssemblyHandle(ComputeId(files), files, _time.GetUtcNow());

        lock (_gate)
        {
            MergeFromDisk();
            _handles[handle.Id] = handle;
            EvictOverCapacity();
            Persist();
        }

        return handle;
    }

    public HandleLookup Resolve(string handleId)
    {
        var id = handleId.Trim();
        AssemblyHandle? handle;
        lock (_gate)
        {
            if (!_loaded || !_handles.ContainsKey(id))
                MergeFromDisk();
            if (!_handles.TryGetValue(id, out handle))
                return new HandleLookup(HandleStatus.Unknown, Handle: null);

            var now = _time.GetUtcNow();
            var persistUsage = now - handle.LastUsedUtc >= UsagePersistInterval;
            handle = handle with { LastUsedUtc = now };
            _handles[id] = handle;
            if (persistUsage)
            {
                MergeFromDisk();
                Persist();
            }
        }

        return handle.ChangedFiles.Count > 0
            ? new HandleLookup(HandleStatus.Stale, handle)
            : new HandleLookup(HandleStatus.Resolved, handle);
    }

    internal static string ComputeId(IReadOnlyList<AssemblyFileStamp> files)
    {
        var caseInsensitive = PathComparers.Comparison == StringComparison.OrdinalIgnoreCase;
        var material = string.Join('\n', files.Select(file =>
            $"{(caseInsensitive ? file.Path.ToUpperInvariant() : file.Path)}|{file.LastWriteTicks}|{file.Length}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return HandlePrefix + Convert.ToHexString(hash)[..IdHexLength].ToLowerInvariant();
    }

    private static AssemblyFileStamp[] StampScope(string assemblyPath, IReadOnlyList<string> additionalAssemblies)
    {
        var primary = Stamp(assemblyPath);
        var additional = additionalAssemblies
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Stamp)
            .Where(stamp => !string.Equals(stamp.Path, primary.Path, PathComparers.Comparison))
            .DistinctBy(stamp => stamp.Path, PathComparers.Comparer)
            .OrderBy(stamp => stamp.Path, PathComparers.Comparer);
        return [primary, .. additional];
    }

    private static AssemblyFileStamp Stamp(string path) =>
        AssemblyFileStamp.Read(path) ?? throw new FileNotFoundException($"Assembly file not found: {path}", path);

    private void EvictOverCapacity()
    {
        var capacity = Math.Max(1, _options.MaxAssemblyHandles);
        if (_handles.Count <= capacity) return;

        foreach (var stale in _handles.Values.OrderByDescending(handle => handle.LastUsedUtc).Skip(capacity).ToArray())
            _handles.Remove(stale.Id);
    }

    private void MergeFromDisk()
    {
        _loaded = true;
        foreach (var stored in Load())
        {
            if (!_handles.TryGetValue(stored.Id, out var current) || current.LastUsedUtc < stored.LastUsedUtc)
                _handles[stored.Id] = stored;
        }
    }

    private AssemblyHandle[] Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];

            var store = JsonSerializer.Deserialize<HandleStore>(File.ReadAllText(StorePath), StoreJsonOptions);
            if (store is not { Version: StoreVersion, Handles: { } entries }) return [];

            return entries
                .Where(IsWellFormed)
                .Select(entry => new AssemblyHandle(
                    entry.Id,
                    entry.Files.Select(file => new AssemblyFileStamp(file.Path, file.LastWriteTicks, file.Length)).ToArray(),
                    entry.LastUsedUtc))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _telemetry.Increment("handles.load_failed");
            return [];
        }
    }

    private static bool IsWellFormed(StoredHandle? entry) =>
        entry is { Id.Length: > 0, Files.Count: > 0 }
        && entry.Files.All(file => file is { Path.Length: > 0 });

    private void Persist()
    {
        var store = new HandleStore(StoreVersion, _handles.Values
            .OrderByDescending(handle => handle.LastUsedUtc)
            .Select(handle => new StoredHandle(
                handle.Id,
                handle.LastUsedUtc,
                handle.Files.Select(file => new StoredFile(file.Path, file.LastWriteTicks, file.Length)).ToArray()))
            .ToArray());

        var temporaryPath = $"{StorePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(_options.StateDirectory);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(store, StoreJsonOptions));
            File.Move(temporaryPath, StorePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _telemetry.Increment("handles.persist_failed");
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed record HandleStore(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("handles")] IReadOnlyList<StoredHandle>? Handles);

    private sealed record StoredHandle(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("lastUsedUtc")] DateTimeOffset LastUsedUtc,
        [property: JsonPropertyName("files")] IReadOnlyList<StoredFile> Files);

    private sealed record StoredFile(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("lastWriteTicks")] long LastWriteTicks,
        [property: JsonPropertyName("length")] long Length);
}
