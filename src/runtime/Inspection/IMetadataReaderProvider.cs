using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Sherlock.MCP.Runtime.Il;

namespace Sherlock.MCP.Runtime.Inspection;

public interface IMetadataReaderProvider
{
    MetadataReaderLease AcquireMetadata(string assemblyPath);
}

public sealed class MetadataReaderLease : IDisposable
{
    private Action? _release;

    internal MetadataReaderLease(PEReader peReader, MetadataReader reader, MetadataTokenResolver resolver, Action release)
    {
        PEReader = peReader;
        Reader = reader;
        Resolver = resolver;
        _release = release;
    }

    public PEReader PEReader { get; }

    public MetadataReader Reader { get; }

    internal MetadataTokenResolver Resolver { get; }

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
