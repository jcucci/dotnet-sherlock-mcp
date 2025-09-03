using System.Reflection;
using System.Xml.Linq;
using Sherlock.MCP.Runtime.Contracts.XmlDocs;

namespace Sherlock.MCP.Runtime;

public interface IXmlDocService
{
    XmlDocInfo? GetXmlDocsForType(Type type);
    XmlDocInfo? GetXmlDocsForMember(MemberInfo member);
}

public class XmlDocService : IXmlDocService
{
    private readonly Dictionary<string, XDocument?> _xmlCache = new();

    public XmlDocInfo? GetXmlDocsForType(Type type)
    {
        var doc = LoadXml(type.Assembly);
        if (doc == null) return null;
        var id = $"T:{type.FullName}";
        return Extract(doc, id);
    }

    public XmlDocInfo? GetXmlDocsForMember(MemberInfo member)
    {
        var doc = LoadXml(member.Module.Assembly);
        if (doc == null) return null;
        var id = BuildMemberId(member);
        if (id != null)
        {
            var info = Extract(doc, id);
            if (info != null) return info;
        }
        // Fallback for methods without signature resolution: first by name
        if (member is MethodBase mb && mb.DeclaringType?.FullName is string fullName)
        {
            var prefix = $"M:{fullName}.{mb.Name}";
            var e = doc.Descendants("member").FirstOrDefault(x => {
                var n = (string?)x.Attribute("name");
                return n != null && n.StartsWith(prefix, StringComparison.Ordinal);
            });
            if (e != null) return Extract(e);
        }
        return null;
    }

    private XDocument? LoadXml(Assembly assembly)
    {
        var key = assembly.Location;
        if (string.IsNullOrEmpty(key)) return null;
        if (_xmlCache.TryGetValue(key, out var cached)) return cached;
        try
        {
            var xmlPath = Path.ChangeExtension(key, ".xml");
            if (!File.Exists(xmlPath)) { _xmlCache[key] = null; return null; }
            var doc = XDocument.Load(xmlPath);
            _xmlCache[key] = doc;
            return doc;
        }
        catch
        {
            _xmlCache[key] = null;
            return null;
        }
    }

    private static XmlDocInfo? Extract(XDocument doc, string memberId)
    {
        var el = doc.Descendants("member").FirstOrDefault(x => (string?)x.Attribute("name") == memberId);
        return el != null ? Extract(el) : (XmlDocInfo?)null;
    }

    private static XmlDocInfo Extract(XElement el)
    {
        string? GetText(string name) => el.Element(name)?.Value?.Trim();
        var summary = GetText("summary");
        var remarks = GetText("remarks");
        var returns = GetText("returns");
        var @params = el.Elements("param")
            .Select(p => new XmlParamInfo((string?)p.Attribute("name") ?? string.Empty, (p.Value ?? string.Empty).Trim()))
            .ToArray();
        return new XmlDocInfo(summary, remarks, returns, @params);
    }

    private static string? BuildMemberId(MemberInfo member)
    {
        var typeName = member.DeclaringType?.FullName;
        if (typeName == null) return null;
        return member.MemberType switch
        {
            MemberTypes.Method => BuildMethodId((MethodBase)member, typeName),
            MemberTypes.Constructor => BuildMethodId((MethodBase)member, typeName),
            MemberTypes.Property => $"P:{typeName}.{member.Name}",
            MemberTypes.Field => $"F:{typeName}.{member.Name}",
            MemberTypes.Event => $"E:{typeName}.{member.Name}",
            _ => null
        };
    }

    private static string BuildMethodId(MethodBase method, string declaringFullName)
    {
        // Best-effort parameter type list using full names; doesn't encode ref/out/array fully per spec.
        var paramTypes = method.GetParameters()
            .Select(p => NormalizeTypeName(p.ParameterType))
            .ToArray();
        var sig = paramTypes.Length > 0 ? $"({string.Join(",", paramTypes)})" : string.Empty;
        return $"M:{declaringFullName}.{method.Name}{sig}";
    }

    private static string NormalizeTypeName(Type t)
    {
        if (t.IsByRef || t.IsPointer) t = t.GetElementType()!;
        if (t.IsArray) return NormalizeTypeName(t.GetElementType()!) + "[]";
        if (t.IsGenericType)
        {
            var name = t.GetGenericTypeDefinition().FullName!;
            var tick = name.IndexOf('`');
            if (tick > 0) name = name.Substring(0, tick);
            var args = t.GetGenericArguments().Select(NormalizeTypeName);
            return $"{name}<{string.Join(",", args)}>";
        }
        return t.FullName ?? t.Name;
    }
}
