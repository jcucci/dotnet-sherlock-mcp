using Sherlock.MCP.Runtime;

namespace Sherlock.MCP.Tests;

public class NameSuggestionsTests
{
    private static readonly string[] TypeNames =
    [
        "App.Services.OrderService",
        "App.Services.OrderValidator",
        "App.Models.Order",
        "App.Models.Customer",
        "App.Outer+Inner"
    ];

    [Fact]
    public void Closest_Typo_ReturnsNearMiss() =>
        Assert.Equal("App.Services.OrderService", NameSuggestions.Closest(TypeNames, "OrderSevice")[0]);

    [Fact]
    public void Closest_DifferentCase_ReturnsExactMatchFirst() =>
        Assert.Equal("App.Models.Customer", NameSuggestions.Closest(TypeNames, "customer")[0]);

    [Fact]
    public void Closest_Substring_ReturnsContainingNames()
    {
        var suggestions = NameSuggestions.Closest(TypeNames, "Order");

        Assert.Equal("App.Models.Order", suggestions[0]);
        Assert.Contains("App.Services.OrderService", suggestions);
        Assert.Contains("App.Services.OrderValidator", suggestions);
    }

    [Fact]
    public void Closest_NestedTypeSimpleName_Matches() =>
        Assert.Contains("App.Outer+Inner", NameSuggestions.Closest(TypeNames, "Iner"));

    [Fact]
    public void Closest_Unrelated_ReturnsEmpty() =>
        Assert.Empty(NameSuggestions.Closest(TypeNames, "Zebra"));

    [Fact]
    public void Closest_WholeNames_ComparesEverySegment()
    {
        string[] files = ["Microsoft.Extensions.Hosting.Abstractions", "Microsoft.Extensions.Logging.Abstractions", "System.Abstractions"];

        var suggestions = NameSuggestions.Closest(files, "Microsoft.Extensions.Loging.Abstractions", compareSimpleNames: false);

        Assert.Equal("Microsoft.Extensions.Logging.Abstractions", suggestions[0]);
        Assert.DoesNotContain("System.Abstractions", suggestions);
    }

    [Fact]
    public void Closest_CapsResults()
    {
        var many = Enumerable.Range(0, 20).Select(i => $"Ns.Widget{i}");

        Assert.Equal(3, NameSuggestions.Closest(many, "Widget", max: 3).Count);
    }

    [Fact]
    public void ForMembers_FiltersByKind()
    {
        var suggestions = NameSuggestions.ForMembers(typeof(TestSampleClass), "PublicEvnt", "method");

        Assert.DoesNotContain("PublicEvent", suggestions);
    }

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("same", "same", 0)]
    public void Distance_ComputesLevenshtein(string a, string b, int expected) =>
        Assert.Equal(expected, NameSuggestions.Distance(a, b));
}
