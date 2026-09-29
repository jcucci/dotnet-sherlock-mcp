using System.Collections.Concurrent;

namespace Sherlock.MCP.Runtime.Inspection;

internal static class NuGetCacheProbe
{
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static string GetCacheRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".nuget", "packages");
    }

    public static bool TryParseCacheLayout(string fullAssemblyPath, out string consumingTfm, out string packageId)
    {
        consumingTfm = "";
        packageId = "";

        var cacheRoot = GetCacheRoot();
        if (string.IsNullOrWhiteSpace(cacheRoot) || !Directory.Exists(cacheRoot)) return false;

        var fullCacheRoot = Path.GetFullPath(cacheRoot);
        var normalized = Path.GetFullPath(fullAssemblyPath);
        if (!normalized.StartsWith(EnsureTrailingSeparator(fullCacheRoot), PathComparison)) return false;

        var relative = Path.GetRelativePath(fullCacheRoot, normalized);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length < 5) return false;

        var folderKind = segments[2];
        if (!folderKind.Equals("lib", StringComparison.OrdinalIgnoreCase) && !folderKind.Equals("ref", StringComparison.OrdinalIgnoreCase))
            return false;

        packageId = segments[0];
        consumingTfm = segments[3];
        return true;
    }

    public static List<string> EnumerateCandidateDependencyDlls(string consumingTfm, string excludePackageId)
    {
        var cacheRoot = GetCacheRoot();
        if (string.IsNullOrWhiteSpace(cacheRoot) || !Directory.Exists(cacheRoot)) return [];

        var snapshot = GetSnapshot(Path.GetFullPath(cacheRoot));
        var cached = snapshot.Results.GetOrAdd((consumingTfm, excludePackageId), key => ResolveCandidates(snapshot.Packages, key.Tfm, key.Exclude));
        return new List<string>(cached);
    }

    internal static TimeProvider Clock { get; set; } = TimeProvider.System;

    internal static void ResetCache() => Volatile.Write(ref _snapshot, null);

    private static CacheSnapshot GetSnapshot(string root)
    {
        var rootWriteTime = SafeGetLastWriteTimeUtc(root);
        var now = Clock.GetUtcNow();
        var current = Volatile.Read(ref _snapshot);
        if (current is not null && current.IsValidFor(root, rootWriteTime, now)) return current;

        var rebuilt = new CacheSnapshot(root, rootWriteTime, now, BuildPackageIndex(root));
        Interlocked.Exchange(ref _snapshot, rebuilt);
        return rebuilt;
    }

    private static PackageEntry[] BuildPackageIndex(string root)
    {
        var packages = new List<PackageEntry>();
        foreach (var packageDir in SafeEnumerateDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            try
            {
                var version = PickHighestVersion(SafeEnumerateDirectories(packageDir).Select(Path.GetFileName).Where(n => n is not null).Cast<string>().ToArray());
                if (version is null) continue;

                var libDir = Path.Combine(packageDir, version, "lib");
                if (!Directory.Exists(libDir)) continue;

                var tfms = SafeEnumerateDirectories(libDir).Select(Path.GetFileName).Where(n => n is not null).Cast<string>().ToArray();
                packages.Add(new PackageEntry(Path.GetFileName(packageDir), libDir, tfms));
            }
            catch { }
        }
        return [.. packages];
    }

    private static string[] ResolveCandidates(PackageEntry[] packages, string consumingTfm, string excludePackageId)
    {
        var candidates = new List<string>();
        foreach (var package in packages)
        {
            if (package.PackageId.Equals(excludePackageId, StringComparison.OrdinalIgnoreCase)) continue;

            var tfm = PickCompatibleTfm(package.Tfms, consumingTfm);
            if (tfm is null) continue;

            try
            {
                candidates.AddRange(Directory.GetFiles(Path.Combine(package.LibDir, tfm), "*.dll", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal));
            }
            catch { }
        }
        return [.. candidates];
    }

    private static DateTime SafeGetLastWriteTimeUtc(string directory)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(directory);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromMinutes(5);

    private static CacheSnapshot? _snapshot;

    private sealed record PackageEntry(string PackageId, string LibDir, string[] Tfms);

    private sealed class CacheSnapshot(string root, DateTime rootWriteTimeUtc, DateTimeOffset builtAt, PackageEntry[] packages)
    {
        public PackageEntry[] Packages { get; } = packages;

        public ConcurrentDictionary<(string Tfm, string Exclude), string[]> Results { get; } = new(CandidateKeyComparer.Instance);

        public bool IsValidFor(string candidateRoot, DateTime candidateWriteTimeUtc, DateTimeOffset now) =>
            string.Equals(root, candidateRoot, PathComparison)
            && rootWriteTimeUtc == candidateWriteTimeUtc
            && now - builtAt < SnapshotTtl;
    }

    private sealed class CandidateKeyComparer : IEqualityComparer<(string Tfm, string Exclude)>
    {
        public static readonly CandidateKeyComparer Instance = new();

        public bool Equals((string Tfm, string Exclude) x, (string Tfm, string Exclude) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Tfm, y.Tfm) && StringComparer.OrdinalIgnoreCase.Equals(x.Exclude, y.Exclude);

        public int GetHashCode((string Tfm, string Exclude) key) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Tfm), StringComparer.OrdinalIgnoreCase.GetHashCode(key.Exclude));
    }

    private static string[] SafeEnumerateDirectories(string directory)
    {
        try
        {
            return Directory.GetDirectories(directory);
        }
        catch
        {
            return [];
        }
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static string? PickHighestVersion(string[] versions)
    {
        if (versions.Length == 0) return null;
        var parsed = versions
            .Select(v => (raw: v, parsed: TryParseVersion(v), isStable: !v.Contains('-')))
            .Where(p => p.parsed is not null)
            .ToArray();
        if (parsed.Length > 0)
            return parsed
                .OrderByDescending(p => p.isStable)
                .ThenByDescending(p => p.parsed!)
                .ThenByDescending(p => p.raw, StringComparer.OrdinalIgnoreCase)
                .First().raw;
        return versions.OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase).First();
    }

    private static Version? TryParseVersion(string raw)
    {
        var core = raw.AsSpan();
        var cut = core.IndexOfAny('-', '+');
        if (cut >= 0) core = core[..cut];
        return Version.TryParse(core, out var v) ? v : null;
    }

    private static string? PickCompatibleTfm(string[] availableTfms, string requested)
    {
        var exact = availableTfms.FirstOrDefault(t => t.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var target = ParseTfm(requested);
        return availableTfms
            .Select(t => (raw: t, parsed: ParseTfm(t)))
            .Where(t => IsCompatible(t.parsed, target))
            .OrderByDescending(t => CompatibilityRank(t.parsed.family, target.family))
            .ThenByDescending(t => t.parsed.version)
            .ThenBy(t => t.raw, StringComparer.OrdinalIgnoreCase)
            .Select(t => t.raw)
            .FirstOrDefault();
    }

    private static bool IsCompatible((TfmFamily family, Version version) available, (TfmFamily family, Version version) target) =>
        target.family switch
        {
            TfmFamily.NetCore => available.family switch
            {
                TfmFamily.NetCore => available.version <= target.version,
                TfmFamily.NetCoreApp => true,
                TfmFamily.NetStandard => available.version <= new Version(2, 1),
                _ => false
            },
            TfmFamily.NetCoreApp => available.family switch
            {
                TfmFamily.NetCoreApp => available.version <= target.version,
                TfmFamily.NetStandard => available.version <= new Version(2, 1),
                _ => false
            },
            TfmFamily.NetStandard => available.family == TfmFamily.NetStandard && available.version <= target.version,
            TfmFamily.NetFramework => (available.family == TfmFamily.NetFramework && available.version <= target.version)
                || (available.family == TfmFamily.NetStandard && available.version <= new Version(2, 0)),
            _ => false
        };

    private static int CompatibilityRank(TfmFamily available, TfmFamily target)
    {
        if (available == target) return 3;
        return available switch
        {
            TfmFamily.NetCore or TfmFamily.NetCoreApp => 2,
            TfmFamily.NetStandard => 1,
            _ => 0
        };
    }

    private enum TfmFamily { NetCore, NetCoreApp, NetStandard, NetFramework, Unknown }

    private static (TfmFamily family, Version version) ParseTfm(string tfm)
    {
        var lower = tfm.ToLowerInvariant();
        if (lower.StartsWith("netstandard", StringComparison.Ordinal))
            return (TfmFamily.NetStandard, TryParseVersion(lower[11..]) ?? new Version(0, 0));
        if (lower.StartsWith("netcoreapp", StringComparison.Ordinal))
            return (TfmFamily.NetCoreApp, TryParseVersion(lower[10..]) ?? new Version(0, 0));
        if (lower.StartsWith("net", StringComparison.Ordinal) && !lower.Contains("framework"))
        {
            var rest = lower[3..];
            if (IsFrameworkStyleTfm(rest))
                return (TfmFamily.NetFramework, ParseFrameworkStyleVersion(rest));
            var version = TryParseVersion(rest);
            if (version is not null) return (TfmFamily.NetCore, version);
        }
        return (TfmFamily.Unknown, new Version(0, 0));
    }

    private static bool IsFrameworkStyleTfm(string rest)
    {
        if (rest.Length is not (2 or 3)) return false;
        foreach (var ch in rest)
            if (!char.IsDigit(ch)) return false;
        return rest[0] is >= '1' and <= '4';
    }

    private static Version ParseFrameworkStyleVersion(string rest)
    {
        var major = rest[0] - '0';
        var minor = rest[1] - '0';
        if (rest.Length == 2) return new Version(major, minor);
        return new Version(major, minor, rest[2] - '0');
    }
}
