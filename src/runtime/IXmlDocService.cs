using System.Reflection;

using Sherlock.MCP.Runtime.Contracts.XmlDocs;

namespace Sherlock.MCP.Runtime;

public interface IXmlDocService
{
    public XmlDocInfo? GetXmlDocsForType(Type type);
    public XmlDocInfo? GetXmlDocsForMember(MemberInfo member);
    public XmlDocInfo? GetXmlDocsById(string assemblyPath, string memberId);
}

