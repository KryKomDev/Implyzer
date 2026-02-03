// Implyzer
// Copyright (c) KryKom 2026

namespace Implyzer;

internal static class Rules {
    internal static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [
        RefVal,
        Type,
        IndirectImpl,
        Constructor,
        UseInstead
    ];

    internal static readonly DiagnosticDescriptor RefVal = new(
        id: "IMPL001",
        title: "Invalid implementing type",
        messageFormat: "Type '{0}' must be a {1} because it implements interface '{2}' whose implementing types must be {3}",
        category: "Implementation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:  "Type '{0}' must be a {1} because it implements interface '{2}' whose implementing types must be {3}."
    );
    
    internal static readonly DiagnosticDescriptor Type = new(
        id: "IMPL002",
        title: "Invalid implementing type",
        messageFormat: "Type '{0}' must be implement '{1}' because it is required by interface '{2}'",
        category: "Implementation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:  "Type '{0}' must be implement '{1}' because it is required by interface '{2}'."
    );

    internal static readonly DiagnosticDescriptor IndirectImpl = new(
        id: "IMPL003",
        title: "Indirect implementation required",
        messageFormat: "Type '{0}' cannot implement '{1}' directly{2}",
        category: "Implementation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Interfaces marked with [IndirectImpl] cannot be implemented directly."
    );

    internal static readonly DiagnosticDescriptor Constructor = new(
        id: "IMPL004",
        title: "Missing parameterless constructor",
        messageFormat: "Type '{0}' must have a public parameterless constructor because it implements interface '{1}'",
        category: "Implementation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Types implementing interfaces with [ImplType(ImplKind.ReferenceTypeNew)] must have a public parameterless constructor."
    );

    internal static readonly DiagnosticDescriptor UseInstead = new(
        id: "IMPL005",
        title: "Use replacement symbol",
        messageFormat: "Use '{0}' instead of '{1}'",
        category: "Design",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The author of this symbol has suggested a replacement."
    );
}