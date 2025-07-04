using Microsoft.Extensions.Logging;
using Sherlock.MCP.Runtime;
namespace Sherlock.MCP.Tests;
public class AssemblyDiscoveryServiceTests
{
    private readonly AssemblyDiscoveryService _service;
    public AssemblyDiscoveryServiceTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<AssemblyDiscoveryService>();
        _service = new AssemblyDiscoveryService(logger);
    }
    [Fact]
    public void GetCommonAssemblyLocations_ShouldReturnNonEmptyArray()
    {
        var locations = _service.GetCommonAssemblyLocations();
        Assert.NotNull(locations);
        Assert.NotEmpty(locations);
        Assert.All(locations, location => Assert.True(Path.IsPathFullyQualified(location)));
    }
    [Fact]
    public void GetFrameworkAssemblyLocations_ShouldReturnSystemAssemblies()
    {
        var assemblies = _service.GetFrameworkAssemblyLocations();
        Assert.NotNull(assemblies);
        Assert.NotEmpty(assemblies);
        Assert.Contains(assemblies, assembly => 
            Path.GetFileName(assembly).StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(assembly).StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void FindAssembliesInDirectory_WithInvalidDirectory_ShouldReturnEmpty()
    {
        var invalidDirectory = "/non/existent/directory";
        var assemblies = _service.FindAssembliesInDirectory(invalidDirectory);
        Assert.NotNull(assemblies);
        Assert.Empty(assemblies);
    }
    [Fact]
    public void FindAssemblyByTypeName_WithNullOrEmptyTypeName_ShouldReturnEmpty()
    {
        var result1 = _service.FindAssemblyByTypeName(null!);
        var result2 = _service.FindAssemblyByTypeName("");
        var result3 = _service.FindAssemblyByTypeName("   ");
        Assert.Empty(result1);
        Assert.Empty(result2);
        Assert.Empty(result3);
    }

    [Fact]
    public void GetNuGetPackageAssemblies_WithNullOrEmptyPackageName_ShouldReturnEmpty()
    {
        var result1 = _service.GetNuGetPackageAssemblies(null!);
        var result2 = _service.GetNuGetPackageAssemblies("");
        var result3 = _service.GetNuGetPackageAssemblies("   ");
        Assert.Empty(result1);
        Assert.Empty(result2);
        Assert.Empty(result3);
    }
    [Fact]
    public void GetNuGetPackageAssemblies_WithNonExistentPackage_ShouldReturnEmpty()
    {
        var assemblies = _service.GetNuGetPackageAssemblies("NonExistentPackageNameThatShouldNotExist123");
        Assert.NotNull(assemblies);
        Assert.Empty(assemblies);
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindAssembliesInDirectory_WithInvalidInput_ShouldReturnEmpty(string directory)
    {
        var assemblies = _service.FindAssembliesInDirectory(directory);
        Assert.NotNull(assemblies);
        Assert.Empty(assemblies);
    }
    [Fact]
    public void FindAssembliesInDirectory_WithNullInput_ShouldReturnEmpty()
    {
        var assemblies = _service.FindAssembliesInDirectory(null!);
        Assert.NotNull(assemblies);
        Assert.Empty(assemblies);
    }
    [Fact]
    public void AssemblyDiscoveryService_ShouldHandleNullLogger()
    {
        var serviceWithoutLogger = new AssemblyDiscoveryService(logger: null);
        Assert.NotNull(serviceWithoutLogger);
    }
}