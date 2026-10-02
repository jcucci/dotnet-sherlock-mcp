namespace Sherlock.MCP.Runtime.ApiDiff;

internal static class ApiVisibility
{
    public const int None = 0;
    public const int Protected = 1;
    public const int Public = 2;

    public static string Name(int visibility) => visibility == Public ? "public" : "protected";
}

internal sealed record ApiTypeSurface(
    string Key,
    string DisplayName,
    string Kind,
    int Visibility,
    bool IsSealed,
    bool IsAbstract,
    bool IsStatic,
    bool IsExtensible,
    IReadOnlyList<string>? BaseTypes,
    IReadOnlyList<string>? Interfaces,
    IReadOnlyDictionary<string, string> Constraints,
    string Signature,
    IReadOnlyDictionary<string, ApiMemberSurface>? Members,
    IReadOnlyDictionary<string, ApiInheritedMember>? InheritedMembers);

internal sealed record ApiInheritedMember(int Visibility, string? ValueType, bool IsStatic, int GetterVisibility, int SetterVisibility);

internal sealed record ApiMemberSurface(
    string Identity,
    string Kind,
    string Name,
    string Signature,
    int Visibility,
    string? ValueType,
    string? ValueTypeDisplay,
    bool IsStatic,
    bool IsVirtual,
    bool IsAbstract,
    bool IsReadOnly,
    bool IsConst,
    string? ConstantValue,
    int GetterVisibility,
    int SetterVisibility,
    bool IsInitOnly,
    IReadOnlyList<ApiParameter> Parameters);

internal sealed record ApiParameter(string Name, string Modifier, bool IsParams, bool IsOptional, string? DefaultValue);
