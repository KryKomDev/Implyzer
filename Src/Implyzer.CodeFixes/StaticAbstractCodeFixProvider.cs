// Implyzer
// Copyright (c) KryKom 2026

using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Implyzer;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StaticAbstractCodeFixProvider))]
[Shared]
public class StaticAbstractCodeFixProvider : CodeFixProvider {
    public override ImmutableArray<string> FixableDiagnosticIds => [
        "IMPL006", // StaticAbstractInterfaceNotPartial
        "IMPL007", // StaticAbstractTargetClassNotPartial
        "IMPL009"  // StaticAbstractMethodNotImplemented
    ];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        foreach (var diagnostic in context.Diagnostics) {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var node = root?.FindNode(diagnostic.Location.SourceSpan);
            var typeDecl = GetTypeDeclaration(node);

            if (diagnostic.Id == "IMPL006") {
                if (typeDecl != null) {
                    var title = "Make interface partial";
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title,
                            c => MakeTypePartialAsync(context.Document, typeDecl, c),
                            title
                        ),
                        diagnostic
                    );
                }
            }
            else if (diagnostic.Id == "IMPL007") {
                if (diagnostic.Properties.TryGetValue("TargetClassFqn", out var targetClassFqn) && targetClassFqn != null) {
                    var title = "Make target class partial";
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title,
                            c => MakeTargetClassPartialAsync(context.Document.Project.Solution, targetClassFqn, c),
                            title
                        ),
                        diagnostic
                    );
                }
            }
            else if (diagnostic.Id == "IMPL009") {
                if (typeDecl != null) {
                    if (diagnostic.Properties.TryGetValue("MethodName", out var methodName) &&
                        diagnostic.Properties.TryGetValue("ReturnType", out var returnType) &&
                        diagnostic.Properties.TryGetValue("Parameters", out var parameters)) {
                        
                        var title = $"Implement static method '{methodName}'";
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title,
                                c => ImplementStaticMethodAsync(context.Document, typeDecl, methodName!, returnType!, parameters!, c),
                                title
                            ),
                            diagnostic
                        );
                    }
                }
            }
        }
    }

    private static async Task<Document> MakeTypePartialAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        CancellationToken cancellationToken
    ) {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var newTypeDecl = AddPartialModifier(typeDecl);
        var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Solution> MakeTargetClassPartialAsync(
        Solution solution,
        string targetClassFqn,
        CancellationToken cancellationToken
    ) {
        foreach (var project in solution.Projects) {
            foreach (var document in project.Documents) {
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (root == null) continue;
                
                var typeDecls = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
                foreach (var typeDecl in typeDecls) {
                    var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                    if (model == null) continue;
                    
                    var symbol = model.GetDeclaredSymbol(typeDecl, cancellationToken);
                    if (symbol != null) {
                        var fqn = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (fqn == targetClassFqn) {
                            var newTypeDecl = AddPartialModifier(typeDecl);
                            var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
                            return solution.WithDocumentSyntaxRoot(document.Id, newRoot);
                        }
                    }
                }
            }
        }
        return solution;
    }

    private static async Task<Document> ImplementStaticMethodAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        string methodName,
        string returnType,
        string parameters,
        CancellationToken cancellationToken
    ) {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var methodDeclarationText = $"\n        public static {returnType} {methodName}({parameters}) => throw new global::System.NotImplementedException();\n";
        var methodDecl = SyntaxFactory.ParseMemberDeclaration(methodDeclarationText) as MethodDeclarationSyntax;
        if (methodDecl == null) return document;

        var newMembers = typeDecl.Members.Add(methodDecl);
        var newTypeDecl = typeDecl.WithMembers(newMembers);
        var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }

    private static TypeDeclarationSyntax? GetTypeDeclaration(SyntaxNode? node) {
        while (node != null && node is not TypeDeclarationSyntax) {
            node = node.Parent;
        }
        return node as TypeDeclarationSyntax;
    }

    private static TypeDeclarationSyntax AddPartialModifier(TypeDeclarationSyntax typeDecl) {
        if (typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
            return typeDecl;

        return typeDecl.WithModifiers(typeDecl.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword)));
    }
}
