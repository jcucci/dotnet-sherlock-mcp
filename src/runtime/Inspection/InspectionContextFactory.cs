namespace Sherlock.MCP.Runtime.Inspection;

public static class InspectionContextFactory
{
    public static IAssemblyInspectionContext Create(string assemblyPath, bool forceRuntimeLoad = false)
    {
        if (forceRuntimeLoad)
        {
            return new IsolatedRuntimeInspectionContext(assemblyPath);
        }

        return new MetadataOnlyInspectionContext(assemblyPath);
    }
}

