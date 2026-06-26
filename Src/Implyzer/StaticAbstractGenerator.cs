// Implyzer
// Copyright (c) KryKom 2026

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Implyzer;

[Generator]
public class StaticAbstractGenerator : IIncrementalGenerator {
    private static readonly SymbolDisplayFormat FullyQualifiedFormatWithNullability =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    private static string FormatAttributes(IEnumerable<AttributeData> attributes, string? typeParamNameToReplace = null) {
        var formatted = new List<string>();
        foreach (var attr in attributes) {
            var formattedAttr = FormatAttribute(attr, typeParamNameToReplace);
            if (!string.IsNullOrEmpty(formattedAttr)) {
                formatted.Add(formattedAttr);
            }
        }
        return formatted.Count > 0 ? string.Join(" ", formatted) + " " : "";
    }

    private static string FormatReturnAttributes(IEnumerable<AttributeData> attributes, string? typeParamNameToReplace = null) {
        var formatted = new List<string>();
        foreach (var attr in attributes) {
            var formattedAttr = FormatAttribute(attr, typeParamNameToReplace);
            if (!string.IsNullOrEmpty(formattedAttr)) {
                if (formattedAttr.StartsWith("[") && formattedAttr.EndsWith("]")) {
                    formattedAttr = "[return: " + formattedAttr.Substring(1);
                }
                formatted.Add(formattedAttr);
            }
        }
        return formatted.Count > 0 ? string.Join(" ", formatted) + " " : "";
    }

    private static string FormatAttribute(AttributeData attribute, string? typeParamNameToReplace = null) {
        if (attribute.AttributeClass == null)
            return "";

        var fullName = attribute.AttributeClass.ToDisplayString(FullyQualifiedFormatWithNullability);
        if (fullName == "global::System.Runtime.CompilerServices.NullableAttribute" || 
            fullName == "global::System.Runtime.CompilerServices.NullableContextAttribute" ||
            fullName == "global::System.Runtime.CompilerServices.NullablePublicOnlyAttribute" ||
            fullName == "global::System.Runtime.CompilerServices.NativeIntegerAttribute" ||
            fullName == "global::System.Runtime.CompilerServices.DynamicAttribute" ||
            fullName == "global::System.Runtime.CompilerServices.TupleElementNamesAttribute" ||
            fullName == "global::System.Runtime.CompilerServices.IsReadOnlyAttribute" ||
            fullName == "global::System.ParamArrayAttribute" ||
            fullName == "global::System.Runtime.InteropServices.OutAttribute" ||
            fullName == "global::System.Runtime.InteropServices.InAttribute") {
            return "";
        }

        var args = new List<string>();

        foreach (var arg in attribute.ConstructorArguments) {
            args.Add(FormatTypedConstant(arg, typeParamNameToReplace));
        }

        foreach (var namedArg in attribute.NamedArguments) {
            args.Add($"{namedArg.Key} = {FormatTypedConstant(namedArg.Value, typeParamNameToReplace)}");
        }

        if (args.Count > 0) {
            return $"[{fullName}({string.Join(", ", args)})]";
        }

        return $"[{fullName}]";
    }

    private static string FormatTypedConstant(TypedConstant constant, string? typeParamNameToReplace) {
        if (constant.IsNull)
            return "null";

        if (constant.Kind == TypedConstantKind.Array) {
            var elements = constant.Values.Select(v => FormatTypedConstant(v, typeParamNameToReplace));
            var arrayType = (IArrayTypeSymbol)constant.Type!;
            var elementTypeName = ToNonGenericTypeString(arrayType.ElementType.WithNullableAnnotation(NullableAnnotation.NotAnnotated), typeParamNameToReplace ?? "");
            return $"new {elementTypeName}[] {{ {string.Join(", ", elements)} }}";
        }

        if (constant.Kind == TypedConstantKind.Type) {
            var typeSymbol = (ITypeSymbol)constant.Value!;
            var typeStr = ToNonGenericTypeString(typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated), typeParamNameToReplace ?? "");
            return $"typeof({typeStr})";
        }

        if (constant.Kind == TypedConstantKind.Enum) {
            return constant.Type!.ToDisplayString(FullyQualifiedFormatWithNullability) + "." + constant.Value;
        }

        if (constant.Value is string s) {
            return Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(s, true);
        }

        if (constant.Value is char c) {
            return Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(c, true);
        }

        if (constant.Value is bool b) {
            return b ? "true" : "false";
        }

        if (constant.Value is double d) {
            return d.ToString(System.Globalization.CultureInfo.InvariantCulture) + "d";
        }

        if (constant.Value is float f) {
            return f.ToString(System.Globalization.CultureInfo.InvariantCulture) + "f";
        }

        if (constant.Value is decimal dec) {
            return dec.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m";
        }

        return constant.Value?.ToString() ?? "null";
    }

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        // Collect interfaces with [StaticAbstract] attributes
        var interfaces = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node,    _) => node is InterfaceDeclarationSyntax { AttributeLists.Count: > 0 },
            transform: static (context, _) => GetInterfaceInfo(context)
        ).Where(static x => x is not null).Select(static (x, _) => x!);

        // Collect class/struct declarations
        var classesAndStructs = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node,    _) => node is ClassDeclarationSyntax or StructDeclarationSyntax,
            transform: static (context, _) => GetTypeSymbol(context)
        ).Where(static x => x is not null).Select(static (x, _) => x!);

        // Combine them with compilation to run generation
        var combined = interfaces.Collect().Combine(classesAndStructs.Collect()).Combine(context.CompilationProvider);

        context.RegisterSourceOutput(
            combined,
            static (spc, source) => {
                var ((ifaces, types), compilation) = source;
                Generate(spc, ifaces, types, compilation);
            }
        );
    }

    private static INamedTypeSymbol? GetInterfaceInfo(GeneratorSyntaxContext context) {
        var interfaceDecl = (InterfaceDeclarationSyntax)context.Node;
        var symbol        = context.SemanticModel.GetDeclaredSymbol(interfaceDecl);

        if (symbol is null)
            return null;

        if (symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "StaticAbstractAttribute" || a.AttributeClass?.Name == "StaticAbstract")) {
            return symbol;
        }

        return null;
    }

    private static INamedTypeSymbol? GetTypeSymbol(GeneratorSyntaxContext context) {
        var typeDecl = (TypeDeclarationSyntax)context.Node;

        return context.SemanticModel.GetDeclaredSymbol(typeDecl);
    }

    private static void Generate(
        SourceProductionContext          spc,
        ImmutableArray<INamedTypeSymbol> ifaces,
        ImmutableArray<INamedTypeSymbol> types,
        Compilation                      compilation
    ) {
        var registryGroups    = new Dictionary<INamedTypeSymbol, List<StaticAbstractInfo>>(SymbolEqualityComparer.Default);
        var interfaceForwards = new Dictionary<INamedTypeSymbol, List<StaticAbstractInfo>>(SymbolEqualityComparer.Default);
        var allInfos          = new List<StaticAbstractInfo>();

        foreach (var iface in ifaces.Distinct(SymbolEqualityComparer.Default).Cast<INamedTypeSymbol>()) {
            foreach (var attr in iface.GetAttributes()) {
                var info = GetStaticAbstractInfo(attr, iface, compilation);

                if (info == null)
                    continue;

                if (info.DelegateSymbol.TypeKind != TypeKind.Delegate)
                    continue;

                allInfos.Add(info);

                if (info.TargetClass != null) {
                    if (!registryGroups.TryGetValue(info.TargetClass, out var list)) {
                        list                             = new List<StaticAbstractInfo>();
                        registryGroups[info.TargetClass] = list;
                    }

                    list.Add(info);
                }
                else {
                    if (!registryGroups.TryGetValue(iface, out var list)) {
                        list                  = new List<StaticAbstractInfo>();
                        registryGroups[iface] = list;
                    }

                    list.Add(info);

                    if (iface.Arity > 0) {
                        if (!interfaceForwards.TryGetValue(iface, out var forwardList)) {
                            forwardList              = new List<StaticAbstractInfo>();
                            interfaceForwards[iface] = forwardList;
                        }

                        forwardList.Add(info);
                    }
                }
            }
        }

        // Generate Registry classes
        foreach (var kvp in registryGroups) {
            var registryClass = kvp.Key;
            var infos         = kvp.Value;

            var ns = registryClass.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (ns.StartsWith("global::")) {
                ns = ns.Substring(8);
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("#pragma warning disable");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns} {{");

            if (registryClass.TypeKind == TypeKind.Class) {
                sb.AppendLine($"    public partial class {registryClass.Name} {{");
            }
            else if (registryClass.TypeKind == TypeKind.Interface) {
                sb.AppendLine(
                    registryClass.Arity > 0 
                        ? $"    public static partial class {registryClass.Name} {{" 
                        : $"    public partial interface {registryClass.Name} {{"
                );
            }

            foreach (var info in infos) {
                GenerateRegistryContent(sb, info);
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            var hintName = $"{registryClass.ContainingNamespace.ToDisplayString()}_{registryClass.Name}_Registry.g.cs";
            spc.AddSource(hintName, sb.ToString());
        }

        // Generate Interface Forwards for generic interfaces
        foreach (var kvp in interfaceForwards) {
            var interfaceSymbol = kvp.Key;
            var infos           = kvp.Value;

            var ns = interfaceSymbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (ns.StartsWith("global::")) {
                ns = ns.Substring(8);
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("#pragma warning disable");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns} {{");

            var typeParamsStr = string.Join(", ", interfaceSymbol.TypeParameters.Select(tp => tp.Name));
            sb.AppendLine($"    public partial interface {interfaceSymbol.Name}<{typeParamsStr}> {{");

            foreach (var info in infos) {
                GenerateInterfaceForwardContent(sb, info, interfaceSymbol);
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            var hintName = $"{interfaceSymbol.ContainingNamespace.ToDisplayString()}_{interfaceSymbol.Name}_Forward.g.cs";
            spc.AddSource(hintName, sb.ToString());
        }

        // Generate Module Initializer
        GenerateModuleInitializer(spc, allInfos, types, compilation);
    }

    private static void GenerateRegistryContent(StringBuilder sb, StaticAbstractInfo info) {
        var delegateSymbol = info.DelegateSymbol.OriginalDefinition;
        var invokeMethod   = delegateSymbol.DelegateInvokeMethod;

        if (invokeMethod == null)
            return;

        var methodName    = info.MethodName;
        var castType      = delegateSymbol.ToDisplayString(FullyQualifiedFormatWithNullability);
        var returnAttributes = FormatReturnAttributes(invokeMethod.GetReturnTypeAttributes());
        var returnTypeStr = invokeMethod.ReturnType.ToDisplayString(FullyQualifiedFormatWithNullability);

        var typeParams = delegateSymbol.TypeParameters;

        var typeParamsStr = typeParams.Length > 0
            ? $"<{string.Join(", ", typeParams.Select(tp => tp.Name))}>"
            : "";

        var constraintClauses = new List<string>();

        foreach (var tp in typeParams) {
            var constraints = new List<string>();

            if (tp.HasReferenceTypeConstraint)
                constraints.Add("class");
            else if (tp.HasValueTypeConstraint)
                constraints.Add("struct");

            foreach (var ct in tp.ConstraintTypes) {
                constraints.Add(ct.ToDisplayString(FullyQualifiedFormatWithNullability));
            }

            if (tp.HasConstructorConstraint)
                constraints.Add("new()");

            if (constraints.Count > 0) {
                constraintClauses.Add($"where {tp.Name} : {string.Join(", ", constraints)}");
            }
        }

        var constraintsStr = constraintClauses.Count > 0 ? " " + string.Join(" ", constraintClauses) : "";

        var paramList = string.Join(
            ", ",
            invokeMethod.Parameters.Select(
                p => {
                    var refKind = p.RefKind switch {
                        RefKind.Ref => "ref ",
                        RefKind.Out => "out ",
                        RefKind.In  => "in ",
                        _           => p.IsParams ? "params " : ""
                    };

                    var attrs = FormatAttributes(p.GetAttributes());
                    return $"{attrs}{refKind}{p.Type.ToDisplayString(FullyQualifiedFormatWithNullability)} {p.Name}";
                }
            )
        );

        var argList = string.Join(
            ", ",
            invokeMethod.Parameters.Select(
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

        string lookupTypeName = "object";

        foreach (var kvp in info.TypeParams) {
            var dtpName = kvp.Value;

            if (delegateSymbol.TypeParameters.Any(tp => tp.Name == dtpName)) {
                lookupTypeName = dtpName;

                break;
            }
        }

        if (lookupTypeName == "object" && delegateSymbol.TypeParameters.Length > 0) {
            lookupTypeName = delegateSymbol.TypeParameters[0].Name;
        }

        sb.AppendLine(
            $$"""
                    private static readonly global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Delegate> _{{methodName}}Registry = new();
            
                    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
                    public static void G_Register_{{methodName}}(global::System.Type type, global::System.Delegate impl) {
                        _{{methodName}}Registry[type] = impl;
                    }
                    
                    {{returnAttributes}}public static {{returnTypeStr}} {{methodName}}{{typeParamsStr}}({{paramList}}){{constraintsStr}} {
                        if (_{{methodName}}Registry.TryGetValue(typeof({{lookupTypeName}}), out var impl)) {
            """
        );

        if (invokeMethod.ReturnsVoid) {
            sb.AppendLine(
                $"""
                                 (({castType})impl)({argList});
                                 return;
                 """
            );
        }
        else {
            sb.AppendLine(
                $"                return (({castType})impl)({argList});"
            );
        }

        sb.AppendLine($"            }}");
        sb.AppendLine($"            throw new global::System.InvalidOperationException($\"No implementation of {methodName} registered for type {{typeof({lookupTypeName})}}.\");");
        sb.AppendLine($"        }}");

        var nonGenericReturnAttributes = FormatReturnAttributes(invokeMethod.GetReturnTypeAttributes(), lookupTypeName);
        var nonGenericParamListElements = new List<string> { "global::System.Type type" };
        nonGenericParamListElements.AddRange(
            invokeMethod.Parameters.Select(
                p => {
                    var refKind = p.RefKind switch {
                        RefKind.Ref => "ref ",
                        RefKind.Out => "out ",
                        RefKind.In  => "in ",
                        _           => p.IsParams ? "params " : ""
                    };

                    var attrs = FormatAttributes(p.GetAttributes(), lookupTypeName);
                    var typeStr = ToNonGenericTypeString(p.Type, lookupTypeName);
                    return $"{attrs}{refKind}{typeStr} {p.Name}";
                }
            )
        );
        var nonGenericParamList = string.Join(", ", nonGenericParamListElements);
        var nonGenericReturnTypeStr = ToNonGenericTypeString(invokeMethod.ReturnType, lookupTypeName);
        var argListWithoutRef = string.Join(", ", invokeMethod.Parameters.Select(p => p.RefKind == RefKind.Out ? "default" : p.Name));
        
        var copyBackStatements = new List<string>();
        for (int i = 0; i < invokeMethod.Parameters.Length; i++) {
            var p = invokeMethod.Parameters[i];
            if (p.RefKind == RefKind.Out || p.RefKind == RefKind.Ref) {
                var typeStr = ToNonGenericTypeString(p.Type, lookupTypeName);
                copyBackStatements.Add($"{p.Name} = ({typeStr})args[{i}];");
            }
        }
        
        var copyBackStr = copyBackStatements.Count > 0 ? "                " + string.Join("\n                ", copyBackStatements) + "\n" : "";

        sb.AppendLine();
        sb.AppendLine(
            $$"""
                    {{nonGenericReturnAttributes}}public static {{nonGenericReturnTypeStr}} {{methodName}}({{nonGenericParamList}}) {
                        if (_{{methodName}}Registry.TryGetValue(type, out var impl)) {
                            var args = new object?[] { {{argListWithoutRef}} };
            """
        );

        if (invokeMethod.ReturnsVoid) {
            sb.AppendLine(
                $$"""
                                impl.DynamicInvoke(args);
                {{copyBackStr}}                return;
                """
            );
        }
        else {
            sb.AppendLine(
                $$"""
                                var resultVal = impl.DynamicInvoke(args);
                {{copyBackStr}}                return ({{nonGenericReturnTypeStr}})resultVal!;
                """
            );
        }

        sb.AppendLine($"            }}");
        sb.AppendLine($"            throw new global::System.InvalidOperationException($\"No implementation of {methodName} registered for type {{type}}.\");");
        sb.AppendLine($"        }}");
    }

    private static void GenerateInterfaceForwardContent(StringBuilder sb, StaticAbstractInfo info, INamedTypeSymbol interfaceSymbol) {
        var delegateSymbol = info.DelegateSymbol;

        var typeArgs = new ITypeSymbol[delegateSymbol.TypeParameters.Length];

        for (int i = 0; i < delegateSymbol.TypeParameters.Length; i++) {
            var          dtp        = delegateSymbol.TypeParameters[i];
            ITypeSymbol? mappedType = null;

            for (int j = 0; j < interfaceSymbol.TypeParameters.Length; j++) {
                var itp = interfaceSymbol.TypeParameters[j];

                if (info.TypeParams.TryGetValue(itp.Name, out var targetName) && targetName == dtp.Name) {
                    mappedType = interfaceSymbol.TypeParameters[j];

                    break;
                }
            }

            typeArgs[i] = mappedType ?? dtp;
        }

        var constructedDelegate = delegateSymbol.OriginalDefinition.Construct(typeArgs);
        var interfaceInvoke     = constructedDelegate.DelegateInvokeMethod;

        if (interfaceInvoke == null)
            return;

        var interfaceParamList = string.Join(
            ", ",
            interfaceInvoke.Parameters.Select(
                p => {
                    var refKind = p.RefKind switch {
                        RefKind.Ref => "ref ",
                        RefKind.Out => "out ",
                        RefKind.In  => "in ",
                        _           => p.IsParams ? "params " : ""
                    };

                    var attrs = FormatAttributes(p.GetAttributes());
                    return $"{attrs}{refKind}{p.Type.ToDisplayString(FullyQualifiedFormatWithNullability)} {p.Name}";
                }
            )
        );

        var interfaceArgList = string.Join(
            ", ",
            interfaceInvoke.Parameters.Select(
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

        var companionTypeArgs = new List<string>();

        foreach (var dtp in delegateSymbol.TypeParameters) {
            string mappedName = "";

            for (int j = 0; j < interfaceSymbol.TypeParameters.Length; j++) {
                var itp = interfaceSymbol.TypeParameters[j];

                if (info.TypeParams.TryGetValue(itp.Name, out var targetName) && targetName == dtp.Name) {
                    mappedName = itp.Name;

                    break;
                }
            }

            if (string.IsNullOrEmpty(mappedName)) {
                mappedName = dtp.Name;
            }

            companionTypeArgs.Add(mappedName);
        }

        var companionTypeArgsStr = companionTypeArgs.Count > 0 ? $"<{string.Join(", ", companionTypeArgs)}>" : "";

        var returnAttributes = FormatReturnAttributes(interfaceInvoke.GetReturnTypeAttributes());
        var returnTypeStr     = interfaceInvoke.ReturnType.ToDisplayString(FullyQualifiedFormatWithNullability);
        var companionClassFqn = $"{interfaceSymbol.ContainingNamespace.ToDisplayString(FullyQualifiedFormatWithNullability)}.{interfaceSymbol.Name}";

        sb.AppendLine($"        {returnAttributes}public static {returnTypeStr} {info.MethodName}({interfaceParamList}) {{");

        sb.AppendLine(
            interfaceInvoke.ReturnsVoid 
                ? $"            {companionClassFqn}.{info.MethodName}{companionTypeArgsStr}({interfaceArgList});" 
                : $"            return {companionClassFqn}.{info.MethodName}{companionTypeArgsStr}({interfaceArgList});"
        );

        sb.AppendLine($"        }}");
    }

    private static void GenerateModuleInitializer(
        SourceProductionContext          spc,
        List<StaticAbstractInfo>         allInfos,
        ImmutableArray<INamedTypeSymbol> types,
        Compilation                      compilation
    ) {
        var registrationStatements = new List<string>();

        foreach (var type in types.Distinct(SymbolEqualityComparer.Default).Cast<INamedTypeSymbol>()) {
            if (type.TypeParameters.Length > 0)
                continue;

            foreach (var iface in type.AllInterfaces) {
                foreach (var attribute in iface.GetAttributes()) {
                    var info = GetStaticAbstractInfo(attribute, iface, compilation);

                    if (info == null)
                        continue;

                    if (info.DelegateSymbol.TypeKind != TypeKind.Delegate)
                        continue;

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

                    var matches = type.GetMembers(info.MethodName)
                        .OfType<IMethodSymbol>()
                        .Any(m => m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && MethodMatchesSignature(m, delegateInvoke));

                    if (!matches)
                        continue;

                    var registryClassFqn = info.TargetClass != null 
                        ? info.TargetClass.ToDisplayString(FullyQualifiedFormatWithNullability) 
                        : $"{info.InterfaceSymbol.ContainingNamespace
                            .ToDisplayString(FullyQualifiedFormatWithNullability)}.{info.InterfaceSymbol.Name}";

                    var delegateTypeStr = constructedDelegate.ToDisplayString(FullyQualifiedFormatWithNullability);
                    var methodGroupStr  = $"{type.ToDisplayString(FullyQualifiedFormatWithNullability)}.{info.MethodName}";

                    registrationStatements.Add($"            {registryClassFqn}.G_Register_{info.MethodName}(typeof({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), new {delegateTypeStr}({methodGroupStr}));");
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable");
        sb.AppendLine();
        sb.AppendLine("namespace Implyzer {");
        sb.AppendLine("    internal static class StaticAbstractRegistry {");
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        public static void Initialize() {");

        foreach (var stmt in registrationStatements) {
            sb.AppendLine(stmt);
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        if (compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute") is null) {
            sb.AppendLine("namespace System.Runtime.CompilerServices {");
            sb.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]");
            sb.AppendLine("    internal sealed class ModuleInitializerAttribute : global::System.Attribute {}");
            sb.AppendLine("}");
        }

        spc.AddSource("StaticAbstractRegistry.g.cs", sb.ToString());
    }

    private static bool MethodMatchesSignature(IMethodSymbol method, IMethodSymbol delegateInvoke) {
        if (method.Parameters.Length != delegateInvoke.Parameters.Length)
            return false;

        if (!SymbolEqualityComparer.IncludeNullability.Equals(method.ReturnType, delegateInvoke.ReturnType))
            return false;

        if (!AttributeListsMatch(method.GetReturnTypeAttributes(), delegateInvoke.GetReturnTypeAttributes()))
            return false;

        for (int i = 0; i < method.Parameters.Length; i++) {
            var p1 = method.Parameters[i];
            var p2 = delegateInvoke.Parameters[i];

            if (p1.RefKind != p2.RefKind)
                return false;

            if (p1.IsParams != p2.IsParams)
                return false;

            if (!SymbolEqualityComparer.IncludeNullability.Equals(p1.Type, p2.Type))
                return false;

            if (!AttributeListsMatch(p1.GetAttributes(), p2.GetAttributes()))
                return false;
        }

        return true;
    }

    private static bool AttributeListsMatch(ImmutableArray<AttributeData> list1, ImmutableArray<AttributeData> list2) {
        var filtered1 = list1.Where(a => !IsCompilerInjectedAttribute(a)).ToList();
        var filtered2 = list2.Where(a => !IsCompilerInjectedAttribute(a)).ToList();

        if (filtered1.Count != filtered2.Count)
            return false;

        for (int i = 0; i < filtered1.Count; i++) {
            if (!AttributesAreEqual(filtered1[i], filtered2[i]))
                return false;
        }

        return true;
    }

    private static bool IsCompilerInjectedAttribute(AttributeData attribute) {
        if (attribute.AttributeClass == null)
            return true;

        var fullName = attribute.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return fullName == "global::System.Runtime.CompilerServices.NullableAttribute" || 
               fullName == "global::System.Runtime.CompilerServices.NullableContextAttribute" ||
               fullName == "global::System.Runtime.CompilerServices.NullablePublicOnlyAttribute" ||
               fullName == "global::System.Runtime.CompilerServices.NativeIntegerAttribute" ||
               fullName == "global::System.Runtime.CompilerServices.DynamicAttribute" ||
               fullName == "global::System.Runtime.CompilerServices.TupleElementNamesAttribute" ||
               fullName == "global::System.Runtime.CompilerServices.IsReadOnlyAttribute" ||
               fullName == "global::System.ParamArrayAttribute" ||
               fullName == "global::System.Runtime.InteropServices.OutAttribute" ||
               fullName == "global::System.Runtime.InteropServices.InAttribute";
    }

    private static bool AttributesAreEqual(AttributeData a1, AttributeData a2) {
        if (!SymbolEqualityComparer.Default.Equals(a1.AttributeClass, a2.AttributeClass))
            return false;

        if (a1.ConstructorArguments.Length != a2.ConstructorArguments.Length)
            return false;

        for (int i = 0; i < a1.ConstructorArguments.Length; i++) {
            if (!TypedConstantsAreEqual(a1.ConstructorArguments[i], a2.ConstructorArguments[i]))
                return false;
        }

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

            for (int i = 0; i < tc1.Values.Length; i++) {
                if (!TypedConstantsAreEqual(tc1.Values[i], tc2.Values[i]))
                    return false;
            }

            return true;
        }

        if (tc1.Kind == TypedConstantKind.Type) {
            return SymbolEqualityComparer.Default.Equals((ITypeSymbol?)tc1.Value, (ITypeSymbol?)tc2.Value);
        }

        return Equals(tc1.Value, tc2.Value);
    }

    private static StaticAbstractInfo? GetStaticAbstractInfo(AttributeData attribute, INamedTypeSymbol interfaceSymbol, Compilation compilation) {
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

            var semanticModel = compilation.GetSemanticModel(attributeSyntax.SyntaxTree);

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

        return new StaticAbstractInfo(methodName, delegateSymbol, typeParams, targetClass, interfaceSymbol);
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
        if (arg.Kind != TypedConstantKind.Array)
            return;

        for (int i = 0; i < arg.Values.Length; i += 2) {
            if (i + 1 >= arg.Values.Length)
                continue;

            if (arg.Values[i].Value is string key && arg.Values[i + 1].Value is string val)
                typeParams[key] = val;
        }
    }

    private static string ToNonGenericTypeString(ITypeSymbol type, string typeParamName) {
        if (type is ITypeParameterSymbol tp && tp.Name == typeParamName) {
            return "object?";
        }
        
        var isNullable = type.NullableAnnotation == NullableAnnotation.Annotated;
        var suffix = isNullable ? "?" : "";

        if (type is INamedTypeSymbol namedType) {
            if (namedType.IsGenericType) {
                var args = namedType.TypeArguments.Select(a => ToNonGenericTypeString(a, typeParamName));
                var baseName = namedType.OriginalDefinition.ToDisplayString(FullyQualifiedFormatWithNullability);
                var idx = baseName.IndexOf('<');
                if (idx >= 0) {
                    baseName = baseName.Substring(0, idx);
                }
                return $"{baseName}<{string.Join(", ", args)}>{suffix}";
            }
        }
        
        if (type is IArrayTypeSymbol arrayType) {
            return $"{ToNonGenericTypeString(arrayType.ElementType, typeParamName)}[]{suffix}";
        }
        
        if (type is IPointerTypeSymbol pointerType) {
            return $"{ToNonGenericTypeString(pointerType.PointedAtType, typeParamName)}*{suffix}";
        }
        
        return type.ToDisplayString(FullyQualifiedFormatWithNullability);
    }

    private class StaticAbstractInfo {
        public string                     MethodName      { get; }
        public INamedTypeSymbol           DelegateSymbol  { get; }
        public Dictionary<string, string> TypeParams      { get; }
        public INamedTypeSymbol?          TargetClass     { get; }
        public INamedTypeSymbol           InterfaceSymbol { get; }

        public StaticAbstractInfo(
            string                     methodName,
            INamedTypeSymbol           delegateSymbol,
            Dictionary<string, string> typeParams,
            INamedTypeSymbol?          targetClass,
            INamedTypeSymbol           interfaceSymbol
        ) {
            MethodName      = methodName;
            DelegateSymbol  = delegateSymbol;
            TypeParams      = typeParams;
            TargetClass     = targetClass;
            InterfaceSymbol = interfaceSymbol;
        }
    }
}