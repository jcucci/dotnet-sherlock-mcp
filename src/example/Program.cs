using System;
using System.IO;
using System.Linq;

Console.WriteLine("=== Sherlock MCP Assembly Discovery Example ===\n");

// Example: Find an assembly by file name
Console.WriteLine("1. Finding an assembly by file name:");
var assemblyFileName = "Sherlock.MCP.Runtime.dll";
var workingDirectory = AppDomain.CurrentDomain.BaseDirectory;
var searchDirectories = new[] { "bin/Debug", "bin/Release", "bin" };
var assemblyPaths = searchDirectories
    .Select(subDir => Path.Combine(workingDirectory, subDir))
    .Where(Directory.Exists)
    .SelectMany(dir => Directory.GetFiles(dir, assemblyFileName, SearchOption.AllDirectories))
    .ToArray();

if (assemblyPaths.Length > 0)
{
    Console.WriteLine($"Found {assemblyPaths.Length} assemblies:");
    foreach (var path in assemblyPaths)
    {
        Console.WriteLine($"   - {path}");
    }
}
else
{
    Console.WriteLine($"Assembly '{assemblyFileName}' not found in common binary folders.");
}

Console.WriteLine("\n=== Example Complete ===");
