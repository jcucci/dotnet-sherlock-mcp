using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Sherlock.MCP.Runtime.Il;
using Sherlock.MCP.Runtime.ProjectAssets;

namespace Sherlock.MCP.Runtime.Inspection;

public sealed class SharedInspectionContextProvider : IInspectionContextProvider, IMetadataReaderProvider, IDisposable
{
    private abstract class LeasedEntry
    {
        private readonly object _gate = new();
        private int _refCount;
        private bool _retired;

        protected LeasedEntry(long fileStampTicks, long fileLength)
        {
            FileStampTicks = fileStampTicks;
            FileLength = fileLength;
        }

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

        protected abstract void DisposeResource();

        private void SafeDispose()
        {
            try { DisposeResource(); } catch { }
        }
    }

    private sealed class Entry : LeasedEntry
    {
        public Entry(IAssemblyInspectionContext context, long fileStampTicks, long fileLength, string assetsStamp, string runtimeConfigStamp)
            : base(fileStampTicks, fileLength)
        {
            Context = context;
            AssetsStamp = assetsStamp;
            RuntimeConfigStamp = runtimeConfigStamp;
        }

        public IAssemblyInspectionContext Context { get; }

        public string AssetsStamp { get; }

        public string RuntimeConfigStamp { get; }

        protected override void DisposeResource() => Context.Dispose();
    }

    private sealed class MetadataEntry : LeasedEntry
    {
        public MetadataEntry(PEReader peReader, long fileStampTicks, long fileLength) : base(fileStampTicks, fileLength)
        {
            PEReader = peReader;
            Reader = peReader.GetMetadataReader();
            Resolver = new MetadataTokenResolver(Reader);
        }

        public PEReader PEReader { get; }

        public MetadataReader Reader { get; }

        public MetadataTokenResolver Resolver { get; }

        protected override void DisposeResource() => PEReader.Dispose();
    }

    private readonly ConcurrentDictionary<string, Lazy<Entry>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<MetadataEntry>> _metadataEntries = new(StringComparer.OrdinalIgnoreCase);
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
        var fileInfo = RequireFile(fullPath);
        var stampTicks = fileInfo.LastWriteTimeUtc.Ticks;
        var length = fileInfo.Length;
        var assetsStamp = ProjectAssetsLocator.AssetsStamp(fullPath);
        var runtimeConfigStamp = FrameworkReferenceResolver.RuntimeConfigStamp(fullPath);

        var entry = AcquireEntry(
            _entries,
            key,
            () => new Entry(InspectionContextFactory.Create(fullPath, additionalSearchDirectories), stampTicks, length, assetsStamp, runtimeConfigStamp),
            e => e.FileStampTicks == stampTicks && e.FileLength == length && e.AssetsStamp == assetsStamp
                && e.RuntimeConfigStamp == runtimeConfigStamp && !FrameworkReferenceResolver.PacksChanged(fullPath, e.Context.Framework));

        _recentAssemblies?.Record(fullPath);
        return new InspectionContextLease(entry.Context, entry.Release);
    }

    public MetadataReaderLease AcquireMetadata(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        var fileInfo = RequireFile(fullPath);
        var stampTicks = fileInfo.LastWriteTimeUtc.Ticks;
        var length = fileInfo.Length;

        var entry = AcquireEntry(
            _metadataEntries,
            fullPath,
            () => new MetadataEntry(OpenPEReader(fullPath), stampTicks, length),
            e => e.FileStampTicks == stampTicks && e.FileLength == length);

        return new MetadataReaderLease(entry.PEReader, entry.Reader, entry.Resolver, entry.Release);
    }

    public void Dispose()
    {
        RetireAll(_entries);
        RetireAll(_metadataEntries);
    }

    private TEntry AcquireEntry<TEntry>(
        ConcurrentDictionary<string, Lazy<TEntry>> entries, string key, Func<TEntry> create, Func<TEntry, bool> isCurrent)
        where TEntry : LeasedEntry
    {
        while (true)
        {
            var lazy = entries.GetOrAdd(key, _ => new Lazy<TEntry>(create, LazyThreadSafetyMode.ExecutionAndPublication));

            TEntry entry;
            try
            {
                entry = lazy.Value;
            }
            catch
            {
                entries.TryRemove(new KeyValuePair<string, Lazy<TEntry>>(key, lazy));
                throw;
            }

            if (!isCurrent(entry))
            {
                if (entries.TryRemove(new KeyValuePair<string, Lazy<TEntry>>(key, lazy)))
                    entry.Retire();
                continue;
            }

            if (!entry.TryAcquire())
            {
                entries.TryRemove(new KeyValuePair<string, Lazy<TEntry>>(key, lazy));
                continue;
            }

            Interlocked.Exchange(ref entry.LastAccess, Interlocked.Increment(ref _accessCounter));
            EvictOverflow(entries);
            return entry;
        }
    }

    private static FileInfo RequireFile(string fullPath)
    {
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"Assembly file not found: {fullPath}", fullPath);
        return fileInfo;
    }

    private static PEReader OpenPEReader(string fullPath)
    {
        var peReader = new PEReader(File.OpenRead(fullPath));
        try
        {
            if (!peReader.HasMetadata)
                throw new BadImageFormatException($"File has no .NET metadata: {fullPath}", fullPath);
            return peReader;
        }
        catch
        {
            peReader.Dispose();
            throw;
        }
    }

    private static void RetireAll<TEntry>(ConcurrentDictionary<string, Lazy<TEntry>> entries) where TEntry : LeasedEntry
    {
        foreach (var pair in entries.ToArray())
        {
            if (!entries.TryRemove(pair)) continue;
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

    private void EvictOverflow<TEntry>(ConcurrentDictionary<string, Lazy<TEntry>> entries) where TEntry : LeasedEntry
    {
        var max = Math.Max(1, _options.MaxLoadedAssemblies);
        if (entries.Count <= max) return;

        var idle = entries
            .Where(p => p.Value.IsValueCreated && p.Value.Value.IsIdle)
            .OrderBy(p => Interlocked.Read(ref p.Value.Value.LastAccess))
            .ToArray();

        var overflow = entries.Count - max;
        foreach (var pair in idle.Take(overflow))
        {
            if (entries.TryRemove(pair))
                pair.Value.Value.Retire();
        }
    }
}
