using System.Text.Json;

namespace Sherlock.MCP.Server.Shared;

public static class ResponseSizeHelper
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// Maximum response size in characters before we consider it too large for MCP
    /// This is conservative to avoid hitting context limits
    /// </summary>
    public const int MaxResponseSize = 100_000;

    /// <summary>
    /// Warning threshold where we should start considering pagination
    /// </summary>
    public const int WarningThreshold = 50_000;

    /// <summary>
    /// Estimates the size of a JSON response object
    /// </summary>
    /// <param name="responseObject">Object to be serialized to JSON</param>
    /// <returns>Estimated size in characters</returns>
    public static int EstimateSize(object responseObject)
    {
        try
        {
            var json = JsonSerializer.Serialize(responseObject, _jsonOptions);
            return json.Length;
        }
        catch
        {
            // If serialization fails, return a large estimate to be safe
            return MaxResponseSize;
        }
    }

    /// <summary>
    /// Checks if a response is too large for safe MCP transmission
    /// </summary>
    /// <param name="responseObject">Object to check</param>
    /// <returns>True if response is too large</returns>
    public static bool IsTooLarge(object responseObject)
    {
        return EstimateSize(responseObject) > MaxResponseSize;
    }

    /// <summary>
    /// Checks if a response is approaching size limits
    /// </summary>
    /// <param name="responseObject">Object to check</param>
    /// <returns>True if response is approaching limits</returns>
    public static bool IsNearLimit(object responseObject)
    {
        return EstimateSize(responseObject) > WarningThreshold;
    }

    /// <summary>
    /// Creates a truncated response when the original is too large
    /// </summary>
    /// <param name="originalResponse">The original response object</param>
    /// <param name="message">Message to include about truncation</param>
    /// <returns>A truncated response object</returns>
    public static object CreateTruncatedResponse(object originalResponse, string message = "Response truncated due to size limits. Use pagination parameters to get complete results.")
    {
        return new
        {
            truncated = true,
            message,
            estimatedSize = EstimateSize(originalResponse),
            maxSize = MaxResponseSize,
            partialData = "Use pagination parameters (maxItems, skip, continuationToken) to retrieve data in smaller chunks"
        };
    }

    /// <summary>
    /// Validates response size and returns error if too large
    /// </summary>
    /// <param name="responseObject">Response to validate</param>
    /// <param name="toolName">Name of the tool generating the response</param>
    /// <returns>Error response if too large, null if acceptable</returns>
    public static string? ValidateResponseSize(object responseObject, string toolName)
    {
        if (IsTooLarge(responseObject))
        {
            var size = EstimateSize(responseObject);
            return JsonHelpers.Error(
                "ResponseTooLarge",
                $"Response from {toolName} is too large ({size:N0} characters, max: {MaxResponseSize:N0}). " +
                $"Use pagination parameters (maxItems, skip, continuationToken) to retrieve data in smaller chunks."
            );
        }

        return null;
    }
}