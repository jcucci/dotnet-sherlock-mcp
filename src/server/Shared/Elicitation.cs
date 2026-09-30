using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sherlock.MCP.Runtime;

namespace Sherlock.MCP.Server.Shared;

public sealed record ElicitationContext(bool CanElicit, IDictionary<string, InputResponse>? Responses)
{
    public static readonly ElicitationContext None = new(CanElicit: false, Responses: null);

    public static ElicitationContext From(RequestContext<CallToolRequestParams>? context) =>
        context is null
            ? None
            : new(
                CanElicit: context.Server.IsMrtrSupported && context.Server.ClientCapabilities?.Elicitation is not null,
                Responses: context.Params?.InputResponses);

    public bool HasResponse(string key) => Responses?.ContainsKey(key) == true;

    public string? Answer(string key)
    {
        if (Responses is null || !Responses.TryGetValue(key, out var response)) return null;

        var result = response.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
        if (result is not { IsAccepted: true, Content: { } content }) return null;
        if (!content.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String) return null;

        var answer = value.GetString();
        return string.IsNullOrWhiteSpace(answer) ? null : answer;
    }
}

public static class Elicitation
{
    public const string TypeNameKey = "typeName";
    public const string TfmKey = "tfm";

    public static string ApplyTypeChoice(ElicitationContext elicitation, string typeName) =>
        elicitation.Answer(TypeNameKey) ?? typeName;

    public static string AmbiguousType(ElicitationContext elicitation, AmbiguousTypeNameException ex)
    {
        if (elicitation.CanElicit && !elicitation.HasResponse(TypeNameKey))
            throw ChooseOne(
                key: TypeNameKey,
                message: $"'{ex.TypeName}' matches {ex.Candidates.Count} types. Which one did you mean?",
                options: ex.Candidates);

        return JsonHelpers.ErrorWithGuidance(
            "AmbiguousTypeName",
            ex.Message,
            suggestion: "Retry with one of the candidate full names as typeName.",
            recommendedParams: new { candidates = ex.Candidates });
    }

    public static InputRequiredException ChooseOne(string key, string message, IReadOnlyList<string> options, string? defaultOption = null)
    {
        var request = new ElicitRequestParams
        {
            Message = message,
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    [key] = new ElicitRequestParams.UntitledSingleSelectEnumSchema
                    {
                        Title = key,
                        Enum = options.ToList(),
                        Default = defaultOption
                    }
                },
                Required = [key]
            }
        };

        return new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest> { [key] = InputRequest.ForElicitation(request) },
            requestState: key);
    }
}
