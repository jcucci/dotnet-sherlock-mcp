namespace Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

public record DerivedTypeRef(
    string TypeFullName,
    string AssemblyPath,
    string Kind
);

public record TypeHierarchy(
    string TypeName,
    string[] InheritanceChain,
    string[] AllInterfaces,
    TypeInfo[] BaseTypes,
    DerivedTypeRef[]? DerivedTypes,
    string? Note
);
