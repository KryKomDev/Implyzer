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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ImplTypeRefValCodeFixProvider)), Shared]
public class ImplTypeRefValCodeFixProvider : CodeFixProvider {
    public override ImmutableArray<string> FixableDiagnosticIds => [Rules.RefVal.Id];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        foreach (var diagnostic in context.Diagnostics) {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var node = root?.FindNode(diagnostic.Location.SourceSpan);

            if (node is not TypeDeclarationSyntax typeDecl) continue;
            
            var isClass = typeDecl.Kind() == SyntaxKind.ClassDeclaration;
            var isStruct = typeDecl.Kind() == SyntaxKind.StructDeclaration;
            
            if (!isClass && !isStruct) continue;

            var title = isClass ? "Change to struct" : "Change to class";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: c => ChangeTypeKindAsync(context.Document, typeDecl, c),
                    equivalenceKey: title
                ),
                diagnostic
            );
        }
    }

    private static async Task<Document> ChangeTypeKindAsync(
        Document              document,
        TypeDeclarationSyntax typeDecl,
        CancellationToken     cancellationToken) 
    {
        var isClass = typeDecl.Kind() == SyntaxKind.ClassDeclaration;
        
        TypeDeclarationSyntax newTypeDecl;
        
        if (isClass) {
            newTypeDecl = SyntaxFactory.StructDeclaration(
                typeDecl.AttributeLists, 
                typeDecl.Modifiers, 
                SyntaxFactory.Token(SyntaxKind.StructKeyword).WithTriviaFrom(typeDecl.Keyword), 
                typeDecl.Identifier, 
                typeDecl.TypeParameterList, 
                typeDecl.BaseList, 
                typeDecl.ConstraintClauses, 
                typeDecl.OpenBraceToken, 
                typeDecl.Members, 
                typeDecl.CloseBraceToken, 
                typeDecl.SemicolonToken);
        }
        else {
             newTypeDecl = SyntaxFactory.ClassDeclaration(
                typeDecl.AttributeLists, 
                typeDecl.Modifiers, 
                SyntaxFactory.Token(SyntaxKind.ClassKeyword).WithTriviaFrom(typeDecl.Keyword), 
                typeDecl.Identifier, 
                typeDecl.TypeParameterList, 
                typeDecl.BaseList, 
                typeDecl.ConstraintClauses, 
                typeDecl.OpenBraceToken, 
                typeDecl.Members, 
                typeDecl.CloseBraceToken, 
                typeDecl.SemicolonToken);
        }
        
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null) return document;
        
        var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}
