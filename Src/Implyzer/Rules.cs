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
        StaticAbstractSignatureNotDelegate
    ];
}