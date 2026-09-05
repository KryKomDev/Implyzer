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

    private static readonly SymbolDisplayFormat FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    private static string FormatAttributes(IEnumerable<AttributeData> attributes, string? typeParamNameToReplace = null) {
        var formatted = new List<string>();

        foreach (var attr in attributes) {
            var formattedAttr = FormatAttribute(attr, typeParamNameToReplace);

            if (!string.IsNullOrEmpty(formattedAttr))
                formatted.Add(formattedAttr);
        }

        return formatted.Count > 0 ? string.Join(" ", formatted) + " " : "";
    }

    private static string FormatReturnAttributes(IEnumerable<AttributeData> attributes, string? typeParamNameToReplace = null) {
        var formatted = new List<string>();

        foreach (var attr in attributes) {
            var formattedAttr = FormatAttribute(attr, typeParamNameToReplace);

            if (string.IsNullOrEmpty(formattedAttr))
                continue;

            if (formattedAttr.StartsWith("[") && formattedAttr.EndsWith("]"))
                formattedAttr = "[return: " + formattedAttr.Substring(1);

            formatted.Add(formattedAttr);
        }

        return formatted.Count > 0 ? string.Join(" ", formatted) + " " : "";
    }

    private static string FormatAttribute(AttributeData attribute, string? typeParamNameToReplace = null) {
        if (attribute.AttributeClass == null)
            return "";

        var fullName = attribute.AttributeClass.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);

        if (fullName
            is "global::System.Runtime.CompilerServices.NullableAttribute"
            or "global::System.Runtime.CompilerServices.NullableContextAttribute"
            or "global::System.Runtime.CompilerServices.NullablePublicOnlyAttribute"
            or "global::System.Runtime.CompilerServices.NativeIntegerAttribute"
            or "global::System.Runtime.CompilerServices.DynamicAttribute"
            or "global::System.Runtime.CompilerServices.TupleElementNamesAttribute"
            or "global::System.Runtime.CompilerServices.IsReadOnlyAttribute"
            or "global::System.ParamArrayAttribute"
            or "global::System.Runtime.InteropServices.OutAttribute"
            or "global::System.Runtime.InteropServices.InAttribute"
        ) {
            return "";
        }

        var args = new List<string>();

        foreach (var arg in attribute.ConstructorArguments)
            args.Add(FormatTypedConstant(arg, typeParamNameToReplace));

        foreach (var namedArg in attribute.NamedArguments)
            args.Add($"{namedArg.Key} = {FormatTypedConstant(namedArg.Value, typeParamNameToReplace)}");

        return args.Count > 0
            ? $"[{fullName}({string.Join(", ", args)})]"
            : $"[{fullName}]";
    }

    private static string FormatTypedConstant(TypedConstant constant, string? typeParamNameToReplace) {
        if (constant.IsNull)
            return "null";

        if (constant.Kind == TypedConstantKind.Array) {
            var elements        = constant.Values.Select(v => FormatTypedConstant(v, typeParamNameToReplace));
            var arrayType       = (IArrayTypeSymbol)constant.Type!;
            var elementTypeName = ToNonGenericTypeString(arrayType.ElementType.WithNullableAnnotation(NullableAnnotation.NotAnnotated), typeParamNameToReplace ?? "");

            return $"new {elementTypeName}[] {{ {string.Join(", ", elements)} }}";
        }

        if (constant.Kind == TypedConstantKind.Type) {
            var typeSymbol = (ITypeSymbol)constant.Value!;
            var typeStr    = ToNonGenericTypeString(typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated), typeParamNameToReplace ?? "");

            return $"typeof({typeStr})";
        }

        if (constant.Kind == TypedConstantKind.Enum)
            return constant.Type!.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY) + "." + constant.Value;

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

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        // Collect interfaces with [StaticAbstract] attributes
        var interfaces = context.SyntaxProvider.CreateSyntaxProvider(
            static (node,    _) => node is InterfaceDeclarationSyntax { AttributeLists.Count: > 0 },
            static (context, _) => GetInterfaceInfo(context)
        ).Where(static x => x is not null).Select(static (x, _) => x!);

        // Collect class/struct declarations
        var classesAndStructs = context.SyntaxProvider.CreateSyntaxProvider(
            static (node,    _) => node is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax,
            static (context, _) => GetTypeSymbol(context)
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

        return
            symbol.OriginalDefinition
                .GetAttributes()
                .Any(a => a.AttributeClass?.Name is "StaticAbstractAttribute" or "StaticAbstract" or "StaticVirtualAttribute" or "StaticVirtual")
                ? symbol
                : null;
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
        var supportsVirtualStatics = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.RuntimeFeature")
            ?.GetMembers("VirtualStaticsInInterfaces").Any() == true;

        var isCSharp11OrGreater = (compilation as CSharpCompilation)?.LanguageVersion >= LanguageVersion.CSharp11 && supportsVirtualStatics;
        var registryGroups      = new Dictionary<INamedTypeSymbol, List<StaticAbstractInfo>>(SymbolEqualityComparer.Default);
        var interfaceForwards   = new Dictionary<INamedTypeSymbol, List<StaticAbstractInfo>>(SymbolEqualityComparer.Default);
        var allInfos            = new List<StaticAbstractInfo>();

        foreach (var iface in ifaces.Distinct(SymbolEqualityComparer.Default).Cast<INamedTypeSymbol>())
        foreach (var info in iface.OriginalDefinition.GetAttributes().Select(attr => GetStaticAbstractInfo(attr, iface, compilation)).OfType<StaticAbstractInfo>().Where(info => info.DelegateSymbol.TypeKind == TypeKind.Delegate)) {
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

                if (iface.Arity <= 0)
                    continue;

                if (!interfaceForwards.TryGetValue(iface, out var forwardList)) {
                    forwardList              = new List<StaticAbstractInfo>();
                    interfaceForwards[iface] = forwardList;
                }

                forwardList.Add(info);
            }
        }

        // Generate Registry classes
        foreach (var kvp in registryGroups) {
            var registryClass = kvp.Key;
            var infos         = kvp.Value;

            var ns = registryClass.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (ns.StartsWith("global::"))
                ns = ns.Substring(8);

            var sb = new StringBuilder();

            sb.AppendLine(
                $$"""
                  // <auto-generated/>
                  #nullable enable
                  #pragma warning disable

                  namespace {{ns}} {
                  """
            );

            if (registryClass.IsRecord) {
                if (registryClass.TypeKind == TypeKind.Struct)
                    sb.AppendLine($"    public partial record struct {registryClass.Name} {{");
                else
                    sb.AppendLine($"    public partial record {registryClass.Name} {{");
            }
            else if (registryClass.TypeKind == TypeKind.Class) {
                sb.AppendLine($"    public partial class {registryClass.Name} {{");
            }
            else if (registryClass.TypeKind == TypeKind.Interface) {
                sb.AppendLine(
                    registryClass.Arity > 0
                        ? $"    public static partial class {registryClass.Name} {{"
                        : $"    public partial interface {registryClass.Name} {{"
                );
            }

            foreach (var info in infos)
                GenerateRegistryContent(sb, info, isCSharp11OrGreater);

            sb.AppendLine(
                """
                    }
                }
                """
            );

            var hintName = $"{registryClass.ContainingNamespace.ToDisplayString()}_{registryClass.Name}_Registry.g.cs";
            spc.AddSource(hintName, sb.ToString());
        }

        // Generate Interface Forwards for generic interfaces
        foreach (var kvp in interfaceForwards) {
            var interfaceSymbol = kvp.Key;
            var infos           = kvp.Value;

            var ns = interfaceSymbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (ns.StartsWith("global::"))
                ns = ns.Substring(8);

            var sb            = new StringBuilder();
            var typeParamsStr = string.Join(", ", interfaceSymbol.TypeParameters.Select(tp => tp.Name));

            sb.AppendLine(
                $$"""
                  // <auto-generated/>
                  #nullable enable
                  #pragma warning disable

                  namespace {{ns}} {
                      public partial interface {{interfaceSymbol.Name}}<{{typeParamsStr}}> {
                  """
            );

            foreach (var info in infos)
                GenerateInterfaceForwardContent(sb, info, interfaceSymbol, isCSharp11OrGreater);

            sb.AppendLine(
                """
                    }
                }
                """
            );

            var hintName = $"{interfaceSymbol.ContainingNamespace.ToDisplayString()}_{interfaceSymbol.Name}_Forward.g.cs";
            spc.AddSource(hintName, sb.ToString());
        }

        // Generate Module Initializer for C# < 11
        if (!isCSharp11OrGreater)
            GenerateModuleInitializer(spc, allInfos, types, compilation);
    }

    private static string GetConstructedInterfaceFqn(StaticAbstractInfo info, string lookupTypeName) {
        var iface     = info.InterfaceSymbol;
        var ifaceName = iface.OriginalDefinition.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);

        if (iface.OriginalDefinition.Arity == 0)
            return ifaceName;

        var idx = ifaceName.IndexOf('<');

        if (idx >= 0)
            ifaceName = ifaceName.Substring(0, idx);

        var typeArgs = new List<string>();

        foreach (var itp in iface.OriginalDefinition.TypeParameters) {
            if (info.TypeParams.TryGetValue(itp.Name, out var dtpName))
                typeArgs.Add(dtpName);
            else
                typeArgs.Add(itp.Name);
        }

        return $"{ifaceName}<{string.Join(", ", typeArgs)}>";
    }

    private static void GenerateRegistryContent(StringBuilder sb, StaticAbstractInfo info, bool isCSharp11OrGreater) {
        var delegateSymbol = info.DelegateSymbol.OriginalDefinition;
        var invokeMethod   = delegateSymbol.DelegateInvokeMethod;

        if (invokeMethod == null)
            return;

        var methodName       = info.MethodName;
        var castType         = delegateSymbol.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);
        var returnAttributes = FormatReturnAttributes(invokeMethod.OriginalDefinition.GetReturnTypeAttributes());
        var returnTypeStr    = invokeMethod.ReturnType.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);

        var typeParams = delegateSymbol.TypeParameters;

        var typeParamsStr = typeParams.Length > 0
            ? $"<{string.Join(", ", typeParams.Select(tp => tp.Name))}>"
            : "";

        var lookupTypeName = "object";

        foreach (var kvp in info.TypeParams) {
            var dtpName = kvp.Value;

            if (delegateSymbol.TypeParameters.Any(tp => tp.Name == dtpName)) {
                lookupTypeName = dtpName;

                break;
            }
        }

        if (lookupTypeName == "object" && delegateSymbol.TypeParameters.Length > 0)
            lookupTypeName = delegateSymbol.TypeParameters[0].Name;

        var constraintClauses = new List<string>();

        foreach (var tp in typeParams) {
            var constraints = new List<string>();

            if (tp.HasReferenceTypeConstraint)
                constraints.Add("class");
            else if (tp.HasValueTypeConstraint)
                constraints.Add("struct");

            foreach (var ct in tp.ConstraintTypes)
                constraints.Add(ct.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY));

            var requiresInterfaceConstraint = isCSharp11OrGreater || (info.HasDefaultImplementation && info.DefaultMethodSymbol?.IsGenericMethod == true);

            if (requiresInterfaceConstraint && tp.Name == lookupTypeName) {
                var interfaceFqn = GetConstructedInterfaceFqn(info, lookupTypeName);

                if (!constraints.Contains(interfaceFqn))
                    constraints.Add(interfaceFqn);
            }

            if (tp.HasConstructorConstraint)
                constraints.Add("new()");

            if (constraints.Count > 0)
                constraintClauses.Add($"where {tp.Name} : {string.Join(", ", constraints)}");
        }

        var requiresFallbackConstraint = isCSharp11OrGreater || (info.HasDefaultImplementation && info.DefaultMethodSymbol?.IsGenericMethod == true);

        if (requiresFallbackConstraint && !typeParams.Any(tp => tp.Name == lookupTypeName)) {
            var interfaceFqn = GetConstructedInterfaceFqn(info, lookupTypeName);
            constraintClauses.Add($"where {lookupTypeName} : {interfaceFqn}");
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

                    var attrs = FormatAttributes(p.OriginalDefinition.GetAttributes());

                    return $"{attrs}{refKind}{p.Type.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY)} {p.Name}";
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

        if (isCSharp11OrGreater) {
            var body = invokeMethod.ReturnsVoid
                ? $"            {lookupTypeName}.{methodName}({argList});\n            return;"
                : $"            return {lookupTypeName}.{methodName}({argList});";

            sb.AppendLine(
                $$"""

                          {{returnAttributes}}public static {{returnTypeStr}} {{methodName}}{{typeParamsStr}}({{paramList}}){{constraintsStr}} {
                  {{body}}
                          }
                  """
            );
        }
        else {
            var body = invokeMethod.ReturnsVoid
                ? $"                (({castType})impl)({argList});\n                return;"
                : $"                return (({castType})impl)({argList});";

            string notFoundBody;

            if (info.HasDefaultImplementation && info.DefaultMethodSymbol != null) {
                var callStr = GetCompanionDefaultMethodCall(info, lookupTypeName, argList);

                notFoundBody = invokeMethod.ReturnsVoid
                    ? $"            {callStr};\n            return;"
                    : $"            return {callStr};";
            }
            else {
                notFoundBody = $"            throw new global::System.InvalidOperationException($\"No implementation of {methodName} registered for type {{typeof({lookupTypeName})}}.\");";
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
                  {{body}}
                              }
                  {{notFoundBody}}
                          }
                  """
            );
        }

        var nonGenericReturnAttributes  = FormatReturnAttributes(invokeMethod.OriginalDefinition.GetReturnTypeAttributes(), lookupTypeName);
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

                    var attrs   = FormatAttributes(p.OriginalDefinition.GetAttributes(), lookupTypeName);
                    var typeStr = ToNonGenericTypeString(p.Type, lookupTypeName);

                    return $"{attrs}{refKind}{typeStr} {p.Name}";
                }
            )
        );

        var nonGenericParamList     = string.Join(", ", nonGenericParamListElements);
        var nonGenericReturnTypeStr = ToNonGenericTypeString(invokeMethod.ReturnType, lookupTypeName);
        var argListWithoutRef       = string.Join(", ", invokeMethod.Parameters.Select(p => p.RefKind == RefKind.Out ? "default" : p.Name));

        var copyBackStatements = new List<string>();

        for (var i = 0; i < invokeMethod.Parameters.Length; i++) {
            var p = invokeMethod.Parameters[i];

            if (p.RefKind != RefKind.Out && p.RefKind != RefKind.Ref)
                continue;

            var typeStr = ToNonGenericTypeString(p.Type, lookupTypeName);
            copyBackStatements.Add($"{p.Name} = ({typeStr})args[{i}];");
        }

        var copyBackStr = copyBackStatements.Count > 0 ? "                " + string.Join("\n                ", copyBackStatements) + "\n" : "";

        if (isCSharp11OrGreater) {
            var body = invokeMethod.ReturnsVoid
                ? $"                method.Invoke(null, args);\n{copyBackStr}                return;"
                : $"                var resultVal = method.Invoke(null, args);\n{copyBackStr}                return ({nonGenericReturnTypeStr})resultVal!;";

            var notFoundBody = info.HasDefaultImplementation && info.DefaultMethodSymbol != null
                ? GetNonGenericDefaultFallback(info, invokeMethod, argListWithoutRef, copyBackStr, nonGenericReturnTypeStr, methodName)
                : "            throw new global::System.InvalidOperationException($\"No implementation of " + methodName + " found for type {type}.\");";

            sb.AppendLine(
                $$"""

                          {{nonGenericReturnAttributes}}public static {{nonGenericReturnTypeStr}} {{methodName}}({{nonGenericParamList}}) {
                              var methods = type.GetMethods(global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static);
                              global::System.Reflection.MethodInfo? method = null;
                              foreach (var m in methods) {
                                  if (m.Name == "{{methodName}}" && m.GetParameters().Length == {{invokeMethod.Parameters.Length}}) {
                                      method = m;
                                      break;
                                  }
                              }
                              if (method != null) {
                                  var args = new object?[] { {{argListWithoutRef}} };
                  {{body}}
                              }
                  {{notFoundBody}}
                          }
                  """
            );
        }
        else {
            var body = invokeMethod.ReturnsVoid
                ? $"                impl.DynamicInvoke(args);\n{copyBackStr}                return;"
                : $"                var resultVal = impl.DynamicInvoke(args);\n{copyBackStr}                return ({nonGenericReturnTypeStr})resultVal!;";

            var notFoundBody = info is { HasDefaultImplementation: true, DefaultMethodSymbol: not null }
                ? GetNonGenericDefaultFallback(info, invokeMethod, argListWithoutRef, copyBackStr, nonGenericReturnTypeStr, methodName)
                : "            throw new global::System.InvalidOperationException($\"No implementation of " + methodName + " registered for type {type}.\");";

            sb.AppendLine(
                $$"""

                          {{nonGenericReturnAttributes}}public static {{nonGenericReturnTypeStr}} {{methodName}}({{nonGenericParamList}}) {
                              if (_{{methodName}}Registry.TryGetValue(type, out var impl)) {
                                  var args = new object?[] { {{argListWithoutRef}} };
                  {{body}}
                              }
                  {{notFoundBody}}
                          }
                  """
            );
        }
    }

    private static void GenerateInterfaceForwardContent(StringBuilder sb, StaticAbstractInfo info, INamedTypeSymbol interfaceSymbol, bool isCSharp11OrGreater) {
        var delegateSymbol = info.DelegateSymbol;

        var typeArgs = new ITypeSymbol[delegateSymbol.TypeParameters.Length];

        for (var i = 0; i < delegateSymbol.TypeParameters.Length; i++) {
            var          dtp        = delegateSymbol.TypeParameters[i];
            ITypeSymbol? mappedType = null;

            for (var j = 0; j < interfaceSymbol.TypeParameters.Length; j++) {
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

                    var attrs = FormatAttributes(p.OriginalDefinition.GetAttributes());

                    return $"{attrs}{refKind}{p.Type.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY)} {p.Name}";
                }
            )
        );

        var returnAttributes = FormatReturnAttributes(interfaceInvoke.OriginalDefinition.GetReturnTypeAttributes());
        var returnTypeStr    = interfaceInvoke.ReturnType.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);

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

        if (isCSharp11OrGreater) {
            if (info.HasDefaultImplementation && info.DefaultMethodSymbol != null) {
                var callStr = GetInterfaceForwardDefaultMethodCall(info, interfaceSymbol, interfaceArgList);

                var body = interfaceInvoke.ReturnsVoid
                    ? $"            {callStr};\n            return;"
                    : $"            return {callStr};";

                sb.AppendLine(
                    $$"""
                              {{returnAttributes}}public static virtual {{returnTypeStr}} {{info.MethodName}}({{interfaceParamList}}) {
                      {{body}}
                              }
                      """
                );
            }
            else {
                sb.AppendLine($"        {returnAttributes}public static abstract {returnTypeStr} {info.MethodName}({interfaceParamList});");
            }
        }
        else {
            var companionTypeArgs = new List<string>();

            foreach (var dtp in delegateSymbol.TypeParameters) {
                var mappedName = "";

                for (var j = 0; j < interfaceSymbol.TypeParameters.Length; j++) {
                    var itp = interfaceSymbol.TypeParameters[j];

                    if (info.TypeParams.TryGetValue(itp.Name, out var targetName) && targetName == dtp.Name) {
                        mappedName = itp.Name;

                        break;
                    }
                }

                if (string.IsNullOrEmpty(mappedName))
                    mappedName = dtp.Name;

                companionTypeArgs.Add(mappedName);
            }

            var companionTypeArgsStr = companionTypeArgs.Count > 0 ? $"<{string.Join(", ", companionTypeArgs)}>" : "";
            var companionClassFqn    = $"{interfaceSymbol.ContainingNamespace.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY)}.{interfaceSymbol.Name}";

            var body = interfaceInvoke.ReturnsVoid
                ? $"            {companionClassFqn}.{info.MethodName}{companionTypeArgsStr}({interfaceArgList});"
                : $"            return {companionClassFqn}.{info.MethodName}{companionTypeArgsStr}({interfaceArgList});";

            sb.AppendLine(
                $$"""
                          {{returnAttributes}}public static {{returnTypeStr}} {{info.MethodName}}({{interfaceParamList}}) {
                  {{body}}
                          }
                  """
            );
        }
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

            foreach (var iface in type.AllInterfaces)
            foreach (var attribute in iface.OriginalDefinition.GetAttributes()) {
                var info = GetStaticAbstractInfo(attribute, iface, compilation);

                if (info == null)
                    continue;

                if (info.DelegateSymbol.TypeKind != TypeKind.Delegate)
                    continue;

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

                var matches = type.GetMembers(info.MethodName)
                    .OfType<IMethodSymbol>()
                    .Any(m => m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && MethodMatchesSignature(m, delegateInvoke));

                if (!matches)
                    continue;

                var registryClassFqn = info.TargetClass != null
                    ? info.TargetClass.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY)
                    : $"{info.InterfaceSymbol.ContainingNamespace
                        .ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY)}.{info.InterfaceSymbol.Name}";

                var delegateTypeStr = constructedDelegate.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);
                var methodGroupStr  = $"{type.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY)}.{info.MethodName}";

                registrationStatements.Add($"            {registryClassFqn}.G_Register_{info.MethodName}(typeof({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), new {delegateTypeStr}({methodGroupStr}));");
            }
        }

        var sb   = new StringBuilder();
        var body = string.Join("\n", registrationStatements);

        sb.AppendLine(
            $$"""
              // <auto-generated/>
              #nullable enable
              #pragma warning disable

              namespace Implyzer {
                  internal static class StaticAbstractRegistry {
                      [global::System.Runtime.CompilerServices.ModuleInitializer]
                      public static void Initialize() {
              {{body}}
                      }
                  }
              }

              """
        );

        if (compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute") is null) {
            sb.AppendLine(
                """
                namespace System.Runtime.CompilerServices {
                    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
                    internal sealed class ModuleInitializerAttribute : global::System.Attribute { }
                }
                """
            );
        }

        spc.AddSource("StaticAbstractRegistry.g.cs", sb.ToString());
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

        return fullName == "global::System.Runtime.CompilerServices.NullableAttribute"           ||
            fullName    == "global::System.Runtime.CompilerServices.NullableContextAttribute"    ||
            fullName    == "global::System.Runtime.CompilerServices.NullablePublicOnlyAttribute" ||
            fullName    == "global::System.Runtime.CompilerServices.NativeIntegerAttribute"      ||
            fullName    == "global::System.Runtime.CompilerServices.DynamicAttribute"            ||
            fullName    == "global::System.Runtime.CompilerServices.TupleElementNamesAttribute"  ||
            fullName    == "global::System.Runtime.CompilerServices.IsReadOnlyAttribute"         ||
            fullName    == "global::System.ParamArrayAttribute"                                  ||
            fullName    == "global::System.Runtime.InteropServices.OutAttribute"                 ||
            fullName    == "global::System.Runtime.InteropServices.InAttribute";
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

    private static StaticAbstractInfo? GetStaticAbstractInfo(AttributeData attribute, INamedTypeSymbol interfaceSymbol, Compilation compilation) {
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
            if (namedArg.Key == "DefaultType" && namedArg.Value.Value is INamedTypeSymbol dt)
                defaultType = dt;
            else if (namedArg.Key == "DefaultMethod" && namedArg.Value.Value is string dm)
                defaultMethod = dm;
        }

        if (attribute.ConstructorArguments.Length >= 2) {
            var methodNameArg = attribute.ConstructorArguments[0];

            if (methodNameArg.Value is string mName)
                methodName = mName;

            var signatureArg = attribute.ConstructorArguments[1];

            if (signatureArg.Value is INamedTypeSymbol delSymbol)
                delegateSymbol = delSymbol;

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
        }
        else {
            var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;

            if (attributeSyntax == null || attributeSyntax.ArgumentList == null || attributeSyntax.ArgumentList.Arguments.Count < 2)
                return null;

            var semanticModel = compilation.GetSemanticModel(attributeSyntax.SyntaxTree);

            foreach (var arg in attributeSyntax.ArgumentList.Arguments) {
                if (arg.NameEquals != null) {
                    var name = arg.NameEquals.Name.Identifier.Text;

                    if (name == "DefaultType" && arg.Expression is TypeOfExpressionSyntax typeofExpr)
                        defaultType ??= semanticModel.GetTypeInfo(typeofExpr.Type).Type as INamedTypeSymbol;
                    else if (name == "DefaultMethod")
                        defaultMethod ??= semanticModel.GetConstantValue(arg.Expression).Value as string;
                }
            }

            var positionalArgs = attributeSyntax.ArgumentList.Arguments.Where(a => a.NameEquals == null).ToList();

            if (positionalArgs.Count >= 2) {
                // 1. methodName
                var expr0 = positionalArgs[0].Expression;
                methodName = semanticModel.GetConstantValue(expr0).Value as string;

                // 2. signature
                var expr1 = positionalArgs[1].Expression;

                if (expr1 is TypeOfExpressionSyntax typeof1)
                    delegateSymbol = semanticModel.GetTypeInfo(typeof1.Type).Type as INamedTypeSymbol;

                // 3. Remaining positional arguments
                if (positionalArgs.Count >= 3) {
                    var expr2 = positionalArgs[2].Expression;

                    if (expr2 is TypeOfExpressionSyntax typeof2) {
                        targetClass = semanticModel.GetTypeInfo(typeof2.Type).Type as INamedTypeSymbol;
                        ParseParamsExpressions(positionalArgs.Skip(3).Select(a => a.Expression), typeParams, semanticModel);
                    }
                    else {
                        ParseParamsExpressions(positionalArgs.Skip(2).Select(a => a.Expression), typeParams, semanticModel);
                    }
                }
            }
        }

        if (methodName == null || delegateSymbol == null)
            return null;

        var info = new StaticAbstractInfo(methodName, delegateSymbol, typeParams, targetClass, interfaceSymbol, defaultType, defaultMethod, isVirtual);

        ResolveDefaultImplementation(info, interfaceSymbol);

        return info;
    }

    private static string GetInterfaceForwardDefaultMethodCall(StaticAbstractInfo info, INamedTypeSymbol interfaceSymbol, string interfaceArgList) {
        var defaultMethod = info.DefaultMethodSymbol!;
        var lookupType    = info.ResolvedDefaultType ?? info.DefaultType ?? info.TargetClass ?? interfaceSymbol;

        if (SymbolEqualityComparer.Default.Equals(lookupType, interfaceSymbol)) {
            return $"{defaultMethod.Name}({interfaceArgList})";
        }

        var typeFqn = lookupType.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);

        if (defaultMethod.IsGenericMethod) {
            var typeArgs = new List<string>();

            for (var i = 0; i < defaultMethod.TypeParameters.Length; i++) {
                if (i < interfaceSymbol.TypeParameters.Length)
                    typeArgs.Add(interfaceSymbol.TypeParameters[i].Name);
                else
                    typeArgs.Add(defaultMethod.TypeParameters[i].Name);
            }

            var typeArgsStr = typeArgs.Count > 0 ? $"<{string.Join(", ", typeArgs)}>" : "";

            return $"{typeFqn}.{defaultMethod.Name}{typeArgsStr}({interfaceArgList})";
        }

        return $"{typeFqn}.{defaultMethod.Name}({interfaceArgList})";
    }

    private static string GetCompanionDefaultMethodCall(StaticAbstractInfo info, string lookupTypeName, string argList) {
        var defaultMethod = info.DefaultMethodSymbol!;
        var lookupType    = info.ResolvedDefaultType ?? info.DefaultType ?? info.TargetClass ?? info.InterfaceSymbol;
        var typeFqn       = lookupType.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);

        if (defaultMethod.IsGenericMethod) {
            return $"{typeFqn}.{defaultMethod.Name}<{lookupTypeName}>({argList})";
        }

        return $"{typeFqn}.{defaultMethod.Name}({argList})";
    }

    private static string GetNonGenericDefaultFallback(StaticAbstractInfo info, IMethodSymbol invokeMethod, string argListWithoutRef, string copyBackStr, string nonGenericReturnTypeStr, string methodName) {
        var defaultMethod  = info.DefaultMethodSymbol!;
        var lookupType     = info.ResolvedDefaultType ?? info.DefaultType ?? info.TargetClass ?? info.InterfaceSymbol;
        var typeFqn        = lookupType.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);
        var methodNameStr  = defaultMethod.Name;
        var paramLen       = defaultMethod.Parameters.Length;
        var isGeneric      = defaultMethod.IsGenericMethod;
        var makeGenericStr = isGeneric ? "if (defMethod.IsGenericMethodDefinition) defMethod = defMethod.MakeGenericMethod(type);" : "";

        var invokeCall = invokeMethod.ReturnsVoid
            ? $"                    defMethod.Invoke(null, args);\n{copyBackStr}                    return;"
            : $"                    var defResult = defMethod.Invoke(null, args);\n{copyBackStr}                    return ({nonGenericReturnTypeStr})defResult!;";

        return
            $$"""
                                  var defMethods = typeof({{typeFqn}}).GetMethods(global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static);
                                  global::System.Reflection.MethodInfo? defMethod = null;
                                  foreach (var m in defMethods) {
                                      if (m.Name == "{{methodNameStr}}" && m.GetParameters().Length == {{paramLen}}) {
                                          defMethod = m;
                                          break;
                                      }
                                  }
                                  if (defMethod != null) {
                                      var args = new object?[] { {{argListWithoutRef}} };
                                      {{makeGenericStr}}
                      {{invokeCall}}
                                  }
                                  throw new global::System.InvalidOperationException($"No implementation of {{methodName}} found for type {type}.");
              """;
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

    private static void ParseParamsExpressions(IEnumerable<ExpressionSyntax> expressions, Dictionary<string, string> typeParams, SemanticModel semanticModel) {
        var elements = new List<string>();

        foreach (var expr in expressions) {
            if (expr is CollectionExpressionSyntax || expr is ArrayCreationExpressionSyntax || expr is ImplicitArrayCreationExpressionSyntax) {
                ParseTypeParamsSyntax(expr, typeParams, semanticModel);

                return;
            }

            var val = semanticModel.GetConstantValue(expr).Value as string;

            if (val != null)
                elements.Add(val);
        }

        for (var i = 0; i < elements.Count; i += 2)
            if (i + 1 < elements.Count)
                typeParams[elements[i]] = elements[i + 1];
    }

    private static void ParseTypeParamsSyntax(ExpressionSyntax expr, Dictionary<string, string> typeParams, SemanticModel semanticModel) {
        if (expr is CollectionExpressionSyntax collection) {
            var elements = new List<string>();

            foreach (var element in collection.Elements)
                if (element is ExpressionElementSyntax exprElem) {
                    var val = semanticModel.GetConstantValue(exprElem.Expression).Value as string;

                    if (val != null)
                        elements.Add(val);
                }

            for (var i = 0; i < elements.Count; i += 2)
                if (i + 1 < elements.Count)
                    typeParams[elements[i]] = elements[i + 1];
        }
        else if (expr is ArrayCreationExpressionSyntax arrayCreate) {
            if (arrayCreate.Initializer != null) {
                var elements = new List<string>();

                foreach (var element in arrayCreate.Initializer.Expressions) {
                    var val = semanticModel.GetConstantValue(element).Value as string;

                    if (val != null)
                        elements.Add(val);
                }

                for (var i = 0; i < elements.Count; i += 2)
                    if (i + 1 < elements.Count)
                        typeParams[elements[i]] = elements[i + 1];
            }
        }
        else if (expr is ImplicitArrayCreationExpressionSyntax implicitArray) {
            if (implicitArray.Initializer != null) {
                var elements = new List<string>();

                foreach (var element in implicitArray.Initializer.Expressions) {
                    var val = semanticModel.GetConstantValue(element).Value as string;

                    if (val != null)
                        elements.Add(val);
                }

                for (var i = 0; i < elements.Count; i += 2)
                    if (i + 1 < elements.Count)
                        typeParams[elements[i]] = elements[i + 1];
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

    private static string ToNonGenericTypeString(ITypeSymbol type, string typeParamName) {
        if (type is ITypeParameterSymbol tp && tp.Name == typeParamName)
            return "object?";

        var isNullable = type.NullableAnnotation == NullableAnnotation.Annotated;
        var suffix     = isNullable ? "?" : "";

        if (type is INamedTypeSymbol namedType)
            if (namedType.IsGenericType) {
                var args     = namedType.TypeArguments.Select(a => ToNonGenericTypeString(a, typeParamName));
                var baseName = namedType.OriginalDefinition.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);
                var idx      = baseName.IndexOf('<');

                if (idx >= 0)
                    baseName = baseName.Substring(0, idx);

                return $"{baseName}<{string.Join(", ", args)}>{suffix}";
            }

        if (type is IArrayTypeSymbol arrayType)
            return $"{ToNonGenericTypeString(arrayType.ElementType, typeParamName)}[]{suffix}";

        if (type is IPointerTypeSymbol pointerType)
            return $"{ToNonGenericTypeString(pointerType.PointedAtType, typeParamName)}*{suffix}";

        return type.ToDisplayString(FULLY_QUALIFIED_FORMAT_WITH_NULLABILITY);
    }

    private class StaticAbstractInfo {
        public string                     MethodName               { get; }
        public INamedTypeSymbol           DelegateSymbol           { get; }
        public Dictionary<string, string> TypeParams               { get; }
        public INamedTypeSymbol?          TargetClass              { get; }
        public INamedTypeSymbol           InterfaceSymbol          { get; }
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
            INamedTypeSymbol           interfaceSymbol,
            INamedTypeSymbol?          defaultType,
            string?                    defaultMethod,
            bool                       isVirtual
        ) {
            MethodName      = methodName;
            DelegateSymbol  = delegateSymbol;
            TypeParams      = typeParams;
            TargetClass     = targetClass;
            InterfaceSymbol = interfaceSymbol;
            DefaultType     = defaultType;
            DefaultMethod   = defaultMethod;
            IsVirtual       = isVirtual;
        }
    }
}