using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Implyzer.Tests;

public class AttributesGeneratorTests {
    [Fact]
    public void TestGeneratorAddsAttributesWhenMissing() {
        // Create an empty compilation
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]
        );

        var             generator = new AttributesGenerator();
        GeneratorDriver driver    = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // Should have generated 4 files (ImplTypeAttribute, IndirectImplAttribute, UseInsteadAttribute, StaticAbstractAttribute)
        Assert.Equal(4, runResult.GeneratedTrees.Length);

        var fileNames = runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.Contains("ImplTypeAttribute.g.cs",       fileNames);
        Assert.Contains("IndirectImplAttribute.g.cs",   fileNames);
        Assert.Contains("UseInsteadAttribute.g.cs",     fileNames);
        Assert.Contains("StaticAbstractAttribute.g.cs", fileNames);

        // Verify that they are public
        var implTypeTree = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("ImplTypeAttribute.g.cs"));
        var implTypeText = implTypeTree.ToString();
        Assert.Contains("public enum ImplKind",                  implTypeText);
        Assert.Contains("public sealed class ImplTypeAttribute", implTypeText);
    }

    [Fact]
    public void TestGeneratorDoesNotAddAttributesWhenAlreadyExists() {
        // Create a compilation that already has ImplTypeAttribute defined
        const string existingAttributeSource =
            """

            namespace Implyzer {
                public enum ImplKind { ReferenceType }
                public class ImplTypeAttribute : System.Attribute {
                    public ImplTypeAttribute(ImplKind kind) {}
                }
            }

            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(existingAttributeSource);

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]
        );

        var             generator = new AttributesGenerator();
        GeneratorDriver driver    = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // ImplTypeAttribute should not be generated because it already exists in compilation,
        // but other attributes should still be generated.
        Assert.Equal(3, runResult.GeneratedTrees.Length);

        var fileNames = runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.DoesNotContain("ImplTypeAttribute.g.cs", fileNames);
        Assert.Contains("IndirectImplAttribute.g.cs",   fileNames);
        Assert.Contains("UseInsteadAttribute.g.cs",     fileNames);
        Assert.Contains("StaticAbstractAttribute.g.cs", fileNames);
    }
}