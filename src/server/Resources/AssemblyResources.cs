using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;

namespace Sherlock.MCP.Server.Resources;

[McpServerResourceType]
public static class AssemblyResources
{
    [McpServerResource(UriTemplate = ResourceUris.TypeTemplate, Name = "type_info", Title = "Type Info", MimeType = ResourceUris.JsonMimeType)]
    [Description("Metadata for a single type (accessibility, inheritance, interfaces, member counts) - the same payload as get_type_info. path is the percent-encoded absolute assembly path; fullName is the percent-encoded type name.")]
    public static string ReadType(ITypeAnalysisService typeAnalysis, string path, string fullName)
    {
        EnsureAssemblyExists(path);
        var info = LoadTypeInfo(typeAnalysis, path, fullName)
            ?? throw new McpProtocolException($"Type '{fullName}' not found in assembly '{path}'", McpErrorCode.InvalidParams);
        return JsonHelpers.Envelope("type.info", info);
    }

    [McpServerResource(UriTemplate = ResourceUris.DocsTemplate, Name = "xml_docs", Title = "XML Docs", MimeType = ResourceUris.JsonMimeType)]
    [Description("XML documentation for a documentation id (e.g. 'T:Ns.Type', 'M:Ns.Type.Method(System.String)') read from the .xml file next to the assembly. path is the percent-encoded absolute assembly path; memberId is the percent-encoded documentation id.")]
    public static string ReadDocs(IXmlDocService xmlDocs, string path, string memberId)
    {
        EnsureAssemblyExists(path);
        var docs = xmlDocs.GetXmlDocsById(path, memberId)
            ?? throw new McpProtocolException($"No XML docs found for '{memberId}' in assembly '{path}'", McpErrorCode.InvalidParams);
        return JsonHelpers.Envelope("xml.member", new { id = memberId, docs });
    }

    private static Runtime.Contracts.TypeAnalysis.TypeInfo? LoadTypeInfo(ITypeAnalysisService typeAnalysis, string path, string fullName)
    {
        try
        {
            return typeAnalysis.GetTypeInfo(path, fullName);
        }
        catch (Exception ex)
        {
            throw new McpProtocolException(
                $"Failed to load type '{fullName}' from '{path}': {ex.Message} If the assembly's dependencies are not next to it or in the NuGet cache, use get_type_info with an assembly path from a build-output folder.",
                McpErrorCode.InternalError);
        }
    }

    private static void EnsureAssemblyExists(string path)
    {
        if (!File.Exists(path))
            throw new McpProtocolException($"Assembly file not found: {path}", McpErrorCode.InvalidParams);
    }
}
