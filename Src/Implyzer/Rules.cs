// Implyzer
// Copyright (c) KryKom 2026

namespace Implyzer;

internal static class Rules {
    internal static readonly DiagnosticDescriptor RefVal = new(
        "IMPL001",
        "Invalid implementing type",
        "Type '{0}' must be a {1} because it implements interface '{2}' whose implementing types must be {3}",
        "Implementation",
        DiagnosticSeverity.Error,
        true,
        "Type '{0}' must be a {1} because it implements interface '{2}' whose implementing types must be {3}."
    );

    internal static readonly DiagnosticDescriptor Type = new(
        "IMPL002",
        "Invalid implementing type",
        "Type '{0}' must be implement '{1}' because it is required by interface '{2}'",
        "Implementation",
        DiagnosticSeverity.Error,
        true,
        "Type '{0}' must be implement '{1}' because it is required by interface '{2}'."
    );

    internal static readonly DiagnosticDescriptor IndirectImpl = new(
        "IMPL003",
        "Indirect implementation required",
        "Type '{0}' cannot implement '{1}' directly{2}",
        "Implementation",
        DiagnosticSeverity.Error,
        true,
        "Interfaces marked with [IndirectImpl] cannot be implemented directly."
    );

    internal static readonly DiagnosticDescriptor Constructor = new(
        "IMPL004",
        "Missing parameterless constructor",
        "Type '{0}' must have a public parameterless constructor because it implements interface '{1}'",
        "Implementation",
        DiagnosticSeverity.Error,
        true,
        "Types implementing interfaces with [ImplType(ImplKind.ReferenceTypeNew)] must have a public parameterless constructor."
    );

    internal static readonly DiagnosticDescriptor UseInstead = new(
        "IMPL005",
        "Use replacement symbol",
        "Use '{0}' instead of '{1}'",
        "Design",
        DiagnosticSeverity.Info,
        true,
        "The author of this symbol has suggested a replacement."
    );

    internal static readonly DiagnosticDescriptor StaticAbstractInterfaceNotPartial = new(
        "IMPL006",
        "Interface must be partial",
        "Interface '{0}' must be partial because it has the [StaticAbstract] attribute and no target class is specified",
        "Design",
        DiagnosticSeverity.Error,
        true,
        "Interfaces with [StaticAbstract] and no target class must be partial."
    );

    internal static readonly DiagnosticDescriptor StaticAbstractTargetClassNotPartial = new(
        "IMPL007",
        "Target class must be partial",
        "Target class '{0}' must be partial",
        "Design",
        DiagnosticSeverity.Error,
        true,
        "Target classes specified in [StaticAbstract] must be partial."
    );

    internal static readonly DiagnosticDescriptor StaticAbstractTargetClassMustBeClass = new(
        "IMPL008",
        "Target class must be a class",
        "Target class '{0}' must be a class",
        "Design",
        DiagnosticSeverity.Error,
        true,
        "Target classes specified in [StaticAbstract] must be classes."
    );

    internal static readonly DiagnosticDescriptor StaticAbstractMethodNotImplemented = new(
        "IMPL009",
        "Static method not implemented",
        "Type '{0}' must implement public static method '{1}' matching signature of delegate '{2}' because it implements interface '{3}'",
        "Implementation",
        DiagnosticSeverity.Error,
        true,
        "Types implementing interfaces with [StaticAbstract] must implement the specified static method."
    );

    internal static readonly DiagnosticDescriptor StaticAbstractSignatureNotDelegate = new(
        "IMPL010",
        "Signature must be a delegate",
        "Signature type '{0}' must be a delegate type",
        "Design",
        DiagnosticSeverity.Error,
        true,
        "The signature parameter of [StaticAbstract] must be a delegate type."
    );

    internal static readonly DiagnosticDescriptor StaticAbstractDefaultMethodNotFound = new(
        "IMPL011",
        "Default implementation method not found",
        "Default implementation method '{0}' was not found on type '{1}'",
        "Design",
        DiagnosticSeverity.Error,
        true,
        "Default implementation method specified in [StaticAbstract] or [StaticVirtual] was not found."
    );

    internal static readonly DiagnosticDescriptor StaticAbstractDefaultMethodSignatureMismatch = new(
        "IMPL012",
        "Default implementation signature mismatch",
        "Default implementation method '{0}' on type '{1}' does not match delegate signature '{2}'",
        "Design",
        DiagnosticSeverity.Error,
        true,
        "The default implementation method must match the signature of the delegate specified in [StaticAbstract] or [StaticVirtual]."
    );

    internal static readonly DiagnosticDescriptor StaticAbstractDefaultMethodMustBeStatic = new(
        "IMPL013",
        "Default implementation method must be static",
        "Default implementation method '{0}' on type '{1}' must be static",
        "Design",
        DiagnosticSeverity.Error,
        true,
        "The default implementation method specified in [StaticAbstract] or [StaticVirtual] must be static."
    );

    internal static readonly DiagnosticDescriptor StaticRegisterTypeMissingMember = new(
        "IMPL014",
        "Registered type missing required static member",
        "Type '{0}' cannot be registered for interface '{1}' because it does not implement static member '{2}' matching signature '{3}'",
        "Implementation",
        DiagnosticSeverity.Error,
        true,
        "Types registered with [StaticRegister] must implement all required static abstract members that do not have default implementations.",
        null,
        "CompilationEnd"
    );

    internal static readonly DiagnosticDescriptor StaticRegisterInterfaceNotStaticAbstract = new(
        "IMPL015",
        "StaticRegister on non-static-abstract interface",
        "Interface '{0}' has [StaticRegister] applied but defines no static abstract or virtual members",
        "Design",
        DiagnosticSeverity.Warning,
        true,
        "Interfaces with [StaticRegister] must define at least one static abstract or virtual member.",
        null,
        "CompilationEnd"
    );

    internal static readonly DiagnosticDescriptor StaticRegisterTypeAlreadyImplementsInterface = new(
        "IMPL016",
        "Redundant static registration",
        "Type '{0}' already implements interface '{1}'; explicit registration via [StaticRegister] is redundant",
        "Design",
        DiagnosticSeverity.Info,
        true,
        "Types that directly implement the interface do not need to be registered via [StaticRegister].",
        null,
        "CompilationEnd"
    );

    internal static readonly DiagnosticDescriptor StaticVirtualTargetTypeNotPartial = new(
        "IMPL017",
        "Target type must be partial",
        "Type '{0}' must be partial because interface '{1}' has static virtual method '{2}' configured to be implemented in target types",
        "Design",
        DiagnosticSeverity.Error,
        true,
        "Types implementing interfaces with static virtual methods configured to be implemented in target types must be partial."
    );

    internal static readonly DiagnosticDescriptor StaticVirtualMethodNotImplemented = new(
        "IMPL018",
        "Static virtual method not implemented in target type",
        "Type '{0}' does not implement static virtual member '{1}' from interface '{2}'",
        "Design",
        DiagnosticSeverity.Hidden,
        true,
        "Static virtual methods have default implementations, but target types can explicitly implement them."
    );

    internal static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [
        RefVal,
        Type,
        IndirectImpl,
        Constructor,
        UseInstead,
        StaticAbstractInterfaceNotPartial,
        StaticAbstractTargetClassNotPartial,
        StaticAbstractTargetClassMustBeClass,
        StaticAbstractMethodNotImplemented,
        StaticAbstractSignatureNotDelegate,
        StaticAbstractDefaultMethodNotFound,
        StaticAbstractDefaultMethodSignatureMismatch,
        StaticAbstractDefaultMethodMustBeStatic,
        StaticRegisterTypeMissingMember,
        StaticRegisterInterfaceNotStaticAbstract,
        StaticRegisterTypeAlreadyImplementsInterface,
        StaticVirtualTargetTypeNotPartial,
        StaticVirtualMethodNotImplemented
    ];
}