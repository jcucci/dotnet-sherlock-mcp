namespace Sherlock.MCP.Tests.DecompilationFixtures;

public class DecompileSubject
{
    private static readonly string Greeting;
    private readonly int _seed;

    static DecompileSubject() => Greeting = "hello-from-cctor";

    public DecompileSubject(int seed) => _seed = seed;

    public DecompileSubject() : this(seed: 7)
    {
    }

    public int Seed => _seed * 3;

    public string Format(string value) => $"{Greeting}:{value}";

    public string Format(string value, int count) => string.Concat(Enumerable.Repeat(value, count + _seed));

    public string Format(Dictionary<string, int> values) => string.Join(";", values.Keys);

    public int Total(int[] values)
    {
        var total = 0;
        foreach (var value in values)
            total += Scale(value);
        return total;
    }

    private int Scale(int value) => value * 1234;

    public class Inner
    {
        public string Describe() => "inner-describe";
    }
}
