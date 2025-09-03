using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.MemberAnalysis;
using System.Reflection;
namespace Sherlock.MCP.Tests;
/// <summary>Sample class for tests with parameters</summary>
public class TestSampleClass
{
    public const int ConstantField = 42;
    public static readonly string ReadOnlyField = "ReadOnly";
    private readonly int _privateReadOnlyField;
    public string PublicField = "Public";
    public TestSampleClass() { }
    public TestSampleClass(int value) { _privateReadOnlyField = value; }
    static TestSampleClass() { }
    [Obsolete("Use NewProperty")]
    public string PublicProperty { get; set; } = "";
    protected virtual string ProtectedVirtualProperty { get; private set; } = "";
    public string this[int index] => index.ToString();
    public static void StaticMethod() { }
    [return: System.ComponentModel.Description("returns nothing")]
    public virtual void VirtualMethod() { }
    protected internal void ProtectedInternalMethod() { }
    /// <summary>Method with parameters and attribute for testing</summary>
    /// <param name="required">required</param>
    /// <param name="optional">optional</param>
    /// <returns>none</returns>
    [Sample]
    public void MethodWithParameters([Sample] int required, string optional = "default", params object[] values) { }
    public T GenericMethod<T>(T value) where T : class => value;
    public event Action? PublicEvent;
    protected virtual event Action? ProtectedVirtualEvent;
    public void OnPublicEvent() => PublicEvent?.Invoke();
}
public abstract class AbstractTestClass
{
    public abstract void AbstractMethod();
    public virtual void VirtualMethod() { }
    protected virtual event Action? ProtectedVirtualEvent;
}
public class Outer
{
    public class Inner { }
}
public sealed class SampleAttribute : Attribute { }
public class MemberAnalysisServiceTests
{
    private readonly IMemberAnalysisService _service;
    private readonly string _testAssemblyPath;
    public MemberAnalysisServiceTests()
    {
        _service = new MemberAnalysisService();
        _testAssemblyPath = Assembly.GetExecutingAssembly().Location;
    }
    [Fact]
    public void GetAllMembers_ReturnsAllPublicMembers()
    {
        var members = _service.GetAllMembers(_testAssemblyPath,
            "Sherlock.MCP.Tests.TestSampleClass",
            new MemberFilterOptions { IncludePublic = true, IncludeNonPublic = false });
        Assert.NotEmpty(members);
        Assert.Contains(members, m => m.Name == "PublicProperty");
        Assert.Contains(members, m => m.Name == "StaticMethod");
        Assert.Contains(members, m => m.Name == "PublicField");
    }
    [Fact]
    public void GetMethods_ReturnsMethodDetails()
    {
        var methods = _service.GetMethods(_testAssemblyPath,
            "Sherlock.MCP.Tests.TestSampleClass");
        Assert.NotEmpty(methods);
        var staticMethod = methods.FirstOrDefault(m => m.Name == "StaticMethod");
        Assert.NotNull(staticMethod);
        Assert.True(staticMethod.IsStatic);
        Assert.Equal("void", staticMethod.ReturnTypeName);
        var virtualMethod = methods.FirstOrDefault(m => m.Name == "VirtualMethod");
        Assert.NotNull(virtualMethod);
        Assert.True(virtualMethod.IsVirtual);
        var genericMethod = methods.FirstOrDefault(m => m.Name == "GenericMethod");
        Assert.NotNull(genericMethod);
        Assert.NotEmpty(genericMethod.GenericTypeParameters);
        var methodWithParams = methods.FirstOrDefault(m => m.Name == "MethodWithParameters");
        Assert.NotNull(methodWithParams);
        Assert.Equal(3, methodWithParams.Parameters.Length);
        Assert.True(methodWithParams.Parameters[1].IsOptional);
        Assert.True(methodWithParams.Parameters[2].IsParams);
        // Attributes projected
        Assert.Contains(methods, m => m.Name == "MethodWithParameters" && m.CustomAttributes.Any(a => a.AttributeType!.Contains("SampleAttribute")));
    }
    [Fact]
    public void GetProperties_ReturnsPropertyDetails()
    {
        var properties = _service.GetProperties(_testAssemblyPath,
            "Sherlock.MCP.Tests.TestSampleClass");
        Assert.NotEmpty(properties);
        var publicProperty = properties.FirstOrDefault(p => p.Name == "PublicProperty");
        Assert.NotNull(publicProperty);
        Assert.True(publicProperty.CanRead);
        Assert.True(publicProperty.CanWrite);
        Assert.Equal("string", publicProperty.TypeName);
        Assert.Contains(publicProperty.CustomAttributes, a => a.AttributeType!.Contains("Obsolete"));
        var indexer = properties.FirstOrDefault(p => p.IsIndexer);
        Assert.NotNull(indexer);
        Assert.Single(indexer.IndexerParameters);
        Assert.Equal("int", indexer.IndexerParameters[0].TypeName);
    }
    [Fact]
    public void GetFields_ReturnsFieldDetails()
    {
        var fields = _service.GetFields(_testAssemblyPath,
            "Sherlock.MCP.Tests.TestSampleClass");
        Assert.NotEmpty(fields);
        var constantField = fields.FirstOrDefault(f => f.Name == "ConstantField");
        Assert.NotNull(constantField);
        Assert.True(constantField.IsConst);
        Assert.True(constantField.IsStatic);
        Assert.Equal(42, constantField.ConstantValue);
        var readOnlyField = fields.FirstOrDefault(f => f.Name == "ReadOnlyField");
        Assert.NotNull(readOnlyField);
        Assert.True(readOnlyField.IsReadOnly);
        Assert.True(readOnlyField.IsStatic);
        var publicField = fields.FirstOrDefault(f => f.Name == "PublicField");
        Assert.NotNull(publicField);
        Assert.False(publicField.IsStatic);
        Assert.Equal("public", publicField.AccessModifier);
    }
    [Fact]
    public void GetEvents_ReturnsEventDetails()
    {
        var events = _service.GetEvents(_testAssemblyPath,
            "Sherlock.MCP.Tests.TestSampleClass");
        Assert.NotEmpty(events);
        var publicEvent = events.FirstOrDefault(e => e.Name == "PublicEvent");
        Assert.NotNull(publicEvent);
        Assert.Equal("Action", publicEvent.EventHandlerTypeName);
        Assert.Equal("public", publicEvent.AccessModifier);
    }
    [Fact]
    public void GetConstructors_ReturnsConstructorDetails()
    {
        var constructors = _service.GetConstructors(_testAssemblyPath,
            "Sherlock.MCP.Tests.TestSampleClass",
            new MemberFilterOptions { IncludePublic = true, IncludeNonPublic = true });
        Assert.NotEmpty(constructors);
        var parameterlessConstructor = constructors.FirstOrDefault(c => c.Parameters.Length == 0 && !c.IsStatic);
        Assert.NotNull(parameterlessConstructor);
        Assert.Equal("public", parameterlessConstructor.AccessModifier);
        var parameterConstructor = constructors.FirstOrDefault(c => c.Parameters.Length == 1);
        Assert.NotNull(parameterConstructor);
        Assert.Equal("int", parameterConstructor.Parameters[0].TypeName);
        var staticConstructor = constructors.FirstOrDefault(c => c.IsStatic);
        Assert.NotNull(staticConstructor);
    }
    [Fact]
    public void MemberFilter_IncludeNonPublic_ReturnsPrivateMembers()
    {
        var methods = _service.GetMethods(_testAssemblyPath,
            "Sherlock.MCP.Tests.TestSampleClass",
            new MemberFilterOptions { IncludePublic = true, IncludeNonPublic = true });
        var protectedMethod = methods.FirstOrDefault(m => m.AccessModifier.Contains("protected"));
        Assert.NotNull(protectedMethod);
    }

    [Fact]
    public void Filtering_Sorting_Paging_Works()
    {
        var methods = _service.GetMethods(_testAssemblyPath,
            "Sherlock.MCP.Tests.TestSampleClass",
            new MemberFilterOptions { NameContains = "Method", SortOrder = "desc", Skip = 0, Take = 2, IncludeDeclaredOnly = true });
        Assert.True(methods.Length <= 2);
        Assert.True(methods.All(m => m.Name.Contains("Method")));
    }

    [Fact]
    public void CaseInsensitive_And_NestedType_Resolution_Works()
    {
        var members = _service.GetAllMembers(_testAssemblyPath, "sherlock.mcp.tests.outer.inner", new MemberFilterOptions { CaseSensitive = false });
        Assert.NotNull(members);
    }

    [Fact]
    public void Attribute_Filtering_Works()
    {
        var methods = _service.GetMethods(_testAssemblyPath,
            "Sherlock.MCP.Tests.TestSampleClass",
            new MemberFilterOptions { HasAttributeContains = "SampleAttribute" });
        Assert.Contains(methods, m => m.Name == "MethodWithParameters");
    }

    [Fact]
    public void XmlDocs_Service_Finds_Docs()
    {
        var xmlService = new XmlDocService();
        var testType = typeof(TestSampleClass);
        var typeDocs = xmlService.GetXmlDocsForType(testType);
        Assert.NotNull(typeDocs);
        var method = testType.GetMethod("MethodWithParameters");
        Assert.NotNull(method);
        var methodDocs = xmlService.GetXmlDocsForMember(method!);
        Assert.NotNull(methodDocs);
        Assert.True((methodDocs!.Summary ?? string.Empty).Length >= 1);
        Assert.True(methodDocs.Params.Length >= 1);
    }

}
