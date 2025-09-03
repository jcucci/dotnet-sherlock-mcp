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

