using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Sherlock.MCP.IntegrationTests;

public class McpStdioProtocolTests
{
    private const int ExpectedToolCount = 36;

    private const string CurrentProtocolVersion = "2026-07-28";

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
    public async Task Initialize_exposes_server_usage_instructions()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        Assert.False(
            string.IsNullOrWhiteSpace(client.ServerInstructions),
            "Server should return MCP instructions so clients can surface usage guidance automatically.");
        Assert.Contains("search_members", client.ServerInstructions);
        Assert.Contains("projection", client.ServerInstructions);
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
    public async Task Client_negotiates_the_current_protocol_revision()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        Assert.Equal(CurrentProtocolVersion, client.NegotiatedProtocolVersion);
    }

    [Fact]
    public async Task Tools_list_advertises_caching_hints()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        var result = await client.ListToolsAsync(new ListToolsRequestParams(), cts.Token);

        Assert.Null(result.NextCursor);
        Assert.Equal(ExpectedToolCount, result.Tools.Count);
        Assert.Equal(CacheScope.Public, result.CacheScope);
        Assert.NotNull(result.TimeToLive);
        Assert.True(
            result.TimeToLive > TimeSpan.Zero,
            $"tools/list should advertise a positive ttlMs; the tool set is static. Got {result.TimeToLive}.");
    }

    [Fact]
    public async Task Resource_templates_list_advertises_templates_and_caching_hints()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        var result = await client.ListResourceTemplatesAsync(new ListResourceTemplatesRequestParams(), cts.Token);

        var templates = result.ResourceTemplates.Select(t => t.UriTemplate).ToHashSet();
        Assert.Contains("sherlock://assembly/{path}/type/{fullName}", templates);
        Assert.Contains("sherlock://assembly/{path}/docs/{memberId}", templates);
        Assert.Contains("sherlock://nuget/{packageId}/{version}", templates);
        Assert.All(result.ResourceTemplates, t => Assert.Equal("application/json", t.MimeType));
        Assert.Equal(CacheScope.Public, result.CacheScope);
        Assert.True(result.TimeToLive > TimeSpan.Zero);
    }

    [Fact]
    public async Task Server_advertises_completions_capability()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        Assert.NotNull(client.ServerCapabilities.Completions);
    }

    [Fact]
    public async Task Complete_type_template_full_name_returns_types_from_path_context()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        var result = await client.CompleteAsync(
            new CompleteRequestParams
            {
                Ref = new ResourceTemplateReference { Uri = "sherlock://assembly/{path}/type/{fullName}" },
                Argument = new Argument { Name = "fullName", Value = "System.Collections.Generic.List" },
                Context = new CompleteContext
                {
                    Arguments = new Dictionary<string, string> { ["path"] = typeof(string).Assembly.Location }
                }
            },
            cts.Token);

        Assert.Contains("System.Collections.Generic.List`1", result.Completion.Values);
        Assert.All(result.Completion.Values, v => Assert.StartsWith("System.Collections.Generic.List", v, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_members_resource_links_read_back_as_type_info()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        var search = await client.CallToolAsync(
            "search_members",
            new Dictionary<string, object?>
            {
                ["assemblyPath"] = typeof(string).Assembly.Location,
                ["nameContains"] = "IsNullOrEmpty",
                ["memberKinds"] = "method"
            },
            cancellationToken: cts.Token);

        Assert.Equal("search.members", Envelope(search).GetProperty("kind").GetString());
        var link = Assert.Single(search.Content.OfType<ResourceLinkBlock>(), l => l.Name == "System.String");

        var read = await client.ReadResourceAsync(new ReadResourceRequestParams { Uri = link.Uri }, cts.Token);

        var contents = Assert.IsType<TextResourceContents>(Assert.Single(read.Contents));
        Assert.Equal("application/json", contents.MimeType);
        using var doc = JsonDocument.Parse(contents.Text);
        Assert.Equal("type.info", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal(CacheScope.Private, read.CacheScope);
        Assert.True(read.TimeToLive > TimeSpan.Zero);
    }

    [Fact]
    public async Task Read_resource_for_unknown_type_returns_invalid_params()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        var uri = $"sherlock://assembly/{Uri.EscapeDataString(typeof(string).Assembly.Location)}/type/Does.Not.Exist";

        var ex = await Assert.ThrowsAsync<ModelContextProtocol.McpProtocolException>(() =>
            client.ReadResourceAsync(new ReadResourceRequestParams { Uri = uri }, cts.Token).AsTask());

        Assert.Equal(ModelContextProtocol.McpErrorCode.InvalidParams, ex.ErrorCode);
    }

    [Fact]
    public async Task Tools_list_returns_a_deterministic_order()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        var first = await client.ListToolsAsync(new ListToolsRequestParams(), cts.Token);
        var second = await client.ListToolsAsync(new ListToolsRequestParams(), cts.Token);

        var names = first.Tools.Select(t => t.Name).ToArray();

        Assert.Equal(names, second.Tools.Select(t => t.Name));
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
    }

    [Fact]
    public async Task Tools_advertise_behavioural_annotations()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        var tools = (await client.ListToolsAsync(new ListToolsRequestParams(), cts.Token))
            .Tools.ToDictionary(t => t.Name);

        foreach (var tool in tools.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Title), $"Tool '{tool.Name}' should advertise a title.");
            Assert.NotNull(tool.Annotations);
            Assert.NotEqual(true, tool.Annotations!.DestructiveHint);
        }

        var readOnly = tools["get_type_methods"];
        Assert.Equal(true, readOnly.Annotations!.ReadOnlyHint);
        Assert.Equal(false, readOnly.Annotations.OpenWorldHint);

        // Discovery tools scan search roots for an unbounded set of assemblies, so they stay open-world.
        Assert.NotEqual(false, tools["find_assembly_by_class_name"].Annotations!.OpenWorldHint);

        // The only tool that mutates server state.
        var mutating = tools["update_runtime_options"];
        Assert.NotEqual(true, mutating.Annotations!.ReadOnlyHint);
        Assert.Equal(true, mutating.Annotations.IdempotentHint);

        var readOnlyCount = tools.Values.Count(t => t.Annotations?.ReadOnlyHint == true);
        Assert.Equal(ExpectedToolCount - 1, readOnlyCount);
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

    [Fact]
    public async Task Ambiguous_type_name_is_resolved_through_elicitation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var prompts = new List<ElicitRequestParams>();
        await using var client = await ConnectAsync(cts.Token, (request, _) =>
        {
            prompts.Add(request!);
            return ValueTask.FromResult(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["typeName"] = JsonSerializer.SerializeToElement(typeof(Ambiguity.Beta.DuplicateGadget).FullName)
                }
            });
        });

        var result = await CallGetTypeInfo(client, nameof(Ambiguity.Beta.DuplicateGadget), cts.Token);

        var prompt = Assert.Single(prompts);
        var schema = Assert.IsType<ElicitRequestParams.UntitledSingleSelectEnumSchema>(prompt.RequestedSchema!.Properties["typeName"]);
        Assert.Equal([typeof(Ambiguity.Alpha.DuplicateGadget).FullName!, typeof(Ambiguity.Beta.DuplicateGadget).FullName!], schema.Enum);
        var envelope = Envelope(result);
        Assert.Equal("type.info", envelope.GetProperty("kind").GetString());
        Assert.Equal(typeof(Ambiguity.Beta.DuplicateGadget).FullName, envelope.GetProperty("data").GetProperty("FullName").GetString());
    }

    [Fact]
    public async Task Ambiguous_type_name_without_elicitation_returns_candidates()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);

        var result = await CallGetTypeInfo(client, nameof(Ambiguity.Beta.DuplicateGadget), cts.Token);

        var envelope = Envelope(result);
        Assert.Equal("AmbiguousTypeName", envelope.GetProperty("code").GetString());
        Assert.Equal(2, envelope.GetProperty("recommendedParams").GetProperty("candidates").GetArrayLength());
    }

    [Fact]
    public async Task Omitted_tfm_for_multi_target_package_is_chosen_through_elicitation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cache = Directory.CreateTempSubdirectory("sherlock-nuget-");
        try
        {
            foreach (var tfm in new[] { "net8.0", "netstandard2.0" })
            {
                var libDir = Directory.CreateDirectory(Path.Combine(cache.FullName, "multi.tfm", "1.0.0", "lib", tfm));
                File.WriteAllText(Path.Combine(libDir.FullName, "Multi.Tfm.dll"), "");
            }

            var prompts = new List<ElicitRequestParams>();
            await using var client = await ConnectAsync(
                cts.Token,
                (request, _) =>
                {
                    prompts.Add(request!);
                    return ValueTask.FromResult(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement> { ["tfm"] = JsonSerializer.SerializeToElement("netstandard2.0") }
                    });
                },
                new Dictionary<string, string?> { ["NUGET_PACKAGES"] = cache.FullName });

            var result = await client.CallToolAsync(
                "find_assembly_by_nuget_package",
                new Dictionary<string, object?> { ["packageId"] = "Multi.Tfm" },
                cancellationToken: cts.Token);

            var schema = Assert.IsType<ElicitRequestParams.UntitledSingleSelectEnumSchema>(Assert.Single(prompts).RequestedSchema!.Properties["tfm"]);
            Assert.Equal(["net8.0", "netstandard2.0"], schema.Enum);
            Assert.Equal("netstandard2.0", Envelope(result).GetProperty("data").GetProperty("resolvedTfm").GetString());
        }
        finally
        {
            cache.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Long_scan_streams_progress_notifications_when_client_sends_progress_token()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await ConnectAsync(cts.Token);
        var progress = new CollectingProgress();

        var result = await client.CallToolAsync(
            "find_assembly_by_class_name",
            new Dictionary<string, object?>
            {
                ["className"] = nameof(McpStdioProtocolTests),
                ["workingDirectory"] = AppContext.BaseDirectory
            },
            progress,
            cancellationToken: cts.Token);

        Assert.Equal("reflection.findByClassName", Envelope(result).GetProperty("kind").GetString());

        while (progress.Count == 0 && !cts.IsCancellationRequested)
            await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);

        var last = progress.Last;
        Assert.NotNull(last.Total);
        Assert.True(last.Progress > 0);
        Assert.True(last.Progress <= last.Total);
    }

    private sealed class CollectingProgress : IProgress<ModelContextProtocol.ProgressNotificationValue>
    {
        private readonly object _gate = new();
        private readonly List<ModelContextProtocol.ProgressNotificationValue> _reports = [];

        public int Count
        {
            get { lock (_gate) return _reports.Count; }
        }

        public ModelContextProtocol.ProgressNotificationValue Last
        {
            get { lock (_gate) return _reports[^1]; }
        }

        public void Report(ModelContextProtocol.ProgressNotificationValue value)
        {
            lock (_gate) _reports.Add(value);
        }
    }

    private static async Task<McpClient> ConnectAsync(
        CancellationToken cancellationToken,
        Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? elicitationHandler = null,
        IDictionary<string, string?>? environment = null)
    {
        Assert.True(File.Exists(ServerDll), $"Expected server DLL at {ServerDll}");

        var transport = new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments = [ServerDll],
                Name = "sherlock-e2e",
                EnvironmentVariables = environment
            },
            NullLoggerFactory.Instance);

        var options = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "sherlock-e2e-tests", Version = "1.0.0" },
            Handlers = new McpClientHandlers { ElicitationHandler = elicitationHandler }
        };

        return await McpClient.CreateAsync(transport, options, NullLoggerFactory.Instance, cancellationToken);
    }

    private static Task<CallToolResult> CallGetTypeInfo(McpClient client, string typeName, CancellationToken cancellationToken) =>
        client.CallToolAsync(
            "get_type_info",
            new Dictionary<string, object?>
            {
                ["assemblyPath"] = typeof(McpStdioProtocolTests).Assembly.Location,
                ["typeName"] = typeName
            },
            cancellationToken: cancellationToken).AsTask();

    private static async Task<CallToolResult> CallGetTypeMethods(
        McpClient client, int maxItems, string? continuationToken, CancellationToken cancellationToken)
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
