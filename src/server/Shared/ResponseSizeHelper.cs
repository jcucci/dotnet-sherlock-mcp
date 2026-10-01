using System.Text.Json;

namespace Sherlock.MCP.Server.Shared;

public static class ResponseSizeHelper
{
    private static readonly JsonSerializerOptions _jsonOptions = JsonHelpers.DefaultOptions;

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
            var (suggestion, alternatives, recommended) = GetGuidanceForTool(toolName);

            return JsonHelpers.ErrorWithGuidance(
                "ResponseTooLarge",
                $"Response from {toolName} is too large ({size:N0} characters, max: {MaxResponseSize:N0}).",
                suggestion,
                alternatives,
                recommended
            );
        }

        return null;
    }

    private static (string suggestion, string[] alternatives, object recommended) GetGuidanceForTool(string toolName) =>
        toolName switch
        {
            "get_all_type_members" => (
                "Use get_type_members with kinds and nameContains filters instead of retrieving all members at once.",
                new[] { "get_type_members" },
                new { maxItems = 20, kinds = "method", nameContains = "<filter_pattern>" }
            ),
            "get_type_members" => (
                "Narrow with kinds or nameContains, keep projection='summary', or reduce page size.",
                Array.Empty<string>(),
                new { maxItems = 25, kinds = "method", nameContains = "<member_name_pattern>", projection = "summary" }
            ),
            "get_type_methods" => (
                "Filter by method name or reduce page size for types with many methods.",
                Array.Empty<string>(),
                new { maxItems = 15, nameContains = "<method_name_pattern>" }
            ),
            "get_type_properties" => (
                "Filter by property name or reduce page size.",
                Array.Empty<string>(),
                new { maxItems = 25, nameContains = "<property_name_pattern>" }
            ),
            "analyze_assembly" => (
                "Use smaller page size for assemblies with many types.",
                new[] { "get_types_from_assembly" },
                new { maxItems = 25 }
            ),
            "analyze_type" => (
                "Use get_type_info for type metadata and get_type_members for targeted member queries.",
                new[] { "get_type_info", "get_type_members" },
                new { maxItems = 15 }
            ),
            "decompile_member" or "decompile_type" => (
                "Lower maxLines and follow continuationToken, or decompile a single member instead of a whole type.",
                new[] { "decompile_member" },
                new { maxLines = 200 }
            ),
            "get_assembly_info" => (
                "Use the lean projection to avoid returning every assembly attribute.",
                Array.Empty<string>(),
                new { projection = "summary" }
            ),
            _ => (
                "Use pagination parameters to retrieve data in smaller chunks.",
                Array.Empty<string>(),
                new { maxItems = 25 }
            )
        };
}