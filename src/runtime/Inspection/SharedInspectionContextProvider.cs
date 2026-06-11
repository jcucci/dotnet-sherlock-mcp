using System.Collections.Concurrent;

namespace Sherlock.MCP.Runtime.Inspection;

public sealed class SharedInspectionContextProvider : IInspectionContextProvider, IDisposable
{
    private sealed class Entry
    {
        private readonly object _gate = new();
        private int _refCount;
        private bool _retired;

        public Entry(IAssemblyInspectionContext context, long fileStampTicks, long fileLength)
        {
            Context = context;
            FileStampTicks = fileStampTicks;
            FileLength = fileLength;
        }

        public IAssemblyInspectionContext Context { get; }

        public long FileStampTicks { get; }

        public long FileLength { get; }

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
    private long _accessCounter;

    public SharedInspectionContextProvider(RuntimeOptions options) => _options = options;

    public InspectionContextLease Acquire(string assemblyPath, bool forceRuntimeLoad = false)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        var key = forceRuntimeLoad ? $"{fullPath}|runtime" : fullPath;
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"Assembly file not found: {fullPath}", fullPath);

        var stampTicks = fileInfo.LastWriteTimeUtc.Ticks;
        var length = fileInfo.Length;

        while (true)
        {
            var lazy = _entries.GetOrAdd(key, _ => new Lazy<Entry>(
                () => new Entry(InspectionContextFactory.Create(fullPath, forceRuntimeLoad), stampTicks, length),
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

            if (entry.FileStampTicks != stampTicks || entry.FileLength != length)
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
