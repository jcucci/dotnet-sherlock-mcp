using System.Collections.Concurrent;
using System.Reflection;
using System.Xml.Linq;
using Sherlock.MCP.Runtime.Contracts.XmlDocs;

namespace Sherlock.MCP.Runtime;

public class XmlDocService : IXmlDocService
{
    private const int MaxCachedDocs = 32;

    private sealed record CachedDoc(long StampTicks, long Length, Dictionary<string, XElement> MembersById);

    private readonly ConcurrentDictionary<string, Lazy<CachedDoc?>> _docs = new(StringComparer.OrdinalIgnoreCase);

    public XmlDocInfo? GetXmlDocsForType(Type type)
    {
        var doc = LoadDoc(type.Assembly);
        if (doc == null) return null;
        var id = $"T:{type.FullName}";
        return doc.MembersById.TryGetValue(id, out var el) ? Extract(el) : null;
    }

    public XmlDocInfo? GetXmlDocsForMember(MemberInfo member)
    {
        var doc = LoadDoc(member.Module.Assembly);
        if (doc == null) return null;
        var id = BuildMemberId(member);
        if (id != null && doc.MembersById.TryGetValue(id, out var el))
            return Extract(el);

        // Fallback for methods without signature resolution: first by name
        if (member is MethodBase mb && mb.DeclaringType?.FullName is string fullName)
        {
            var prefix = $"M:{fullName}.{mb.Name}";
            var match = doc.MembersById
                .Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => p.Value)
                .FirstOrDefault();
            if (match != null) return Extract(match);
        }
        return null;
    }

    private CachedDoc? LoadDoc(Assembly assembly)
    {
        var key = assembly.Location;
        if (string.IsNullOrEmpty(key)) return null;

        var xmlPath = Path.ChangeExtension(key, ".xml");
        var fileInfo = new FileInfo(xmlPath);
        if (!fileInfo.Exists) return null;

        var stampTicks = fileInfo.LastWriteTimeUtc.Ticks;
        var length = fileInfo.Length;

        while (true)
        {
            var lazy = _docs.GetOrAdd(key, _ => new Lazy<CachedDoc?>(
                () => ParseDoc(xmlPath, stampTicks, length),
                LazyThreadSafetyMode.ExecutionAndPublication));

            var cached = lazy.Value;
            if (cached == null)
            {
                _docs.TryRemove(new KeyValuePair<string, Lazy<CachedDoc?>>(key, lazy));
                return null;
            }

            if (cached.StampTicks != stampTicks || cached.Length != length)
            {
                _docs.TryRemove(new KeyValuePair<string, Lazy<CachedDoc?>>(key, lazy));
                continue;
            }

            EvictOverflow();
            return cached;
        }
    }

    private static CachedDoc? ParseDoc(string xmlPath, long stampTicks, long length)
    {
        try
        {
            var doc = XDocument.Load(xmlPath);
            var membersById = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var el in doc.Descendants("member"))
            {
                var name = (string?)el.Attribute("name");
                if (name != null) membersById.TryAdd(name, el);
            }
            return new CachedDoc(stampTicks, length, membersById);
        }
        catch
        {
            return null;
        }
    }

    private void EvictOverflow()
    {
        if (_docs.Count <= MaxCachedDocs) return;
        foreach (var pair in _docs.ToArray())
        {
            if (_docs.Count <= MaxCachedDocs) return;
            _docs.TryRemove(pair);
        }
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
