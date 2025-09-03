namespace Sherlock.MCP.Runtime.Contracts.XmlDocs;

public record XmlDocInfo(
    string? Summary,
    string? Remarks,
    string? Returns,
    XmlParamInfo[] Params
);
