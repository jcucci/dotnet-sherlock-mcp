namespace Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

public enum TypeKind
{
    Class,
    Interface,
    Enum,
    Struct,
    Delegate,
    Array,
    Pointer,
    ByRef,
    GenericParameter,
    Unknown
}