namespace Sherlock.MCP.Runtime;

public readonly record struct ScanProgress(int Completed, int Total, string? Current = null);

public sealed class ProgressCounter
{
    private readonly IProgress<ScanProgress>? _progress;
    private readonly int _total;
    private readonly int _step;
    private readonly object _gate = new();
    private int _completed;

    public ProgressCounter(IProgress<ScanProgress>? progress, int total)
    {
        _progress = progress;
        _total = total;
        _step = Math.Max(1, total / 100);
    }

    public void Increment(string? current = null)
    {
        if (_progress is null) return;

        lock (_gate)
        {
            _completed++;
            if (_completed % _step == 0 || _completed == _total)
                _progress.Report(new ScanProgress(_completed, _total, current));
        }
    }
}
