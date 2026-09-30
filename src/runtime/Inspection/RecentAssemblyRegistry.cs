namespace Sherlock.MCP.Runtime.Inspection;

public sealed class RecentAssemblyRegistry : IRecentAssemblyRegistry
{
    private const int DefaultCapacity = 64;

    private readonly LinkedList<string> _paths = new();
    private readonly object _gate = new();
    private readonly int _capacity;

    public RecentAssemblyRegistry() : this(DefaultCapacity)
    {
    }

    public RecentAssemblyRegistry(int capacity) => _capacity = Math.Max(1, capacity);

    public void Record(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath)) return;

        var fullPath = Path.GetFullPath(assemblyPath);
        lock (_gate)
        {
            var existing = FindNode(fullPath);
            if (existing is not null) _paths.Remove(existing);
            _paths.AddFirst(fullPath);
            while (_paths.Count > _capacity) _paths.RemoveLast();
        }
    }

    public IReadOnlyList<string> GetRecent()
    {
        lock (_gate) return [.. _paths];
    }

    private LinkedListNode<string>? FindNode(string fullPath)
    {
        for (var node = _paths.First; node is not null; node = node.Next)
            if (string.Equals(node.Value, fullPath, StringComparison.OrdinalIgnoreCase))
                return node;
        return null;
    }
}
