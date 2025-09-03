using System.Reflection;

namespace Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

public record TypeInfo(
    string FullName,
    string Name,
    string? Namespace,
    TypeKind Kind,
    AccessibilityLevel Accessibility,
    bool IsAbstract,
    bool IsSealed,
    bool IsStatic,
    bool IsGeneric,
    bool IsNested,
    string? AssemblyName,
    string? BaseType,
    string[] Interfaces,
    AttributeInfo[] Attributes,
    GenericParameterInfo[] GenericParameters,
    TypeInfo[] NestedTypes
);
