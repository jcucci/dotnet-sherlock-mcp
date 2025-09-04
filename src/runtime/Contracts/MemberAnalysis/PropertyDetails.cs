using System.Reflection;
using Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

namespace Sherlock.MCP.Runtime.Contracts.MemberAnalysis;

public record PropertyDetails(
    string Name,
    string TypeName,
    PropertyAttributes Attributes,
    string AccessModifier,
    bool IsStatic,
    bool IsVirtual,
    bool IsAbstract,
    bool IsSealed,
    bool IsOverride,
    bool CanRead,
    bool CanWrite,
    bool IsIndexer,
    ParameterDetails[] IndexerParameters,
    string? GetterAccessModifier,
    string? SetterAccessModifier,
    AttributeInfo[] CustomAttributes,
    string Signature
);
