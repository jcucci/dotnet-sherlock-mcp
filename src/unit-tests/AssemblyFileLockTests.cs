using System.Reflection;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Tests;

public class AssemblyFileLockTests : IDisposable
{
    private readonly string _workDirectory = Directory.CreateTempSubdirectory("sherlock-lock-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_workDirectory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Leased_Assemblies_And_Their_Dependencies_Can_Be_Overwritten_And_Deleted()
    {
        var assemblyPath = CopyIn(Assembly.GetExecutingAssembly().Location);
        var dependencyPath = CopyIn(typeof(RuntimeOptions).Assembly.Location);
        using var provider = new SharedInspectionContextProvider(new RuntimeOptions());

        using var contextLease = provider.Acquire(assemblyPath);
        using var metadataLease = provider.AcquireMetadata(assemblyPath);
        var dependency = contextLease.Assembly.GetType(typeof(RuntimeOptionsHolder).FullName!, throwOnError: true)!
            .GetProperty(nameof(RuntimeOptionsHolder.Options))!.PropertyType.Assembly;

        Assert.Equal(dependencyPath, AssemblyLocations.Of(dependency), ignoreCase: true);
        Assert.Equal(assemblyPath, AssemblyLocations.Of(contextLease.Assembly), ignoreCase: true);

        File.Copy(typeof(RuntimeOptions).Assembly.Location, assemblyPath, overwrite: true);
        File.Delete(dependencyPath);

        Assert.False(File.Exists(dependencyPath));
    }

    private string CopyIn(string source)
    {
        var target = Path.Combine(_workDirectory, Path.GetFileName(source));
        File.Copy(source, target);
        return target;
    }

    public sealed class RuntimeOptionsHolder
    {
        public RuntimeOptions Options { get; } = new();
    }
}
