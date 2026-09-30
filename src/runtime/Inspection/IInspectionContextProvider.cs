using System.Reflection;

namespace Sherlock.MCP.Runtime.Inspection;

public interface IInspectionContextProvider
{
    InspectionContextLease Acquire(string assemblyPath, IReadOnlyList<string>? additionalSearchDirectories = null);
}

public sealed class InspectionContextLease : IDisposable
{
    private Action? _release;

    internal InspectionContextLease(IAssemblyInspectionContext context, Action release)
    {
        Context = context;
        _release = release;
    }

    public IAssemblyInspectionContext Context { get; }

    public Assembly Assembly => Context.Assembly;

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
