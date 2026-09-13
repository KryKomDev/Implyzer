// Implyzer
// Copyright (c) KryKom 2026

using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Implyzer;

[Generator]
public class AttributesGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        context.RegisterSourceOutput(
            context.CompilationProvider,
            (pCtx, compilation) => {
                RegSrc(pCtx, compilation, "ImplTypeAttribute");
                RegSrc(pCtx, compilation, "IndirectImplAttribute");
                RegSrc(pCtx, compilation, "UseInsteadAttribute");
                RegSrc(pCtx, compilation, "StaticAbstractAttribute");
                RegSrc(pCtx, compilation, "StaticVirtualAttribute");
                RegSrc(pCtx, compilation, "StaticDefaultAttribute");
                RegSrc(pCtx, compilation, "StaticRegisterAttribute");
                RegSrc(pCtx, compilation, "ImplementInTargetTypesAttribute");
            }
        );
    }

    private static void RegSrc(SourceProductionContext context, Compilation compilation, string name) {
        var existingTypes = compilation.GetTypesByMetadataName($"Implyzer.{name}");
        if (existingTypes.Length > 0) {
            // If already defined in the current assembly, do not re-emit.
            if (existingTypes.Any(t => SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, compilation.Assembly)))
                return;

            // Find all external types that are accessible to the current assembly.
            var accessibleExternal = existingTypes
                .Where(t => !SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, compilation.Assembly)
                            && compilation.IsSymbolAccessibleWithin(t, compilation.Assembly))
                .ToList();

            // If exactly one accessible external type exists, the current assembly can use it without ambiguity or collision.
            if (accessibleExternal.Count == 1)
                return;
        }

        var assembly     = Assembly.GetExecutingAssembly();
        var resourceName = $"Implyzer.Templates.{name}.cs";

        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
            return;

        using var reader = new StreamReader(stream);
        var       source = reader.ReadToEnd();

        context.AddSource($"{name}.g.cs", SourceText.From(source, Encoding.UTF8));
    }
}