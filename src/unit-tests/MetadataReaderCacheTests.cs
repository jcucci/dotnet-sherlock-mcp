using System.Reflection;
using System.Reflection.Metadata;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.Il;
using Sherlock.MCP.Runtime.Contracts.ReverseLookup;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Tests;

public class MetadataReaderCacheTests
{
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private readonly string _runtimeAssemblyPath = typeof(RuntimeOptions).Assembly.Location;

    [Fact]
    public void AcquireMetadata_Reuses_Reader_For_Unchanged_File()
    {
        using var provider = new SharedInspectionContextProvider(new RuntimeOptions());

        MetadataReader first;
        MetadataReader second;
        using (var lease = provider.AcquireMetadata(_testAssemblyPath))
            first = lease.Reader;
        using (var lease = provider.AcquireMetadata(_testAssemblyPath))
            second = lease.Reader;

        Assert.Same(first, second);
    }

    [Fact]
    public void AcquireMetadata_Returns_Fresh_Reader_After_File_Change()
    {
        using var provider = new SharedInspectionContextProvider(new RuntimeOptions());
        var tempPath = Path.Combine(Path.GetTempPath(), $"sherlock-md-fresh-{Guid.NewGuid():N}.dll");
        try
        {
            File.Copy(_testAssemblyPath, tempPath);
            string firstName;
            using (var lease = provider.AcquireMetadata(tempPath))
                firstName = AssemblyName(lease.Reader);

            File.Delete(tempPath);
            File.Copy(_runtimeAssemblyPath, tempPath);
            File.SetLastWriteTimeUtc(tempPath, DateTime.UtcNow.AddSeconds(5));
            string secondName;
            using (var lease = provider.AcquireMetadata(tempPath))
                secondName = AssemblyName(lease.Reader);

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
    public void AcquireMetadata_Evicts_Idle_Readers_But_Keeps_Leased_Ones_Usable()
    {
        using var provider = new SharedInspectionContextProvider(new RuntimeOptions { MaxLoadedAssemblies = 1 });

        var held = provider.AcquireMetadata(_testAssemblyPath);
        var heldReader = held.Reader;
        using (provider.AcquireMetadata(_runtimeAssemblyPath)) { }

        Assert.Equal(AssemblyName(heldReader), Assembly.GetExecutingAssembly().GetName().Name);
        held.Dispose();

        using (provider.AcquireMetadata(_runtimeAssemblyPath)) { }
        using var reacquired = provider.AcquireMetadata(_testAssemblyPath);

        Assert.NotSame(heldReader, reacquired.Reader);
    }

    [Fact]
    public void AcquireMetadata_Lease_Survives_Provider_Dispose_Until_Released()
    {
        var provider = new SharedInspectionContextProvider(new RuntimeOptions());
        using var lease = provider.AcquireMetadata(_testAssemblyPath);

        provider.Dispose();

        Assert.Equal(Assembly.GetExecutingAssembly().GetName().Name, AssemblyName(lease.Reader));
        Assert.NotEmpty(lease.Reader.TypeDefinitions);
    }

    [Fact]
    public void AcquireMetadata_Missing_File_Throws_FileNotFound()
    {
        using var provider = new SharedInspectionContextProvider(new RuntimeOptions());

        Assert.Throws<FileNotFoundException>(() =>
            provider.AcquireMetadata(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll")));
    }

    [Fact]
    public void AcquireMetadata_Handles_Concurrent_Acquire()
    {
        using var provider = new SharedInspectionContextProvider(new RuntimeOptions { MaxLoadedAssemblies = 2 });
        var paths = new[]
        {
            _testAssemblyPath,
            _runtimeAssemblyPath,
            typeof(Sherlock.MCP.Server.Shared.CacheKeyHelper).Assembly.Location
        };

        Parallel.For(0, 32, i =>
        {
            using var lease = provider.AcquireMetadata(paths[i % paths.Length]);
            Assert.NotEmpty(AssemblyName(lease.Reader));
        });
    }

    [Fact]
    public void FindInboundCallers_Concurrent_Calls_Match_Serial_Result()
    {
        IIlAnalysisService service = new IlAnalysisService();
        var paths = new[] { _testAssemblyPath, _runtimeAssemblyPath };
        var expected = Describe(service.FindInboundCallers(paths, "Console", new ReverseLookupOptions()));

        var results = new string[8];
        Parallel.For(0, results.Length, i =>
            results[i] = Describe(service.FindInboundCallers(paths, "Console", new ReverseLookupOptions())));

        Assert.NotEmpty(expected);
        Assert.All(results, r => Assert.Equal(expected, r));
    }

    private static string AssemblyName(MetadataReader reader) =>
        reader.GetString(reader.GetAssemblyDefinition().Name);

    private static string Describe(InboundCallHit[] hits) =>
        string.Join("\n", hits.Select(h => $"{h.AssemblyPath}|{h.CallerTypeFullName}|{h.CallerMethod}|{h.ReferenceKind}|{h.TargetMember}"));
}
