// Implyzer
// Copyright (c) KryKom 2026

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Implyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class IndirectImplAnalyzer : DiagnosticAnalyzer {
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rules.IndirectImpl];

    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration);
    }

    private static void AnalyzeSyntax(SyntaxNodeAnalysisContext context) {
        var typeDeclaration = (TypeDeclarationSyntax)context.Node;

        if (typeDeclaration.BaseList == null) return;

        foreach (var baseType in typeDeclaration.BaseList.Types) {
            var typeSymbol = context.SemanticModel.GetTypeInfo(baseType.Type).Type;
            
            if (typeSymbol is not INamedTypeSymbol namedTypeSymbol || namedTypeSymbol.TypeKind != TypeKind.Interface) 
                continue;

            foreach (var attribute in namedTypeSymbol.GetAttributes()) {
                if (attribute.AttributeClass?.Name != nameof(IndirectImplAttribute)) continue;

                var properties = ImmutableDictionary<string, string?>.Empty;
                var messageExtra = "";
                if (attribute.ConstructorArguments.Length > 0 && !attribute.ConstructorArguments[0].IsNull) {
                     if (attribute.ConstructorArguments[0].Value is INamedTypeSymbol suggestion) {
                         messageExtra = $", implement '{suggestion.Name}' instead";
                         properties = properties.Add("Suggestion", suggestion.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                     }
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Rules.IndirectImpl,
                    baseType.GetLocation(),
                    properties,
                    typeDeclaration.Identifier.Text,
                    namedTypeSymbol.Name,
                    messageExtra
                ));
            }
        }
    }
}
