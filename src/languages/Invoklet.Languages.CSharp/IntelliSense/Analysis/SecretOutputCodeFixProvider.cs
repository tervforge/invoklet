using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Invoklet.Languages.CSharp.IntelliSense.Analysis;

/// <summary>
/// The fix of <see cref="SecretOutputAnalyzer"/>: removing the Dump turns <c>x.Dump()</c> into <c>x</c>, and the line goes only when what is
/// left could not stand as a statement (<c>token;</c>); removing the output takes away a <c>Console.Write</c>/<c>WriteLine</c> statement.
/// </summary>
internal sealed class SecretOutputCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [SecretOutputAnalyzer.Id];

    // Each output is a decision of its own.
    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics[0];
        if (root?.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>()
            is not { } invocation) return;
        var isDump = diagnostic.Properties.TryGetValue(SecretOutputAnalyzer.SinkProperty, out var sink) && sink == SecretOutputAnalyzer.DumpSink;
        diagnostic.Properties.TryGetValue(SecretOutputAnalyzer.SecretProperty, out var secret);
        var statement = invocation.Parent as ExpressionStatementSyntax;

        SyntaxNode? fixedRoot;
        if (isDump && Receiver(invocation) is { } receiver && (statement is null || CanStandAlone(receiver)))
            fixedRoot = root.ReplaceNode(invocation, receiver.WithTriviaFrom(invocation));
        else if (statement is not null)
            fixedRoot = Remove(root, statement.Parent is GlobalStatementSyntax global ? global : statement);
        else
            return;
        if (fixedRoot is null) return;

        context.RegisterCodeFix(CodeAction.Create(
            isDump ? $"Remove .Dump() of the secret '{secret}'" : $"Remove the output of the secret '{secret}'",
            _ => Task.FromResult(context.Document.WithSyntaxRoot(fixedRoot)),
            equivalenceKey: SecretOutputAnalyzer.Id + sink), diagnostic);
    }

    // What Dump was called on: `x` of `x.Dump(..)`, or the first argument of `ObjectExtensions.Dump(x, ..)`.
    private static ExpressionSyntax? Receiver(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "ObjectExtensions" } }
            => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression,
        MemberAccessExpressionSyntax member => member.Expression,
        _ => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression,
    };

    // What C# accepts as a statement of its own: `Util.GetSecret("x");` stays, `token;` doesn't.
    private static bool CanStandAlone(ExpressionSyntax expression) => expression is
        InvocationExpressionSyntax or AssignmentExpressionSyntax or AwaitExpressionSyntax or ObjectCreationExpressionSyntax
        or PostfixUnaryExpressionSyntax or PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreIncrementExpression or (int)SyntaxKind.PreDecrementExpression };

    // The whole line goes; comments written above it stay.
    private static SyntaxNode? Remove(SyntaxNode root, SyntaxNode statement)
    {
        var hasComments = statement.GetLeadingTrivia().Any(trivia =>
            trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia));
        return root.RemoveNode(statement, hasComments ? SyntaxRemoveOptions.KeepLeadingTrivia : SyntaxRemoveOptions.KeepNoTrivia);
    }
}
