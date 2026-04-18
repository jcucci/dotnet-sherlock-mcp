using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.TypeAnalysis;

namespace Sherlock.MCP.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class TypeCtorAttribute : Attribute
{
    public TypeCtorAttribute(Type target) => Target = target;
    public Type Target { get; }
    public Type? Alternate { get; set; }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class TypeArrayCtorAttribute : Attribute
{
    public TypeArrayCtorAttribute(Type[] targets) => Targets = targets;
    public Type[] Targets { get; }
}

internal class AttributeUtilsTestSubject
{
    [TypeCtor(typeof(int))]
    public void CtorTypeArgMethod() { }

    [TypeCtor(typeof(string), Alternate = typeof(Guid))]
    public void NamedTypeArgMethod() { }

    [TypeArrayCtor(new[] { typeof(int), typeof(string) })]
    public void TypeArrayArgMethod() { }
}

public class AttributeUtilsTests
{
    [Fact]
    public void Convert_ProjectsCtorTypeArg_ToTypeRef_AndSerializes()
    {
        var method = typeof(AttributeUtilsTestSubject).GetMethod(nameof(AttributeUtilsTestSubject.CtorTypeArgMethod))!;

        var attrs = AttributeUtils.FromMember(method);
        var attr = Assert.Single(attrs, a => a.AttributeType == typeof(TypeCtorAttribute).FullName);
        var projected = Assert.IsType<TypeRef>(attr.ConstructorArguments[0]);
        Assert.Equal(typeof(int).FullName, projected.FullName);
        Assert.Equal(typeof(int).Assembly.GetName().Name, projected.AssemblyName);

        var json = JsonSerializer.Serialize(attrs);
        Assert.Contains(typeof(int).FullName!, json);
    }

    [Fact]
    public void Convert_ProjectsNamedTypeArg_ToTypeRef_AndSerializes()
    {
        var method = typeof(AttributeUtilsTestSubject).GetMethod(nameof(AttributeUtilsTestSubject.NamedTypeArgMethod))!;

        var attrs = AttributeUtils.FromMember(method);
        var attr = Assert.Single(attrs, a => a.AttributeType == typeof(TypeCtorAttribute).FullName);
        Assert.True(attr.NamedArguments.ContainsKey("Alternate"));
        var projected = Assert.IsType<TypeRef>(attr.NamedArguments["Alternate"]);
        Assert.Equal(typeof(Guid).FullName, projected.FullName);

        var json = JsonSerializer.Serialize(attrs);
        Assert.Contains(typeof(Guid).FullName!, json);
    }

    [Fact]
    public void Convert_ProjectsTypeArrayArg_ToTypeRefArray_AndSerializes()
    {
        var method = typeof(AttributeUtilsTestSubject).GetMethod(nameof(AttributeUtilsTestSubject.TypeArrayArgMethod))!;

        var attrs = AttributeUtils.FromMember(method);
        var attr = Assert.Single(attrs, a => a.AttributeType == typeof(TypeArrayCtorAttribute).FullName);
        var arr = Assert.IsType<object?[]>(attr.ConstructorArguments[0]);
        Assert.Equal(2, arr.Length);
        var first = Assert.IsType<TypeRef>(arr[0]);
        var second = Assert.IsType<TypeRef>(arr[1]);
        Assert.Equal(typeof(int).FullName, first.FullName);
        Assert.Equal(typeof(string).FullName, second.FullName);

        var json = JsonSerializer.Serialize(attrs);
        Assert.Contains(typeof(int).FullName!, json);
        Assert.Contains(typeof(string).FullName!, json);
    }
}
