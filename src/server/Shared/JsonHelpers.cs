using System.Text.Json;

namespace Sherlock.MCP.Server.Shared;

public static class JsonHelpers
{
    public const string SchemaVersion = "1.0.0";

    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true
    };

    public static string Envelope(string kind, object data) =>
        JsonSerializer.Serialize(new { kind, version = SchemaVersion, data }, DefaultOptions);

    public static string Error(string code, string message, object? details = null) =>
        JsonSerializer.Serialize(new { kind = "error", version = SchemaVersion, code, message, details }, DefaultOptions);

    public static string Serialize(object obj) =>
        JsonSerializer.Serialize(obj, DefaultOptions);
}
