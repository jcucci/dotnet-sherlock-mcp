using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Sherlock.MCP.IntegrationTests;

public class McpStdioProtocolTests
{
    private const int ExpectedToolCount = 36;

    private static readonly Regex SnakeCase = new("^[a-z0-9]+(_[a-z0-9]+)*$", RegexOptions.Compiled);

    private static string ServerDll => Path.Combine(AppContext.BaseDirectory, "Sherlock.MCP.Server.dll");

    [Fact]
    public async Task Initialize_handshake_succeeds()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        Assert.NotNull(client.ServerInfo);
        Assert.False(string.IsNullOrWhiteSpace(client.ServerInfo!.Name));
    }

    [Fact]
    public async Task Tools_list_returns_expected_count_and_snake_case_names()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
        var names = tools.Select(t => t.Name).ToHashSet();

        Assert.True(
            tools.Count == ExpectedToolCount,
            $"Expected {ExpectedToolCount} tools but server exposed {tools.Count}. " +
            $"If a tool was intentionally added or removed, update ExpectedToolCount. " +
            $"Tools: {string.Join(", ", names.OrderBy(n => n))}");

        Assert.Contains("get_type_methods", names);
        Assert.Contains("find_implementations_of", names);
        Assert.Contains("update_runtime_options", names);

        foreach (var name in names)
            Assert.True(SnakeCase.IsMatch(name), $"Tool name '{name}' is not snake_case.");
    }

    [Fact]
    public async Task Call_get_type_methods_returns_well_formed_envelope()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        var result = await client.CallToolAsync(
            "get_type_methods",
            new Dictionary<string, object?>
            {
                ["assemblyPath"] = typeof(string).Assembly.Location,
                ["typeName"] = "System.String"
            },
            cancellationToken: cts.Token);

        Assert.NotEqual(true, result.IsError);

        var envelope = Envelope(result);
        Assert.Equal("member.methods", envelope.GetProperty("kind").GetString());
        Assert.Equal("1.0.0", envelope.GetProperty("version").GetString());

        var data = envelope.GetProperty("data");
        var total = data.GetProperty("total").GetInt32();
        var count = data.GetProperty("count").GetInt32();
        Assert.True(count <= total, $"count ({count}) should not exceed total ({total}).");
        Assert.Equal(JsonValueKind.Array, data.GetProperty("methods").ValueKind);
        Assert.Equal(count, data.GetProperty("methods").GetArrayLength());
    }

    [Fact]
    public async Task Continuation_token_round_trip_has_no_overlap_and_stable_total()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        var firstData = Envelope(await CallGetTypeMethods(client, maxItems: 10, continuationToken: null, cancellationToken: cts.Token))
            .GetProperty("data");

        var totalPage1 = firstData.GetProperty("total").GetInt32();
        Assert.True(totalPage1 > 10, $"System.String should expose more than 10 methods (got {totalPage1}).");

        var nextToken = firstData.GetProperty("nextToken").GetString();
        Assert.False(string.IsNullOrEmpty(nextToken), "Expected a nextToken when more results remain.");

        var firstSignatures = Signatures(firstData);
        Assert.Equal(10, firstSignatures.Count);

        var secondData = Envelope(await CallGetTypeMethods(client, maxItems: 10, continuationToken: nextToken, cancellationToken: cts.Token))
            .GetProperty("data");

        Assert.Equal(totalPage1, secondData.GetProperty("total").GetInt32());

        var secondSignatures = Signatures(secondData);
        Assert.NotEmpty(secondSignatures);
        Assert.Empty(firstSignatures.Intersect(secondSignatures));
    }

    [Fact]
    public async Task Unknown_type_returns_TypeNotFound_error_envelope()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        var result = await client.CallToolAsync(
            "get_type_methods",
            new Dictionary<string, object?>
            {
                ["assemblyPath"] = typeof(string).Assembly.Location,
                ["typeName"] = "No.Such.Type"
            },
            cancellationToken: cts.Token);

        Assert.NotEqual(true, result.IsError);

        var envelope = Envelope(result);
        Assert.Equal("error", envelope.GetProperty("kind").GetString());
        Assert.Equal("TypeNotFound", envelope.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(envelope.GetProperty("message").GetString()));
    }

    private static async Task<IMcpClient> ConnectAsync(CancellationToken cancellationToken)
    {
        Assert.True(File.Exists(ServerDll), $"Expected server DLL at {ServerDll}");

        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments = [ServerDll],
                Name = "sherlock-e2e"
            },
            NullLoggerFactory.Instance);

        var options = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "sherlock-e2e-tests", Version = "1.0.0" }
        };

        return await McpClientFactory.CreateAsync(transport, options, NullLoggerFactory.Instance, cancellationToken);
    }

    private static async Task<CallToolResult> CallGetTypeMethods(
        IMcpClient client, int maxItems, string? continuationToken, CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["assemblyPath"] = typeof(string).Assembly.Location,
            ["typeName"] = "System.String",
            ["maxItems"] = maxItems
        };
        if (continuationToken != null)
            arguments["continuationToken"] = continuationToken;

        return await client.CallToolAsync("get_type_methods", arguments, cancellationToken: cancellationToken);
    }

    private static JsonElement Envelope(CallToolResult result)
    {
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static HashSet<string> Signatures(JsonElement data) =>
        data.GetProperty("methods")
            .EnumerateArray()
            .Select(m => m.GetProperty("signature").GetString()!)
            .ToHashSet();
}
