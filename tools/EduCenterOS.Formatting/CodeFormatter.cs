using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace EduCenterOS.Formatting;

internal static class CodeFormatter
{
    internal static string Format(string source)
    {
        var original = CSharpSyntaxTree.ParseText(source).GetRoot();

        if (original.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            throw new InvalidOperationException("Formatting.InvalidSyntax");

        var formatted = source;

        for (var pass = 0; pass < 32; pass++)
        {
            var next = FormatPass(formatted);

            if (next == formatted)
            {
                var result = CSharpSyntaxTree.ParseText(formatted).GetRoot();

                if (!original.DescendantTokens().Select(token => (token.RawKind, token.Text))
                    .SequenceEqual(result.DescendantTokens().Select(token => (token.RawKind, token.Text))))
                    throw new InvalidOperationException("Formatting.SyntaxChanged");

                if (!ContentTrivia(original).SequenceEqual(ContentTrivia(result)))
                    throw new InvalidOperationException("Formatting.CommentsOrDirectivesChanged");

                return formatted;
            }

            formatted = next;
        }

        throw new InvalidOperationException("Formatting.DidNotConverge");
    }

    private static IEnumerable<string> ContentTrivia(SyntaxNode node) => node.DescendantTrivia(descendIntoTrivia: true)
        .Where(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
        .Select(trivia => trivia.ToFullString());

    private static string FormatPass(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var gaps = new Dictionary<TextSpan, string>();

        string Indent(SyntaxToken token)
        {
            var start = source.LastIndexOf('\n', Math.Max(0, token.SpanStart - 1)) + 1;
            var end = start;

            while (end < source.Length && source[end] is ' ' or '\t') end++;

            return source[start..end].Replace("\t", "    ", StringComparison.Ordinal);
        }

        void Gap(SyntaxToken left, SyntaxToken right, int lines, string indent, bool exact = false)
        {
            if (left.RawKind == 0 || right.RawKind == 0 || left.Span.End > right.SpanStart) return;

            var span = TextSpan.FromBounds(left.Span.End, right.SpanStart);
            var current = source[span.Start..span.End];

            if (!current.All(char.IsWhiteSpace)) return;

            var count = exact ? lines : Math.Max(lines, Math.Min(2, current.Count(character => character == '\n')));
            gaps[span] = new string('\n', count) + indent;
        }

        void Members(SyntaxList<MemberDeclarationSyntax> members)
        {
            for (var index = 1; index < members.Count; index++)
            {
                var previous = members[index - 1];
                var current = members[index];
                var compact = previous is FieldDeclarationSyntax or PropertyDeclarationSyntax
                    && current is FieldDeclarationSyntax or PropertyDeclarationSyntax;
                Gap(previous.GetLastToken(), current.GetFirstToken(), compact ? 1 : 2, Indent(current.GetFirstToken()));
            }
        }

        foreach (var node in root.DescendantNodesAndSelf())
        {
            if (node is CompilationUnitSyntax compilation) Members(compilation.Members);

            if (node is BaseNamespaceDeclarationSyntax scope) Members(scope.Members);

            if (node is TypeDeclarationSyntax type && !type.OpenBraceToken.IsMissing && type.OpenBraceToken.RawKind != 0)
            {
                var indent = Indent(type.GetFirstToken());
                Gap(type.OpenBraceToken.GetPreviousToken(), type.OpenBraceToken, 1, indent);
                Gap(type.OpenBraceToken, type.OpenBraceToken.GetNextToken(), 1, type.Members.Count == 0 ? indent : indent + "    ");
                Gap(type.CloseBraceToken.GetPreviousToken(), type.CloseBraceToken, 1, indent);
                Members(type.Members);
            }

            if (node is BlockSyntax block)
            {
                var owner = block.Parent is not null and not BlockSyntax ? block.Parent.GetFirstToken() : block.OpenBraceToken.GetPreviousToken();
                var indent = Indent(owner);
                Gap(block.OpenBraceToken.GetPreviousToken(), block.OpenBraceToken, 1, indent);
                Gap(block.OpenBraceToken, block.OpenBraceToken.GetNextToken(), 1, block.Statements.Count == 0 ? indent : indent + "    ");
                Gap(block.CloseBraceToken.GetPreviousToken(), block.CloseBraceToken, 1, indent);

                for (var index = 1; index < block.Statements.Count; index++)
                    Gap(block.Statements[index - 1].GetLastToken(), block.Statements[index].GetFirstToken(), 1, indent + "    ");
            }

            if (node is AttributeListSyntax attributes && attributes.Parent is MemberDeclarationSyntax)
            {
                var indent = Indent(attributes.GetFirstToken());
                Gap(attributes.GetFirstToken().GetPreviousToken(), attributes.GetFirstToken(), 1, indent);
                Gap(attributes.CloseBracketToken, attributes.CloseBracketToken.GetNextToken(), 1, indent);
            }

            if (node is BaseObjectCreationExpressionSyntax creation && IsErrorConstruction(creation)
                && creation.ArgumentList is { Arguments.Count: > 0 } arguments)
            {
                var indent = Indent(creation.GetFirstToken());
                Gap(arguments.OpenParenToken, arguments.Arguments[0].GetFirstToken(), 1, indent + "    ", exact: true);

                foreach (var comma in arguments.Arguments.GetSeparators())
                    Gap(comma, comma.GetNextToken(), 1, indent + "    ", exact: true);

                Gap(arguments.Arguments.Last().GetLastToken(), arguments.CloseParenToken, 1, indent, exact: true);
            }
        }

        // Clean trivia only: whitespace inside strings, raw strings and comments is data.
        foreach (var token in root.DescendantTokens())
        {
            var next = token.GetNextToken();

            if (next.RawKind == 0) continue;

            var span = TextSpan.FromBounds(token.Span.End, next.SpanStart);
            var current = source[span.Start..span.End];

            if (!gaps.ContainsKey(span) && current.All(char.IsWhiteSpace))
                gaps[span] = Regex.Replace(current, "[ \\t]+(?=\\r?\\n)", "").Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        var formatted = SourceText.From(source).WithChanges(gaps.Select(gap => new TextChange(gap.Key, gap.Value)).OrderBy(change => change.Span.Start)).ToString();
        var last = CSharpSyntaxTree.ParseText(formatted).GetRoot().GetLastToken(includeZeroWidth: true).GetPreviousToken();

        if (formatted[last.Span.End..].All(char.IsWhiteSpace)) formatted = formatted[..last.Span.End] + "\n";

        return formatted;
    }

    private static bool IsErrorConstruction(BaseObjectCreationExpressionSyntax creation)
    {
        if (creation is ObjectCreationExpressionSyntax explicitNew)
            return explicitNew.Type.ToString() is "Error" or "EduCenterOS.BuildingBlocks.Results.Error";

        if (creation.ArgumentList is { Arguments.Count: >= 3 } arguments
            && arguments.Arguments[1].DescendantTokens().Any(token => token.ValueText == "ErrorCategory")) return true;

        return creation.Parent is ArrowExpressionClauseSyntax arrow && arrow.Parent switch
        {
            PropertyDeclarationSyntax property => property.Type.ToString() == "Error",
            MethodDeclarationSyntax method => method.ReturnType.ToString() == "Error",
            _ => false
        } || creation.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } }
            && declaration.Type.ToString() == "Error"
            || creation.Ancestors().OfType<InvocationExpressionSyntax>().Any(invocation => invocation.Expression.ToString() == "Error.Validation");
    }
}
