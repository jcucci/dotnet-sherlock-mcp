namespace Sherlock.MCP.Runtime.Contracts.Common;

public sealed record PagedResult<T>(int Total, T[] Items);
