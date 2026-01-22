namespace ImplTypeCheck;

internal static class Rules {
    internal static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [
        RefVal,
        Type
    ];

    internal static readonly DiagnosticDescriptor RefVal = new(
        id: "IMPL0001",
        title: "Invalid implementing type",
        messageFormat: "Type '{0}' must be a {1} because it implements interface '{2}' whose implementing types must be {3}",
        category: "Implementation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:  "Type '{0}' must be a {1} because it implements interface '{2}' whose implementing types must be {3}."
    );
    
    internal static readonly DiagnosticDescriptor Type = new(
        id: "IMPL0002",
        title: "Invalid implementing type",
        messageFormat: "Type '{0}' must be implement '{1}' because it is required by interface '{2}'",
        category: "Implementation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:  "Type '{0}' must be implement '{1}' because it is required by interface '{2}'."
    );
}