using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Tests;

public class NuGetCacheProbeTests
{
    [Fact]
    public void TryParseCacheLayout_RecognizesNuGetLibPath()
    {
        using var cache = new TempCache();
        var dll = cache.AddPackageDll("pkga", "1.0.0", "net8.0", "PkgA.dll");

        var parsed = NuGetCacheProbe.TryParseCacheLayout(dll, out var tfm, out var packageId);

        Assert.True(parsed);
        Assert.Equal("net8.0", tfm);
        Assert.Equal("pkga", packageId);
    }

    [Fact]
    public void TryParseCacheLayout_RejectsPathOutsideCache()
    {
        using var cache = new TempCache();
        var outside = Path.Combine(Path.GetTempPath(), $"sherlock_outside_{Guid.NewGuid():N}.dll");

        var parsed = NuGetCacheProbe.TryParseCacheLayout(outside, out _, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void EnumerateCandidateDependencyDlls_FindsSibling_ExcludesSelf()
    {
        using var cache = new TempCache();
        var primary = cache.AddPackageDll("pkga", "1.0.0", "net8.0", "PkgA.dll");
        var dependency = cache.AddPackageDll("depb", "2.0.0", "net8.0", "DepB.dll");

        var candidates = NuGetCacheProbe.EnumerateCandidateDependencyDlls("net8.0", "pkga");

        Assert.Contains(candidates, c => string.Equals(c, dependency, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(candidates, c => string.Equals(c, primary, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumerateCandidateDependencyDlls_FallsBackToCompatibleTfm()
    {
        using var cache = new TempCache();
        cache.AddPackageDll("pkga", "1.0.0", "net8.0", "PkgA.dll");
        var dependency = cache.AddPackageDll("depb", "2.0.0", "netstandard2.0", "DepB.dll");

        var candidates = NuGetCacheProbe.EnumerateCandidateDependencyDlls("net8.0", "pkga");

        Assert.Contains(candidates, c => string.Equals(c, dependency, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumerateCandidateDependencyDlls_AcceptsLowerNetTfm()
    {
        using var cache = new TempCache();
        cache.AddPackageDll("pkga", "1.0.0", "net8.0", "PkgA.dll");
        var dependency = cache.AddPackageDll("depb", "2.0.0", "net6.0", "DepB.dll");

        var candidates = NuGetCacheProbe.EnumerateCandidateDependencyDlls("net8.0", "pkga");

        Assert.Contains(candidates, c => string.Equals(c, dependency, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumerateCandidateDependencyDlls_PrefersNearestCompatibleTfm()
    {
        using var cache = new TempCache();
        cache.AddPackageDll("pkga", "1.0.0", "net8.0", "PkgA.dll");
        cache.AddPackageDll("depb", "2.0.0", "net6.0", "DepB.dll");
        var preferred = cache.AddPackageDll("depb", "2.0.0", "net8.0", "DepB.dll");

        var candidates = NuGetCacheProbe.EnumerateCandidateDependencyDlls("net8.0", "pkga");

        Assert.Contains(preferred, candidates);
        Assert.DoesNotContain(candidates, c => c.Contains($"{Path.DirectorySeparatorChar}net6.0{Path.DirectorySeparatorChar}"));
    }

    [Fact]
    public void EnumerateCandidateDependencyDlls_ReusesSnapshot_WhenCacheUnchanged()
    {
        using var cache = new TempCache();
        cache.AddPackageDll("pkga", "1.0.0", "net8.0", "PkgA.dll");
        var dependency = cache.AddPackageDll("depb", "2.0.0", "net8.0", "DepB.dll");
        var rootWriteTime = Directory.GetLastWriteTimeUtc(cache.Root);

        NuGetCacheProbe.EnumerateCandidateDependencyDlls("net8.0", "pkga");
        File.Delete(dependency);
        Directory.SetLastWriteTimeUtc(cache.Root, rootWriteTime);
        var candidates = NuGetCacheProbe.EnumerateCandidateDependencyDlls("net8.0", "pkga");

        Assert.Contains(dependency, candidates);
    }

    [Fact]
    public void EnumerateCandidateDependencyDlls_Rebuilds_WhenRootMtimeChanges()
    {
        using var cache = new TempCache();
        cache.AddPackageDll("pkga", "1.0.0", "net8.0", "PkgA.dll");
        var rootWriteTime = Directory.GetLastWriteTimeUtc(cache.Root);

        NuGetCacheProbe.EnumerateCandidateDependencyDlls("net8.0", "pkga");
        var added = cache.AddPackageDll("depc", "1.0.0", "net8.0", "DepC.dll");
        Directory.SetLastWriteTimeUtc(cache.Root, rootWriteTime.AddSeconds(5));
        var candidates = NuGetCacheProbe.EnumerateCandidateDependencyDlls("net8.0", "pkga");

        Assert.Contains(added, candidates);
    }

    [Fact]
    public void EnumerateCandidateDependencyDlls_Rebuilds_AfterTtl()
    {
        using var cache = new TempCache();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        NuGetCacheProbe.Clock = clock;
        cache.AddPackageDll("pkga", "1.0.0", "net8.0", "PkgA.dll");
        cache.AddPackageDll("depb", "1.0.0", "net8.0", "DepB.dll");
        var rootWriteTime = Directory.GetLastWriteTimeUtc(cache.Root);

        NuGetCacheProbe.EnumerateCandidateDependencyDlls("net8.0", "pkga");
        var newer = cache.AddPackageDll("depb", "2.0.0", "net8.0", "DepB.dll");
        Directory.SetLastWriteTimeUtc(cache.Root, rootWriteTime);

        Assert.DoesNotContain(newer, NuGetCacheProbe.EnumerateCandidateDependencyDlls("net8.0", "pkga"));

        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Contains(newer, NuGetCacheProbe.EnumerateCandidateDependencyDlls("net8.0", "pkga"));
    }

    [Fact]
    public void EnumerateCandidateDependencyDlls_SeparatesResultsByTfmAndExclude()
    {
        using var cache = new TempCache();
        var pkgA = cache.AddPackageDll("pkga", "1.0.0", "net8.0", "PkgA.dll");
        var depB = cache.AddPackageDll("depb", "1.0.0", "net8.0", "DepB.dll");
        var depBStandard = cache.AddPackageDll("depb", "1.0.0", "netstandard2.0", "DepB.dll");

        var forNet8 = NuGetCacheProbe.EnumerateCandidateDependencyDlls("net8.0", "pkga");
        var forStandard = NuGetCacheProbe.EnumerateCandidateDependencyDlls("netstandard2.0", "depb");

        Assert.Equal([depB], forNet8);
        Assert.DoesNotContain(pkgA, forStandard);
        Assert.DoesNotContain(depBStandard, forStandard);
        Assert.DoesNotContain(depB, forStandard);
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class TempCache : IDisposable
    {
        private static readonly object EnvLock = new();
        private readonly string? _previous;

        public TempCache()
        {
            Monitor.Enter(EnvLock);
            try
            {
                Root = Path.Combine(Path.GetTempPath(), $"sherlock_cache_{Guid.NewGuid():N}");
                Directory.CreateDirectory(Root);
                _previous = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
                Environment.SetEnvironmentVariable("NUGET_PACKAGES", Root);
                NuGetCacheProbe.ResetCache();
            }
            catch
            {
                Monitor.Exit(EnvLock);
                throw;
            }
        }

        public string Root { get; } = "";

        public string AddPackageDll(string packageId, string version, string tfm, string fileName)
        {
            var dir = Path.Combine(Root, packageId, version, "lib", tfm);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);
            File.WriteAllText(path, "");
            return path;
        }

        public void Dispose()
        {
            try
            {
                Environment.SetEnvironmentVariable("NUGET_PACKAGES", _previous);
                NuGetCacheProbe.Clock = TimeProvider.System;
                NuGetCacheProbe.ResetCache();
                try { Directory.Delete(Root, recursive: true); } catch { }
            }
            finally
            {
                Monitor.Exit(EnvLock);
            }
        }
    }
}
