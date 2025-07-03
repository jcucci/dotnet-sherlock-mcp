using System.Text.Json;

namespace Sherlock.MCP.Server.Shared;

/// <summary>
/// Shared JSON serialization utilities for MCP tool responses
/// </summary>
public static class JsonHelpers
{
    /// <summary>
    /// Standard JSON serialization options for MCP responses
    /// </summary>
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Creates a standardized error response
    /// </summary>
    /// <param name="message">The error message</param>
    /// <returns>JSON serialized error response</returns>
    public static string CreateErrorResponse(string message) =>
        JsonSerializer.Serialize(new { error = message }, DefaultOptions);

    /// <summary>
    /// Serializes an object with standard options
    /// </summary>
    /// <param name="obj">Object to serialize</param>
    /// <returns>JSON string</returns>
    public static string Serialize(object obj) =>
        JsonSerializer.Serialize(obj, DefaultOptions);
}