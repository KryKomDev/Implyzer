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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ImplTypeInheritanceCodeFixProvider)), Shared]
public class ImplTypeInheritanceCodeFixProvider : CodeFixProvider {
    public override ImmutableArray<string> FixableDiagnosticIds => [Rules.Type.Id];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        foreach (var diagnostic in context.Diagnostics) {
            if (!diagnostic.Properties.TryGetValue("RequiredBaseType", out var requiredBaseType) || requiredBaseType == null) 
                continue;

            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var node = root?.FindNode(diagnostic.Location.SourceSpan);

            if (node is not TypeDeclarationSyntax typeDecl) continue;

            var title = $"Inherit from '{requiredBaseType}'";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: c => AddBaseTypeAsync(context.Document, typeDecl, requiredBaseType, c),
                    equivalenceKey: "InheritBaseType"
                ),
                diagnostic
            );
        }
    }

    private static async Task<Document> AddBaseTypeAsync(
        Document              document,
        TypeDeclarationSyntax typeDecl,
        string                requiredBaseType,
        CancellationToken     cancellationToken) 
    {
        var baseTypeNode = SyntaxFactory.ParseTypeName(requiredBaseType);
        var simpleBaseType = SyntaxFactory.SimpleBaseType(baseTypeNode);
        
        var newBaseList = typeDecl.BaseList;
        newBaseList = newBaseList == null 
            ? SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(simpleBaseType)) 
            : newBaseList.WithTypes(newBaseList.Types.Insert(0, simpleBaseType));

        var newTypeDecl = typeDecl.WithBaseList(newBaseList);
        
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null) return document;

        var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}
