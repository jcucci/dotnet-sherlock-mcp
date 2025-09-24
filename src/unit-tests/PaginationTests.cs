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