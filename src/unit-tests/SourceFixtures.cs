using System.ComponentModel;

namespace Sherlock.MCP.Tests.SourceFixtures;

public class SourceSubject
{
    private readonly List<string> _log = ["seeded"];

    public SourceSubject()
    {
        _log.Add("constructed");
    }

    /// <summary>Adds an entry to the log.</summary>
    [Description("source-fixture")]
    public void Add(string entry)
    {
        // original comment survives
        _log.Add(entry);
    }

    public async Task<int> CountAsync()
    {
        await Task.Yield();
        return _log.Count;
    }

    public int Count => _log.Count;
}
