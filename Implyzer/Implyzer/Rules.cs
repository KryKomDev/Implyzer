// Implyzer
// Copyright (c) KryKom 2026

namespace Implyzer;

internal static class Rules {
    internal static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [
        RefVal,
        Type,
        IndirectImpl
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
}