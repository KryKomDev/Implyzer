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

        // Should have generated 8 files (ImplTypeAttribute, IndirectImplAttribute, UseInsteadAttribute, StaticAbstractAttribute, StaticVirtualAttribute, StaticDefaultAttribute, StaticRegisterAttribute, ImplementInTargetTypesAttribute)
        Assert.Equal(8, runResult.GeneratedTrees.Length);

        var fileNames = runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.Contains("ImplTypeAttribute.g.cs",              fileNames);
        Assert.Contains("IndirectImplAttribute.g.cs",          fileNames);
        Assert.Contains("UseInsteadAttribute.g.cs",            fileNames);
        Assert.Contains("StaticAbstractAttribute.g.cs",        fileNames);
        Assert.Contains("StaticVirtualAttribute.g.cs",         fileNames);
        Assert.Contains("StaticDefaultAttribute.g.cs",         fileNames);
        Assert.Contains("StaticRegisterAttribute.g.cs",        fileNames);
        Assert.Contains("ImplementInTargetTypesAttribute.g.cs", fileNames);

        // Verify that they are internal
        var implTypeTree = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("ImplTypeAttribute.g.cs"));
        var implTypeText = implTypeTree.ToString();
        Assert.Contains("internal enum ImplKind",                  implTypeText);
        Assert.Contains("internal sealed class ImplTypeAttribute", implTypeText);
    }

    [Fact]
    public void TestGeneratorDoesNotAddAttributesWhenAlreadyExists() {
        // Create a compilation that already has ImplTypeAttribute defined
        const string existingAttributeSource =
            """

            namespace Implyzer {
                internal enum ImplKind { ReferenceType }
                internal class ImplTypeAttribute : System.Attribute {
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
        Assert.Equal(7, runResult.GeneratedTrees.Length);

        var fileNames = runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.DoesNotContain("ImplTypeAttribute.g.cs",        fileNames);
        Assert.Contains("IndirectImplAttribute.g.cs",          fileNames);
        Assert.Contains("UseInsteadAttribute.g.cs",            fileNames);
        Assert.Contains("StaticAbstractAttribute.g.cs",        fileNames);
        Assert.Contains("StaticVirtualAttribute.g.cs",         fileNames);
        Assert.Contains("StaticDefaultAttribute.g.cs",         fileNames);
        Assert.Contains("StaticRegisterAttribute.g.cs",        fileNames);
        Assert.Contains("ImplementInTargetTypesAttribute.g.cs", fileNames);
    }

    [Fact]
    public void TestGeneratorWithImportedLibraryHavingInternalAttributes() {
        // Library compiles with Implyzer (internal attributes)
        var generator = new AttributesGenerator();
        var libCompilation = CSharpCompilation.Create(
            "LibraryAssembly",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        GeneratorDriver libDriver = CSharpGeneratorDriver.Create(generator);
        libDriver = libDriver.RunGenerators(libCompilation);
        var libRunResult = libDriver.GetRunResult();

        var libCompilationWithSources = libCompilation.AddSyntaxTrees(libRunResult.GeneratedTrees);
        using var ms = new MemoryStream();
        var emitResult = libCompilationWithSources.Emit(ms);
        Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics.Select(d => d.ToString())));
        ms.Seek(0, SeekOrigin.Begin);
        var libraryReference = MetadataReference.CreateFromStream(ms);

        // Consuming project also uses Implyzer and references LibraryAssembly
        const string projectSource =
            """
            using Implyzer;

            [StaticAbstract("Foo", typeof(System.Action))]
            public interface ITest { }
            """;
        var projectSyntaxTree = CSharpSyntaxTree.ParseText(projectSource);
        var projectCompilation = CSharpCompilation.Create(
            "ProjectAssembly",
            [projectSyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location), libraryReference],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        GeneratorDriver projectDriver = CSharpGeneratorDriver.Create(generator);
        projectDriver = projectDriver.RunGenerators(projectCompilation);
        var projectRunResult = projectDriver.GetRunResult();

        // Project should generate its own internal attributes because library's attributes are internal and inaccessible
        Assert.Equal(8, projectRunResult.GeneratedTrees.Length);

        // Project compilation should emit cleanly with 0 warnings/errors (no CS0436 or CS0433)
        var projectCompilationWithSources = projectCompilation.AddSyntaxTrees(projectRunResult.GeneratedTrees);
        using var msProj = new MemoryStream();
        var projEmitResult = projectCompilationWithSources.Emit(msProj);
        Assert.True(projEmitResult.Success, string.Join("\n", projEmitResult.Diagnostics.Select(d => d.ToString())));
        Assert.Empty(projEmitResult.Diagnostics.Where(d => d.Id is "CS0436" or "CS0433" or "CS0122"));
    }

    [Fact]
    public void TestGeneratorWithImportedLibraryHavingPublicAttributes() {
        // Simulate a referenced library compiled with an older Implyzer version where attributes were public
        const string oldLibSource =
            """
            namespace Implyzer {
                public enum ImplKind { ReferenceType }
                public class ImplTypeAttribute : System.Attribute {
                    public ImplTypeAttribute(ImplKind kind) {}
                }
            }
            """;
        var libCompilation = CSharpCompilation.Create(
            "OldLibraryAssembly",
            [CSharpSyntaxTree.ParseText(oldLibSource)],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        using var ms = new MemoryStream();
        Assert.True(libCompilation.Emit(ms).Success);
        ms.Seek(0, SeekOrigin.Begin);
        var libraryReference = MetadataReference.CreateFromStream(ms);

        // Consuming project uses Implyzer
        var projectCompilation = CSharpCompilation.Create(
            "ProjectAssembly",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location), libraryReference],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var generator = new AttributesGenerator();
        GeneratorDriver projectDriver = CSharpGeneratorDriver.Create(generator);
        projectDriver = projectDriver.RunGenerators(projectCompilation);
        var projectRunResult = projectDriver.GetRunResult();

        // ImplTypeAttribute should NOT be generated because an accessible public version exists from OldLibraryAssembly,
        // preventing CS0436! The remaining 7 attributes should be generated.
        Assert.Equal(7, projectRunResult.GeneratedTrees.Length);
        var fileNames = projectRunResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.DoesNotContain("ImplTypeAttribute.g.cs", fileNames);
    }

    [Fact]
    public void TestGeneratorWithTwoImportedLibrariesHavingPublicAttributes() {
        // Two libraries both exposing public attributes (e.g. older Implyzer)
        const string libSource =
            """
            namespace Implyzer {
                public class StaticAbstractAttribute : System.Attribute {
                    public StaticAbstractAttribute(string name, System.Type sig) {}
                }
            }
            """;
        var lib1Compilation = CSharpCompilation.Create(
            "Lib1Assembly",
            [CSharpSyntaxTree.ParseText(libSource)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        using var ms1 = new MemoryStream();
        Assert.True(lib1Compilation.Emit(ms1).Success);
        ms1.Seek(0, SeekOrigin.Begin);
        var lib1Ref = MetadataReference.CreateFromStream(ms1);

        var lib2Compilation = CSharpCompilation.Create(
            "Lib2Assembly",
            [CSharpSyntaxTree.ParseText(libSource)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        using var ms2 = new MemoryStream();
        Assert.True(lib2Compilation.Emit(ms2).Success);
        ms2.Seek(0, SeekOrigin.Begin);
        var lib2Ref = MetadataReference.CreateFromStream(ms2);

        // Consuming project references both. Because there are 2 accessible public attributes,
        // it must generate its own internal attribute to avoid fatal CS0433.
        const string userSource =
            """
            using Implyzer;

            [StaticAbstract("Foo", typeof(System.Action))]
            public interface ITest {}
            """;
        var projectCompilation = CSharpCompilation.Create(
            "ProjAssembly",
            [CSharpSyntaxTree.ParseText(userSource)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location), lib1Ref, lib2Ref],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var generator = new AttributesGenerator();
        GeneratorDriver projectDriver = CSharpGeneratorDriver.Create(generator);
        projectDriver = projectDriver.RunGenerators(projectCompilation);
        var projectRunResult = projectDriver.GetRunResult();

        // Should generate StaticAbstractAttribute to shadow the ambiguous references
        var fileNames = projectRunResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.Contains("StaticAbstractAttribute.g.cs", fileNames);

        var projectCompilationWithSources = projectCompilation.AddSyntaxTrees(projectRunResult.GeneratedTrees);
        using var msProj = new MemoryStream();
        var projEmit = projectCompilationWithSources.Emit(msProj);
        // CS0433 fatal error must NOT occur.
        Assert.True(projEmit.Success, string.Join("\n", projEmit.Diagnostics.Select(d => d.ToString())));
        Assert.Empty(projEmit.Diagnostics.Where(d => d.Id == "CS0433"));
    }
}