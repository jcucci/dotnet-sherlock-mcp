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
