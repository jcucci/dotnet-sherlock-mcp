using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Reflection;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class XmlDocTools
{
    [McpServerTool(Title = "Get XML Docs for Type", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets XML documentation (summary, remarks, examples) for a type from adjacent .xml file. Requires XML docs to be generated during build. Lightweight response.")]
    public static string GetXmlDocsForType(
        IXmlDocService xmlDocs,
        IInspectionContextProvider contexts,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name. Prefer full name")] string typeName,
        [Description("Case sensitive matching (default: false)")] bool caseSensitive = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath)) return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            using var lease = contexts.Acquire(assemblyPath);
            var asm = lease.Assembly;
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var type = TypeNameResolver.Resolve(asm, typeName, comparison).OrThrowIfAmbiguous(typeName);
            if (type == null) return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");
            var info = xmlDocs.GetXmlDocsForType(type);
            return info == null
                ? JsonHelpers.Error("XmlNotFound", "No XML docs found for type")
                : JsonHelpers.Envelope("xml.type", new { type = type.FullName, docs = info });
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get type XML docs: {ex.Message}");
        }
    }

    [McpServerTool(Title = "Get XML Docs for Member", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Gets XML documentation (summary, params, returns, exceptions) for a member from adjacent .xml file. Requires XML docs to be generated during build. Lightweight response.")]
    public static string GetXmlDocsForMember(
        IXmlDocService xmlDocs,
        IInspectionContextProvider contexts,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name. Prefer full name")] string typeName,
        [Description("Member name (simple; if overloaded, first match used)")] string memberName,
        [Description("Case sensitive matching (default: false)")] bool caseSensitive = false,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var elicitation = ElicitationContext.From(context);
        typeName = Elicitation.ApplyTypeChoice(elicitation, typeName);
        try
        {
            if (!File.Exists(assemblyPath)) return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            using var lease = contexts.Acquire(assemblyPath);
            var asm = lease.Assembly;
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var type = TypeNameResolver.Resolve(asm, typeName, comparison).OrThrowIfAmbiguous(typeName);
            if (type == null) return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");
            var member = (MemberInfo?) type.GetMembers(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)
                .FirstOrDefault(m => string.Equals(m.Name, memberName, comparison));
            if (member == null) return JsonHelpers.Error("MemberNotFound", $"Member '{memberName}' not found");
            var info = xmlDocs.GetXmlDocsForMember(member);
            return info == null
                ? JsonHelpers.Error("XmlNotFound", "No XML docs found for member")
                : JsonHelpers.Envelope("xml.member", new { type = type.FullName, member = member.Name, docs = info });
        }
        catch (AmbiguousTypeNameException ex)
        {
            return Elicitation.AmbiguousType(elicitation, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get member XML docs: {ex.Message}");
        }
    }
}

