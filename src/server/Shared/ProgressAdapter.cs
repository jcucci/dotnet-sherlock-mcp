using ModelContextProtocol;
using Sherlock.MCP.Runtime;

namespace Sherlock.MCP.Server.Shared;

public static class ProgressAdapter
{
    public static IProgress<ScanProgress>? ForPhase(IProgress<ProgressNotificationValue>? progress, int phase = 0, int phaseCount = 1) =>
        progress is null ? null : new PhaseProgress(progress, phase, phaseCount);

    private sealed class PhaseProgress(IProgress<ProgressNotificationValue> inner, int phase, int phaseCount) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => inner.Report(new ProgressNotificationValue
        {
            Progress = (phase * value.Total) + value.Completed,
            Total = value.Total * phaseCount,
            Message = value.Current is null ? null : $"Scanned {value.Current}"
        });
    }
}
