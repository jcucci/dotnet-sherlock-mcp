using System.Reflection;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Contracts.MemberAnalysis;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Shared;

namespace Sherlock.MCP.Tests;

public class PhaseAImprovementTests
{
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;

    [Fact]
    public void XmlDocService_Is_ThreadSafe_Under_Concurrent_Access()
    {
        var xmlService = new XmlDocService();
        var testType = typeof(TestSampleClass);
        var method = testType.GetMethod("MethodWithParameters")!;

        Parallel.For(0, 64, _ =>
        {
            var typeDocs = xmlService.GetXmlDocsForType(testType);
            Assert.NotNull(typeDocs);
            var methodDocs = xmlService.GetXmlDocsForMember(method);
            Assert.NotNull(methodDocs);
        });
    }

    [Fact]
    public void FileStamp_Changes_When_File_Is_Rewritten()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sherlock-stamp-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(path, "one");
            var first = CacheKeyHelper.FileStamp(path);
            File.WriteAllText(path, "two-longer");
            var second = CacheKeyHelper.FileStamp(path);
            Assert.NotEqual(first, second);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FileStamp_Is_Stable_For_Unchanged_File()
    {
        var first = CacheKeyHelper.FileStamp(_testAssemblyPath);
        var second = CacheKeyHelper.FileStamp(_testAssemblyPath);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ToolResponseCache_Stays_Bounded_And_Keeps_Recent_Entries()
    {
        var options = new RuntimeOptions { MaxCachedResponses = 16 };
        var cache = new InMemoryToolResponseCache(options);

        for (var i = 0; i < 100; i++)
            cache.Set($"key-{i}", $"payload-{i}", TimeSpan.FromMinutes(5 + i));

        var retrievable = Enumerable.Range(0, 100).Count(i => cache.TryGet($"key-{i}", out _));
        Assert.InRange(retrievable, 1, 17);
        Assert.True(cache.TryGet("key-99", out var newest));
        Assert.Equal("payload-99", newest);
    }

    [Fact]
    public void ContextProvider_Returns_Fresh_Metadata_After_File_Change()
    {
        var provider = new SharedInspectionContextProvider(new RuntimeOptions());
        var tempPath = Path.Combine(Path.GetTempPath(), $"sherlock-fresh-{Guid.NewGuid():N}.dll");
        var otherAssemblyPath = typeof(RuntimeOptions).Assembly.Location;
        try
        {
            File.Copy(_testAssemblyPath, tempPath);
            string? firstName;
            using (var lease = provider.Acquire(tempPath))
                firstName = lease.Assembly.GetName().Name;

            File.Delete(tempPath);
            File.Copy(otherAssemblyPath, tempPath);
            File.SetLastWriteTimeUtc(tempPath, DateTime.UtcNow.AddSeconds(5));
            string? secondName;
            using (var lease = provider.Acquire(tempPath))
                secondName = lease.Assembly.GetName().Name;

            Assert.NotEqual(firstName, secondName);
            Assert.Equal(typeof(RuntimeOptions).Assembly.GetName().Name, secondName);
        }
        finally
        {
            provider.Dispose();
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void ContextProvider_Reuses_Context_For_Unchanged_File()
    {
        var provider = new SharedInspectionContextProvider(new RuntimeOptions());
        try
        {
            Assembly first;
            Assembly second;
            using (var lease = provider.Acquire(_testAssemblyPath))
                first = lease.Assembly;
            using (var lease = provider.Acquire(_testAssemblyPath))
                second = lease.Assembly;
            Assert.Same(first, second);
        }
        finally
        {
            provider.Dispose();
        }
    }

    [Fact]
    public void ContextProvider_Handles_Concurrent_Acquire()
    {
        var provider = new SharedInspectionContextProvider(new RuntimeOptions { MaxLoadedAssemblies = 2 });
        var paths = new[]
        {
            _testAssemblyPath,
            typeof(RuntimeOptions).Assembly.Location,
            typeof(Sherlock.MCP.Server.Shared.CacheKeyHelper).Assembly.Location
        };
        try
        {
            Parallel.For(0, 32, i =>
            {
                using var lease = provider.Acquire(paths[i % paths.Length]);
                Assert.NotNull(lease.Assembly.FullName);
            });
        }
        finally
        {
            provider.Dispose();
        }
    }

    [Fact]
    public void GetMethodsPage_Matches_Unpaged_Results()
    {
        var service = new MemberAnalysisService();
        var options = new MemberFilterOptions();
        var all = service.GetMethods(_testAssemblyPath, "Sherlock.MCP.Tests.TestSampleClass", options);
        var page = service.GetMethodsPage(_testAssemblyPath, "Sherlock.MCP.Tests.TestSampleClass", new MemberFilterOptions(), offset: 0, pageSize: int.MaxValue);

        Assert.Equal(all.Length, page.Total);
        Assert.Equal(all.Select(m => m.Signature), page.Items.Select(m => m.Signature));
    }

    [Fact]
    public void GetMethodsPage_Total_Is_Stable_Across_Pages()
    {
        var service = new MemberAnalysisService();
        var first = service.GetMethodsPage(_testAssemblyPath, "Sherlock.MCP.Tests.TestSampleClass", new MemberFilterOptions(), offset: 0, pageSize: 2);
        var second = service.GetMethodsPage(_testAssemblyPath, "Sherlock.MCP.Tests.TestSampleClass", new MemberFilterOptions(), offset: 2, pageSize: 2);

        Assert.Equal(first.Total, second.Total);
        Assert.Equal(2, first.Items.Length);
        Assert.Empty(first.Items.Select(m => m.Signature).Intersect(second.Items.Select(m => m.Signature)));
    }

    [Fact]
    public void Members_Sort_By_Access_Is_Supported()
    {
        var service = new MemberAnalysisService();
        var options = new MemberFilterOptions
        {
            IncludeNonPublic = true,
            SortBy = "access"
        };
        var methods = service.GetMethods(_testAssemblyPath, "Sherlock.MCP.Tests.TestSampleClass", options);

        Assert.NotEmpty(methods);
        var modifiers = methods.Select(m => m.AccessModifier).ToArray();
        Assert.Equal(modifiers.OrderBy(m => m), modifiers);
    }
}
