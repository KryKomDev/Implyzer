// Implyzer
// Copyright (c) KryKom 2026

using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Implyzer;

[Shared]
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ImplTypeConstructorCodeFixProvider))]
public class ImplTypeConstructorCodeFixProvider : CodeFixProvider {
    public override ImmutableArray<string> FixableDiagnosticIds => [Rules.Constructor.Id];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        foreach (var diagnostic in context.Diagnostics) {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var node = root?.FindNode(diagnostic.Location.SourceSpan);

            if (node is not TypeDeclarationSyntax typeDecl ||
                (typeDecl.Kind() != SyntaxKind.ClassDeclaration && typeDecl.Kind() != SyntaxKind.RecordDeclaration))
                continue;

            // Check if we need to add a constructor or make an existing one public
            var ctor = typeDecl.Members.OfType<ConstructorDeclarationSyntax>()
                .FirstOrDefault(
                    c => c.ParameterList.Parameters.Count == 0 && !c.Modifiers
                        .Any(m => m.IsKind(SyntaxKind.StaticKeyword))
                );

            var title = ctor != null
                ? "Make parameterless constructor public"
                : "Add public parameterless constructor";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    c => FixConstructorAsync(context.Document, typeDecl, ctor, c),
                    title
                ),
                diagnostic
            );
        }
    }

    private static async Task<Document> FixConstructorAsync(
        Document                      document,
        TypeDeclarationSyntax         typeDecl,
        ConstructorDeclarationSyntax? existingCtor,
        CancellationToken             cancellationToken
    ) {
        var root = await document.GetSyntaxRootAsync(cancellationToken);

        if (root == null)
            return document;

        TypeDeclarationSyntax newTypeDecl;

        if (existingCtor != null) {
            var newModifiers = existingCtor.Modifiers
                .Where(
                    m => !m.IsKind(SyntaxKind.PrivateKeyword)  &&
                        !m.IsKind(SyntaxKind.ProtectedKeyword) &&
                        !m.IsKind(SyntaxKind.InternalKeyword)
                )
                .ToList();

            if (!newModifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
                newModifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.PublicKeyword));

            var newCtor = existingCtor.WithModifiers(SyntaxFactory.TokenList(newModifiers));
            newTypeDecl = typeDecl.ReplaceNode(existingCtor, newCtor);
        }
        else {
            var newCtor = SyntaxFactory.ConstructorDeclaration(typeDecl.Identifier)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithBody(SyntaxFactory.Block());

            var firstCtor = typeDecl.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();

            if (firstCtor != null) {
                newTypeDecl = typeDecl.InsertNodesBefore(firstCtor, [newCtor]);
            }
            else {
                var newMembers = typeDecl.Members.Insert(0, newCtor);
                newTypeDecl = typeDecl.WithMembers(newMembers);
            }
        }

        var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);

        return document.WithSyntaxRoot(newRoot);
    }
}