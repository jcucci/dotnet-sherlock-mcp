using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Server.Shared;
using System.ComponentModel;
using System.Reflection;

namespace Sherlock.MCP.Server.Tools;

[McpServerToolType]
public static class XmlDocTools
{
    [McpServerTool]
    [Description("Gets XML documentation for a type from an adjacent .xml file")]
    public static string GetXmlDocsForType(
        IXmlDocService xmlDocs,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name. Prefer full name")] string typeName,
        [Description("Case sensitive matching (default: false)")] bool caseSensitive = false)
    {
        try
        {
            if (!File.Exists(assemblyPath)) return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            var asm = Assembly.LoadFrom(assemblyPath);
            var type = asm.GetType(typeName, false, !caseSensitive)
                    ?? asm.GetTypes().FirstOrDefault(t => string.Equals(t.FullName, typeName, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) || string.Equals(t.Name, typeName, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
            if (type == null) return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");
            var info = xmlDocs.GetXmlDocsForType(type);
            return info == null
                ? JsonHelpers.Error("XmlNotFound", "No XML docs found for type")
                : JsonHelpers.Envelope("xml.type", new { type = type.FullName, docs = info });
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get type XML docs: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Gets XML documentation for a member from an adjacent .xml file")]
    public static string GetXmlDocsForMember(
        IXmlDocService xmlDocs,
        [Description("Path to the .NET assembly file (.dll or .exe)")] string assemblyPath,
        [Description("Type name. Prefer full name")] string typeName,
        [Description("Member name (simple; if overloaded, first match used)")] string memberName,
        [Description("Case sensitive matching (default: false)")] bool caseSensitive = false)
    {
        try
        {
            if (!File.Exists(assemblyPath)) return JsonHelpers.Error("AssemblyNotFound", $"Assembly file not found: {assemblyPath}");
            var asm = Assembly.LoadFrom(assemblyPath);
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var type = asm.GetType(typeName, false, !caseSensitive)
                    ?? asm.GetTypes().FirstOrDefault(t => string.Equals(t.FullName, typeName, comparison) || string.Equals(t.Name, typeName, comparison));
            if (type == null) return JsonHelpers.Error("TypeNotFound", $"Type '{typeName}' not found in assembly");
            var member = (MemberInfo?) type.GetMembers(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)
                .FirstOrDefault(m => string.Equals(m.Name, memberName, comparison));
            if (member == null) return JsonHelpers.Error("MemberNotFound", $"Member '{memberName}' not found");
            var info = xmlDocs.GetXmlDocsForMember(member);
            return info == null
                ? JsonHelpers.Error("XmlNotFound", "No XML docs found for member")
                : JsonHelpers.Envelope("xml.member", new { type = type.FullName, member = member.Name, docs = info });
        }
        catch (Exception ex)
        {
            return JsonHelpers.Error("InternalError", $"Failed to get member XML docs: {ex.Message}");
        }
    }
}

