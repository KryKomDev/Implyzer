// Implyzer
// Copyright (c) KryKom 2026

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Implyzer;

[ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(StaticVirtualCodeRefactoringProvider))]
[Shared]
public class StaticVirtualCodeRefactoringProvider : CodeRefactoringProvider {
    private static readonly SymbolDisplayFormat FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context) {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var node = root.FindNode(context.Span);
        var typeDecl = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDecl == null || typeDecl is InterfaceDeclarationSyntax)
            return;

        if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() != null ||
            node.FirstAncestorOrSelf<PropertyDeclarationSyntax>() != null) {
            return;
        }

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return;

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, context.CancellationToken) as INamedTypeSymbol;
        if (typeSymbol == null || typeSymbol.TypeKind == TypeKind.Interface)
            return;

        var unimplemented = GetUnimplementedStaticVirtualMethods(typeSymbol);
        if (unimplemented.Count == 0)
            return;

        foreach (var method in unimplemented) {
            var title = $"Implement static virtual method '{method.MethodName}'";
            context.RegisterRefactoring(
                CodeAction.Create(
                    title,
                    c => StaticAbstractCodeFixProvider.ImplementStaticMethodAsync(
                        context.Document,
                        typeDecl,
                        method.MethodName,
                        method.ReturnType,
                        method.Parameters,
                        method.DefaultCall,
                        c
                    ),
                    title
                )
            );
        }

        if (unimplemented.Count > 1) {
            const string allTitle = "Implement all static virtual methods";
            context.RegisterRefactoring(
                CodeAction.Create(
                    allTitle,
                    c => StaticAbstractCodeFixProvider.ImplementStaticMethodsAsync(
                        context.Document,
                        typeDecl,
                        unimplemented.Select(m => (m.MethodName, m.ReturnType, m.Parameters, m.DefaultCall)),
                        c
                    ),
                    allTitle
                )
            );
        }
    }

    private class MethodInfo {
        public string  MethodName  { get; }
        public string  ReturnType  { get; }
        public string  Parameters  { get; }
        public string? DefaultCall { get; }

        public MethodInfo(string methodName, string returnType, string parameters, string? defaultCall) {
            MethodName  = methodName;
            ReturnType  = returnType;
            Parameters  = parameters;
            DefaultCall = defaultCall;
        }
    }

    private static List<MethodInfo> GetUnimplementedStaticVirtualMethods(INamedTypeSymbol typeSymbol) {
        var results = new List<MethodInfo>();

        foreach (var iface in typeSymbol.AllInterfaces) {
            foreach (var attr in iface.OriginalDefinition.GetAttributes()) {
                var attrName = attr.AttributeClass?.Name;
                if (attrName is not ("StaticVirtualAttribute" or "StaticVirtual" or "StaticAbstractAttribute" or "StaticAbstract"))
                    continue;

                if (attr.ConstructorArguments.Length < 2)
                    continue;

                var methodName     = attr.ConstructorArguments[0].Value as string;
                var delegateSymbol = attr.ConstructorArguments[1].Value as INamedTypeSymbol;
                if (methodName == null || delegateSymbol == null || delegateSymbol.TypeKind != TypeKind.Delegate)
                    continue;

                var isVirtual   = attrName is "StaticVirtualAttribute" or "StaticVirtual";
                var typeParams  = new Dictionary<string, string>();
                INamedTypeSymbol? targetClass = null;

                if (attr.ConstructorArguments.Length == 3) {
                    ParseTypeParamsArray(attr.ConstructorArguments[2], typeParams);
                }
                else if (attr.ConstructorArguments.Length == 4) {
                    targetClass = attr.ConstructorArguments[2].Value as INamedTypeSymbol;
                    ParseTypeParamsArray(attr.ConstructorArguments[3], typeParams);
                }

                INamedTypeSymbol? defaultType       = null;
                string?           defaultMethodName = null;

                foreach (var na in attr.NamedArguments) {
                    if (na.Key == "DefaultType" && na.Value.Value is INamedTypeSymbol dt)
                        defaultType = dt;
                    else if (na.Key == "DefaultMethod" && na.Value.Value is string dm)
                        defaultMethodName = dm;
                }

                var isDefaultRequested = isVirtual || defaultType != null || defaultMethodName != null;
                var lookupType         = defaultType ?? targetClass ?? iface;
                var lookupMethod       = defaultMethodName ?? methodName;

                if (defaultMethodName == null) {
                    foreach (var member in lookupType.GetMembers().OfType<IMethodSymbol>()) {
                        foreach (var a in member.GetAttributes()) {
                            if (a.AttributeClass?.Name is "StaticDefaultAttribute" or "StaticDefault") {
                                if (a.ConstructorArguments.Length == 0 || a.ConstructorArguments[0].Value is null || Equals(a.ConstructorArguments[0].Value, methodName)) {
                                    isDefaultRequested = true;
                                    lookupMethod       = member.Name;
                                    break;
                                }
                            }
                        }

                        if (lookupMethod != methodName)
                            break;
                    }
                }

                if (!isDefaultRequested)
                    continue;

                var candidates          = lookupType.GetMembers(lookupMethod).OfType<IMethodSymbol>().ToList();
                var defaultMethodSymbol = candidates.FirstOrDefault(m => m.IsStatic && DefaultMethodMatchesSignature(m, delegateSymbol, iface, typeParams));

                defaultMethodSymbol ??= iface.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic);

                if (defaultMethodSymbol == null && !isVirtual)
                    continue;

                var defTypeArgs = new ITypeSymbol[delegateSymbol.TypeParameters.Length];
                for (var i = 0; i < delegateSymbol.TypeParameters.Length; i++) {
                    var          dtp    = delegateSymbol.TypeParameters[i];
                    ITypeSymbol? mapped = null;

                    for (var j = 0; j < iface.OriginalDefinition.TypeParameters.Length; j++) {
                        var itp = iface.OriginalDefinition.TypeParameters[j];
                        if (typeParams.TryGetValue(itp.Name, out var targetName) && targetName == dtp.Name) {
                            mapped = iface.TypeArguments[j];
                            break;
                        }
                    }

                    defTypeArgs[i] = mapped ?? dtp;
                }

                var constructedDelegate = delegateSymbol.OriginalDefinition.Construct(defTypeArgs);
                var delegateInvoke      = constructedDelegate.DelegateInvokeMethod;
                if (delegateInvoke == null)
                    continue;

                var hasMatch = typeSymbol.GetMembers(methodName).OfType<IMethodSymbol>()
                    .Any(m => !IsGenerated(m) && m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && MethodMatchesSignature(m, delegateInvoke));

                if (hasMatch)
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

                string? defaultCall = null;
                if (defaultMethodSymbol != null) {
                    defaultCall = BuildDefaultCall(defaultMethodSymbol, lookupType, iface, delegateInvoke, typeSymbol, typeParams);
                }

                if (!results.Any(r => r.MethodName == methodName)) {
                    results.Add(new MethodInfo(methodName, returnTypeFqn, paramsText, defaultCall));
                }
            }
        }

        return results;
    }

    private static string BuildDefaultCall(
        IMethodSymbol              defaultMethod,
        INamedTypeSymbol           lookupType,
        INamedTypeSymbol           iface,
        IMethodSymbol              delegateInvoke,
        INamedTypeSymbol           targetType,
        Dictionary<string, string> typeParams
    ) {
        var argList = string.Join(
            ", ",
            delegateInvoke.Parameters.Select(
                p => {
                    var refKind = p.RefKind switch {
                        RefKind.Ref => "ref ",
                        RefKind.Out => "out ",
                        RefKind.In  => "in ",
                        _           => ""
                    };
                    return $"{refKind}{p.Name}";
                }
            )
        );

        if (SymbolEqualityComparer.Default.Equals(lookupType, iface)) {
            var ifaceFqn = iface.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);
            return $"{ifaceFqn}.{defaultMethod.Name}({argList})";
        }

        var typeFqn = lookupType.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);

        if (defaultMethod.IsGenericMethod) {
            var methodTypeArguments = new List<string>();

            for (var i = 0; i < defaultMethod.TypeParameters.Length; i++) {
                var          dtp    = defaultMethod.TypeParameters[i];
                ITypeSymbol? mapped = null;

                for (var j = 0; j < iface.OriginalDefinition.TypeParameters.Length; j++) {
                    var itp = iface.OriginalDefinition.TypeParameters[j];
                    if (itp.Name == dtp.Name) {
                        mapped = iface.TypeArguments[j];
                        break;
                    }
                }

                if (mapped == null) {
                    foreach (var kvp in typeParams) {
                        if (kvp.Value == dtp.Name) {
                            for (var j = 0; j < iface.OriginalDefinition.TypeParameters.Length; j++) {
                                if (iface.OriginalDefinition.TypeParameters[j].Name == kvp.Key) {
                                    mapped = iface.TypeArguments[j];
                                    break;
                                }
                            }
                            if (mapped != null) break;
                        }
                    }
                }

                if (mapped == null && i < iface.TypeArguments.Length) {
                    mapped = iface.TypeArguments[i];
                }

                mapped ??= targetType;
                methodTypeArguments.Add(mapped.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY));
            }

            var typeArgsStr = $"<{string.Join(", ", methodTypeArguments)}>";
            return $"{typeFqn}.{defaultMethod.Name}{typeArgsStr}({argList})";
        }

        return $"{typeFqn}.{defaultMethod.Name}({argList})";
    }

    private static bool DefaultMethodMatchesSignature(
        IMethodSymbol              method,
        INamedTypeSymbol           delegateSymbol,
        INamedTypeSymbol           interfaceSymbol,
        Dictionary<string, string> typeParams
    ) {
        var delegateDef = delegateSymbol.OriginalDefinition;
        var typeArgs    = new ITypeSymbol[delegateDef.TypeParameters.Length];

        for (var i = 0; i < delegateDef.TypeParameters.Length; i++) {
            var          dtp        = delegateDef.TypeParameters[i];
            ITypeSymbol? mappedType = null;

            for (var j = 0; j < interfaceSymbol.OriginalDefinition.TypeParameters.Length; j++) {
                var itp = interfaceSymbol.OriginalDefinition.TypeParameters[j];
                if (typeParams.TryGetValue(itp.Name, out var targetName) && targetName == dtp.Name) {
                    mappedType = itp;
                    break;
                }
            }

            typeArgs[i] = mappedType ?? dtp;
        }

        var constructedDelegate = delegateDef.TypeParameters.Length > 0
            ? delegateDef.Construct(typeArgs)
            : delegateDef;

        var expectedInvoke = constructedDelegate.DelegateInvokeMethod;
        if (expectedInvoke == null)
            return false;

        if (method.Parameters.Length != expectedInvoke.Parameters.Length)
            return false;

        if (method.IsGenericMethod) {
            ITypeSymbol[] methodTypeArgs;
            if (method.TypeParameters.Length == typeArgs.Length)
                methodTypeArgs = typeArgs;
            else if (method.TypeParameters.Length == interfaceSymbol.OriginalDefinition.TypeParameters.Length)
                methodTypeArgs = interfaceSymbol.OriginalDefinition.TypeParameters.Cast<ITypeSymbol>().ToArray();
            else
                return false;

            try {
                var constructedMethod = method.Construct(methodTypeArgs);
                return MethodMatchesSignature(constructedMethod, expectedInvoke);
            }
            catch {
                return false;
            }
        }

        return MethodMatchesSignature(method, expectedInvoke);
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

    private static string FormatAttributes(ImmutableArray<AttributeData> attributes) {
        if (attributes.Length == 0)
            return "";

        var formatted = new List<string>();
        foreach (var attr in attributes) {
            var name = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (name != null)
                formatted.Add($"[{name}] ");
        }

        return string.Join("", formatted);
    }

    private static string FormatReturnAttributes(ImmutableArray<AttributeData> attributes) {
        if (attributes.Length == 0)
            return "";

        var formatted = new List<string>();
        foreach (var attr in attributes) {
            var name = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (name != null)
                formatted.Add($"[return: {name}] ");
        }

        return string.Join("", formatted);
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

    private static bool IsGenerated(ISymbol symbol) {
        if (symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "GeneratedCodeAttribute" or "CompilerGeneratedAttribute")) {
            return true;
        }

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences) {
            if (IsGeneratedSyntaxTree(syntaxRef.SyntaxTree))
                return true;
        }

        return false;
    }

    private static bool IsGeneratedSyntaxTree(SyntaxTree? tree) {
        if (tree == null)
            return false;

        var path = tree.FilePath;
        if (!string.IsNullOrEmpty(path)) {
            if (path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
                path.IndexOf(".StaticVirtual.g.cs", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("_StaticVirtual.g.cs", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        var root = tree.GetRoot();
        if (root.HasLeadingTrivia) {
            foreach (var trivia in root.GetLeadingTrivia()) {
                var text = trivia.ToString();
                if (text.Contains("<auto-generated") || text.Contains("<autogenerated")) {
                    return true;
                }
            }
        }

        return false;
    }
}
