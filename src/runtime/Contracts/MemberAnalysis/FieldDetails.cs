using System.Reflection;

namespace Sherlock.MCP.Runtime.Contracts.MemberAnalysis;

public record FieldDetails(
    string Name,
    string TypeName,
    FieldAttributes Attributes,
    string AccessModifier,
    bool IsStatic,
    bool IsReadOnly,
    bool IsConst,
    bool IsVolatile,
    bool IsInitOnly,
    object? ConstantValue,
    string Signature
);