using System.Reflection;
using Sherlock.MCP.Runtime.Contracts.XmlDocs;

namespace Sherlock.MCP.Runtime;

public interface IXmlDocService
{
    XmlDocInfo? GetXmlDocsForType(Type type);
    XmlDocInfo? GetXmlDocsForMember(MemberInfo member);
}

