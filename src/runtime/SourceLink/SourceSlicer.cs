using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sherlock.MCP.Runtime.SourceLink;

public sealed record SourceSlice(string Text, int StartLine, int EndLine);

public static class SourceSlicer
{
    public static string Decode(byte[] content)
    {
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static SourceSlice Slice(string document, MemberSourceLocation location)
    {
        var lines = document.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var (start, end) = MemberLines(document, location.AnchorLine) ?? (location.StartLine, location.EndLine);
        start = Math.Clamp(start, 1, lines.Length);
        end = Math.Clamp(end, start, lines.Length);
        return new SourceSlice(Dedent(lines[(start - 1)..end]), start, end);
    }

    private static (int Start, int End)? MemberLines(string document, int anchorLine)
    {
        var tree = CSharpSyntaxTree.ParseText(document);
        var text = tree.GetText();
        if (anchorLine < 1 || anchorLine > text.Lines.Count) return null;

        var line = text.Lines[anchorLine - 1];
        var lineText = line.ToString();
        var position = line.Start + (lineText.Length - lineText.TrimStart().Length);
        var member = tree.GetRoot().FindToken(position).Parent?
            .AncestorsAndSelf()
            .FirstOrDefault(node => node is BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax or BaseTypeDeclarationSyntax);
        if (member is null or BaseTypeDeclarationSyntax) return null;

        var docComment = member.GetLeadingTrivia().FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
        var startPosition = docComment.RawKind != 0 ? docComment.FullSpan.Start : member.SpanStart;
        return (text.Lines.GetLineFromPosition(startPosition).LineNumber + 1,
                text.Lines.GetLineFromPosition(member.Span.End).LineNumber + 1);
    }

    private static string Dedent(string[] lines)
    {
        var indent = lines
            .Where(line => line.Trim().Length > 0)
            .Select(line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();
        return string.Join('\n', lines.Select(line => line.Length >= indent ? line[indent..] : line.TrimStart()));
    }
}
