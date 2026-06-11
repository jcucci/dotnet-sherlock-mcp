using Sherlock.MCP.Runtime.Contracts.Common;
using Sherlock.MCP.Runtime.Contracts.Search;

namespace Sherlock.MCP.Runtime;

public interface ISearchService
{
    PagedResult<MemberSearchHit> SearchMembers(
        string assemblyPath, string nameContains, SearchOptions options, int offset, int pageSize);
}
