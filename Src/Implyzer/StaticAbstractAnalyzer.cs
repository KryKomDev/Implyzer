// Implyzer
// Copyright (c) KryKom 2026

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Implyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class StaticAbstractAnalyzer : DiagnosticAnalyzer {
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Rules.SupportedDiagnostics;

    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context) {
        var symbol = (INamedTypeSymbol)context.Symbol;

        if (symbol.TypeKind == TypeKind.Interface) {
            AnalyzeInterface(context, symbol);
        }
        else if (symbol.TypeKind == TypeKind.Class || symbol.TypeKind == TypeKind.Struct) {
            AnalyzeImplementingType(context, symbol);
        }
    }

    private static void AnalyzeInterface(SymbolAnalysisContext context, INamedTypeSymbol interfaceSymbol) {
        foreach (var attribute in interfaceSymbol.GetAttributes()) {
            var info = GetStaticAbstractInfo(attribute, context.Compilation);

            if (info == null)
                continue;

            // 1. Validate signature is a delegate
            if (info.DelegateSymbol.TypeKind != TypeKind.Delegate) {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        Rules.StaticAbstractSignatureNotDelegate,
                        attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? interfaceSymbol.Locations[0],
                        info.DelegateSymbol.Name
                    )
                );

                continue;
            }

            // 2. Validate targetClass if specified
            if (info.TargetClass != null) {
                if (info.TargetClass.TypeKind != TypeKind.Class) {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Rules.StaticAbstractTargetClassMustBeClass,
                            attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? interfaceSymbol.Locations[0],
                            info.TargetClass.Name
                        )
                    );
                }
                else if (!IsPartial(info.TargetClass)) {
                    var properties = ImmutableDictionary<string, string?>.Empty
                        .Add("TargetClassFqn", info.TargetClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Rules.StaticAbstractTargetClassNotPartial,
                            attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? interfaceSymbol.Locations[0],
                            properties,
                            info.TargetClass.Name
                        )
                    );
                }
            }
            else {
                // If targetClass is not specified, the interface itself must be partial
                if (!IsPartial(interfaceSymbol)) {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Rules.StaticAbstractInterfaceNotPartial,
                            interfaceSymbol.Locations[0],
                            interfaceSymbol.Name
                        )
                    );
                }
            }
        }
    }

    private static void AnalyzeImplementingType(SymbolAnalysisContext context, INamedTypeSymbol typeSymbol) {
        foreach (var iface in typeSymbol.AllInterfaces) {
            foreach (var attribute in iface.GetAttributes()) {
                var info = GetStaticAbstractInfo(attribute, context.Compilation);

                if (info == null)
                    continue;

                if (info.DelegateSymbol.TypeKind != TypeKind.Delegate)
                    continue;

                // Build type arguments for constructed delegate
                var typeArgs = new ITypeSymbol[info.DelegateSymbol.TypeParameters.Length];

                for (int i = 0; i < info.DelegateSymbol.TypeParameters.Length; i++) {
                    var          dtp        = info.DelegateSymbol.TypeParameters[i];
                    ITypeSymbol? mappedType = null;

                    for (int j = 0; j < iface.OriginalDefinition.TypeParameters.Length; j++) {
                        var itp = iface.OriginalDefinition.TypeParameters[j];

                        if (info.TypeParams.TryGetValue(itp.Name, out var targetName) && targetName == dtp.Name) {
                            mappedType = iface.TypeArguments[j];

                            break;
                        }
                    }

                    typeArgs[i] = mappedType ?? dtp;
                }

                var constructedDelegate = info.DelegateSymbol.OriginalDefinition.Construct(typeArgs);
                var delegateInvoke      = constructedDelegate.DelegateInvokeMethod;

                if (delegateInvoke == null)
                    continue;

                // Check if typeSymbol implements a public static method matching signature
                var matches = typeSymbol.GetMembers(info.MethodName)
                    .OfType<IMethodSymbol>()
                    .Any(m => m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && MethodMatchesSignature(m, delegateInvoke));

                if (!matches) {
                    var returnTypeFqn = delegateInvoke.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var paramStrings = delegateInvoke.Parameters.Select(p => {
                        var refKind = p.RefKind switch {
                            RefKind.Ref => "ref ",
                            RefKind.Out => "out ",
                            RefKind.In => "in ",
                            _ => ""
                        };
                        return $"{refKind}{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}";
                    });
                    var paramsText = string.Join(", ", paramStrings);

                    var properties = ImmutableDictionary<string, string?>.Empty
                        .Add("MethodName", info.MethodName)
                        .Add("ReturnType", returnTypeFqn)
                        .Add("Parameters", paramsText);

                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Rules.StaticAbstractMethodNotImplemented,
                            typeSymbol.Locations[0],
                            properties,
                            typeSymbol.Name,
                            info.MethodName,
                            constructedDelegate.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                            iface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                        )
                    );
                }
            }
        }
    }

    private static bool MethodMatchesSignature(IMethodSymbol method, IMethodSymbol delegateInvoke) {
        if (method.Parameters.Length != delegateInvoke.Parameters.Length)
            return false;

        if (!SymbolEqualityComparer.Default.Equals(method.ReturnType, delegateInvoke.ReturnType))
            return false;

        for (int i = 0; i < method.Parameters.Length; i++) {
            var p1 = method.Parameters[i];
            var p2 = delegateInvoke.Parameters[i];

            if (p1.RefKind != p2.RefKind)
                return false;

            if (!SymbolEqualityComparer.Default.Equals(p1.Type, p2.Type))
                return false;
        }

        return true;
    }

    private static bool IsPartial(INamedTypeSymbol symbol) {
        foreach (var reference in symbol.DeclaringSyntaxReferences) {
            var node = reference.GetSyntax();

            if (node is TypeDeclarationSyntax typeDecl && typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword)) {
                return true;
            }
        }

        return false;
    }

    private static StaticAbstractInfo? GetStaticAbstractInfo(AttributeData attribute, Compilation compilation) {
        if (attribute.AttributeClass?.Name != "StaticAbstractAttribute" && attribute.AttributeClass?.Name != "StaticAbstract")
            return null;

        string? methodName = null;
        INamedTypeSymbol? delegateSymbol = null;
        var typeParams = new Dictionary<string, string>();
        INamedTypeSymbol? targetClass = null;

        if (attribute.ConstructorArguments.Length >= 2) {
            var methodNameArg = attribute.ConstructorArguments[0];
            if (methodNameArg.Value is string mName) methodName = mName;

            var signatureArg = attribute.ConstructorArguments[1];
            if (signatureArg.Value is INamedTypeSymbol delSymbol) delegateSymbol = delSymbol;

            if (attribute.ConstructorArguments.Length == 3) {
                var arg2 = attribute.ConstructorArguments[2];
                ParseTypeParamsArray(arg2, typeParams);
            }
            else if (attribute.ConstructorArguments.Length == 4) {
                var arg2 = attribute.ConstructorArguments[2];
                targetClass = arg2.Value as INamedTypeSymbol;

                var arg3 = attribute.ConstructorArguments[3];
                ParseTypeParamsArray(arg3, typeParams);
            }
        } else {
            var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
            if (attributeSyntax == null || attributeSyntax.ArgumentList == null || attributeSyntax.ArgumentList.Arguments.Count < 2)
                return null;

#pragma warning disable RS1030
            var semanticModel = compilation.GetSemanticModel(attributeSyntax.SyntaxTree);
#pragma warning restore RS1030

            // 1. methodName
            var expr0 = attributeSyntax.ArgumentList.Arguments[0].Expression;
            methodName = semanticModel.GetConstantValue(expr0).Value as string;

            // 2. signature
            var expr1 = attributeSyntax.ArgumentList.Arguments[1].Expression;
            if (expr1 is TypeOfExpressionSyntax typeof1) {
                delegateSymbol = semanticModel.GetTypeInfo(typeof1.Type).Type as INamedTypeSymbol;
            }

            // 3. Remaining arguments
            var argsCount = attributeSyntax.ArgumentList.Arguments.Count;
            if (argsCount >= 3) {
                var expr2 = attributeSyntax.ArgumentList.Arguments[2].Expression;
                if (expr2 is TypeOfExpressionSyntax typeof2) {
                    targetClass = semanticModel.GetTypeInfo(typeof2.Type).Type as INamedTypeSymbol;
                    ParseParamsExpressions(attributeSyntax.ArgumentList.Arguments.Skip(3).Select(a => a.Expression), typeParams, semanticModel);
                } else {
                    ParseParamsExpressions(attributeSyntax.ArgumentList.Arguments.Skip(2).Select(a => a.Expression), typeParams, semanticModel);
                }
            }
        }

        if (methodName == null || delegateSymbol == null)
            return null;

        return new StaticAbstractInfo(methodName, delegateSymbol, typeParams, targetClass, attribute);
    }

    private static void ParseParamsExpressions(System.Collections.Generic.IEnumerable<ExpressionSyntax> expressions, Dictionary<string, string> typeParams, SemanticModel semanticModel) {
        var elements = new List<string>();
        foreach (var expr in expressions) {
            if (expr is CollectionExpressionSyntax || expr is ArrayCreationExpressionSyntax || expr is ImplicitArrayCreationExpressionSyntax) {
                ParseTypeParamsSyntax(expr, typeParams, semanticModel);
                return;
            }
            var val = semanticModel.GetConstantValue(expr).Value as string;
            if (val != null) elements.Add(val);
        }
        for (int i = 0; i < elements.Count; i += 2) {
            if (i + 1 < elements.Count) {
                typeParams[elements[i]] = elements[i + 1];
            }
        }
    }

    private static void ParseTypeParamsSyntax(ExpressionSyntax expr, Dictionary<string, string> typeParams, SemanticModel semanticModel) {
        if (expr is CollectionExpressionSyntax collection) {
            var elements = new List<string>();
            foreach (var element in collection.Elements) {
                if (element is ExpressionElementSyntax exprElem) {
                    var val = semanticModel.GetConstantValue(exprElem.Expression).Value as string;
                    if (val != null) elements.Add(val);
                }
            }
            for (int i = 0; i < elements.Count; i += 2) {
                if (i + 1 < elements.Count) {
                    typeParams[elements[i]] = elements[i + 1];
                }
            }
        } else if (expr is ArrayCreationExpressionSyntax arrayCreate) {
            if (arrayCreate.Initializer != null) {
                var elements = new List<string>();
                foreach (var element in arrayCreate.Initializer.Expressions) {
                    var val = semanticModel.GetConstantValue(element).Value as string;
                    if (val != null) elements.Add(val);
                }
                for (int i = 0; i < elements.Count; i += 2) {
                    if (i + 1 < elements.Count) {
                        typeParams[elements[i]] = elements[i + 1];
                    }
                }
            }
        } else if (expr is ImplicitArrayCreationExpressionSyntax implicitArray) {
            if (implicitArray.Initializer != null) {
                var elements = new List<string>();
                foreach (var element in implicitArray.Initializer.Expressions) {
                    var val = semanticModel.GetConstantValue(element).Value as string;
                    if (val != null) elements.Add(val);
                }
                for (int i = 0; i < elements.Count; i += 2) {
                    if (i + 1 < elements.Count) {
                        typeParams[elements[i]] = elements[i + 1];
                    }
                }
            }
        }
    }

    private static void ParseTypeParamsArray(TypedConstant arg, Dictionary<string, string> typeParams) {
        if (arg.Kind == TypedConstantKind.Array) {
            for (int i = 0; i < arg.Values.Length; i += 2) {
                if (i + 1 < arg.Values.Length) {
                    var key = arg.Values[i].Value as string;
                    var val = arg.Values[i + 1].Value as string;

                    if (key != null && val != null) {
                        typeParams[key] = val;
                    }
                }
            }
        }
    }

    private class StaticAbstractInfo {
        public string                     MethodName     { get; }
        public INamedTypeSymbol           DelegateSymbol { get; }
        public Dictionary<string, string> TypeParams     { get; }
        public INamedTypeSymbol?          TargetClass    { get; }
        public AttributeData              AttributeData  { get; }

        public StaticAbstractInfo(string methodName, INamedTypeSymbol delegateSymbol, Dictionary<string, string> typeParams, INamedTypeSymbol? targetClass, AttributeData attributeData) {
            MethodName     = methodName;
            DelegateSymbol = delegateSymbol;
            TypeParams     = typeParams;
            TargetClass    = targetClass;
            AttributeData  = attributeData;
        }
    }
}