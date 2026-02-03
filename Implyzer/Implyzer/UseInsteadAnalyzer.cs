// Implyzer
// Copyright (c) KryKom 2026

using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Implyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UseInsteadAnalyzer : DiagnosticAnalyzer {
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rules.UseInstead];

    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        
        context.RegisterOperationAction(AnalyzeOperation, 
            OperationKind.FieldReference,
            OperationKind.PropertyReference,
            OperationKind.MethodReference,
            OperationKind.EventReference,
            OperationKind.ObjectCreation,
            OperationKind.Invocation
        );
    }

    private static void AnalyzeOperation(OperationAnalysisContext context) {
        ISymbol? symbol = null;

        switch (context.Operation) {
            case IMemberReferenceOperation memberRef:
                // If it's a method reference that is part of an invocation, we skip it here
                // and let the Invocation handle it to avoid potential double reporting or confusion,
                // BUT Invocation doesn't always have a clear "syntax" for just the method name if we want to be precise.
                // However, usually highlighting the call is fine.
                // Let's actually check if we should handle it here.
                // If it is a method call `foo.Bar()`, `foo.Bar` is the MemberReference.
                // Parent is Invocation.
                if (memberRef.Member.Kind == SymbolKind.Method && memberRef.Parent is IInvocationOperation)
                    return;
                
                symbol = memberRef.Member;
                break;
                
            case IInvocationOperation invocation:
                symbol = invocation.TargetMethod;
                break;

            case IObjectCreationOperation objectCreation:
                symbol = objectCreation.Constructor;
                break;
        }

        if (symbol == null) return;

        CheckSymbol(context, symbol);
    }

    private static void CheckSymbol(OperationAnalysisContext context, ISymbol symbol) {
        // We need to check the symbol itself, and potentially the property/event if the symbol is an accessor?
        // But the attribute is likely on the Property, not the GetMethod.
        // If symbol is a Method, and it is a property accessor, we should check the Property.
        
        if (symbol is IMethodSymbol { AssociatedSymbol: { } associatedSymbol }) {
            if (CheckAttributes(context, associatedSymbol)) 
                return;
        }
        
        if (CheckAttributes(context, symbol)) return;
        
        // If it is a constructor, check the containing type
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } && symbol.ContainingType != null) 
            CheckAttributes(context, symbol.ContainingType);
    }

    private static bool CheckAttributes(OperationAnalysisContext context, ISymbol symbol) {
        foreach (var attribute in symbol.GetAttributes()) {
            if (attribute.AttributeClass?.Name != nameof(UseInsteadAttribute)) continue;
            
            // We found the attribute. Extract the message.
            if (attribute.ConstructorArguments.Length <= 0) continue;

            string replacement = "";

            replacement = attribute.ConstructorArguments.Length switch {
                // Case 1: [UseInstead(typeof(Type), "Member")] or [UseInstead(typeof(Type), new[] { typeof(P1) })]
                2 => Replacement(attribute, replacement),
                // Case 2: [UseInstead(typeof(Type))] or [UseInstead("String")]
                1 => ExtractReplacementInfo(attribute, replacement),
                _ => replacement
            };

            if (string.IsNullOrEmpty(replacement)) continue;

            var properties = ImmutableDictionary<string, string?>.Empty.Add("Replacement", replacement);

            context.ReportDiagnostic(Diagnostic.Create(
                Rules.UseInstead,
                GetLocation(context.Operation),
                properties,
                replacement,
                symbol.Name
            ));
            
            // Report only once per symbol/usage
            return true;
        }
        
        return false;
    }

    private static string ExtractReplacementInfo(AttributeData attribute, string replacement) {
        var arg = attribute.ConstructorArguments[0];

        switch (arg) {
            case { Kind: TypedConstantKind.Type, Value: ISymbol replacementType }: {
                replacement = replacementType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
                    
                // Check for named argument "MemberName" or "ParameterTypes"
                foreach (var namedArg in attribute.NamedArguments) {
                    
                    if (namedArg is { Key: "MemberName", Value.Value: string name }) {
                        replacement += $".{name}";
                        break;
                    }

                    if (namedArg.Key != "ParameterTypes" || namedArg.Value.Kind != TypedConstantKind.Array) continue;
                    var paramsString = string.Join(", ", namedArg.Value.Values.Select(v => 
                        v.Value is ISymbol paramType 
                            ? paramType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) 
                            : v.Value?.ToString()));
                    
                    replacement += $"({paramsString})";
                    break;
                }

                break;
            }
            case { Kind: TypedConstantKind.Primitive, Value: string replacementString }:
                replacement = replacementString;
                break;
        }

        return replacement;
    }

    private static string Replacement(AttributeData attribute, string replacement) {
        var typeArg   = attribute.ConstructorArguments[0];
        var secondArg = attribute.ConstructorArguments[1];

        if (typeArg is not { Kind: TypedConstantKind.Type, Value: ISymbol typeSymbol })
            return replacement;
        
        if (secondArg is { Kind: TypedConstantKind.Primitive, Value: string memberName }) {
            replacement = $"{typeSymbol.ToDisplayString(
                SymbolDisplayFormat.CSharpShortErrorMessageFormat)}.{memberName}";
        }
        else if (secondArg.Kind == TypedConstantKind.Array) {
            
            // It is Type[] parameterTypes
            var paramsString = string.Join(", ", secondArg.Values.Select(v => 
                v.Value is ISymbol paramType 
                    ? paramType.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) 
                    : v.Value?.ToString()));
                        
            replacement = $"{typeSymbol.ToDisplayString(
                SymbolDisplayFormat.CSharpShortErrorMessageFormat)}({paramsString})";
        }

        return replacement;
    }

    private static Location GetLocation(IOperation operation) {
        var syntax = operation.Syntax;

        switch (syntax) {
            case InvocationExpressionSyntax invSyntax:
                switch (invSyntax.Expression) {
                    case MemberAccessExpressionSyntax memberAccess:
                        return memberAccess.Name.GetLocation();
                    case IdentifierNameSyntax identifier:
                        return identifier.GetLocation();
                }
                break;
            case ObjectCreationExpressionSyntax creationSyntax:
                return creationSyntax.Type.GetLocation();
            case MemberAccessExpressionSyntax memberAccessSyntax:
                return memberAccessSyntax.Name.GetLocation();
        }

        return syntax.GetLocation();
    }
}
