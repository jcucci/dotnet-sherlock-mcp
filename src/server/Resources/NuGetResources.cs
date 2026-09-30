using ModelContextProtocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.ProjectAnalysis;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;

namespace Sherlock.MCP.Server.Resources;

[McpServerResourceType]
public static class NuGetResources
{
    [McpServerResource(UriTemplate = ResourceUris.NuGetTemplate, Name = "nuget_package", Title = "NuGet Package Assembly", MimeType = ResourceUris.JsonMimeType)]
    [Description("Resolves a package version in the local NuGet cache to its primary assembly path - the same payload as find_assembly_by_nuget_package. packageId and version support completion from the cache contents.")]
    public static async Task<string> ReadPackage(IProjectAnalysisService projectAnalysis, string packageId, string version)
    {
        NugetAssemblyLookup lookup;
        try
        {
            lookup = await projectAnalysis.FindAssemblyInNugetCacheAsync(packageId, version);
        }
        catch (ArgumentException ex)
        {
            throw new McpProtocolException(ex.Message, McpErrorCode.InvalidParams);
        }

        if (lookup.Failure is NugetLookupFailure failure)
            throw new McpProtocolException(NuGetLookupResponse.FailureMessage(lookup, failure), McpErrorCode.InvalidParams);

        return JsonHelpers.Envelope(NuGetLookupResponse.Kind, NuGetLookupResponse.Success(lookup));
    }
}
