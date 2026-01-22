using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace ImplTypeCheck;

[Generator]
public class ImplTypeAttributeGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        context.RegisterPostInitializationOutput(ctx => {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "ImplTypeCheck.Templates.ImplTypeAttribute.cs";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return;

            using var reader = new StreamReader(stream);
            var source = reader.ReadToEnd();

            ctx.AddSource("ImplTypeAttribute.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }
}