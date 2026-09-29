using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class AssemblyLocatorTests : IDisposable
{
    private static readonly string SourceAssembly = Assembly.GetExecutingAssembly().Location;
    private static readonly string AssemblyFileName = Path.GetFileName(SourceAssembly);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sherlock_locator_{Guid.NewGuid():N}");

    public AssemblyLocatorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Place(string relativeDirectory, DateTime? lastWriteUtc = null)
    {
        var directory = Path.Combine(_root, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, AssemblyFileName);
        File.Copy(SourceAssembly, path);
        File.SetLastWriteTimeUtc(path, lastWriteUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        return path;
    }

    [Fact]
    public void FindByClassName_SkipsExcludedDirectories()
    {
        var binCopy = Place("proj/bin/Debug/net10.0");
        Place("proj/obj/Debug/net10.0");
        Place("proj/obj/Debug/net10.0/ref");
        Place("node_modules/x");
        Place(".git/x");
        Place("packages/x");

        var result = AssemblyLocator.FindByClassName(_root, "TestSampleClass");

        Assert.Equal([binCopy], result);
    }

    [Fact]
    public void FindByFileName_SkipsExcludedDirectories()
    {
        var binCopy = Place("proj/bin/Release/net10.0");
        Place("proj/obj/Release/net10.0");
        Place(".vs/x");

        var result = AssemblyLocator.FindByFileName(_root, AssemblyFileName);

        Assert.Equal([binCopy], result);
    }

    [Fact]
    public void Ranking_PrefersBinThenNewestThenShortestPath()
    {
        var older = Place("a/bin/Debug/net10.0", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = Place("b/bin/Debug/net10.0", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var outsideBin = Place("publish", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        var longerTie = Place("c/nested/bin/Debug/net10.0", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var first = AssemblyLocator.FindByFileName(_root, AssemblyFileName);
        var second = AssemblyLocator.FindByFileName(_root, AssemblyFileName);

        Assert.Equal([newer, older, longerTie, outsideBin], first);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("TestSampleClass")]
    [InlineData("Sherlock.MCP.Tests.TestSampleClass")]
    [InlineData("testsampleclass")]
    [InlineData("Outer+Inner")]
    [InlineData("Sherlock.MCP.Tests.Outer+Inner")]
    public void FindByClassName_MatchesSimpleFullAndNestedNames(string className)
    {
        var binCopy = Place("proj/bin/Debug/net10.0");

        Assert.Equal([binCopy], AssemblyLocator.FindByClassName(_root, className));
    }

    [Fact]
    public void FindByClassName_InspectsEachSameNamedFileIndependently()
    {
        var matching = Place("a/bin/Debug/net10.0");
        var unrelated = Path.Combine(_root, "b/bin/Debug/net10.0", AssemblyFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(unrelated)!);
        File.Copy(typeof(AssemblyLocator).Assembly.Location, unrelated);
        File.SetLastWriteTimeUtc(unrelated, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal([matching], AssemblyLocator.FindByClassName(_root, "TestSampleClass"));
        Assert.Equal([unrelated], AssemblyLocator.FindByClassName(_root, "Sherlock.MCP.Runtime.Inspection.AssemblyLocator"));
    }

    [Fact]
    public void FindByClassName_ReturnsEmpty_ForUnknownType()
    {
        Place("proj/bin/Debug/net10.0");

        Assert.Empty(AssemblyLocator.FindByClassName(_root, "DefinitelyNotARealTypeName"));
    }

    [Fact]
    public void FindByClassName_SkipsNonPeFiles()
    {
        var binCopy = Place("proj/bin/Debug/net10.0");
        File.WriteAllText(Path.Combine(_root, "proj/bin/Debug/net10.0/junk.dll"), "not a PE file");

        Assert.Equal([binCopy], AssemblyLocator.FindByClassName(_root, "TestSampleClass"));
    }

    [Fact]
    public void FindAssemblyByClassName_Tool_ReturnsBestMatchAndCandidates()
    {
        var newer = Place("a/bin/Debug/net10.0", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var older = Place("b/bin/Debug/net10.0");

        var data = JsonDocument.Parse(ReflectionTools.FindAssemblyByClassName("TestSampleClass", _root))
            .RootElement.GetProperty("data");

        Assert.Equal(newer, data.GetProperty("foundAssembly").GetString());
        Assert.Equal(2, data.GetProperty("candidateCount").GetInt32());
        Assert.Equal([newer, older], data.GetProperty("candidates").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void FindAssemblyByFileName_Tool_ReturnsInvalidArgument_ForMissingDirectory()
    {
        var root = JsonDocument.Parse(ReflectionTools.FindAssemblyByFileName(AssemblyFileName, Path.Combine(_root, "missing")))
            .RootElement;

        Assert.Equal("error", root.GetProperty("kind").GetString());
        Assert.Equal("InvalidArgument", root.GetProperty("code").GetString());
    }

    [Fact]
    public void FindAssemblyByClassName_Tool_ReturnsNotFoundWithGuidance()
    {
        var root = JsonDocument.Parse(ReflectionTools.FindAssemblyByClassName("DefinitelyNotARealTypeName", _root))
            .RootElement;

        Assert.Equal("AssemblyNotFound", root.GetProperty("code").GetString());
        Assert.True(root.GetProperty("alternativeTools").GetArrayLength() > 0);
    }
}
