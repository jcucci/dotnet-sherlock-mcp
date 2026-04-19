using System.Collections.Generic;
using Sherlock.MCP.Runtime;

namespace Sherlock.MCP.Tests;

public class TypeNameMatcherTests
{
    [Fact]
    public void Matches_SimpleName() =>
        Assert.True(TypeNameMatcher.Matches(typeof(string), "String"));

    [Fact]
    public void Matches_FullName() =>
        Assert.True(TypeNameMatcher.Matches(typeof(string), "System.String"));

    [Fact]
    public void Matches_BuiltInAlias() =>
        Assert.True(TypeNameMatcher.Matches(typeof(int), "int"));

    [Fact]
    public void Matches_BuiltInAlias_Bool() =>
        Assert.True(TypeNameMatcher.Matches(typeof(bool), "bool"));

    [Fact]
    public void Matches_OpenGeneric_BareName() =>
        Assert.True(TypeNameMatcher.Matches(typeof(List<>), "List"));

    [Fact]
    public void Matches_ConstructedGeneric_BareName() =>
        Assert.True(TypeNameMatcher.Matches(typeof(List<int>), "List"));

    [Fact]
    public void Matches_ConstructedGeneric_AngleBracketOpen() =>
        Assert.True(TypeNameMatcher.Matches(typeof(List<int>), "List<>"));

    [Fact]
    public void Matches_ConstructedGeneric_AngleBracketWithTypeParam() =>
        Assert.True(TypeNameMatcher.Matches(typeof(List<int>), "List<T>"));

    [Fact]
    public void Matches_ConstructedGeneric_BacktickArity() =>
        Assert.True(TypeNameMatcher.Matches(typeof(List<int>), "List`1"));

    [Fact]
    public void Matches_ConstructedGeneric_FullNameWithArity() =>
        Assert.True(TypeNameMatcher.Matches(typeof(List<int>), "System.Collections.Generic.List`1"));

    [Fact]
    public void Matches_NestedType_PlusSeparator() =>
        Assert.True(TypeNameMatcher.Matches(typeof(Outer.Inner), "Outer+Inner"));

    [Fact]
    public void Matches_NestedType_DotSeparator() =>
        Assert.True(TypeNameMatcher.Matches(typeof(Outer.Inner), "Outer.Inner"));

    [Fact]
    public void Matches_Array_UnwrapsToElement() =>
        Assert.True(TypeNameMatcher.Matches(typeof(int[]), "int"));

    [Fact]
    public void Matches_MultiDimArray_UnwrapsToElement() =>
        Assert.True(TypeNameMatcher.Matches(typeof(string[,]), "String"));

    [Fact]
    public void Matches_ByRef_UnwrapsToElement()
    {
        var byRefInt = typeof(int).MakeByRefType();
        Assert.True(TypeNameMatcher.Matches(byRefInt, "int"));
    }

    [Fact]
    public void Matches_Pointer_UnwrapsToElement()
    {
        var ptrInt = typeof(int).MakePointerType();
        Assert.True(TypeNameMatcher.Matches(ptrInt, "Int32"));
    }

    [Fact]
    public void Matches_Nullable_QuestionMarkSyntax() =>
        Assert.True(TypeNameMatcher.Matches(typeof(int?), "int?"));

    [Fact]
    public void Matches_Nullable_ExplicitNullableSyntax() =>
        Assert.True(TypeNameMatcher.Matches(typeof(int?), "Nullable<int>"));

    [Fact]
    public void Matches_Nullable_BareName() =>
        Assert.True(TypeNameMatcher.Matches(typeof(int?), "Nullable"));

    [Fact]
    public void Matches_AssemblyQualifiedName_Stripped() =>
        Assert.True(TypeNameMatcher.Matches(
            typeof(List<int>),
            "System.Collections.Generic.List`1, System.Private.CoreLib, Version=7.0.0.0"));

    [Fact]
    public void Matches_GenericParameter_AlwaysFalse()
    {
        var listOpen = typeof(List<>);
        var tParam = listOpen.GetGenericArguments()[0];
        Assert.False(TypeNameMatcher.Matches(tParam, "T"));
        Assert.False(TypeNameMatcher.Matches(tParam, "int"));
    }

    [Fact]
    public void Matches_CaseSensitive_Strict()
    {
        Assert.False(TypeNameMatcher.Matches(typeof(string), "STRING", caseSensitive: true));
        Assert.True(TypeNameMatcher.Matches(typeof(string), "String", caseSensitive: true));
    }

    [Fact]
    public void Matches_CaseInsensitive_Default()
    {
        Assert.True(TypeNameMatcher.Matches(typeof(string), "STRING"));
        Assert.True(TypeNameMatcher.Matches(typeof(string), "string"));
    }

    [Fact]
    public void Matches_NullCandidate_False() =>
        Assert.False(TypeNameMatcher.Matches((Type?)null, "int"));

    [Fact]
    public void Matches_NullOrEmptyUserName_False()
    {
        Assert.False(TypeNameMatcher.Matches(typeof(int), null));
        Assert.False(TypeNameMatcher.Matches(typeof(int), ""));
        Assert.False(TypeNameMatcher.Matches(typeof(int), "   "));
    }

    [Fact]
    public void Matches_DifferentType_False()
    {
        Assert.False(TypeNameMatcher.Matches(typeof(int), "string"));
        Assert.False(TypeNameMatcher.Matches(typeof(List<int>), "Dictionary"));
    }
}
