using System.Reflection;
using Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

namespace Sherlock.MCP.Runtime.Contracts.MemberAnalysis;

public record ConstructorDetails(
    ParameterDetails[] Parameters,
    MethodAttributes Attributes,
    string AccessModifier,
    bool IsStatic,
    AttributeInfo[] CustomAttributes,
    string Signature
);
