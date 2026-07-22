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
    private static readonly SymbolDisplayFormat FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Rules.SupportedDiagnostics;

    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context) {
        var symbol = (INamedTypeSymbol)context.Symbol;

        if (symbol.TypeKind == TypeKind.Interface)
            AnalyzeInterface(context, symbol);
        else if (symbol.TypeKind == TypeKind.Class || symbol.TypeKind == TypeKind.Struct)
            AnalyzeImplementingType(context, symbol);
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
                if (!IsPartial(interfaceSymbol))
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

    private static void AnalyzeImplementingType(SymbolAnalysisContext context, INamedTypeSymbol typeSymbol) {
        foreach (var iface in typeSymbol.AllInterfaces)
        foreach (var attribute in iface.OriginalDefinition.GetAttributes()) {
            var info = GetStaticAbstractInfo(attribute, context.Compilation);

            if (info == null)
                continue;

            if (info.DelegateSymbol.TypeKind != TypeKind.Delegate)
                continue;

            // Build type arguments for constructed delegate
            var typeArgs = new ITypeSymbol[info.DelegateSymbol.TypeParameters.Length];

            for (var i = 0; i < info.DelegateSymbol.TypeParameters.Length; i++) {
                var          dtp        = info.DelegateSymbol.TypeParameters[i];
                ITypeSymbol? mappedType = null;

                for (var j = 0; j < iface.OriginalDefinition.TypeParameters.Length; j++) {
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

            if (matches)
                continue;

            var returnAttributes = FormatReturnAttributes(delegateInvoke.OriginalDefinition.GetReturnTypeAttributes());
            var returnTypeFqn    = returnAttributes + delegateInvoke.ReturnType.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);

            var paramStrings = delegateInvoke.Parameters.Select(
                p => {
                    var refKind = p.RefKind switch {
                        RefKind.Ref => "ref ",
                        RefKind.Out => "out ",
                        RefKind.In  => "in ",
                        _           => p.IsParams ? "params " : ""
                    };

                    var attrs = FormatAttributes(p.OriginalDefinition.GetAttributes());

                    return $"{attrs}{refKind}{p.Type.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY)} {p.Name}";
                }
            );

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

            if (p1.IsParams != p2.IsParams)
                return false;

            if (!SymbolEqualityComparer.Default.Equals(p1.Type, p2.Type))
                return false;
        }

        return true;
    }

    private static bool AttributeListsMatch(ImmutableArray<AttributeData> list1, ImmutableArray<AttributeData> list2) {
        var filtered1 = list1.Where(a => !IsCompilerInjectedAttribute(a)).ToList();
        var filtered2 = list2.Where(a => !IsCompilerInjectedAttribute(a)).ToList();

        if (filtered1.Count != filtered2.Count)
            return false;

        for (var i = 0; i < filtered1.Count; i++)
            if (!AttributesAreEqual(filtered1[i], filtered2[i]))
                return false;

        return true;
    }

    private static bool IsCompilerInjectedAttribute(AttributeData attribute) {
        if (attribute.AttributeClass == null)
            return true;

        var fullName = attribute.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return fullName 
            is "global::System.Runtime.CompilerServices.NullableAttribute"
            or "global::System.Runtime.CompilerServices.NullableContextAttribute"
            or "global::System.Runtime.CompilerServices.NullablePublicOnlyAttribute"
            or "global::System.Runtime.CompilerServices.NativeIntegerAttribute"
            or "global::System.Runtime.CompilerServices.DynamicAttribute"
            or "global::System.Runtime.CompilerServices.TupleElementNamesAttribute"
            or "global::System.Runtime.CompilerServices.IsReadOnlyAttribute"
            or "global::System.ParamArrayAttribute"
            or "global::System.Runtime.InteropServices.OutAttribute"
            or "global::System.Runtime.InteropServices.InAttribute";
    }

    private static bool AttributesAreEqual(AttributeData a1, AttributeData a2) {
        if (!SymbolEqualityComparer.Default.Equals(a1.AttributeClass, a2.AttributeClass))
            return false;

        if (a1.ConstructorArguments.Length != a2.ConstructorArguments.Length)
            return false;

        for (var i = 0; i < a1.ConstructorArguments.Length; i++)
            if (!TypedConstantsAreEqual(a1.ConstructorArguments[i], a2.ConstructorArguments[i]))
                return false;

        if (a1.NamedArguments.Length != a2.NamedArguments.Length)
            return false;

        foreach (var na1 in a1.NamedArguments) {
            var match = a2.NamedArguments.FirstOrDefault(na2 => na2.Key == na1.Key);

            if (match.Key == null || !TypedConstantsAreEqual(na1.Value, match.Value))
                return false;
        }

        return true;
    }

    private static bool TypedConstantsAreEqual(TypedConstant tc1, TypedConstant tc2) {
        if (tc1.Kind != tc2.Kind)
            return false;

        if (tc1.IsNull != tc2.IsNull)
            return false;

        if (tc1.IsNull)
            return true;

        if (tc1.Kind == TypedConstantKind.Array) {
            if (tc1.Values.Length != tc2.Values.Length)
                return false;

            for (var i = 0; i < tc1.Values.Length; i++)
                if (!TypedConstantsAreEqual(tc1.Values[i], tc2.Values[i]))
                    return false;

            return true;
        }

        if (tc1.Kind == TypedConstantKind.Type)
            return SymbolEqualityComparer.Default.Equals((ITypeSymbol?)tc1.Value, (ITypeSymbol?)tc2.Value);

        return Equals(tc1.Value, tc2.Value);
    }

    private static string FormatAttributes(IEnumerable<AttributeData> attributes) {
        var formatted = new List<string>();

        foreach (var attr in attributes) {
            var formattedAttr = FormatAttribute(attr);

            if (!string.IsNullOrEmpty(formattedAttr))
                formatted.Add(formattedAttr);
        }

        return formatted.Count > 0 ? string.Join(" ", formatted) + " " : "";
    }

    private static string FormatReturnAttributes(IEnumerable<AttributeData> attributes) {
        var formatted = new List<string>();

        foreach (var attr in attributes) {
            var formattedAttr = FormatAttribute(attr);

            if (!string.IsNullOrEmpty(formattedAttr)) {
                if (formattedAttr.StartsWith("[") && formattedAttr.EndsWith("]"))
                    formattedAttr = "[return: " + formattedAttr.Substring(1);

                formatted.Add(formattedAttr);
            }
        }

        return formatted.Count > 0 ? string.Join(" ", formatted) + " " : "";
    }

    private static string FormatAttribute(AttributeData attribute) {
        if (attribute.AttributeClass == null)
            return "";

        var fullName = attribute.AttributeClass.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);

        if (fullName == "global::System.Runtime.CompilerServices.NullableAttribute"           ||
            fullName == "global::System.Runtime.CompilerServices.NullableContextAttribute"    ||
            fullName == "global::System.Runtime.CompilerServices.NullablePublicOnlyAttribute" ||
            fullName == "global::System.Runtime.CompilerServices.NativeIntegerAttribute"      ||
            fullName == "global::System.Runtime.CompilerServices.DynamicAttribute"            ||
            fullName == "global::System.Runtime.CompilerServices.TupleElementNamesAttribute"  ||
            fullName == "global::System.Runtime.CompilerServices.IsReadOnlyAttribute"         ||
            fullName == "global::System.ParamArrayAttribute"                                  ||
            fullName == "global::System.Runtime.InteropServices.OutAttribute"                 ||
            fullName == "global::System.Runtime.InteropServices.InAttribute")
            return "";

        var args = new List<string>();

        foreach (var arg in attribute.ConstructorArguments)
            args.Add(FormatTypedConstant(arg));

        foreach (var namedArg in attribute.NamedArguments)
            args.Add($"{namedArg.Key} = {FormatTypedConstant(namedArg.Value)}");

        return args.Count > 0 
            ? $"[{fullName}({string.Join(", ", args)})]" 
            : $"[{fullName}]";
    }

    private static string FormatTypedConstant(TypedConstant constant) {
        if (constant.IsNull)
            return "null";

        switch (constant.Kind) {
            case TypedConstantKind.Array: {
                var elements        = constant.Values.Select(FormatTypedConstant);
                var arrayType       = (IArrayTypeSymbol)constant.Type!;
                var elementTypeName = arrayType.ElementType.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);

                return $"new {elementTypeName}[] {{ {string.Join(", ", elements)} }}";
            }
            case TypedConstantKind.Type: {
                var typeSymbol = (ITypeSymbol)constant.Value!;

                return $"typeof({typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY)})";
            }
            case TypedConstantKind.Enum: {
                return constant.Type!.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY) + "." + constant.Value;
            }
            case TypedConstantKind.Error:     
            case TypedConstantKind.Primitive:
            default:
                return constant.Value switch {
                    string s    => SymbolDisplay.FormatLiteral(s, true),
                    char c      => SymbolDisplay.FormatLiteral(c, true),
                    bool b      => b ? "true" : "false",
                    double d    => d.ToString(System.Globalization.CultureInfo.InvariantCulture)   + "d",
                    float f     => f.ToString(System.Globalization.CultureInfo.InvariantCulture)   + "f",
                    decimal dec => dec.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m",
                    _           => constant.Value?.ToString() ?? "null"
                };
        }
    }

    private static bool IsPartial(INamedTypeSymbol symbol) {
        foreach (var reference in symbol.DeclaringSyntaxReferences) {
            var node = reference.GetSyntax();

            if (node is TypeDeclarationSyntax typeDecl && typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
                return true;
        }

        return false;
    }

    private static StaticAbstractInfo? GetStaticAbstractInfo(AttributeData attribute, Compilation compilation) {
        if (attribute.AttributeClass?.Name != "StaticAbstractAttribute" && attribute.AttributeClass?.Name != "StaticAbstract")
            return null;

        string?           methodName     = null;
        INamedTypeSymbol? delegateSymbol = null;
        var               typeParams     = new Dictionary<string, string>();
        INamedTypeSymbol? targetClass    = null;

        if (attribute.ConstructorArguments.Length >= 2) {
            var methodNameArg = attribute.ConstructorArguments[0];

            if (methodNameArg.Value is string mName)
                methodName = mName;

            var signatureArg = attribute.ConstructorArguments[1];

            if (signatureArg.Value is INamedTypeSymbol delSymbol)
                delegateSymbol = delSymbol;

            switch (attribute.ConstructorArguments.Length) {
                case 3: {
                    var arg2 = attribute.ConstructorArguments[2];
                    ParseTypeParamsArray(arg2, typeParams);

                    break;
                }
                case 4: {
                    var arg2 = attribute.ConstructorArguments[2];
                    targetClass = arg2.Value as INamedTypeSymbol;

                    var arg3 = attribute.ConstructorArguments[3];
                    ParseTypeParamsArray(arg3, typeParams);

                    break;
                }
            }
        }
        else {
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

            if (expr1 is TypeOfExpressionSyntax typeof1)
                delegateSymbol = semanticModel.GetTypeInfo(typeof1.Type).Type as INamedTypeSymbol;

            // 3. Remaining arguments
            var argsCount = attributeSyntax.ArgumentList.Arguments.Count;

            if (argsCount >= 3) {
                var expr2 = attributeSyntax.ArgumentList.Arguments[2].Expression;

                if (expr2 is TypeOfExpressionSyntax typeof2) {
                    targetClass = semanticModel.GetTypeInfo(typeof2.Type).Type as INamedTypeSymbol;
                    ParseParamsExpressions(attributeSyntax.ArgumentList.Arguments.Skip(3).Select(a => a.Expression), typeParams, semanticModel);
                }
                else {
                    ParseParamsExpressions(attributeSyntax.ArgumentList.Arguments.Skip(2).Select(a => a.Expression), typeParams, semanticModel);
                }
            }
        }

        if (methodName == null || delegateSymbol == null)
            return null;

        return new StaticAbstractInfo(methodName, delegateSymbol, typeParams, targetClass, attribute);
    }

    private static void ParseParamsExpressions(IEnumerable<ExpressionSyntax> expressions, Dictionary<string, string> typeParams, SemanticModel semanticModel) {
        var elements = new List<string>();

        foreach (var expr in expressions) {
            if (expr is CollectionExpressionSyntax || expr is ArrayCreationExpressionSyntax || expr is ImplicitArrayCreationExpressionSyntax) {
                ParseTypeParamsSyntax(expr, typeParams, semanticModel);

                return;
            }

            if (semanticModel.GetConstantValue(expr).Value is string val)
                elements.Add(val);
        }

        for (var i = 0; i < elements.Count; i += 2) {
            if (i + 1 < elements.Count)
                typeParams[elements[i]] = elements[i + 1];
        }
    }

    private static void ParseTypeParamsSyntax(ExpressionSyntax expr, Dictionary<string, string> typeParams, SemanticModel semanticModel) {
        switch (expr) {
            case CollectionExpressionSyntax collection: {
                var elements = new List<string>();

                foreach (var element in collection.Elements) {
                    if (element is not ExpressionElementSyntax exprElem)
                        continue;

                    if (semanticModel.GetConstantValue(exprElem.Expression).Value is string val)
                        elements.Add(val);
                }

                for (var i = 0; i < elements.Count; i += 2) {
                    if (i + 1 < elements.Count)
                        typeParams[elements[i]] = elements[i + 1];
                }

                break;
            }
            case ArrayCreationExpressionSyntax { Initializer: null }: {
                return;
            }
            case ArrayCreationExpressionSyntax arrayCreate: {
                var elements = new List<string>();

                foreach (var element in arrayCreate.Initializer.Expressions) {
                    if (semanticModel.GetConstantValue(element).Value is string val)
                        elements.Add(val);
                }

                for (var i = 0; i < elements.Count; i += 2) {
                    if (i + 1 < elements.Count)
                        typeParams[elements[i]] = elements[i + 1];
                }

                break;
            }
            case ImplicitArrayCreationExpressionSyntax implicitArray: {
                var elements = new List<string>();

                foreach (var element in implicitArray.Initializer.Expressions) {
                    if (semanticModel.GetConstantValue(element).Value is string val)
                        elements.Add(val);
                }

                for (var i = 0; i < elements.Count; i += 2) {
                    if (i + 1 < elements.Count)
                        typeParams[elements[i]] = elements[i + 1];
                }

                break;
            }
        }
    }

    private static void ParseTypeParamsArray(TypedConstant arg, Dictionary<string, string> typeParams) {
        if (arg.Kind != TypedConstantKind.Array)
            return;

        for (var i = 0; i < arg.Values.Length; i += 2) {
            if (i + 1 >= arg.Values.Length)
                continue;

            if (arg.Values[i].Value is string key && arg.Values[i + 1].Value is string val)
                typeParams[key] = val;
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