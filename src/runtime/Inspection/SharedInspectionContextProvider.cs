using System.Collections.Concurrent;
using Sherlock.MCP.Runtime.ProjectAssets;

namespace Sherlock.MCP.Runtime.Inspection;

public sealed class SharedInspectionContextProvider : IInspectionContextProvider, IDisposable
{
    private sealed class Entry
    {
        private readonly object _gate = new();
        private int _refCount;
        private bool _retired;

        public Entry(IAssemblyInspectionContext context, long fileStampTicks, long fileLength, string assetsStamp, string runtimeConfigStamp)
        {
            RuntimeConfigStamp = runtimeConfigStamp;
            Context = context;
            FileStampTicks = fileStampTicks;
            FileLength = fileLength;
            AssetsStamp = assetsStamp;
        }

        public IAssemblyInspectionContext Context { get; }

        public long FileStampTicks { get; }

        public long FileLength { get; }

        public string AssetsStamp { get; }

        public string RuntimeConfigStamp { get; }

        public long LastAccess;

        public bool TryAcquire()
        {
            lock (_gate)
            {
                if (_retired) return false;
                _refCount++;
                return true;
            }
        }

        public void Release()
        {
            bool disposeNow;
            lock (_gate)
            {
                _refCount--;
                disposeNow = _retired && _refCount == 0;
            }
            if (disposeNow) SafeDispose();
        }

        public void Retire()
        {
            bool disposeNow;
            lock (_gate)
            {
                if (_retired) return;
                _retired = true;
                disposeNow = _refCount == 0;
            }
            if (disposeNow) SafeDispose();
        }

        public bool IsIdle
        {
            get { lock (_gate) return !_retired && _refCount == 0; }
        }

        private void SafeDispose()
        {
            try { Context.Dispose(); } catch { }
        }
    }

    private readonly ConcurrentDictionary<string, Lazy<Entry>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly RuntimeOptions _options;
    private readonly IRecentAssemblyRegistry? _recentAssemblies;
    private long _accessCounter;

    public SharedInspectionContextProvider(RuntimeOptions options) : this(options, recentAssemblies: null)
    {
    }

    public SharedInspectionContextProvider(RuntimeOptions options, IRecentAssemblyRegistry? recentAssemblies)
    {
        _options = options;
        _recentAssemblies = recentAssemblies;
    }

    public InspectionContextLease Acquire(string assemblyPath, IReadOnlyList<string>? additionalSearchDirectories = null)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        var key = fullPath + BuildDepsKey(additionalSearchDirectories);
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"Assembly file not found: {fullPath}", fullPath);

        var stampTicks = fileInfo.LastWriteTimeUtc.Ticks;
        var length = fileInfo.Length;
        var assetsStamp = ProjectAssetsLocator.AssetsStamp(fullPath);
        var runtimeConfigStamp = FrameworkReferenceResolver.RuntimeConfigStamp(fullPath);

        while (true)
        {
            var lazy = _entries.GetOrAdd(key, _ => new Lazy<Entry>(
                () => new Entry(InspectionContextFactory.Create(fullPath, additionalSearchDirectories), stampTicks, length, assetsStamp, runtimeConfigStamp),
                LazyThreadSafetyMode.ExecutionAndPublication));

            Entry entry;
            try
            {
                entry = lazy.Value;
            }
            catch
            {
                _entries.TryRemove(new KeyValuePair<string, Lazy<Entry>>(key, lazy));
                throw;
            }

            if (entry.FileStampTicks != stampTicks || entry.FileLength != length || entry.AssetsStamp != assetsStamp
                || entry.RuntimeConfigStamp != runtimeConfigStamp || FrameworkReferenceResolver.PacksChanged(fullPath, entry.Context.Framework))
            {
                if (_entries.TryRemove(new KeyValuePair<string, Lazy<Entry>>(key, lazy)))
                    entry.Retire();
                continue;
            }

            if (!entry.TryAcquire())
            {
                _entries.TryRemove(new KeyValuePair<string, Lazy<Entry>>(key, lazy));
                continue;
            }

            Interlocked.Exchange(ref entry.LastAccess, Interlocked.Increment(ref _accessCounter));
            EvictOverflow();
            _recentAssemblies?.Record(fullPath);
            return new InspectionContextLease(entry.Context, entry.Release);
        }
    }

    public void Dispose()
    {
        foreach (var pair in _entries.ToArray())
        {
            if (!_entries.TryRemove(pair)) continue;
            if (pair.Value.IsValueCreated)
                pair.Value.Value.Retire();
        }
    }

    private static string BuildDepsKey(IReadOnlyList<string>? directories)
    {
        if (directories == null || directories.Count == 0) return "";

        var normalized = directories
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0) return "";

        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("|", normalized));
        return "|deps:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
    }

    private void EvictOverflow()
    {
        var max = Math.Max(1, _options.MaxLoadedAssemblies);
        if (_entries.Count <= max) return;

        var idle = _entries
            .Where(p => p.Value.IsValueCreated && p.Value.Value.IsIdle)
            .OrderBy(p => Interlocked.Read(ref p.Value.Value.LastAccess))
            .ToArray();

        var overflow = _entries.Count - max;
        foreach (var pair in idle.Take(overflow))
        {
            if (_entries.TryRemove(pair))
                pair.Value.Value.Retire();
        }
    }
}
