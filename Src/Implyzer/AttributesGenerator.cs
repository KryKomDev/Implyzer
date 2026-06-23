// Implyzer
// Copyright (c) KryKom 2026

using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace Implyzer;

[Generator]
public class AttributesGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        context.RegisterSourceOutput(
            context.CompilationProvider,
            (productionContext, compilation) => {
                CheckAndRegisterResource(productionContext, compilation, "ImplTypeAttribute",       "Implyzer.ImplTypeAttribute");
                CheckAndRegisterResource(productionContext, compilation, "IndirectImplAttribute",   "Implyzer.IndirectImplAttribute");
                CheckAndRegisterResource(productionContext, compilation, "UseInsteadAttribute",     "Implyzer.UseInsteadAttribute");
                CheckAndRegisterResource(productionContext, compilation, "StaticAbstractAttribute", "Implyzer.StaticAbstractAttribute");
            }
        );
    }

    private static void CheckAndRegisterResource(SourceProductionContext context, Compilation compilation, string name, string metadataName) {
        if (compilation.GetTypeByMetadataName(metadataName) is not null)
            return;

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