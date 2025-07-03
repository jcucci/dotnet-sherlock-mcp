namespace Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

public record TypeHierarchy(
    string TypeName,
    string[] InheritanceChain,
    string[] AllInterfaces,
    TypeInfo[] BaseTypes,
    TypeInfo[] DerivedTypes
);