using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.TypeAnalysis;
using System.Reflection;

namespace Sherlock.MCP.Tests;

public class TypeAnalysisServiceTests
{
    private readonly ITypeAnalysisService _service = new TypeAnalysisService();

    [Fact]
    public void LoadAssembly_And_GetTypesFromAssembly_Works()
    {
        var asmPath = Assembly.GetExecutingAssembly().Location;
        var asm = _service.LoadAssembly(asmPath);
        Assert.NotNull(asm);

        var types = _service.GetTypesFromAssembly(asmPath);
        Assert.True(types.Length > 0);
        Assert.Contains(types, t => t.Name == nameof(MemberAnalysisServiceTests));
    }

    [Fact]
    public void GetTypeInfo_Returns_Metadata_For_Generic_Types()
    {
        var type = typeof(Dictionary<string, List<int>>);
        var info = _service.GetTypeInfo(type);

        Assert.Equal(type.Name, info.Name);
        Assert.True(info.IsGeneric);
        Assert.Equal(TypeKind.Class, info.Kind);
        Assert.NotNull(info.AssemblyName);
        Assert.True(info.Attributes.Length >= 0);
    }

    [Fact]
    public void GetTypeHierarchy_Returns_BaseTypes_And_Interfaces()
    {
        var type = typeof(DerivedSample);
        var hierarchy = _service.GetTypeHierarchy(type);

        Assert.Equal(type.FullName, hierarchy.TypeName);
        Assert.Contains(typeof(BaseSample).FullName, hierarchy.InheritanceChain);
        Assert.Contains(typeof(IDisposable).FullName, hierarchy.AllInterfaces);
    }

    [Fact]
    public void GetGenericTypeInfo_Reports_Parameters_And_Arguments()
    {
        var type = typeof(List<string>);
        var info = _service.GetGenericTypeInfo(type);
        Assert.True(info.IsConstructedGenericType);
        Assert.Contains("System.String", info.GenericArguments);

        var def = typeof(List<>);
        var defInfo = _service.GetGenericTypeInfo(def);
        Assert.True(defInfo.IsGenericTypeDefinition);
        Assert.True(defInfo.GenericParameters.Length >= 1);
    }
}

public class BaseSample : IDisposable
{
    public void Dispose() { }
}

public class DerivedSample : BaseSample, IDisposable { }

