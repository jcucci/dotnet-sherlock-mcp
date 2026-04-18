using System.Text.Json;
using Sherlock.MCP.Runtime;
using Sherlock.MCP.Runtime.Caching;
using Sherlock.MCP.Runtime.Telemetry;
using Sherlock.MCP.Server.Tools;
using Sherlock.MCP.Server.Middleware;
using System.Reflection;

namespace Sherlock.MCP.Tests;

public class PaginationTests
{
    private readonly ITypeAnalysisService _typeAnalysisService = new TypeAnalysisService();
    private readonly IMemberAnalysisService _memberAnalysisService = new MemberAnalysisService();
    private readonly RuntimeOptions _runtimeOptions = new RuntimeOptions();
    private readonly ToolMiddleware _middleware;
    private readonly string _testAssemblyPath;

    public PaginationTests()
    {
        var cache = new InMemoryToolResponseCache();
        var telemetry = new NoopTelemetry();
        _middleware = new ToolMiddleware(cache, telemetry, _runtimeOptions);
        _testAssemblyPath = Assembly.GetExecutingAssembly().Location;
    }

    [Fact]
    public void AnalyzeAssembly_WithPagination_ReturnsCorrectPageSize()
    {
        // Act
        var result = ReflectionTools.AnalyzeAssembly(_runtimeOptions, _testAssemblyPath, maxItems: 5);

        // Assert
        Assert.NotNull(result);
        Assert.DoesNotContain("\"error\"", result);

        // Parse JSON to check pagination
        var jsonDoc = JsonDocument.Parse(result);
        var data = jsonDoc.RootElement.GetProperty("data");

        var totalTypes = data.GetProperty("totalTypeCount").GetInt32();
        var returnedTypes = data.GetProperty("returnedTypeCount").GetInt32();
        var types = data.GetProperty("types");

        Assert.True(totalTypes > 5, "Should have more than 5 total types");
        Assert.Equal(5, returnedTypes);
        Assert.Equal(5, types.GetArrayLength());
        Assert.True(data.TryGetProperty("nextToken", out var nextTokenProp), "Should have nextToken when more data available");
    }

    [Fact]
    public void GetTypesFromAssembly_WithPagination_ReturnsCorrectPageSize()
    {
        // Act
        var result = TypeAnalysisTools.GetTypesFromAssembly(_typeAnalysisService, _testAssemblyPath, maxItems: 3);

        // Assert
        Assert.NotNull(result);
        Assert.DoesNotContain("\"error\"", result);

        // Parse JSON to check pagination
        var jsonDoc = JsonDocument.Parse(result);
        var data = jsonDoc.RootElement.GetProperty("data");

        var totalTypes = data.GetProperty("totalTypeCount").GetInt32();
        var returnedTypes = data.GetProperty("returnedTypeCount").GetInt32();
        var types = data.GetProperty("types");

        Assert.True(totalTypes > 3, "Should have more than 3 total types");
        Assert.Equal(3, returnedTypes);
        Assert.Equal(3, types.GetArrayLength());
        Assert.True(data.TryGetProperty("nextToken", out var nextTokenProp), "Should have nextToken when more data available");
    }

    [Fact]
    public void GetTypeProperties_WithPagination_ReturnsCorrectPageSize()
    {
        // Arrange
        var typeName = typeof(TestSampleClass).FullName!;

        // Act
        var result = MemberAnalysisTools.GetTypeProperties(
            _memberAnalysisService, _middleware, _runtimeOptions,
            _testAssemblyPath, typeName, maxItems: 2);

        // Assert
        Assert.NotNull(result);
        Assert.DoesNotContain("\"error\"", result);

        // Parse JSON to check pagination
        var jsonDoc = JsonDocument.Parse(result);
        var data = jsonDoc.RootElement.GetProperty("data");

        var total = data.GetProperty("total").GetInt32();
        var count = data.GetProperty("count").GetInt32();
        var properties = data.GetProperty("properties");

        Assert.True(count <= 2, "Should return at most 2 properties");
        Assert.Equal(count, properties.GetArrayLength());

        if (total > 2)
        {
            Assert.True(data.TryGetProperty("nextToken", out var nextTokenProp), "Should have nextToken when more data available");
        }
    }

    [Fact]
    public void ContinuationToken_WithInvalidToken_ReturnsError()
    {
        // Act
        var result = ReflectionTools.AnalyzeAssembly(_runtimeOptions, _testAssemblyPath, maxItems: 5, continuationToken: "invalid-token");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("InvalidContinuationToken", result);
    }

    [Fact]
    public void GetTypesFromAssembly_DefaultProjection_IsSummary()
    {
        var result = TypeAnalysisTools.GetTypesFromAssembly(_typeAnalysisService, _testAssemblyPath, maxItems: 3);

        Assert.DoesNotContain("\"error\"", result);
        var jsonDoc = JsonDocument.Parse(result);
        var data = jsonDoc.RootElement.GetProperty("data");

        Assert.Equal("summary", data.GetProperty("projection").GetString());

        var types = data.GetProperty("types");
        Assert.True(types.GetArrayLength() > 0, "Expected at least one type in the page");
        var first = types[0];

        Assert.True(first.TryGetProperty("FullName", out _), "Summary type should have FullName");
        Assert.True(first.TryGetProperty("Namespace", out _), "Summary type should have Namespace");
        Assert.True(first.TryGetProperty("Kind", out _), "Summary type should have Kind");

        Assert.False(first.TryGetProperty("Attributes", out _), "Summary type should NOT include Attributes");
        Assert.False(first.TryGetProperty("Interfaces", out _), "Summary type should NOT include Interfaces");
        Assert.False(first.TryGetProperty("GenericParameters", out _), "Summary type should NOT include GenericParameters");
        Assert.False(first.TryGetProperty("NestedTypes", out _), "Summary type should NOT include NestedTypes");
    }

    [Fact]
    public void GetTypesFromAssembly_FullProjection_IncludesHeavyFields()
    {
        var result = TypeAnalysisTools.GetTypesFromAssembly(_typeAnalysisService, _testAssemblyPath, maxItems: 3, projection: "full");

        Assert.DoesNotContain("\"error\"", result);
        var jsonDoc = JsonDocument.Parse(result);
        var data = jsonDoc.RootElement.GetProperty("data");

        Assert.Equal("full", data.GetProperty("projection").GetString());

        var types = data.GetProperty("types");
        Assert.True(types.GetArrayLength() > 0, "Expected at least one type in the page");
        var first = types[0];

        Assert.True(first.TryGetProperty("Attributes", out _), "Full type should include Attributes");
        Assert.True(first.TryGetProperty("Interfaces", out _), "Full type should include Interfaces");
        Assert.True(first.TryGetProperty("GenericParameters", out _), "Full type should include GenericParameters");
    }

    [Fact]
    public void GetTypesFromAssembly_InvalidProjection_ReturnsError()
    {
        var result = TypeAnalysisTools.GetTypesFromAssembly(_typeAnalysisService, _testAssemblyPath, projection: "partial");

        Assert.Contains("InvalidProjection", result);
    }

    [Fact]
    public void GetTypeMethods_DefaultProjection_IsSummary()
    {
        var typeName = typeof(TestSampleClass).FullName!;

        var result = MemberAnalysisTools.GetTypeMethods(
            _memberAnalysisService, _middleware, _runtimeOptions,
            _testAssemblyPath, typeName, maxItems: 3);

        Assert.DoesNotContain("\"error\"", result);
        var jsonDoc = JsonDocument.Parse(result);
        var data = jsonDoc.RootElement.GetProperty("data");

        Assert.Equal("summary", data.GetProperty("projection").GetString());

        var methods = data.GetProperty("methods");
        Assert.True(methods.GetArrayLength() > 0, "Expected at least one method in the page");
        var first = methods[0];

        Assert.True(first.TryGetProperty("name", out _), "Summary method should have name");
        Assert.True(first.TryGetProperty("signature", out _), "Summary method should have signature");

        Assert.False(first.TryGetProperty("parameters", out _), "Summary method should NOT include parameters");
        Assert.False(first.TryGetProperty("attributes", out _), "Summary method should NOT include attributes");
        Assert.False(first.TryGetProperty("returnType", out _), "Summary method should NOT include returnType");
    }

    [Fact]
    public void GetTypeMethods_FullProjection_IncludesParameters()
    {
        var typeName = typeof(TestSampleClass).FullName!;

        var result = MemberAnalysisTools.GetTypeMethods(
            _memberAnalysisService, _middleware, _runtimeOptions,
            _testAssemblyPath, typeName, maxItems: 3, projection: "full");

        Assert.DoesNotContain("\"error\"", result);
        var jsonDoc = JsonDocument.Parse(result);
        var data = jsonDoc.RootElement.GetProperty("data");

        Assert.Equal("full", data.GetProperty("projection").GetString());

        var methods = data.GetProperty("methods");
        Assert.True(methods.GetArrayLength() > 0, "Expected at least one method in the page");
        var first = methods[0];

        Assert.True(first.TryGetProperty("parameters", out _), "Full method should include parameters");
        Assert.True(first.TryGetProperty("returnType", out _), "Full method should include returnType");
        Assert.True(first.TryGetProperty("attributes", out _), "Full method should include attributes");
    }

    [Fact]
    public void GetTypeMethods_ContinuationToken_RoundTripsToNextPage()
    {
        var typeName = typeof(TestSampleClass).FullName!;

        var page1 = MemberAnalysisTools.GetTypeMethods(
            _memberAnalysisService, _middleware, _runtimeOptions,
            _testAssemblyPath, typeName, maxItems: 1, noCache: true);

        Assert.DoesNotContain("\"error\"", page1);
        var page1Doc = JsonDocument.Parse(page1);
        var page1Data = page1Doc.RootElement.GetProperty("data");
        Assert.True(page1Data.GetProperty("total").GetInt32() > 1, "Need >1 method on TestSampleClass for round-trip test");

        var nextToken = page1Data.GetProperty("nextToken").GetString();
        Assert.False(string.IsNullOrEmpty(nextToken), "Page 1 should mint a nextToken when more pages remain");

        var page2 = MemberAnalysisTools.GetTypeMethods(
            _memberAnalysisService, _middleware, _runtimeOptions,
            _testAssemblyPath, typeName, maxItems: 1, continuationToken: nextToken, noCache: true);

        Assert.DoesNotContain("InvalidContinuationToken", page2);
        Assert.DoesNotContain("\"error\"", page2);
        var page2Data = JsonDocument.Parse(page2).RootElement.GetProperty("data");
        Assert.Equal(1, page2Data.GetProperty("count").GetInt32());
    }

    [Fact]
    public void GetTypeProperties_ContinuationToken_RoundTripsToNextPage()
    {
        var typeName = typeof(TestSampleClass).FullName!;

        var page1 = MemberAnalysisTools.GetTypeProperties(
            _memberAnalysisService, _middleware, _runtimeOptions,
            _testAssemblyPath, typeName, includeNonPublic: true, maxItems: 1, noCache: true);

        Assert.DoesNotContain("\"error\"", page1);
        var page1Data = JsonDocument.Parse(page1).RootElement.GetProperty("data");
        Assert.True(page1Data.GetProperty("total").GetInt32() > 1, "Need >1 property on TestSampleClass for round-trip test");

        var nextToken = page1Data.GetProperty("nextToken").GetString();
        Assert.False(string.IsNullOrEmpty(nextToken), "Page 1 should mint a nextToken when more pages remain");

        var page2 = MemberAnalysisTools.GetTypeProperties(
            _memberAnalysisService, _middleware, _runtimeOptions,
            _testAssemblyPath, typeName, includeNonPublic: true, maxItems: 1, continuationToken: nextToken, noCache: true);

        Assert.DoesNotContain("InvalidContinuationToken", page2);
        Assert.DoesNotContain("\"error\"", page2);
        var page2Data = JsonDocument.Parse(page2).RootElement.GetProperty("data");
        Assert.Equal(1, page2Data.GetProperty("count").GetInt32());
    }

    [Fact]
    public void GetTypeMethods_InvalidProjection_ReturnsError()
    {
        var typeName = typeof(TestSampleClass).FullName!;

        var result = MemberAnalysisTools.GetTypeMethods(
            _memberAnalysisService, _middleware, _runtimeOptions,
            _testAssemblyPath, typeName, projection: "partial");

        Assert.Contains("InvalidProjection", result);
    }

    [Fact]
    public void DefaultPageSize_UsesConfiguredValue()
    {
        // Arrange
        var originalDefaultMaxItems = _runtimeOptions.DefaultMaxItems;
        _runtimeOptions.DefaultMaxItems = 5; // Use a smaller page size for testing

        try
        {
            // Act - don't specify maxItems to test default
            var result = ReflectionTools.AnalyzeAssembly(_runtimeOptions, _testAssemblyPath);

            // Assert
            Assert.NotNull(result);
            Assert.DoesNotContain("\"error\"", result);

            // Parse JSON to check default page size is applied
            var jsonDoc = JsonDocument.Parse(result);
            var data = jsonDoc.RootElement.GetProperty("data");

            var returnedTypes = data.GetProperty("returnedTypeCount").GetInt32();
            var totalTypes = data.GetProperty("totalTypeCount").GetInt32();

            // Should return either all types (if total <= 5) or exactly 5 types
            var expectedCount = Math.Min(totalTypes, 5);
            Assert.Equal(expectedCount, returnedTypes);

            // If there are more types than our page size, should have pagination
            if (totalTypes > 5)
            {
                Assert.True(data.TryGetProperty("nextToken", out var nextTokenProp), "Should have nextToken when more data available");
            }
        }
        finally
        {
            // Restore original value
            _runtimeOptions.DefaultMaxItems = originalDefaultMaxItems;
        }
    }
}