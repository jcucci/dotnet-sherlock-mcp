using Microsoft.CodeAnalysis.CSharp;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Inspection;
using System.Runtime.InteropServices;

namespace Sherlock.MCP.Benchmarks;

public static class BenchmarkCorpus
{
    public const string Small = "small";
    public const string Large = "large";
    public const string NuGet = "nuget";

    public static string SmallPath => typeof(RuntimeOptions).Assembly.Location;

    public static string LargePath => typeof(CSharpSyntaxTree).Assembly.Location;

    public static string LargeCompanionPath => typeof(Microsoft.CodeAnalysis.SyntaxTree).Assembly.Location;

    public static string NuGetPath => ResolveNuGetPath();

    public static string FrameworkDirectory => RuntimeEnvironment.GetRuntimeDirectory();

    public static string[] ReverseLookupScope => [LargePath, LargeCompanionPath, SmallPath];

    public static string PathFor(string corpus) => corpus switch
    {
        Small => SmallPath,
        Large => LargePath,
        NuGet => NuGetPath,
        _ => throw new ArgumentOutOfRangeException(nameof(corpus), corpus, "Unknown corpus entry")
    };

    private static string ResolveNuGetPath()
    {
        Version version = typeof(ICSharpCode.Decompiler.CSharp.CSharpDecompiler).Assembly.GetName().Version
            ?? throw new InvalidOperationException("ICSharpCode.Decompiler has no assembly version");
        string packageRoot = Path.Combine(NuGetCacheProbe.GetCacheRoot(), "icsharpcode.decompiler");
        string? match = Directory.Exists(packageRoot)
            ? Directory.EnumerateDirectories(packageRoot)
                .Where(dir => Path.GetFileName(dir).StartsWith($"{version.Major}.{version.Minor}.", StringComparison.Ordinal))
                .SelectMany(dir => Directory.EnumerateFiles(dir, "ICSharpCode.Decompiler.dll", SearchOption.AllDirectories))
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .FirstOrDefault()
            : null;

        return match ?? throw new InvalidOperationException(
            $"ICSharpCode.Decompiler {version.Major}.{version.Minor}.x was not found under {packageRoot}; run 'dotnet restore' first.");
    }
}
