using System.Reflection;
using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Inspection;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Middleware;
using Sherlock.MCP.Server.Tools;

namespace Sherlock.MCP.Tests;

public class GetTypeMembersTests
{
    private static readonly string TestAssemblyPath = Assembly.GetExecutingAssembly().Location;
    private static readonly string SampleType = typeof(TestSampleClass).FullName!;

    private readonly IMemberAnalysisService _service = new MemberAnalysisService();
    private readonly RuntimeOptions _options = new();
    private readonly IInspectionContextProvider _contexts;
    private readonly ToolMiddleware _middleware;

    public GetTypeMembersTests()
    {
        _contexts = new SharedInspectionContextProvider(_options);
        _middleware = new ToolMiddleware(new InMemoryToolResponseCache(), new NoopTelemetry(), _options);
    }

    [Fact]
    public void DefaultProjection_IsSummaryAcrossAllKinds()
    {
        var data = Data(Call());

        Assert.Equal("summary", data.GetProperty("projection").GetString());
        Assert.Equal(["constructor", "property", "field", "event", "method"], Strings(data.GetProperty("kinds")));

        var members = data.GetProperty("members").EnumerateArray().ToArray();
        Assert.Equal(data.GetProperty("total").GetInt32(), members.Length);
        Assert.All(members, member =>
            Assert.Equal(["kind", "name", "signature"], member.EnumerateObject().Select(p => p.Name).ToArray()));
        Assert.Equal(
            ["constructor", "property", "field", "event", "method"],
            members.Select(m => m.GetProperty("kind").GetString()!).Distinct().ToArray());
    }

    [Fact]
    public void CountsByKind_SumToTotal()
    {
        var data = Data(Call());

        var counts = data.GetProperty("countsByKind").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
        Assert.Equal(data.GetProperty("total").GetInt32(), counts.Values.Sum());
        Assert.Equal(2, counts["constructor"]);
        Assert.Equal(3, counts["field"]);
    }

    [Theory]
    [InlineData("field,event")]
    [InlineData("EVENT|Field")]
    [InlineData(" field , event ")]
    public void Kinds_FiltersToRequestedKinds(string kinds)
    {
        var data = Data(Call(kinds: kinds));

        Assert.Equal(["field", "event"], Strings(data.GetProperty("kinds")));
        var returned = data.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("kind").GetString()).ToHashSet();
        Assert.Equal(new HashSet<string?> { "field", "event" }, returned);
        Assert.Contains(data.GetProperty("members").EnumerateArray(), m => m.GetProperty("name").GetString() == "PublicEvent");
    }

    [Fact]
    public void Kinds_Unknown_ReturnsInvalidArgument()
    {
        var json = Call(kinds: "method,widget");

        Assert.Contains("InvalidArgument", json);
        Assert.Contains("widget", json);
    }

    [Fact]
    public void Projection_Invalid_ReturnsError() =>
        Assert.Contains("InvalidProjection", Call(projection: "verbose"));

    [Fact]
    public void FullProjection_AddsPerKindFields()
    {
        var members = Data(Call(projection: "full")).GetProperty("members").EnumerateArray().ToArray();

        var method = members.Single(m => m.GetProperty("name").GetString() == nameof(TestSampleClass.MethodWithParameters));
        Assert.Equal("method", method.GetProperty("kind").GetString());
        Assert.Equal(3, method.GetProperty("parameters").GetArrayLength());
        Assert.True(method.TryGetProperty("returnType", out _));

        var property = members.Single(m => m.GetProperty("name").GetString() == "PublicProperty");
        Assert.True(property.GetProperty("canWrite").GetBoolean());

        var field = members.Single(m => m.GetProperty("name").GetString() == nameof(TestSampleClass.ConstantField));
        Assert.True(field.GetProperty("isConst").GetBoolean());

        var eventMember = members.Single(m => m.GetProperty("name").GetString() == nameof(TestSampleClass.PublicEvent));
        Assert.True(eventMember.TryGetProperty("eventHandlerTypeName", out _));

        var constructor = members.First(m => m.GetProperty("kind").GetString() == "constructor");
        Assert.Equal(".ctor", constructor.GetProperty("name").GetString());
        Assert.True(constructor.TryGetProperty("attributes", out _));
        Assert.True(constructor.TryGetProperty("parameters", out _));
    }

    [Fact]
    public void StaticConstructor_IsNamedCctor()
    {
        var members = Data(Call(kinds: "constructor", includeNonPublic: true)).GetProperty("members").EnumerateArray();

        Assert.Contains(members, m => m.GetProperty("name").GetString() == ".cctor");
    }

    [Fact]
    public void ContinuationToken_PagesAcrossKindBoundaries()
    {
        var all = Data(Call()).GetProperty("members").EnumerateArray()
            .Select(m => m.GetProperty("signature").GetString())
            .ToArray();

        var paged = new List<string?>();
        string? token = null;
        do
        {
            var data = Data(Call(maxItems: 3, continuationToken: token));
            paged.AddRange(data.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("signature").GetString()));
            token = data.GetProperty("nextToken").GetString();
        } while (token is not null);

        Assert.Equal(all, paged);
    }

    [Fact]
    public void ContinuationToken_FromDifferentKinds_IsRejected()
    {
        var token = Data(Call(maxItems: 1)).GetProperty("nextToken").GetString();

        Assert.Contains("InvalidContinuationToken", Call(kinds: "method", continuationToken: token));
    }

    [Fact]
    public void NameContains_FiltersAcrossKinds()
    {
        var names = Data(Call(nameContains: "Public")).GetProperty("members").EnumerateArray()
            .Select(m => m.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("PublicField", names);
        Assert.Contains("PublicProperty", names);
        Assert.Contains("PublicEvent", names);
        Assert.Contains("OnPublicEvent", names);
        Assert.DoesNotContain("StaticMethod", names);
    }

    [Fact]
    public void UnknownType_ReturnsTypeNotFound() =>
        Assert.Contains("TypeNotFound", Call(typeName: "Sherlock.MCP.Tests.DoesNotExist"));

    private string Call(
        string? typeName = null,
        string? kinds = null,
        bool includeNonPublic = false,
        string? nameContains = null,
        int? maxItems = null,
        string? continuationToken = null,
        string projection = "summary") =>
        MemberAnalysisTools.GetTypeMembers(
            _service, _contexts, _middleware, _options,
            TestAssemblyPath, typeName ?? SampleType,
            kinds: kinds,
            includeNonPublic: includeNonPublic,
            nameContains: nameContains,
            maxItems: maxItems ?? 100,
            continuationToken: continuationToken,
            projection: projection,
            noCache: true).Text();

    private static JsonElement Data(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal("member.members", root.GetProperty("kind").GetString());
        return root.GetProperty("data");
    }

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(e => e.GetString()!).ToArray();
}
