// Implyzer
// Copyright (c) KryKom 2026

using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Implyzer;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IndirectImplCodeFixProvider)), Shared]
public class IndirectImplCodeFixProvider : CodeFixProvider {
    public override ImmutableArray<string> FixableDiagnosticIds => [Rules.IndirectImpl.Id];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        foreach (var diagnostic in context.Diagnostics) {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var node = root?.FindNode(diagnostic.Location.SourceSpan);

            // Expecting a BaseTypeSyntax (e.g., SimpleBaseTypeSyntax)
            if (node is not BaseTypeSyntax baseTypeNode) continue;

            diagnostic.Properties.TryGetValue("Suggestion", out var suggestion);

            if (!string.IsNullOrEmpty(suggestion)) {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: $"Implement '{suggestion}' instead",
                        createChangedDocument: c => ReplaceInterfaceAsync(context.Document, baseTypeNode, suggestion!, c),
                        equivalenceKey: "ReplaceInterface"
                    ),
                    diagnostic
                );
            } 

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Remove interface implementation",
                    createChangedDocument: c => RemoveInterfaceAsync(context.Document, baseTypeNode, c),
                    equivalenceKey: "RemoveInterface"
                ),
                diagnostic
            );
        }
    }

    private static async Task<Document> ReplaceInterfaceAsync(
        Document          document,
        BaseTypeSyntax    oldNode,
        string            suggestion,
        CancellationToken cancellationToken) 
    {
        var newTypeNode = SyntaxFactory.ParseTypeName(suggestion);
        var newNode = SyntaxFactory.SimpleBaseType(newTypeNode).WithTriviaFrom(oldNode);

        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null) return document;

        var newRoot = root.ReplaceNode(oldNode, newNode);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> RemoveInterfaceAsync(
        Document          document,
        BaseTypeSyntax    node,
        CancellationToken cancellationToken) 
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null) return document;

        var newRoot = root.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia);
        return document.WithSyntaxRoot(newRoot!);
    }
}
