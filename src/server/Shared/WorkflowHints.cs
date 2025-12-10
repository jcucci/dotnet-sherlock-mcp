namespace Sherlock.MCP.Server.Shared;

public static class WorkflowHints
{
    public static object? ForTypeInfo(
        int methodCount,
        int propertyCount,
        int fieldCount,
        bool hasBaseType,
        bool hasInterfaces,
        int totalMembers)
    {
        var nextSteps = new List<object>();
        var warnings = new List<string>();

        if (methodCount > 50)
        {
            nextSteps.Add(new { tool = "GetTypeMethods", reason = $"Type has {methodCount} methods - use nameContains filter" });
            warnings.Add("Large type - use specific member tools with filtering for efficiency");
        }
        else if (methodCount > 0)
            nextSteps.Add(new { tool = "GetTypeMethods", reason = $"Explore {methodCount} methods" });

        if (propertyCount > 30)
            nextSteps.Add(new { tool = "GetTypeProperties", reason = $"Type has {propertyCount} properties" });

        if (hasBaseType)
            nextSteps.Add(new { tool = "GetTypeHierarchy", reason = "View inheritance chain and base class members" });

        if (hasInterfaces)
            nextSteps.Add(new { tool = "GetTypeHierarchy", reason = "View implemented interfaces" });

        if (totalMembers > 100)
            warnings.Add($"Type has {totalMembers} total members - avoid GetAllTypeMembers, use targeted tools");

        return nextSteps.Count > 0 || warnings.Count > 0
            ? new { nextSteps, warnings }
            : null;
    }

    public static object? ForAssemblyAnalysis(int typeCount)
    {
        var nextSteps = new List<object>();
        var warnings = new List<string>();

        if (typeCount > 100)
        {
            warnings.Add($"Large assembly with {typeCount} types - use pagination (maxItems=25)");
            nextSteps.Add(new { tool = "GetTypesFromAssembly", reason = "Use for filtered/paginated type listing" });
        }

        nextSteps.Add(new { tool = "GetTypeInfo", reason = "Get detailed info for a specific type" });

        return nextSteps.Count > 0 || warnings.Count > 0
            ? new { nextSteps, warnings }
            : null;
    }

    public static object? ForMemberList(string memberType, int total, int returned)
    {
        var nextSteps = new List<object>();
        var warnings = new List<string>();

        if (total > returned)
        {
            warnings.Add($"Showing {returned} of {total} {memberType} - use pagination to see more");
            nextSteps.Add(new { tool = $"GetType{memberType}", reason = "Use continuationToken or skip for next page" });
        }

        if (memberType == "Methods" && total > 0)
            nextSteps.Add(new { tool = "AnalyzeMethod", reason = "Get detailed info for specific method including overloads" });

        if (memberType == "Methods" || memberType == "Properties")
            nextSteps.Add(new { tool = "GetXmlDocsForMember", reason = "Get XML documentation for a member" });

        return nextSteps.Count > 0 || warnings.Count > 0
            ? new { nextSteps, warnings }
            : null;
    }
}
