namespace Sherlock.MCP.Runtime.Telemetry;

public interface ITelemetry
{
    void TrackDuration(string name, TimeSpan duration);

    void Increment(string name);
}

public sealed class NoopTelemetry : ITelemetry
{
    public void TrackDuration(string name, TimeSpan duration) { }

    public void Increment(string name) { }
}

