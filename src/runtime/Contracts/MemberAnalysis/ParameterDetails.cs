using System.Reflection;
namespace Sherlock.MCP.Runtime.Contracts.MemberAnalysis;
public record ParameterDetails(
    string Name,
    string TypeName,
    string? DefaultValue,
    bool IsOptional,
    bool IsOut,
    bool IsRef,
    bool IsIn,
    bool IsParams,
    ParameterAttributes Attributes
);