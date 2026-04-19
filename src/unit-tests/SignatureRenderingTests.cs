using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Contracts.MemberAnalysis;
using System.Reflection;

namespace Sherlock.MCP.Tests;

public class SignatureRenderingTests
{
    private readonly IMemberAnalysisService _memberService = new MemberAnalysisService();
    private readonly ITypeAnalysisService _typeService = new TypeAnalysisService();
    private readonly string _testAssemblyPath = Assembly.GetExecutingAssembly().Location;

    [Fact]
    public void Nullable_RendersAsQuestionSuffix()
    {
        var methods = _memberService.GetMethods(_testAssemblyPath,
            "Sherlock.MCP.Tests.NullableAndDefaults");
        var nullables = methods.FirstOrDefault(m => m.Name == "Nullables");
        Assert.NotNull(nullables);
        Assert.Equal(2, nullables.Parameters.Length);
        Assert.Equal("int?", nullables.Parameters[0].TypeName);
        Assert.Equal("double?", nullables.Parameters[1].TypeName);
        Assert.DoesNotContain("Nullable<", nullables.Signature);
    }

    [Fact]
    public void Defaults_UseLowercaseCSharpLiterals()
    {
        var methods = _memberService.GetMethods(_testAssemblyPath,
            "Sherlock.MCP.Tests.NullableAndDefaults");
        var defaults = methods.FirstOrDefault(m => m.Name == "Defaults");
        Assert.NotNull(defaults);
        Assert.Equal("false", defaults.Parameters[0].DefaultValue);
        Assert.Equal("null", defaults.Parameters[1].DefaultValue);
        Assert.Equal("0", defaults.Parameters[2].DefaultValue);
        Assert.Contains("flag = false", defaults.Signature);
        Assert.Contains("name = null", defaults.Signature);
    }

    [Fact]
    public void InterfaceMembers_OmitPublicAndAbstract()
    {
        var methods = _memberService.GetMethods(_testAssemblyPath,
            "Sherlock.MCP.Tests.ITestSignatureShape");
        var op = methods.FirstOrDefault(m => m.Name == "AbstractOp");
        Assert.NotNull(op);
        Assert.Equal("void AbstractOp(int id)", op.Signature);

        var props = _memberService.GetProperties(_testAssemblyPath,
            "Sherlock.MCP.Tests.ITestSignatureShape");
        var label = props.FirstOrDefault(p => p.Name == "Label");
        Assert.NotNull(label);
        Assert.DoesNotContain("public", label.Signature);
        Assert.DoesNotContain("abstract", label.Signature);

        var events = _memberService.GetEvents(_testAssemblyPath,
            "Sherlock.MCP.Tests.ITestSignatureShape");
        var fired = events.FirstOrDefault(e => e.Name == "Fired");
        Assert.NotNull(fired);
        Assert.DoesNotContain("public", fired.Signature);
        Assert.DoesNotContain("abstract", fired.Signature);
    }

    [Fact]
    public void GenericTypeFullName_HasNoBacktick()
    {
        var info = _typeService.GetTypeInfo(typeof(GenericHolder<,>));
        Assert.DoesNotContain("`", info.FullName);
        Assert.Equal("Sherlock.MCP.Tests.GenericHolder<T, U>", info.FullName);

        var closed = _typeService.GetTypeInfo(typeof(GenericHolder<int, string>));
        Assert.DoesNotContain("`", closed.FullName);
        Assert.Equal("Sherlock.MCP.Tests.GenericHolder<System.Int32, System.String>", closed.FullName);
    }
}
