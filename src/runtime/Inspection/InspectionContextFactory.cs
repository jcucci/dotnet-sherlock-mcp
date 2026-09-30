namespace Sherlock.MCP.Runtime.Inspection;

public static class InspectionContextFactory
{
    public static IAssemblyInspectionContext Create(string assemblyPath, IReadOnlyList<string>? additionalSearchDirectories = null) =>
        new MetadataOnlyInspectionContext(assemblyPath, additionalSearchDirectories);
}
