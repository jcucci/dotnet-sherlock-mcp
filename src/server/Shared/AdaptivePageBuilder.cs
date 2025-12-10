using System.Text.Json;

namespace Sherlock.MCP.Server.Shared;

public class AdaptivePageBuilder<T>
{
    private readonly List<T> _items = new();
    private readonly int _targetSize;
    private readonly JsonSerializerOptions _serializerOptions;
    private int _currentEstimatedSize;
    private bool _wasReduced;

    public AdaptivePageBuilder(
        int targetSize = ResponseSizeHelper.WarningThreshold,
        JsonSerializerOptions? serializerOptions = null)
    {
        _targetSize = targetSize;
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions { WriteIndented = false };
        _currentEstimatedSize = 0;
        _wasReduced = false;
    }

    public bool TryAdd(T item)
    {
        var itemJson = JsonSerializer.Serialize(item, _serializerOptions);
        var itemSize = itemJson.Length;

        if (_currentEstimatedSize + itemSize > _targetSize && _items.Count > 0)
        {
            _wasReduced = true;
            return false;
        }

        _items.Add(item);
        _currentEstimatedSize += itemSize;
        return true;
    }

    public bool TryAddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
            if (!TryAdd(item))
                return false;
        return true;
    }

    public (T[] items, bool wasReduced, int actualCount, int estimatedSize) Build() =>
        (_items.ToArray(), _wasReduced, _items.Count, _currentEstimatedSize);

    public bool WasReduced => _wasReduced;
    public int Count => _items.Count;
    public int EstimatedSize => _currentEstimatedSize;
}
