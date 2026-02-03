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
        RegisterResource(context, "ImplTypeAttribute");
        RegisterResource(context, "IndirectImplAttribute");
        RegisterResource(context, "UseInsteadAttribute");
    }

    private static void RegisterResource(IncrementalGeneratorInitializationContext context, string name) {
        context.RegisterPostInitializationOutput(ctx => {
            var assembly     = Assembly.GetExecutingAssembly();
            var resourceName = $"Implyzer.Templates.{name}.cs";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return;

            using var reader = new StreamReader(stream);
            var       source = reader.ReadToEnd();

            ctx.AddSource($"{name}.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }
}