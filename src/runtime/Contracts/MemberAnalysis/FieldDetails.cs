using System.Reflection;
using Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

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
    AttributeInfo[] CustomAttributes,
    string Signature
);
