using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Implyzer.Tests;

public class StaticAbstractGeneratorTests {
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference SystemReference = MetadataReference.CreateFromFile(typeof(System.Collections.Generic.Dictionary<,>).Assembly.Location);
    private static readonly MetadataReference ComponentModelReference = MetadataReference.CreateFromFile(typeof(System.ComponentModel.EditorBrowsableAttribute).Assembly.Location);

    private static Compilation CreateCompilation(string source) {
        // Add the StaticAbstractAttribute definition to the compilation
        const string attributeSource = 
            """
            using System;
            using System.Collections.Generic;

            namespace Implyzer {
                [AttributeUsage(AttributeTargets.Interface, AllowMultiple = true)]
                public class StaticAbstractAttribute : Attribute {
                    public string MethodName { get; }
                    public Type Signature { get; }
                    public Dictionary<string, string> TypeParams { get; }
                    public Type? TargetClass { get; }

                    public StaticAbstractAttribute(string methodName, Type signature, params string[] typeParams) {
                        MethodName = methodName;
                        Signature = signature;
                        TypeParams = ToDictionary(typeParams);
                        TargetClass = null;
                    }

                    public StaticAbstractAttribute(string methodName, Type signature, Type targetClass, params string[] typeParams) {
                        MethodName = methodName;
                        Signature = signature;
                        TypeParams = ToDictionary(typeParams);
                        TargetClass = targetClass;
                    }

                    private static Dictionary<string, string> ToDictionary(string[] array) {
                        var dict = new Dictionary<string, string>();
                        if (array != null) {
                            for (int i = 0; i < array.Length; i += 2) {
                                if (i + 1 < array.Length) {
                                    dict[array[i]] = array[i + 1];
                                }
                            }
                        }
                        return dict;
                    }
                }
            }
            """;

        return CSharpCompilation.Create(
            "TestAssembly",
            [
                CSharpSyntaxTree.ParseText(attributeSource),
                CSharpSyntaxTree.ParseText(source)
            ],
            [
                CorlibReference,
                SystemReference,
                ComponentModelReference
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    [Fact]
    public void TestGeneratorWithTargetClass() {
        const string source =
            """
            using System;
            using Implyzer;

            namespace TestNamespace {
                public delegate bool TryParse<T>(string input, out T result);

                [StaticAbstract("TryParse", typeof(TryParse<object>), typeof(ParserRegistry), "TSelf", "T")]
                public interface IParser<TSelf> where TSelf : IParser<TSelf> {}

                public partial class ParserRegistry {}

                public class Color : IParser<Color> {
                    public static bool TryParse(string input, out Color result) {
                        result = new Color();
                        return true;
                    }
                }
            }
            """;

        var compilation = CreateCompilation(source);
        var generator   = new StaticAbstractGenerator();
        GeneratorDriver driver  = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // Should have generated: Registry file and StaticAbstractRegistry (ModuleInitializer)
        Assert.Equal(2, runResult.GeneratedTrees.Length);

        var fileNames = runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.Contains("TestNamespace_ParserRegistry_Registry.g.cs", fileNames);
        Assert.Contains("StaticAbstractRegistry.g.cs",                 fileNames);

        var registrySource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_ParserRegistry_Registry.g.cs")).ToString();
        Assert.Contains("public partial class ParserRegistry", registrySource);
        Assert.Contains("private static readonly global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Delegate> _TryParseRegistry", registrySource);
        Assert.Contains("public static void G_Register_TryParse", registrySource);
        Assert.Contains("public static bool TryParse<T>(string input, out T result)", registrySource);
        Assert.Contains("public static bool TryParse(global::System.Type type, string input, out object? result)", registrySource);

        var moduleInitializerSource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("StaticAbstractRegistry.g.cs")).ToString();
        Assert.Contains("global::TestNamespace.ParserRegistry.G_Register_TryParse", moduleInitializerSource);
        Assert.Contains("typeof(global::TestNamespace.Color)", moduleInitializerSource);
    }

    [Fact]
    public void TestGeneratorWithGenericInterfaceNoTargetClass() {
        const string source =
            """
            using System;
            using Implyzer;

            namespace TestNamespace {
                public delegate bool TryParse<T>(string input, out T result);

                [StaticAbstract("TryParse", typeof(TryParse<object>), "TSelf", "T")]
                public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

                public class Color : IParser<Color> {
                    public static bool TryParse(string input, out Color result) {
                        result = new Color();
                        return true;
                    }
                }
            }
            """;

        var compilation = CreateCompilation(source);
        var generator   = new StaticAbstractGenerator();
        GeneratorDriver driver  = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // Should have generated: Registry file (companion class), Forward file (interface partial part), and StaticAbstractRegistry
        Assert.Equal(3, runResult.GeneratedTrees.Length);

        var fileNames = runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.Contains("TestNamespace_IParser_Registry.g.cs", fileNames);
        Assert.Contains("TestNamespace_IParser_Forward.g.cs",  fileNames);
        Assert.Contains("StaticAbstractRegistry.g.cs",         fileNames);

        var registrySource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_IParser_Registry.g.cs")).ToString();
        Assert.Contains("public static partial class IParser", registrySource);
        Assert.Contains("private static readonly global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Delegate> _TryParseRegistry", registrySource);
        Assert.Contains("public static void G_Register_TryParse", registrySource);
        Assert.Contains("public static bool TryParse<T>(string input, out T result)", registrySource);
        Assert.Contains("public static bool TryParse(global::System.Type type, string input, out object? result)", registrySource);

        var forwardSource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_IParser_Forward.g.cs")).ToString();
        Assert.Contains("public partial interface IParser<TSelf>", forwardSource);
        Assert.Contains("public static bool TryParse(string input, out TSelf result)", forwardSource);
        Assert.Contains("global::TestNamespace.IParser.TryParse<TSelf>(input, out result)", forwardSource);

        var moduleInitializerSource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("StaticAbstractRegistry.g.cs")).ToString();
        Assert.Contains("global::TestNamespace.IParser.G_Register_TryParse", moduleInitializerSource);
        Assert.Contains("typeof(global::TestNamespace.Color)", moduleInitializerSource);
    }

    [Fact]
    public void TestGeneratorWithAttributesNullabilityAndParams() {
        const string source =
            """
            using System;
            using System.Diagnostics.CodeAnalysis;
            using Implyzer;

            namespace TestNamespace {
                [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
                public class CustomAttribute : Attribute {}

                public delegate bool TryParse<T>(
                    [Custom] string? input, 
                    [NotNullWhen(true)] out T? result,
                    params int[] extra
                );

                [StaticAbstract("TryParse", typeof(TryParse<object>), "TSelf", "T")]
                public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

                public class Color : IParser<Color> {
                    public static bool TryParse(string? input, [NotNullWhen(true)] out Color? result, params int[] extra) {
                        result = new Color();
                        return true;
                    }
                }
            }
            """;

        var compilation = CreateCompilation(source);
        var generator   = new StaticAbstractGenerator();
        GeneratorDriver driver  = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        Assert.Equal(3, runResult.GeneratedTrees.Length);

        var registrySource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_IParser_Registry.g.cs")).ToString();
        
        Assert.Contains("public static bool TryParse<T>([global::TestNamespace.CustomAttribute] string? input, [global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] out T? result, params int[] extra)", registrySource);
        Assert.Contains("public static bool TryParse(global::System.Type type, [global::TestNamespace.CustomAttribute] string? input, [global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] out object? result, params int[] extra)", registrySource);

        var forwardSource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_IParser_Forward.g.cs")).ToString();
        
        Assert.Contains("public static bool TryParse([global::TestNamespace.CustomAttribute] string? input, [global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] out TSelf? result, params int[] extra)", forwardSource);
    }
}

