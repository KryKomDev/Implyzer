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
        context.RegisterCompilationAction(AnalyzeCompilation);
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
            var info = GetStaticAbstractInfo(attribute, interfaceSymbol);

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

            // 3. Validate default implementation if requested
            var isDefaultRequested = info.DefaultType != null || info.DefaultMethod != null || info.IsVirtual;

            if (isDefaultRequested && !info.HasDefaultImplementation) {
                var lookupType       = info.ResolvedDefaultType ?? info.DefaultType ?? info.TargetClass ?? interfaceSymbol;
                var lookupMethodName = info.DefaultMethod       ?? info.MethodName;

                var candidates = lookupType.GetMembers(lookupMethodName).OfType<IMethodSymbol>().ToList();

                if (candidates.Count == 0) {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Rules.StaticAbstractDefaultMethodNotFound,
                            attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? interfaceSymbol.Locations[0],
                            lookupMethodName,
                            lookupType.Name
                        )
                    );
                }
                else if (!candidates.Any(m => m.IsStatic)) {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Rules.StaticAbstractDefaultMethodMustBeStatic,
                            attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? interfaceSymbol.Locations[0],
                            lookupMethodName,
                            lookupType.Name
                        )
                    );
                }
                else {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Rules.StaticAbstractDefaultMethodSignatureMismatch,
                            attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? interfaceSymbol.Locations[0],
                            lookupMethodName,
                            lookupType.Name,
                            info.DelegateSymbol.Name
                        )
                    );
                }
            }
        }

        AnalyzeInterfaceStaticRegister(context, interfaceSymbol);
    }

    private static void AnalyzeImplementingType(SymbolAnalysisContext context, INamedTypeSymbol typeSymbol) {
        foreach (var iface in typeSymbol.AllInterfaces)
        foreach (var attribute in iface.OriginalDefinition.GetAttributes()) {
            var info = GetStaticAbstractInfo(attribute, iface.OriginalDefinition);

            if (info == null)
                continue;

            if (info.DelegateSymbol.TypeKind != TypeKind.Delegate)
                continue;

            if (info.HasDefaultImplementation)
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

    private static void AnalyzeInterfaceStaticRegister(SymbolAnalysisContext context, INamedTypeSymbol interfaceSymbol) {
        var registerAttrs = interfaceSymbol.GetAttributes()
            .Where(a => a.AttributeClass?.Name is "StaticRegisterAttribute" or "StaticRegister")
            .ToList();

        if (registerAttrs.Count == 0)
            return;

        var contracts = GetInterfaceContracts(interfaceSymbol);

        if (contracts.Count == 0) {
            foreach (var attr in registerAttrs) {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        Rules.StaticRegisterInterfaceNotStaticAbstract,
                        attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? interfaceSymbol.Locations[0],
                        interfaceSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                    )
                );
            }

            return;
        }

        foreach (var attr in registerAttrs) {
            var parsed      = ParseStaticRegisterAttribute(attr, interfaceSymbol);
            var targetIface = parsed.TargetInterface ?? interfaceSymbol;

            ValidateRegisteredTypes(
                reportDiagnostic: context.ReportDiagnostic,
                attribute: attr,
                targetInterface: targetIface,
                candidateTypes: parsed.Types,
                strict: parsed.Strict,
                targetInterfaceIsPositional: false,
                contracts: contracts,
                fallbackLocation: interfaceSymbol.Locations[0]
            );
        }
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context) {
        foreach (var attr in context.Compilation.Assembly.GetAttributes()) {
            if (attr.AttributeClass?.Name is not ("StaticRegisterAttribute" or "StaticRegister"))
                continue;

            var parsed = ParseStaticRegisterAttribute(attr, null);

            if (parsed.TargetInterface == null)
                continue;

            var contracts = GetInterfaceContracts(parsed.TargetInterface);

            if (contracts.Count == 0) {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        Rules.StaticRegisterInterfaceNotStaticAbstract,
                        attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None,
                        parsed.TargetInterface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                    )
                );
            }
            else {
                ValidateRegisteredTypes(
                    reportDiagnostic: context.ReportDiagnostic,
                    attribute: attr,
                    targetInterface: parsed.TargetInterface,
                    candidateTypes: parsed.Types,
                    strict: parsed.Strict,
                    targetInterfaceIsPositional: parsed.TargetInterfaceIsPositional,
                    contracts: contracts,
                    fallbackLocation: attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None
                );
            }
        }
    }

    private static void ValidateRegisteredTypes(
        Action<Diagnostic>       reportDiagnostic,
        AttributeData            attribute,
        INamedTypeSymbol         targetInterface,
        List<ITypeSymbol>        candidateTypes,
        bool                     strict,
        bool                     targetInterfaceIsPositional,
        List<StaticAbstractInfo> contracts,
        Location                 fallbackLocation
    ) {
        for (var i = 0; i < candidateTypes.Count; i++) {
            var candidateType       = candidateTypes[i];
            var normalizedCandidate = candidateType is INamedTypeSymbol { IsUnboundGenericType: true } named ? named.OriginalDefinition : candidateType;
            var candidateLocation   = GetCandidateTypeLocation(attribute, candidateType, i, targetInterfaceIsPositional, fallbackLocation);

            // 1. Redundancy check (IMPL016)
            var alreadyImplements = normalizedCandidate.AllInterfaces.Any(
                iface =>
                    SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, targetInterface.OriginalDefinition)
            );

            if (alreadyImplements) {
                reportDiagnostic(
                    Diagnostic.Create(
                        Rules.StaticRegisterTypeAlreadyImplementsInterface,
                        candidateLocation,
                        candidateType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        targetInterface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                    )
                );
            }

            // 2. Conformance check (IMPL014)
            foreach (var contract in contracts) {
                if (contract.HasDefaultImplementation)
                    continue;

                if (!CandidateTypeImplementsContract(candidateType, contract, targetInterface)) {
                    if (strict) {
                        var constructedDelegate = ConstructDelegateForCandidateType(contract, targetInterface, candidateType);
                        var delegateDisplay     = constructedDelegate?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? contract.DelegateSymbol.Name;

                        reportDiagnostic(
                            Diagnostic.Create(
                                Rules.StaticRegisterTypeMissingMember,
                                candidateLocation,
                                candidateType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                targetInterface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                contract.MethodName,
                                delegateDisplay
                            )
                        );
                    }

                    break;
                }
            }
        }
    }

    private static Location GetCandidateTypeLocation(
        AttributeData attribute,
        ITypeSymbol   candidateType,
        int           index,
        bool          targetInterfaceIsPositional,
        Location      fallback
    ) {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax { ArgumentList: not null } syntax) {
            var positionalArgs = syntax.ArgumentList.Arguments.Where(a => a.NameEquals == null).ToList();
            var argIndex       = targetInterfaceIsPositional ? index + 1 : index;

            if (argIndex >= 0 && argIndex < positionalArgs.Count) {
                return positionalArgs[argIndex].GetLocation();
            }

            return syntax.GetLocation();
        }

        return fallback;
    }

    private static (INamedTypeSymbol? TargetInterface, List<ITypeSymbol> Types, bool Strict, bool TargetInterfaceIsPositional) ParseStaticRegisterAttribute(
        AttributeData     attribute,
        INamedTypeSymbol? contextInterface
    ) {
        var               strict                      = true;
        INamedTypeSymbol? targetInterface             = contextInterface?.OriginalDefinition;
        var               targetInterfaceIsPositional = false;

        foreach (var na in attribute.NamedArguments) {
            if (na is { Key: "Strict", Value.Value: bool b })
                strict = b;
            else if (na is { Key: "TargetInterface", Value.Value: INamedTypeSymbol iface })
                targetInterface = iface.OriginalDefinition;
        }

        var types = new List<ITypeSymbol>();

        if (attribute.ConstructorArguments.Length > 0) {
            var arg0 = attribute.ConstructorArguments[0];

            if (arg0.Kind == TypedConstantKind.Array) {
                foreach (var val in arg0.Values) {
                    if (val.Value is ITypeSymbol ts)
                        types.Add(ts);
                }
            }
            else if (arg0 is { Kind: TypedConstantKind.Type, Value: ITypeSymbol ts }) {
                types.Add(ts);
            }
        }

        // If on assembly and TargetInterface was not specified via property, but first type is an interface
        if (contextInterface == null && targetInterface == null && types.Count > 0 && types[0].TypeKind == TypeKind.Interface) {
            targetInterface = (types[0] as INamedTypeSymbol)?.OriginalDefinition;
            types.RemoveAt(0);
            targetInterfaceIsPositional = true;
        }

        return (targetInterface?.OriginalDefinition, types, strict, targetInterfaceIsPositional);
    }

    private static List<StaticAbstractInfo> GetInterfaceContracts(INamedTypeSymbol interfaceSymbol) {
        var contracts = new List<StaticAbstractInfo>();

        foreach (var attr in interfaceSymbol.OriginalDefinition.GetAttributes()) {
            var info = GetStaticAbstractInfo(attr, interfaceSymbol.OriginalDefinition);

            if (info != null && info.DelegateSymbol.TypeKind == TypeKind.Delegate)
                contracts.Add(info);
        }

        return contracts;
    }

    private static INamedTypeSymbol? ConstructDelegateForCandidateType(
        StaticAbstractInfo contract,
        INamedTypeSymbol   interfaceSymbol,
        ITypeSymbol        candidateType
    ) {
        if (candidateType is INamedTypeSymbol { IsUnboundGenericType: true } namedCandidate) {
            candidateType = namedCandidate.OriginalDefinition;
        }

        var delegateDef = contract.DelegateSymbol.OriginalDefinition;

        if (delegateDef.TypeParameters.Length == 0)
            return delegateDef;

        var typeArgs = new ITypeSymbol[delegateDef.TypeParameters.Length];

        for (var i = 0; i < delegateDef.TypeParameters.Length; i++) {
            var          dtp        = delegateDef.TypeParameters[i];
            ITypeSymbol? mappedType = null;

            for (var j = 0; j < interfaceSymbol.OriginalDefinition.TypeParameters.Length; j++) {
                var itp = interfaceSymbol.OriginalDefinition.TypeParameters[j];

                if (contract.TypeParams.TryGetValue(itp.Name, out var targetName) && targetName == dtp.Name) {
                    mappedType = candidateType;

                    break;
                }
            }

            if (mappedType == null && interfaceSymbol.OriginalDefinition.TypeParameters.Length == 1 && delegateDef.TypeParameters.Length == 1) {
                mappedType = candidateType;
            }

            typeArgs[i] = mappedType ?? dtp;
        }

        try {
            return delegateDef.Construct(typeArgs);
        }
        catch {
            return null;
        }
    }

    private static bool CandidateTypeImplementsContract(
        ITypeSymbol        candidateType,
        StaticAbstractInfo contract,
        INamedTypeSymbol   interfaceSymbol
    ) {
        if (candidateType is INamedTypeSymbol { IsUnboundGenericType: true } namedCandidate) {
            candidateType = namedCandidate.OriginalDefinition;
        }

        var constructedDelegate = ConstructDelegateForCandidateType(contract, interfaceSymbol, candidateType);

        if (constructedDelegate == null)
            return false;

        var delegateInvoke = constructedDelegate.DelegateInvokeMethod;

        if (delegateInvoke == null)
            return false;

        // 1. Method lookup
        for (var current = candidateType; current != null; current = current.BaseType) {
            var methods = current.GetMembers(contract.MethodName).OfType<IMethodSymbol>();

            foreach (var m in methods) {
                if (!m.IsStatic || m.DeclaredAccessibility != Accessibility.Public)
                    continue;

                if (m.IsGenericMethod) {
                    if (m.TypeParameters.Length == constructedDelegate.TypeParameters.Length) {
                        try {
                            var typeArgs    = constructedDelegate.TypeArguments.ToArray();
                            var constructed = m.Construct(typeArgs);

                            if (MethodMatchesSignature(constructed, delegateInvoke))
                                return true;
                        }
                        catch { }
                    }
                }
                else if (MethodMatchesSignature(m, delegateInvoke)) {
                    return true;
                }
            }
        }

        // 2. Property lookup
        for (var current = candidateType; current != null; current = current.BaseType) {
            var properties = current.GetMembers(contract.MethodName).OfType<IPropertySymbol>();

            foreach (var p in properties) {
                if (!p.IsStatic || p.DeclaredAccessibility != Accessibility.Public)
                    continue;

                if (delegateInvoke.Parameters.Length == 0 && !delegateInvoke.ReturnsVoid) {
                    if (p.GetMethod is { IsStatic: true, DeclaredAccessibility: Accessibility.Public }) {
                        if (SymbolEqualityComparer.Default.Equals(p.Type, delegateInvoke.ReturnType))
                            return true;
                    }
                }
                else if (delegateInvoke.Parameters.Length == 1 && delegateInvoke.ReturnsVoid) {
                    if (p.SetMethod is { IsStatic: true, DeclaredAccessibility: Accessibility.Public }) {
                        if (SymbolEqualityComparer.Default.Equals(p.Type, delegateInvoke.Parameters[0].Type))
                            return true;
                    }
                }
            }
        }

        // 3. Property getter/setter method name lookup (e.g. get_Zero)
        if (contract.MethodName.StartsWith("get_")) {
            var propName = contract.MethodName.Substring(4);

            for (var current = candidateType; current != null; current = current.BaseType) {
                foreach (var p in current.GetMembers(propName).OfType<IPropertySymbol>()) {
                    if (p.IsStatic && p is { DeclaredAccessibility: Accessibility.Public, GetMethod: not null }) {
                        if (SymbolEqualityComparer.Default.Equals(p.Type, delegateInvoke.ReturnType))
                            return true;
                    }
                }
            }
        }
        else {
            var getMethodName = "get_" + contract.MethodName;

            for (var current = candidateType; current != null; current = current.BaseType) {
                foreach (var m in current.GetMembers(getMethodName).OfType<IMethodSymbol>()) {
                    if (m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && MethodMatchesSignature(m, delegateInvoke))
                        return true;
                }
            }
        }

        return false;
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

    private static StaticAbstractInfo? GetStaticAbstractInfo(AttributeData attribute, INamedTypeSymbol? interfaceSymbol = null) {
        var attrName = attribute.AttributeClass?.Name;

        if (attrName != "StaticAbstractAttribute" && attrName != "StaticAbstract" &&
            attrName != "StaticVirtualAttribute"  && attrName != "StaticVirtual")
            return null;

        var               isVirtual      = attrName is "StaticVirtualAttribute" or "StaticVirtual";
        string?           methodName     = null;
        INamedTypeSymbol? delegateSymbol = null;
        var               typeParams     = new Dictionary<string, string>();
        INamedTypeSymbol? targetClass    = null;
        INamedTypeSymbol? defaultType    = null;
        string?           defaultMethod  = null;

        foreach (var namedArg in attribute.NamedArguments) {
            if (namedArg is { Key: "DefaultType", Value.Value: INamedTypeSymbol dt })
                defaultType = dt;
            else if (namedArg is { Key: "DefaultMethod", Value.Value: string dm })
                defaultMethod = dm;
        }

        if (attribute.ConstructorArguments.Length < 2)
            return null;

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

        if (methodName == null || delegateSymbol == null)
            return null;

        var info = new StaticAbstractInfo(methodName, delegateSymbol, typeParams, targetClass, attribute, defaultType, defaultMethod, isVirtual);

        if (interfaceSymbol != null)
            ResolveDefaultImplementation(info, interfaceSymbol);

        return info;
    }

    private static void ResolveDefaultImplementation(StaticAbstractInfo info, INamedTypeSymbol interfaceSymbol) {
        var isDefaultRequested = info.DefaultType != null || info.DefaultMethod != null || info.IsVirtual;
        var lookupType         = info.DefaultType   ?? info.TargetClass ?? interfaceSymbol;
        var lookupMethodName   = info.DefaultMethod ?? info.MethodName;

        if (info.DefaultMethod == null) {
            foreach (var member in lookupType.GetMembers().OfType<IMethodSymbol>()) {
                foreach (var attr in member.GetAttributes()) {
                    if (attr.AttributeClass?.Name is "StaticDefaultAttribute" or "StaticDefault") {
                        if (attr.ConstructorArguments.Length == 0 || attr.ConstructorArguments[0].Value is null || Equals(attr.ConstructorArguments[0].Value, info.MethodName)) {
                            isDefaultRequested = true;
                            lookupMethodName   = member.Name;

                            break;
                        }
                    }
                }

                if (lookupMethodName != info.MethodName)
                    break;
            }
        }

        if (!isDefaultRequested)
            return;

        info.ResolvedDefaultType = lookupType;

        var candidates     = lookupType.GetMembers(lookupMethodName).OfType<IMethodSymbol>().ToList();
        var matchingMethod = candidates.FirstOrDefault(m => m.IsStatic && DefaultMethodMatchesSignature(m, info.DelegateSymbol, interfaceSymbol, info.TypeParams));

        if (matchingMethod != null) {
            info.HasDefaultImplementation = true;
            info.DefaultMethodSymbol      = matchingMethod;
        }
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

            if (method.TypeParameters.Length == typeArgs.Length) {
                methodTypeArgs = typeArgs;
            }
            else if (method.TypeParameters.Length == interfaceSymbol.OriginalDefinition.TypeParameters.Length) {
                methodTypeArgs = interfaceSymbol.OriginalDefinition.TypeParameters.Cast<ITypeSymbol>().ToArray();
            }
            else {
                return false;
            }

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
        public string                     MethodName               { get; }
        public INamedTypeSymbol           DelegateSymbol           { get; }
        public Dictionary<string, string> TypeParams               { get; }
        public INamedTypeSymbol?          TargetClass              { get; }
        public AttributeData              AttributeData            { get; }
        public INamedTypeSymbol?          DefaultType              { get; }
        public string?                    DefaultMethod            { get; }
        public bool                       IsVirtual                { get; }
        public bool                       HasDefaultImplementation { get; set; }
        public IMethodSymbol?             DefaultMethodSymbol      { get; set; }
        public INamedTypeSymbol?          ResolvedDefaultType      { get; set; }

        public StaticAbstractInfo(
            string                     methodName,
            INamedTypeSymbol           delegateSymbol,
            Dictionary<string, string> typeParams,
            INamedTypeSymbol?          targetClass,
            AttributeData              attributeData,
            INamedTypeSymbol?          defaultType,
            string?                    defaultMethod,
            bool                       isVirtual
        ) {
            MethodName     = methodName;
            DelegateSymbol = delegateSymbol;
            TypeParams     = typeParams;
            TargetClass    = targetClass;
            AttributeData  = attributeData;
            DefaultType    = defaultType;
            DefaultMethod  = defaultMethod;
            IsVirtual      = isVirtual;
        }
    }
}