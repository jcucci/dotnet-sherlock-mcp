using System.Reflection;
using Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

namespace Sherlock.MCP.Runtime.Contracts.MemberAnalysis;

public record EventDetails(
    string Name,
    string EventHandlerTypeName,
    EventAttributes Attributes,
    string AccessModifier,
    bool IsStatic,
    bool IsVirtual,
    bool IsAbstract,
    bool IsSealed,
    bool IsOverride,
    string? AddMethodAccessModifier,
    string? RemoveMethodAccessModifier,
    AttributeInfo[] CustomAttributes,
    string Signature
);
