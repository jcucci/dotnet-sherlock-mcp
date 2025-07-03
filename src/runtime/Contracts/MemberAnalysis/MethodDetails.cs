using System.Reflection;

namespace Sherlock.MCP.Runtime.Contracts.MemberAnalysis;

public record MethodDetails(
    string Name,
    string ReturnTypeName,
    ParameterDetails[] Parameters,
    string[] GenericTypeParameters,
    MethodAttributes Attributes,
    string AccessModifier,
    bool IsStatic,
    bool IsVirtual,
    bool IsAbstract,
    bool IsSealed,
    bool IsOverride,
    bool IsOperator,
    bool IsExtensionMethod,
    string Signature
);