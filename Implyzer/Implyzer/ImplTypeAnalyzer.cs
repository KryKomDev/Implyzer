// Implyzer
// Copyright (c) KryKom 2026

using System.Linq;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Implyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ImplTypeAnalyzer : DiagnosticAnalyzer {
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Rules.SupportedDiagnostics;

    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context) {
        var namedTypeSymbol = (INamedTypeSymbol)context.Symbol;

        if (namedTypeSymbol.TypeKind != TypeKind.Class && namedTypeSymbol.TypeKind != TypeKind.Struct)
            return;

        foreach (var interfaceType in namedTypeSymbol.AllInterfaces)
        foreach (var attribute in interfaceType.GetAttributes()) {
            if (attribute.AttributeClass?.Name != nameof(ImplTypeAttribute)) continue;
            if (attribute.ConstructorArguments.Length <= 0) continue;

            var arg = attribute.ConstructorArguments[0];

            switch (arg.Kind) {
                case TypedConstantKind.Enum when arg.Value is int intValue: {
                    ValidateRefVal(context, intValue, namedTypeSymbol, interfaceType);
                    break;
                }
                case TypedConstantKind.Type when arg.Value is INamedTypeSymbol requiredBaseType: {
                    ValidateBaseType(context, namedTypeSymbol, interfaceType, requiredBaseType);
                    break;
                }
            }
        }
    }

    private static void ValidateBaseType(
        SymbolAnalysisContext context,
        INamedTypeSymbol      namedTypeSymbol,
        INamedTypeSymbol      interfaceType,
        INamedTypeSymbol      requiredBaseType) 
    {
        if (namedTypeSymbol.TypeKind != TypeKind.Class) {
            var diagnostic = Diagnostic.Create(
                Rules.RefVal,
                namedTypeSymbol.Locations[0],
                namedTypeSymbol.Name,
                "reference type (class)",
                interfaceType.Name,
                "ReferenceType");
            context.ReportDiagnostic(diagnostic);
            return;
        }

        // Check inheritance
        if (InheritsFrom(namedTypeSymbol, requiredBaseType)) return;
        
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add("RequiredBaseType", requiredBaseType
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        context.ReportDiagnostic(Diagnostic.Create(
            Rules.Type,
            namedTypeSymbol.Locations[0],
            properties,
            namedTypeSymbol .Name,
            requiredBaseType.Name,
            interfaceType   .Name
        ));
    }

    private static void ValidateRefVal(
        SymbolAnalysisContext context,
        int                   intValue,
        INamedTypeSymbol      namedTypeSymbol,
        INamedTypeSymbol      interfaceType) 
    {
        var isReferenceType = intValue == 0;
        var isValueType = intValue == 1;
        var isValueTypeNew = intValue == 2;

        var violation = false;
        var requiredKind = "";

        if ((isReferenceType || isValueTypeNew) && namedTypeSymbol.TypeKind != TypeKind.Class) {
            violation = true;
            requiredKind = "reference type (class)";
        }
        else if (isValueType && namedTypeSymbol.TypeKind != TypeKind.Struct) {
            violation = true;
            requiredKind = "value type (struct)";
        }

        if (violation) {
            var diagnostic = Diagnostic.Create(
                Rules.RefVal,
                namedTypeSymbol.Locations[0],
                namedTypeSymbol.Name,
                requiredKind,
                interfaceType.Name,
                isReferenceType ? "ReferenceType" : (isValueType ? "ValueType" : "ReferenceTypeNew"));

            context.ReportDiagnostic(diagnostic);
        }

        if (!isValueTypeNew || violation) return;
        
        if (!HasPublicParameterlessConstructor(namedTypeSymbol)) {
            context.ReportDiagnostic(Diagnostic.Create(
                Rules.Constructor,
                namedTypeSymbol.Locations[0],
                namedTypeSymbol.Name,
                interfaceType  .Name
            ));
        }
    }

    private static bool HasPublicParameterlessConstructor(INamedTypeSymbol type) {
        if (type.TypeKind == TypeKind.Struct) return true;
        return type.InstanceConstructors
            .Any(c => !c.IsStatic && c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType) {
        var current = type.BaseType;
        while (current != null) {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
            current = current.BaseType;
        }

        return false;
    }
}