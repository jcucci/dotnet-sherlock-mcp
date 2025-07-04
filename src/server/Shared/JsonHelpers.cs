using System.Text.Json;
namespace Sherlock.MCP.Server.Shared;
public static class JsonHelpers
{
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true
    };
    public static string CreateErrorResponse(string message) =>
        JsonSerializer.Serialize(new { error = message }, DefaultOptions);
    public static string Serialize(object obj) =>
        JsonSerializer.Serialize(obj, DefaultOptions);
}