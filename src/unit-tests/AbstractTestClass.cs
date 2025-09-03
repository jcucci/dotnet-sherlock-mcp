namespace Sherlock.MCP.Tests;

public abstract class AbstractTestClass
{
    public abstract void AbstractMethod();
    public virtual void VirtualMethod() { }
    protected virtual event Action? ProtectedVirtualEvent;
}

