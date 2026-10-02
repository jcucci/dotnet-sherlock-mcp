using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.ApiDiff;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Tests;

public class ApiDiffServiceTests
{
    private static readonly ApiDiffPair Pair = ApiDiffFixtures.EmitPair();
    private static readonly ApiDiffResult Diff =
        new ApiDiffService(new SharedInspectionContextProvider(new RuntimeOptions())).Compare(Pair.LeftPath, null, Pair.RightPath, null);

    private static ApiChange TypeChange(string type) =>
        Assert.Single(Diff.Changes, c => c.Scope == ApiChangeScope.Type && c.TypeName == type);

    private static ApiChange MemberChange(string type, string member, ApiChangeKind? kind = null) =>
        Assert.Single(Diff.Changes, c => c.Scope == ApiChangeScope.Member && c.TypeName == type && c.MemberName == member && (kind == null || c.Change == kind));

    private static void AssertReason(ApiChange change, string fragment, bool breaking) =>
        Assert.Contains(change.Reasons, r => r.Description.Contains(fragment, StringComparison.Ordinal) && r.Breaking == breaking);

    [Fact]
    public void ReportsAssemblyIdentities()
    {
        Assert.Equal(ApiDiffFixtures.AssemblyName, Diff.Left.Name);
        Assert.Equal(ApiDiffFixtures.AssemblyName, Diff.Right.Name);
    }

    [Theory]
    [InlineData("Acme.Lib.Removed")]
    [InlineData("Acme.Lib.WillBeInternal")]
    public void RemovedOrHiddenType_IsBreaking(string type)
    {
        var change = TypeChange(type);
        Assert.Equal(ApiChangeKind.Removed, change.Change);
        Assert.True(change.Breaking);
    }

    [Fact]
    public void AddedType_IsNotBreaking()
    {
        var change = TypeChange("Acme.Lib.NewType");
        Assert.Equal(ApiChangeKind.Added, change.Change);
        Assert.False(change.Breaking);
        Assert.Equal("public class Acme.Lib.NewType", change.RightSignature);
    }

    [Fact]
    public void SealedClass_IsBreaking() => AssertReason(TypeChange("Acme.Lib.WillSeal"), "became sealed", breaking: true);

    [Fact]
    public void RemovedInterface_IsBreaking() => AssertReason(TypeChange("Acme.Lib.Shape"), "no longer implements Acme.Lib.IShape", breaking: true);

    [Fact]
    public void AddedGenericConstraint_IsBreaking() => AssertReason(TypeChange("Acme.Lib.Box<T>"), "generic constraint added: T : class", breaking: true);

    [Fact]
    public void AbstractMemberAddedToInterface_IsBreaking()
    {
        var change = MemberChange("Acme.Lib.IShape", "Perimeter");
        Assert.Equal(ApiChangeKind.Added, change.Change);
        AssertReason(change, "added to an interface", breaking: true);
    }

    [Fact]
    public void DefaultInterfaceMemberAdded_IsNotBreaking()
    {
        var change = MemberChange("Acme.Lib.IShape", "Sides");
        Assert.Equal(ApiChangeKind.Added, change.Change);
        Assert.False(change.Breaking);
    }

    [Fact]
    public void AbstractMemberAddedToInheritableClass_IsBreaking() =>
        AssertReason(MemberChange("Acme.Lib.Animal", "Eat"), "inheritable class", breaking: true);

    [Fact]
    public void RemovedMember_IsBreaking()
    {
        var change = MemberChange("Acme.Lib.Widget", "Gone");
        Assert.Equal(ApiChangeKind.Removed, change.Change);
        Assert.True(change.Breaking);
        Assert.Equal("public void Gone()", change.LeftSignature);
    }

    [Fact]
    public void ReturnTypeChange_IsBreaking() => AssertReason(MemberChange("Acme.Lib.Widget", "Size"), "return type changed from long to int", breaking: true);

    [Fact]
    public void FieldBecameReadonly_IsBreaking() => AssertReason(MemberChange("Acme.Lib.Widget", "Mutable"), "became readonly", breaking: true);

    [Fact]
    public void FieldNoLongerReadonly_IsNotBreaking()
    {
        var change = MemberChange("Acme.Lib.Widget", "Fixed");
        Assert.False(change.Breaking);
        AssertReason(change, "no longer readonly", breaking: false);
    }

    [Fact]
    public void ConstValueChange_IsNotBreaking() => AssertReason(MemberChange("Acme.Lib.Widget", "Max"), "constant value changed from 10 to 20", breaking: false);

    [Fact]
    public void EnumValueChange_IsBreaking() => AssertReason(MemberChange("Acme.Lib.Color", "Green"), "enum value changed from 2 to 3", breaking: true);

    [Fact]
    public void SetterRemoved_IsBreaking() => AssertReason(MemberChange("Acme.Lib.Widget", "Name"), "setter removed", breaking: true);

    [Fact]
    public void ParameterRename_IsNotBreaking()
    {
        var change = MemberChange("Acme.Lib.Widget", "Rename");
        Assert.False(change.Breaking);
        AssertReason(change, "parameter renamed from oldName to newName", breaking: false);
    }

    [Fact]
    public void OptionalParameterRequired_IsBreaking() => AssertReason(MemberChange("Acme.Lib.Widget", "Optional"), "no longer optional", breaking: true);

    [Fact]
    public void VirtualRemoved_IsBreaking() => AssertReason(MemberChange("Acme.Lib.Widget", "Hook"), "no longer virtual", breaking: true);

    [Fact]
    public void ParameterTypeChange_IsRemovedPlusAdded()
    {
        Assert.True(MemberChange("Acme.Lib.Widget", "Overload", ApiChangeKind.Removed).Breaking);
        Assert.False(MemberChange("Acme.Lib.Widget", "Overload", ApiChangeKind.Added).Breaking);
    }

    [Fact]
    public void SetterAccessibilityReduced_IsBreaking() =>
        AssertReason(MemberChange("Acme.Lib.Widget", "Setter"), "setter accessibility reduced from public to protected", breaking: true);

    [Fact]
    public void GetterAccessibilityReduced_IsBreaking() =>
        AssertReason(MemberChange("Acme.Lib.Widget", "Getter"), "getter accessibility reduced from public to protected", breaking: true);

    [Fact]
    public void SetterBecameInitOnly_IsBreaking() =>
        AssertReason(MemberChange("Acme.Lib.Widget", "Init"), "setter became init-only", breaking: true);

    [Fact]
    public void ParamsAdded_IsNotBreaking()
    {
        var change = MemberChange("Acme.Lib.Widget", "Params");
        Assert.False(change.Breaking);
        AssertReason(change, "parameter xs became params", breaking: false);
    }

    [Fact]
    public void ParamsRemoved_IsBreaking() =>
        AssertReason(MemberChange("Acme.Lib.Widget", "Unparams"), "no longer params", breaking: true);

    [Fact]
    public void RenamedGenericParameter_KeepsItsConstraints() =>
        Assert.DoesNotContain(Diff.Changes, c => c.TypeName.StartsWith("Acme.Lib.Renamed", StringComparison.Ordinal));

    [Theory]
    [InlineData("Moved")]
    [InlineData("Promoted")]
    public void MemberStillInheritedFromBase_IsNotBreaking(string member)
    {
        var change = MemberChange("Acme.Lib.Derived", member);
        Assert.Equal(ApiChangeKind.Removed, change.Change);
        Assert.False(change.Breaking);
        AssertReason(change, "still inherited from a base type", breaking: false);
    }

    [Fact]
    public void InheritedMemberWithADifferentType_IsBreaking()
    {
        var change = MemberChange("Acme.Lib.Derived", "Retyped");
        Assert.Equal(ApiChangeKind.Removed, change.Change);
        Assert.True(change.Breaking);
    }

    [Fact]
    public void OptionalWithoutDefaultMadeRequired_IsBreaking() =>
        AssertReason(MemberChange("Acme.Lib.Derived", "Attr"), "no longer optional", breaking: true);

    [Fact]
    public void ProtectedMembersOfClassesWithoutAccessibleConstructors_AreIgnored() =>
        Assert.DoesNotContain(Diff.Changes, c => c.TypeName == "Acme.Lib.NoCtor");

    [Fact]
    public void UnchangedMembers_AreNotReported()
    {
        Assert.DoesNotContain(Diff.Changes, c => c.MemberName is "Spin" or "Count" or "Helper" or "Run" or "Speak" or "Area");
        Assert.DoesNotContain(Diff.Changes, c => c.TypeName == "Acme.Lib.Helpers");
    }

    [Fact]
    public void ProtectedMembersOfSealedTypes_AreIgnored() =>
        Assert.DoesNotContain(Diff.Changes, c => c.TypeName == "Acme.Lib.Locked");

    [Fact]
    public void NamespaceFilter_LimitsTheComparison()
    {
        var filtered = new ApiDiffService(new SharedInspectionContextProvider(new RuntimeOptions()))
            .Compare(Pair.LeftPath, null, Pair.RightPath, null, namespacePrefix: "acme.lib.internal");

        var change = Assert.Single(filtered.Changes);
        Assert.Equal("Acme.Lib.Internal.Plumbing", change.TypeName);
        Assert.Equal("Drain", change.MemberName);
    }

    [Fact]
    public void IdenticalAssemblies_HaveNoChanges()
    {
        var diff = new ApiDiffService(new SharedInspectionContextProvider(new RuntimeOptions()))
            .Compare(Pair.LeftPath, null, ApiDiffFixtures.Emit(ApiDiffFixtures.Version1), null);

        Assert.Empty(diff.Changes);
        Assert.Empty(diff.Warnings);
    }

    [Fact]
    public void Cancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new ApiDiffService(new SharedInspectionContextProvider(new RuntimeOptions()))
                .Compare(Pair.LeftPath, null, Pair.RightPath, null, cancellationToken: cts.Token));
    }
}
