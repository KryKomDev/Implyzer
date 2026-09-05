using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Implyzer.Tests;

public class StaticAbstractGeneratorTests {
    private static readonly MetadataReference CORLIB_REFERENCE          = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference SYSTEM_REFERENCE          = MetadataReference.CreateFromFile(typeof(System.Collections.Generic.Dictionary<,>).Assembly.Location);
    private static readonly MetadataReference COMPONENT_MODEL_REFERENCE = MetadataReference.CreateFromFile(typeof(System.ComponentModel.EditorBrowsableAttribute).Assembly.Location);

    private static Compilation CreateCompilation(string source, LanguageVersion languageVersion = LanguageVersion.CSharp10, bool enableVirtualStatics = false) {
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
                    public Type? DefaultType { get; set; }
                    public string? DefaultMethod { get; set; }

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

                [AttributeUsage(AttributeTargets.Interface, AllowMultiple = true)]
                public class StaticVirtualAttribute : StaticAbstractAttribute {
                    public StaticVirtualAttribute(string methodName, Type signature, params string[] typeParams)
                        : base(methodName, signature, typeParams) { }

                    public StaticVirtualAttribute(string methodName, Type signature, Type targetClass, params string[] typeParams)
                        : base(methodName, signature, targetClass, typeParams) { }
                }

                [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
                public class StaticDefaultAttribute : Attribute {
                    public string? MethodName { get; }
                    public StaticDefaultAttribute(string? methodName = null) {
                        MethodName = methodName;
                    }
                }
            }
            """;

        var parseOptions = new CSharpParseOptions(languageVersion);

        var syntaxTrees = new System.Collections.Generic.List<SyntaxTree> {
            CSharpSyntaxTree.ParseText(attributeSource, parseOptions),
            CSharpSyntaxTree.ParseText(source,          parseOptions)
        };

        if (!enableVirtualStatics)
            return CSharpCompilation.Create(
                "TestAssembly",
                syntaxTrees,
                [
                    CORLIB_REFERENCE,
                    SYSTEM_REFERENCE,
                    COMPONENT_MODEL_REFERENCE
                ],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

        const string runtimeFeatureSource =
            """
            namespace System.Runtime.CompilerServices {
                public static class RuntimeFeature {
                    public const string VirtualStaticsInInterfaces = "VirtualStaticsInInterfaces";
                }
            }
            """;

        syntaxTrees.Add(CSharpSyntaxTree.ParseText(runtimeFeatureSource, parseOptions));

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            [
                CORLIB_REFERENCE,
                SYSTEM_REFERENCE,
                COMPONENT_MODEL_REFERENCE
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

        var             compilation = CreateCompilation(source);
        var             generator   = new StaticAbstractGenerator();
        GeneratorDriver driver      = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // Should have generated: Registry file and StaticAbstractRegistry (ModuleInitializer)
        Assert.Equal(2, runResult.GeneratedTrees.Length);

        var fileNames = runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.Contains("TestNamespace_ParserRegistry_Registry.g.cs", fileNames);
        Assert.Contains("StaticAbstractRegistry.g.cs",                fileNames);

        var registrySource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_ParserRegistry_Registry.g.cs")).ToString();
        Assert.Contains("public partial class ParserRegistry",                                                                                                   registrySource);
        Assert.Contains("private static readonly global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Delegate> _TryParseRegistry", registrySource);
        Assert.Contains("public static void G_Register_TryParse",                                                                                                registrySource);
        Assert.Contains("public static bool TryParse<T>(string input, out T result)",                                                                            registrySource);
        Assert.Contains("public static bool TryParse(global::System.Type type, string input, out object? result)",                                               registrySource);

        var moduleInitializerSource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("StaticAbstractRegistry.g.cs")).ToString();
        Assert.Contains("global::TestNamespace.ParserRegistry.G_Register_TryParse", moduleInitializerSource);
        Assert.Contains("typeof(global::TestNamespace.Color)",                      moduleInitializerSource);
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

        var             compilation = CreateCompilation(source);
        var             generator   = new StaticAbstractGenerator();
        GeneratorDriver driver      = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // Should have generated: Registry file (companion class), Forward file (interface partial part), and StaticAbstractRegistry
        Assert.Equal(3, runResult.GeneratedTrees.Length);

        var fileNames = runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.Contains("TestNamespace_IParser_Registry.g.cs", fileNames);
        Assert.Contains("TestNamespace_IParser_Forward.g.cs",  fileNames);
        Assert.Contains("StaticAbstractRegistry.g.cs",         fileNames);

        var registrySource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_IParser_Registry.g.cs")).ToString();
        Assert.Contains("public static partial class IParser",                                                                                                   registrySource);
        Assert.Contains("private static readonly global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Delegate> _TryParseRegistry", registrySource);
        Assert.Contains("public static void G_Register_TryParse",                                                                                                registrySource);
        Assert.Contains("public static bool TryParse<T>(string input, out T result)",                                                                            registrySource);
        Assert.Contains("public static bool TryParse(global::System.Type type, string input, out object? result)",                                               registrySource);

        var forwardSource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_IParser_Forward.g.cs")).ToString();
        Assert.Contains("public partial interface IParser<TSelf>",                          forwardSource);
        Assert.Contains("public static bool TryParse(string input, out TSelf result)",      forwardSource);
        Assert.Contains("global::TestNamespace.IParser.TryParse<TSelf>(input, out result)", forwardSource);

        var moduleInitializerSource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("StaticAbstractRegistry.g.cs")).ToString();
        Assert.Contains("global::TestNamespace.IParser.G_Register_TryParse", moduleInitializerSource);
        Assert.Contains("typeof(global::TestNamespace.Color)",               moduleInitializerSource);
    }

    [Fact]
    public void TestGeneratorWithCSharp11NativeStaticAbstract() {
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

        var             compilation = CreateCompilation(source, LanguageVersion.CSharp11);
        var             generator   = new StaticAbstractGenerator();
        GeneratorDriver driver      = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // Should have generated: Registry file (companion class) and Forward file (interface partial part with static abstract)
        Assert.Equal(2, runResult.GeneratedTrees.Length);

        var fileNames = runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.Contains("TestNamespace_IParser_Registry.g.cs", fileNames);
        Assert.Contains("TestNamespace_IParser_Forward.g.cs",  fileNames);
        Assert.DoesNotContain("StaticAbstractRegistry.g.cs", fileNames);

        var registrySource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_IParser_Registry.g.cs")).ToString();
        Assert.Contains("public static partial class IParser",                                                                            registrySource);
        Assert.Contains("public static bool TryParse<T>(string input, out T result) where T : global::TestNamespace.IParser<T>",          registrySource);
        Assert.Contains("return T.TryParse(input, out result);",                                                                          registrySource);
        Assert.Contains("public static bool TryParse(global::System.Type type, string input, out object? result)",                        registrySource);
        Assert.Contains("type.GetMethods(global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static)", registrySource);

        var forwardSource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_IParser_Forward.g.cs")).ToString();
        Assert.Contains("public partial interface IParser<TSelf>",                               forwardSource);
        Assert.Contains("public static abstract bool TryParse(string input, out TSelf result);", forwardSource);
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

        var             compilation = CreateCompilation(source);
        var             generator   = new StaticAbstractGenerator();
        GeneratorDriver driver      = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        Assert.Equal(3, runResult.GeneratedTrees.Length);

        var registrySource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_IParser_Registry.g.cs")).ToString();

        Assert.Contains("public static bool TryParse<T>([global::TestNamespace.CustomAttribute] string? input, [global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] out T? result, params int[] extra)",                             registrySource);
        Assert.Contains("public static bool TryParse(global::System.Type type, [global::TestNamespace.CustomAttribute] string? input, [global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] out object? result, params int[] extra)", registrySource);

        var forwardSource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_IParser_Forward.g.cs")).ToString();

        Assert.Contains("public static bool TryParse([global::TestNamespace.CustomAttribute] string? input, [global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] out TSelf? result, params int[] extra)", forwardSource);
    }

    [Fact]
    public void TestGeneratorWithMetadataInterface() {
        const string librarySource =
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

            namespace LibraryNamespace {
                public delegate bool TryParse<T>(string input, out T result);

                [Implyzer.StaticAbstract("TryParse", typeof(TryParse<object>), "TSelf", "T")]
                public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}
            }
            """;

        // Compile library to MetadataReference
        var librarySyntaxTree = CSharpSyntaxTree.ParseText(librarySource, new CSharpParseOptions(LanguageVersion.CSharp10));

        var libraryCompilation = CSharpCompilation.Create(
            "LibraryAssembly",
            [librarySyntaxTree],
            [CORLIB_REFERENCE, SYSTEM_REFERENCE, COMPONENT_MODEL_REFERENCE],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        using var ms         = new MemoryStream();
        var       emitResult = libraryCompilation.Emit(ms);
        Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics.Select(d => d.ToString())));
        ms.Seek(0, SeekOrigin.Begin);
        var libraryReference = MetadataReference.CreateFromStream(ms);

        // Compile Main Assembly referencing LibraryAssembly
        const string mainSource =
            """
            using LibraryNamespace;

            namespace TestNamespace {
                public class Color : IParser<Color> {
                    public static bool TryParse(string input, out Color result) {
                        result = new Color();
                        return true;
                    }
                }
            }
            """;

        var mainSyntaxTree = CSharpSyntaxTree.ParseText(mainSource, new CSharpParseOptions(LanguageVersion.CSharp10));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [mainSyntaxTree],
            [CORLIB_REFERENCE, SYSTEM_REFERENCE, COMPONENT_MODEL_REFERENCE, libraryReference],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var             generator = new StaticAbstractGenerator();
        GeneratorDriver driver    = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // Should generate StaticAbstractRegistry
        Assert.Contains("StaticAbstractRegistry.g.cs", runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)));

        var moduleInitializerSource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("StaticAbstractRegistry.g.cs")).ToString();
        Assert.Contains("global::LibraryNamespace.IParser.G_Register_TryParse", moduleInitializerSource);
        Assert.Contains("typeof(global::TestNamespace.Color)",                  moduleInitializerSource);
    }

    [Fact]
    public void TestGeneratorWithRecordTargetClass() {
        const string source =
            """
            using System;
            using Implyzer;

            namespace TestNamespace {
                public delegate bool TryParse<T>(string input, out T result);

                [StaticAbstract("TryParse", typeof(TryParse<object>), typeof(ParserRegistry), "TSelf", "T")]
                public interface IParser<TSelf> where TSelf : IParser<TSelf> {}

                public partial record ParserRegistry;

                public record struct Color : IParser<Color> {
                    public static bool TryParse(string input, out Color result) {
                        result = new Color();
                        return true;
                    }
                }
            }
            """;

        var             compilation = CreateCompilation(source);
        var             generator   = new StaticAbstractGenerator();
        GeneratorDriver driver      = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // Should have generated: Registry file and StaticAbstractRegistry (ModuleInitializer)
        Assert.Equal(2, runResult.GeneratedTrees.Length);

        var fileNames = runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.Contains("TestNamespace_ParserRegistry_Registry.g.cs", fileNames);
        Assert.Contains("StaticAbstractRegistry.g.cs",                fileNames);

        var registrySource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_ParserRegistry_Registry.g.cs")).ToString();
        Assert.Contains("public partial record ParserRegistry", registrySource);

        var moduleInitializerSource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("StaticAbstractRegistry.g.cs")).ToString();
        Assert.Contains("global::TestNamespace.ParserRegistry.G_Register_TryParse", moduleInitializerSource);
        Assert.Contains("typeof(global::TestNamespace.Color)",                      moduleInitializerSource);
    }

    [Fact]
    public void TestGeneratorWithDefaultImplementation_CSharp11() {
        const string source =
            """
            using System;
            using Implyzer;

            namespace TestNamespace {
                public delegate T Parse<T>(string input);

                public static class ParserDefaults {
                    public static T Parse<T>(string input) where T : IParser<T> => throw null!;
                }

                [StaticVirtual("Parse", typeof(Parse<object>), DefaultType = typeof(ParserDefaults), typeParams: new[] { "TSelf", "T" })]
                public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

                public class Color : IParser<Color> {}
            }
            """;

        var             compilation = CreateCompilation(source, LanguageVersion.CSharp11, enableVirtualStatics: true);
        var             generator   = new StaticAbstractGenerator();
        GeneratorDriver driver      = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var fileNames = runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.Contains("TestNamespace_IParser_Forward.g.cs", fileNames);

        var forwardSource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_IParser_Forward.g.cs")).ToString();
        Assert.Contains("public static virtual TSelf Parse(string input)", forwardSource);
        Assert.Contains("ParserDefaults.Parse<TSelf>(input)",              forwardSource);
    }

    [Fact]
    public void TestGeneratorWithDefaultImplementation_CSharp10() {
        const string source =
            """
            using System;
            using Implyzer;

            namespace TestNamespace {
                public delegate T Parse<T>(string input);

                public static class ParserDefaults {
                    public static T Parse<T>(string input) where T : IParser<T> => throw null!;
                }

                [StaticVirtual("Parse", typeof(Parse<object>), DefaultType = typeof(ParserDefaults), typeParams: new[] { "TSelf", "T" })]
                public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

                public class Color : IParser<Color> {}
            }
            """;

        var             compilation = CreateCompilation(source, LanguageVersion.CSharp10, enableVirtualStatics: false);
        var             generator   = new StaticAbstractGenerator();
        GeneratorDriver driver      = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var fileNames = runResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();
        Assert.Contains("TestNamespace_IParser_Registry.g.cs", fileNames);

        var registrySource = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("TestNamespace_IParser_Registry.g.cs")).ToString();
        Assert.Contains("ParserDefaults.Parse<T>(input)",    registrySource);
        Assert.Contains("defMethod.MakeGenericMethod(type)", registrySource);
    }
}