using System.Reflection;
using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Runtime.ApiDiff;

public interface IApiDiffService
{
    ApiDiffResult Compare(
        string leftAssemblyPath,
        IReadOnlyList<string>? leftSearchDirectories,
        string rightAssemblyPath,
        IReadOnlyList<string>? rightSearchDirectories,
        string? namespacePrefix = null,
        CancellationToken cancellationToken = default);
}

public sealed class ApiDiffService : IApiDiffService
{
    private readonly IInspectionContextProvider _contexts;

    public ApiDiffService(IInspectionContextProvider contexts) => _contexts = contexts;

    public ApiDiffResult Compare(
        string leftAssemblyPath,
        IReadOnlyList<string>? leftSearchDirectories,
        string rightAssemblyPath,
        IReadOnlyList<string>? rightSearchDirectories,
        string? namespacePrefix = null,
        CancellationToken cancellationToken = default)
    {
        using var left = _contexts.Acquire(leftAssemblyPath, leftSearchDirectories);
        using var right = _contexts.Acquire(rightAssemblyPath, rightSearchDirectories);
        var warnings = new List<string>();
        var leftSurface = ApiSurfaceReader.Read(left.Context.GetTypes(), namespacePrefix, warnings, cancellationToken);
        var rightSurface = ApiSurfaceReader.Read(right.Context.GetTypes(), namespacePrefix, warnings, cancellationToken);
        var changes = ApiSurfaceComparer.Compare(leftSurface, rightSurface, cancellationToken);
        return new ApiDiffResult(Identity(left.Assembly), Identity(right.Assembly), changes, warnings.Distinct().ToArray());
    }

    private static ApiAssemblyIdentity Identity(Assembly assembly)
    {
        var name = assembly.GetName();
        return new ApiAssemblyIdentity(name.Name ?? assembly.FullName ?? "", name.Version?.ToString());
    }
}
