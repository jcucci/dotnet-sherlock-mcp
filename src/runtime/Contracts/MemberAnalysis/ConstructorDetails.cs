using System.Reflection;

namespace Sherlock.MCP.Runtime.Contracts.MemberAnalysis;

public record ConstructorDetails(
    ParameterDetails[] Parameters,
    MethodAttributes Attributes,
    string AccessModifier,
    bool IsStatic,
    string Signature
);