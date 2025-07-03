using Microsoft.Extensions.Logging;
using Sherlock.MCP.Runtime;

// Create a logger for demonstration
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger<AssemblyDiscoveryService>();

// Create the assembly discovery service
var assemblyDiscovery = new AssemblyDiscoveryService(logger);

Console.WriteLine("=== Sherlock MCP Assembly Discovery Example ===\n");

// Example 1: Get common assembly locations
Console.WriteLine("1. Common Assembly Locations:");
var commonLocations = assemblyDiscovery.GetCommonAssemblyLocations();
foreach (var location in commonLocations.Take(5)) // Show first 5
{
    Console.WriteLine($"   - {location}");
}
Console.WriteLine($"   ... and {commonLocations.Length - 5} more locations\n");

// Example 2: Get framework assemblies
Console.WriteLine("2. Framework Assembly Locations:");
var frameworkAssemblies = assemblyDiscovery.GetFrameworkAssemblyLocations();
foreach (var assembly in frameworkAssemblies.Take(10)) // Show first 10
{
    Console.WriteLine($"   - {Path.GetFileName(assembly)}");
}
Console.WriteLine($"   ... and {frameworkAssemblies.Length - 10} more framework assemblies\n");

// Example 3: Find assemblies in a specific directory
Console.WriteLine("3. Finding Assemblies in Current Directory:");
var currentDir = AppDomain.CurrentDomain.BaseDirectory;
var assembliesInDir = assemblyDiscovery.FindAssembliesInDirectory(currentDir, recursive: false);
if (assembliesInDir.Length > 0)
{
    foreach (var assembly in assembliesInDir)
    {
        Console.WriteLine($"   - {Path.GetFileName(assembly)}");
    }
}
else
{
    Console.WriteLine("   No managed assemblies found in current directory");
}
Console.WriteLine();

// Example 4: Search for assemblies containing a specific type
Console.WriteLine("4. Finding Assemblies Containing 'String' Type:");
var assembliesWithString = await assemblyDiscovery.FindAssemblyByTypeNameAsync("String");
foreach (var assembly in assembliesWithString.Take(5)) // Show first 5
{
    Console.WriteLine($"   - {Path.GetFileName(assembly)}");
}
if (assembliesWithString.Length > 5)
{
    Console.WriteLine($"   ... and {assembliesWithString.Length - 5} more assemblies");
}
Console.WriteLine();

// Example 5: Search for NuGet package assemblies
Console.WriteLine("5. Searching for 'Microsoft.Extensions.Logging' NuGet Package Assemblies:");
var nugetAssemblies = assemblyDiscovery.GetNuGetPackageAssemblies("Microsoft.Extensions.Logging");
if (nugetAssemblies.Length > 0)
{
    foreach (var assembly in nugetAssemblies.Take(5)) // Show first 5
    {
        Console.WriteLine($"   - {Path.GetFileName(assembly)}");
    }
    if (nugetAssemblies.Length > 5)
    {
        Console.WriteLine($"   ... and {nugetAssemblies.Length - 5} more package assemblies");
    }
}
else
{
    Console.WriteLine("   No assemblies found for this package");
}

Console.WriteLine("\n=== Example Complete ===");