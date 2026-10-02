namespace Sherlock.MCP.Runtime.ApiDiff;

internal static class ApiSurfaceComparer
{
    public static IReadOnlyList<ApiChange> Compare(
        IReadOnlyDictionary<string, ApiTypeSurface> left,
        IReadOnlyDictionary<string, ApiTypeSurface> right,
        CancellationToken cancellationToken)
    {
        var changes = new List<ApiChange>();
        foreach (var (key, leftType) in left)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!right.TryGetValue(key, out var rightType))
            {
                changes.Add(TypeChange(ApiChangeKind.Removed, leftType, leftType.Signature, null, [new("type removed or no longer visible", Breaking: true)]));
                continue;
            }
            var reasons = TypeReasons(leftType, rightType);
            if (reasons.Count > 0)
                changes.Add(TypeChange(ApiChangeKind.Changed, rightType, leftType.Signature, rightType.Signature, reasons));
            if (leftType.Members != null && rightType.Members != null)
                CompareMembers(leftType, rightType, changes, cancellationToken);
        }

        foreach (var (key, rightType) in right)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!left.ContainsKey(key))
                changes.Add(TypeChange(ApiChangeKind.Added, rightType, null, rightType.Signature, []));
        }

        return changes
            .OrderBy(change => change.TypeName, StringComparer.Ordinal)
            .ThenBy(change => change.Scope)
            .ThenBy(change => change.MemberKind, StringComparer.Ordinal)
            .ThenBy(change => change.MemberName, StringComparer.Ordinal)
            .ThenBy(change => change.RightSignature ?? change.LeftSignature, StringComparer.Ordinal)
            .ToArray();
    }

    private static ApiChange TypeChange(
        ApiChangeKind kind, ApiTypeSurface type, string? leftSignature, string? rightSignature, IReadOnlyList<ApiChangeReason> reasons) =>
        new(ApiChangeScope.Type, kind, reasons.Any(r => r.Breaking), type.DisplayName, null, null, leftSignature, rightSignature, reasons);

    private static ApiChange MemberChange(
        ApiChangeKind kind, ApiTypeSurface type, ApiMemberSurface member, string? leftSignature, string? rightSignature,
        IReadOnlyList<ApiChangeReason> reasons) =>
        new(ApiChangeScope.Member, kind, reasons.Any(r => r.Breaking), type.DisplayName, member.Kind, member.Name, leftSignature, rightSignature, reasons);

    private static List<ApiChangeReason> TypeReasons(ApiTypeSurface left, ApiTypeSurface right)
    {
        var reasons = new List<ApiChangeReason>();
        if (left.Kind != right.Kind)
            reasons.Add(new($"kind changed from {left.Kind} to {right.Kind}", Breaking: true));
        AddVisibilityReason(reasons, left.Visibility, right.Visibility);

        if (left.Kind == "class" && right.Kind == "class")
            AddClassModifierReasons(reasons, left, right);

        if (left.BaseTypes != null && right.BaseTypes != null)
        {
            reasons.AddRange(left.BaseTypes.Except(right.BaseTypes).Select(b => new ApiChangeReason($"no longer derives from {b}", Breaking: true)));
            reasons.AddRange(right.BaseTypes.Except(left.BaseTypes).Select(b => new ApiChangeReason($"now derives from {b}", Breaking: false)));
        }
        if (left.Interfaces != null && right.Interfaces != null)
        {
            reasons.AddRange(left.Interfaces.Except(right.Interfaces).Select(i => new ApiChangeReason($"no longer implements {i}", Breaking: true)));
            reasons.AddRange(right.Interfaces.Except(left.Interfaces).Select(i => new ApiChangeReason($"now implements {i}", Breaking: false)));
        }
        reasons.AddRange(right.Constraints.Where(c => !left.Constraints.ContainsKey(c.Key))
            .Select(c => new ApiChangeReason($"generic constraint added: {c.Value}", Breaking: true)));
        reasons.AddRange(left.Constraints.Where(c => !right.Constraints.ContainsKey(c.Key))
            .Select(c => new ApiChangeReason($"generic constraint removed: {c.Value}", Breaking: false)));
        return reasons;
    }

    private static void AddClassModifierReasons(List<ApiChangeReason> reasons, ApiTypeSurface left, ApiTypeSurface right)
    {
        if (left.IsStatic != right.IsStatic)
        {
            reasons.Add(right.IsStatic ? new("became static", Breaking: true) : new("no longer static", Breaking: false));
            return;
        }
        if (left.IsStatic) return;
        if (left.IsSealed != right.IsSealed)
            reasons.Add(right.IsSealed ? new("became sealed", Breaking: true) : new("no longer sealed", Breaking: false));
        if (left.IsAbstract != right.IsAbstract)
            reasons.Add(right.IsAbstract ? new("became abstract", Breaking: true) : new("no longer abstract", Breaking: false));
    }

    private static void AddVisibilityReason(List<ApiChangeReason> reasons, int left, int right)
    {
        if (right < left)
            reasons.Add(new($"accessibility reduced from {ApiVisibility.Name(left)} to {ApiVisibility.Name(right)}", Breaking: true));
        else if (right > left)
            reasons.Add(new($"accessibility increased from {ApiVisibility.Name(left)} to {ApiVisibility.Name(right)}", Breaking: false));
    }

    private static void CompareMembers(
        ApiTypeSurface leftType, ApiTypeSurface rightType, List<ApiChange> changes, CancellationToken cancellationToken)
    {
        foreach (var (identity, left) in leftType.Members!)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!rightType.Members!.TryGetValue(identity, out var right))
            {
                changes.Add(MemberChange(ApiChangeKind.Removed, rightType, left, left.Signature, null, [RemovedMemberReason(rightType, left)]));
                continue;
            }
            var reasons = MemberReasons(rightType, left, right);
            if (reasons.Count > 0)
                changes.Add(MemberChange(ApiChangeKind.Changed, rightType, right, left.Signature, right.Signature, reasons));
        }

        foreach (var (identity, right) in rightType.Members!)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!leftType.Members.ContainsKey(identity))
                changes.Add(MemberChange(ApiChangeKind.Added, rightType, right, null, right.Signature, AddedMemberReasons(rightType, right)));
        }
    }

    private static ApiChangeReason RemovedMemberReason(ApiTypeSurface type, ApiMemberSurface member) =>
        type.InheritedMembers?.GetValueOrDefault(member.Identity) is { } inherited && IsCompatibleReplacement(inherited, member)
            ? new("no longer declared here but still inherited from a base type; callers are unaffected", Breaking: false)
            : new("member removed or no longer visible", Breaking: true);

    private static bool IsCompatibleReplacement(ApiInheritedMember inherited, ApiMemberSurface member) =>
        inherited.Visibility >= member.Visibility
        && inherited.ValueType == member.ValueType
        && inherited.IsStatic == member.IsStatic
        && inherited.GetterVisibility >= member.GetterVisibility
        && inherited.SetterVisibility >= member.SetterVisibility;

    private static IReadOnlyList<ApiChangeReason> AddedMemberReasons(ApiTypeSurface type, ApiMemberSurface member)
    {
        if (!member.IsAbstract) return [];
        if (type.Kind == "interface") return [new("abstract member added to an interface; implementers must add it", Breaking: true)];
        if (type.IsExtensible) return [new("abstract member added to an inheritable class; derived classes must override it", Breaking: true)];
        return [];
    }

    private static List<ApiChangeReason> MemberReasons(ApiTypeSurface type, ApiMemberSurface left, ApiMemberSurface right)
    {
        var reasons = new List<ApiChangeReason>();
        if (left.Kind != "property")
            AddVisibilityReason(reasons, left.Visibility, right.Visibility);
        if (left.ValueType != right.ValueType)
            reasons.Add(new($"{ValueTypeLabel(left.Kind)} changed from {left.ValueTypeDisplay} to {right.ValueTypeDisplay}", Breaking: true));
        if (left.IsStatic != right.IsStatic)
            reasons.Add(new(right.IsStatic ? "became static" : "became an instance member", Breaking: true));
        AddVirtualityReasons(reasons, type, left, right);
        AddFieldReasons(reasons, type, left, right);
        AddAccessorReasons(reasons, left, right);
        AddParameterReasons(reasons, left, right);
        return reasons;
    }

    private static string ValueTypeLabel(string kind) => kind switch
    {
        "method" => "return type",
        "property" => "property type",
        "field" => "field type",
        "event" => "event handler type",
        _ => "type"
    };

    private static void AddVirtualityReasons(List<ApiChangeReason> reasons, ApiTypeSurface type, ApiMemberSurface left, ApiMemberSurface right)
    {
        if (type.Kind == "interface")
        {
            if (!left.IsAbstract && right.IsAbstract)
                reasons.Add(new("default implementation removed; implementers must provide it", Breaking: true));
            return;
        }
        if (!left.IsAbstract && right.IsAbstract)
            reasons.Add(new("became abstract", Breaking: true));
        else if (left.IsAbstract && !right.IsAbstract)
            reasons.Add(new("no longer abstract", Breaking: false));
        else if (left.IsVirtual && !right.IsVirtual)
            reasons.Add(new("no longer virtual; overrides break", Breaking: true));
        else if (!left.IsVirtual && right.IsVirtual)
            reasons.Add(new("became virtual", Breaking: false));
    }

    private static void AddFieldReasons(List<ApiChangeReason> reasons, ApiTypeSurface type, ApiMemberSurface left, ApiMemberSurface right)
    {
        if (left.Kind != "field") return;
        if (left.IsConst != right.IsConst)
        {
            reasons.Add(new(right.IsConst ? "became const" : "no longer const", Breaking: true));
            return;
        }
        if (!left.IsReadOnly && right.IsReadOnly)
            reasons.Add(new("became readonly", Breaking: true));
        else if (left.IsReadOnly && !right.IsReadOnly)
            reasons.Add(new("no longer readonly", Breaking: false));
        if (left.IsConst && left.ConstantValue != right.ConstantValue)
            reasons.Add(type.Kind == "enum"
                ? new($"enum value changed from {left.ConstantValue} to {right.ConstantValue}", Breaking: true)
                : new($"constant value changed from {left.ConstantValue} to {right.ConstantValue}; compiled callers keep the old value", Breaking: false));
    }

    private static void AddAccessorReasons(List<ApiChangeReason> reasons, ApiMemberSurface left, ApiMemberSurface right)
    {
        if (left.Kind != "property") return;
        AddAccessorReason(reasons, "getter", left.GetterVisibility, right.GetterVisibility);
        AddAccessorReason(reasons, "setter", left.SetterVisibility, right.SetterVisibility);
        if (left.SetterVisibility == ApiVisibility.None || right.SetterVisibility == ApiVisibility.None || left.IsInitOnly == right.IsInitOnly) return;
        reasons.Add(right.IsInitOnly
            ? new("setter became init-only; assignments after construction break", Breaking: true)
            : new("init accessor became a setter", Breaking: false));
    }

    private static void AddAccessorReason(List<ApiChangeReason> reasons, string accessor, int left, int right)
    {
        if (left == right) return;
        if (right == ApiVisibility.None)
            reasons.Add(new($"{accessor} removed", Breaking: true));
        else if (left == ApiVisibility.None)
            reasons.Add(new($"{accessor} added", Breaking: false));
        else if (right < left)
            reasons.Add(new($"{accessor} accessibility reduced from {ApiVisibility.Name(left)} to {ApiVisibility.Name(right)}", Breaking: true));
        else
            reasons.Add(new($"{accessor} accessibility increased from {ApiVisibility.Name(left)} to {ApiVisibility.Name(right)}", Breaking: false));
    }

    private static void AddParameterReasons(List<ApiChangeReason> reasons, ApiMemberSurface left, ApiMemberSurface right)
    {
        var count = Math.Min(left.Parameters.Count, right.Parameters.Count);
        for (var i = 0; i < count; i++)
        {
            var before = left.Parameters[i];
            var after = right.Parameters[i];
            if (before.Modifier != after.Modifier)
                reasons.Add(new($"parameter {after.Name} modifier changed from '{before.Modifier}' to '{after.Modifier}'", Breaking: true));
            if (before.IsParams != after.IsParams)
                reasons.Add(after.IsParams
                    ? new($"parameter {after.Name} became params", Breaking: false)
                    : new($"parameter {after.Name} is no longer params; callers passing separate arguments break", Breaking: true));
            if (before.Name != after.Name)
                reasons.Add(new($"parameter renamed from {before.Name} to {after.Name}; breaks callers using named arguments", Breaking: false));
            if (before.IsOptional && !after.IsOptional)
                reasons.Add(new($"parameter {after.Name} is no longer optional", Breaking: true));
            else if (!before.IsOptional && after.IsOptional)
                reasons.Add(new($"parameter {after.Name} became optional", Breaking: false));
            else if (before.IsOptional && before.DefaultValue != after.DefaultValue)
                reasons.Add(new($"default value of {after.Name} changed from {before.DefaultValue ?? "(none)"} to {after.DefaultValue ?? "(none)"}; compiled callers keep the old value", Breaking: false));
        }
    }
}
