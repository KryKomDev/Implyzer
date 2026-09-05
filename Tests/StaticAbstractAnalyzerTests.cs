using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Implyzer.Tests;

public static class VerifyStaticAbstract {
    public static DiagnosticResult Diagnostic(string diagnosticId) => CSharpAnalyzerVerifier<StaticAbstractAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);

    public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected) {
        var test = new CSharpAnalyzerTest<StaticAbstractAnalyzer, DefaultVerifier> {
            TestCode = source
        };

        test.SolutionTransforms.Add(
            (solution, projectId) => {
                var project = solution.GetProject(projectId);

                if (project == null)
                    return solution;

                var parseOptions = project.ParseOptions as Microsoft.CodeAnalysis.CSharp.CSharpParseOptions;

                if (parseOptions == null)
                    return solution;

                return solution.WithProjectParseOptions(projectId, parseOptions.WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest));
            }
        );

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}

public class StaticAbstractAnalyzerTests {
    private static string CreateTestSource(string testSnippet) =>
        $$"""
          using System;
          using System.Collections.Generic;
          using Implyzer;

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

          namespace TestNamespace
          {
              {{testSnippet}}
          }
          """;

    [Fact]
    public async Task TestValidStaticAbstractNoDiagnostics() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class Color : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
            }
            """;

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestInterfaceNotPartial() {
        var test =
            """
            public delegate bool MyDelegate(string input);

            [StaticAbstract("Method", typeof(MyDelegate))]
            public interface {|#0:IParser|} {}
            """;

        var expected = VerifyStaticAbstract.Diagnostic(Rules.StaticAbstractInterfaceNotPartial.Id)
            .WithLocation(0)
            .WithArguments("IParser");

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestTargetClassNotPartial() {
        var test =
            """
            public delegate bool MyDelegate(string input);

            public class Registry {}

            [{|#0:StaticAbstract("Method", typeof(MyDelegate), typeof(Registry))|}]
            public interface IParser {}
            """;

        var expected = VerifyStaticAbstract.Diagnostic(Rules.StaticAbstractTargetClassNotPartial.Id)
            .WithLocation(0)
            .WithArguments("Registry");

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestTargetClassNotClass() {
        var test =
            """
            public delegate bool MyDelegate(string input);

            public struct Registry {}

            [{|#0:StaticAbstract("Method", typeof(MyDelegate), typeof(Registry))|}]
            public interface IParser {}
            """;

        var expected = VerifyStaticAbstract.Diagnostic(Rules.StaticAbstractTargetClassMustBeClass.Id)
            .WithLocation(0)
            .WithArguments("Registry");

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestMethodNotImplemented() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class {|#0:Color|} : IParser<Color> {}
            """;

        var expected = VerifyStaticAbstract.Diagnostic(Rules.StaticAbstractMethodNotImplemented.Id)
            .WithLocation(0)
            .WithArguments("Color", "TryParse", "TryParse<Color>", "IParser<Color>");

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestSignatureNotDelegate() {
        var test =
            """
            public class NotADelegate {}

            [{|#0:StaticAbstract("Method", typeof(NotADelegate))|}]
            public partial interface IParser {}
            """;

        var expected = VerifyStaticAbstract.Diagnostic(Rules.StaticAbstractSignatureNotDelegate.Id)
            .WithLocation(0)
            .WithArguments("NotADelegate");

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestMethodImplementationNullabilityMismatch() {
        var test =
            """
            #nullable enable
            public delegate bool TryParse<T>(string? input, out T? result);

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class Color : IParser<Color> {
                // Mismatch: input is 'string' instead of 'string?' - now allowed!
                public static bool TryParse(string input, out Color? result) {
                    result = new Color();
                    return true;
                }
            }
            """;

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestMethodImplementationParamsMismatch() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result, params int[] extra);

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class {|#0:Color|} : IParser<Color> {
                // Mismatch: missing 'params' keyword on extra
                public static bool TryParse(string input, out Color result, int[] extra) {
                    result = new Color();
                    return true;
                }
            }
            """;

        var expected = VerifyStaticAbstract.Diagnostic(Rules.StaticAbstractMethodNotImplemented.Id)
            .WithLocation(0)
            .WithArguments("Color", "TryParse", "TryParse<Color>", "IParser<Color>");

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestMethodImplementationAttributeMismatch() {
        var test =
            """
            using System;

            [AttributeUsage(AttributeTargets.Parameter)]
            public class CustomAttribute : Attribute {}

            public delegate bool TryParse<T>([Custom] string input, out T result);

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class Color : IParser<Color> {
                // Mismatch: missing [Custom] attribute on input - now allowed!
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
            }
            """;

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestMetadataInterfaceNotImplemented() {
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

        var testCode =
            """
            using LibraryNamespace;

            namespace TestNamespace {
                public class {|#0:Color|} : IParser<Color> {}
            }
            """;

        var test = new CSharpAnalyzerTest<StaticAbstractAnalyzer, DefaultVerifier> {
            TestCode = testCode
        };

        test.SolutionTransforms.Add(
            (solution, projectId) => {
                var libProjectId = Microsoft.CodeAnalysis.ProjectId.CreateNewId("LibraryProject");
                solution = solution.AddProject(libProjectId, "LibraryProject", "LibraryProject", Microsoft.CodeAnalysis.LanguageNames.CSharp);
                var mainProject = solution.GetProject(projectId)!;

                var libProject = solution.GetProject(libProjectId)!
                    .WithMetadataReferences(mainProject.MetadataReferences)
                    .WithCompilationOptions(mainProject.CompilationOptions!)
                    .WithParseOptions(((Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)mainProject.ParseOptions!).WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest));

                solution = libProject.Solution;
                var docId = Microsoft.CodeAnalysis.DocumentId.CreateNewId(libProjectId);
                solution = solution.AddDocument(docId, "Library.cs", librarySource);

                return solution.AddProjectReference(projectId, new Microsoft.CodeAnalysis.ProjectReference(libProjectId));
            }
        );

        var expected = VerifyStaticAbstract.Diagnostic(Rules.StaticAbstractMethodNotImplemented.Id)
            .WithLocation(0)
            .WithArguments("Color", "TryParse", "TryParse<Color>", "IParser<Color>");

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }

    [Fact]
    public async Task TestValidStaticAbstractWithDefaultImplementation_NoDiagnostics() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticAbstract("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class Color : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
                // Parse is omitted because it has a default implementation
            }
            """;

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestValidStaticAbstractWithDefaultImplementation_Overridden_NoDiagnostics() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticAbstract("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class Color : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
                // Parse is explicitly overridden
                public static Color Parse(string input) => new Color();
            }
            """;

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestValidStaticVirtualAttribute_NoDiagnostics() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class Color : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
            }
            """;

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestStaticDefaultAttribute_NoDiagnostics() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);

            public static class ParserDefaults {
                [StaticDefault("Parse")]
                public static T CustomParseDefault<T>(string input) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class Color : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
            }
            """;

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestDefaultMethodNotFound_Diagnostic() {
        var test =
            """
            public delegate T Parse<T>(string input);

            public static class ParserDefaults {}

            [{|#0:StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults), DefaultMethod = "NonExistent")|}]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}
            """;

        var expected = VerifyStaticAbstract.Diagnostic(Rules.StaticAbstractDefaultMethodNotFound.Id)
            .WithLocation(0)
            .WithArguments("NonExistent", "ParserDefaults");

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestDefaultMethodSignatureMismatch_Diagnostic() {
        var test =
            """
            public delegate T Parse<T>(string input);

            public static class ParserDefaults {
                public static T Parse<T>(int number) where T : IParser<T> => throw null!;
            }

            [{|#0:StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))|}]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}
            """;

        var expected = VerifyStaticAbstract.Diagnostic(Rules.StaticAbstractDefaultMethodSignatureMismatch.Id)
            .WithLocation(0)
            .WithArguments("Parse", "ParserDefaults", "Parse");

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestDefaultMethodMustBeStatic_Diagnostic() {
        var test =
            """
            public delegate T Parse<T>(string input);

            public class ParserDefaults {
                public T Parse<T>(string input) where T : IParser<T> => throw null!;
            }

            [{|#0:StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))|}]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}
            """;

        var expected = VerifyStaticAbstract.Diagnostic(Rules.StaticAbstractDefaultMethodMustBeStatic.Id)
            .WithLocation(0)
            .WithArguments("Parse", "ParserDefaults");

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestOpenGenericImplementingType_Success() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);

            [StaticAbstract("TryParse", typeof(TryParse<object>), "TSelf", "T")]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class Box<T> : IParser<Box<T>> {
                public static bool TryParse(string input, out Box<T> result) {
                    result = new Box<T>();
                    return true;
                }
            }
            """;

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestOpenGenericImplementingType_MissingMethod_Diagnostic() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);

            [StaticAbstract("TryParse", typeof(TryParse<object>), "TSelf", "T")]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class {|#0:Box|}<T> : IParser<Box<T>> {
            }
            """;

        var expected = VerifyStaticAbstract.Diagnostic(Rules.StaticAbstractMethodNotImplemented.Id)
            .WithLocation(0)
            .WithArguments("Box", "TryParse", "TryParse<Box<T>>", "IParser<Box<T>>");

        await VerifyStaticAbstract.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }
}