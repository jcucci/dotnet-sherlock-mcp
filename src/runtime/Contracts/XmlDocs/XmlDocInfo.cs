namespace Sherlock.MCP.Runtime.Contracts.XmlDocs;

public record XmlParamInfo(string Name, string Text);

public record XmlDocInfo(
    string? Summary,
    string? Remarks,
    string? Returns,
    XmlParamInfo[] Params
);

