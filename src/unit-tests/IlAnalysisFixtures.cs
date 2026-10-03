namespace Sherlock.MCP.Tests.IlAnalysisFixtures;

public class IlSampleSubject
{
    private int _counter;
    private static string _label = "x";

    public void DoWork()
    {
        Console.WriteLine("hi");
        _counter = Compute(_counter);
        var list = new List<int>();
        list.Add(_counter);
        _counter = list.Count;
        Helper();
        _label = "y";
        Console.WriteLine(_label);
    }

    public int Helper() => 42;

    public int Helper(int seed) => seed;

    private int Compute(int x) => x + 1;

    public abstract class Bodyless
    {
        public abstract void Nothing();
    }
}

public class StaticCtorSubject
{
    private static readonly List<int> _shared = CreateShared();

    private static List<int> CreateShared() => new();

    public int UseShared() => _shared.Count;
}

public class CallChainSubject
{
    public void Entry()
    {
        StepOne();
        Console.WriteLine("entry");
    }

    private void StepOne() => StepTwo();

    private void StepTwo() => Console.WriteLine("two");

    public int Countdown(int n) => n <= 0 ? 0 : Countdown(n - 1);

    public void Ping(int n)
    {
        if (n > 0) Pong(n - 1);
    }

    public void Pong(int n)
    {
        if (n > 0) Ping(n - 1);
    }
}

public class GenericCallSubject<T>
{
    private readonly List<T> _items = new();

    public void Save(T item)
    {
        Validate(item);
        new LocalCache<int>().Put(1);
    }

    private void Validate(T item) => _items.Add(item);
}

public class LocalCache<TValue>
{
    public void Put(TValue value) => Store(value, 0);

    public void Put(TValue value, int ttl) => Store(value, ttl);

    private void Store(TValue value, int ttl) => Console.WriteLine(ttl);
}
