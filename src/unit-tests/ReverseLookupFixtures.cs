namespace Sherlock.MCP.Tests.ReverseLookupFixtures;

public interface ISampleEventReader
{
    void Read();
}

public interface ISampleEventReader<T> : ISampleEventReader
{
    T? Next();
}

public class ConcreteReader : ISampleEventReader
{
    public void Read() { }
}

public class TypedReader : ISampleEventReader<RecordedEvent>
{
    public void Read() { }
    public RecordedEvent? Next() => null;
}

public class FakeEventReader : ConcreteReader
{
}

public record RecordedEvent(string Name, long Position);

public class Snapshot<T>
{
    public T? Value { get; init; }
}

public class SnapshotFactory
{
    public Snapshot<int> CreateIntSnapshot() => new() { Value = 0 };
    public Snapshot<RecordedEvent> CreateEventSnapshot() => new();
    public int GetCount() => 0;
    public Task<Snapshot<string>> CreateAsync() => Task.FromResult(new Snapshot<string>());
}

public class EventStore
{
    public RecordedEvent? LastEvent;
    public List<RecordedEvent> All { get; } = new();
    public Snapshot<RecordedEvent>? CurrentSnapshot { get; set; }

    public void Append(RecordedEvent evt) { }
    public RecordedEvent? GetAt(int index) => null;
    public event Action<RecordedEvent>? OnAppended;
    public void RaiseOnAppended(RecordedEvent evt) => OnAppended?.Invoke(evt);
}
