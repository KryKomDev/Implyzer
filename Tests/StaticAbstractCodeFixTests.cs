using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Implyzer.Tests;

public static class VerifyStaticAbstractCodeFix {
    public static async Task VerifyCodeFixAsync(string source, string fixedSource, params DiagnosticResult[] expected) =>
        await VerifyCodeFixCoreAsync(source, fixedSource, null, null, null, expected);

    public static async Task VerifyCodeFixAsync(string source, string fixedSource, int? codeActionIndex, params DiagnosticResult[] expected) =>
        await VerifyCodeFixCoreAsync(source, fixedSource, codeActionIndex, null, null, expected);

    public static async Task VerifyCodeFixIterativeAsync(string source, string fixedSource, int numberOfIterations, params DiagnosticResult[] expected) =>
        await VerifyCodeFixCoreAsync(source, fixedSource, null, numberOfIterations, null, expected);

    public static async Task VerifyCodeFixWithFixedStateAsync(string source, string fixedSource, int codeActionIndex, DiagnosticResult[] fixedExpected, params DiagnosticResult[] expected) =>
        await VerifyCodeFixCoreAsync(source, fixedSource, codeActionIndex, 1, fixedExpected, expected);

    private static async Task VerifyCodeFixCoreAsync(string source, string fixedSource, int? codeActionIndex, int? numberOfIterations, DiagnosticResult[]? fixedExpected, DiagnosticResult[] expected) {
        var test = new CSharpCodeFixTest<StaticAbstractAnalyzer, StaticAbstractCodeFixProvider, DefaultVerifier> {
            TestCode        = source,
            FixedCode       = fixedSource,
            CodeActionIndex = codeActionIndex
        };

        if (numberOfIterations.HasValue) {
            test.NumberOfIncrementalIterations = numberOfIterations.Value;
            test.NumberOfFixAllIterations         = numberOfIterations.Value;
        }

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
        if (fixedExpected != null) {
            test.CodeFixTestBehaviors = CodeFixTestBehaviors.FixOne;
            test.FixedState.ExpectedDiagnostics.AddRange(fixedExpected);
        }

        await test.RunAsync();
    }

    public static DiagnosticResult Diagnostic(string diagnosticId) => CSharpAnalyzerVerifier<StaticAbstractAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);
}

public static class VerifyStaticVirtualRefactoring {
    public static async Task VerifyRefactoringAsync(string source, string fixedSource, int? codeActionIndex = null) {
        var test = new CSharpCodeRefactoringTest<StaticVirtualCodeRefactoringProvider, DefaultVerifier> {
            TestCode        = source,
            FixedCode       = fixedSource,
            CodeActionIndex = codeActionIndex
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

        await test.RunAsync();
    }
}

public class StaticAbstractCodeFixTests {
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
                  public bool ImplementInTargetTypes { get; set; }

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
          }

          namespace TestNamespace
          {
              {{testSnippet}}
          }
          """;

    [Fact]
    public async Task TestMakeInterfacePartial() {
        var test =
            """
            public delegate bool MyDelegate(string input);

            [StaticAbstract("Method", typeof(MyDelegate))]
            public interface {|#0:IParser|} {}
            """;

        var fixedTest =
            """
            public delegate bool MyDelegate(string input);

            [StaticAbstract("Method", typeof(MyDelegate))]
            public partial interface IParser {}
            """;

        var expected = VerifyStaticAbstractCodeFix.Diagnostic(Rules.StaticAbstractInterfaceNotPartial.Id)
            .WithLocation(0)
            .WithArguments("IParser");

        await VerifyStaticAbstractCodeFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), expected);
    }

    [Fact]
    public async Task TestMakeTargetClassPartial() {
        var test =
            """
            public delegate bool MyDelegate(string input);

            public class Registry {}

            [{|#0:StaticAbstract("Method", typeof(MyDelegate), typeof(Registry))|}]
            public interface IParser {}
            """;

        var fixedTest =
            """
            public delegate bool MyDelegate(string input);

            public partial class Registry {}

            [StaticAbstract("Method", typeof(MyDelegate), typeof(Registry))]
            public interface IParser {}
            """;

        var expected = VerifyStaticAbstractCodeFix.Diagnostic(Rules.StaticAbstractTargetClassNotPartial.Id)
            .WithLocation(0)
            .WithArguments("Registry");

        await VerifyStaticAbstractCodeFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), expected);
    }

    [Fact]
    public async Task TestImplementStaticMethod() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class {|#0:Color|} : IParser<Color> {}
            """;

        var fixedTest =
            """
            public delegate bool TryParse<T>(string input, out T result);

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class Color : IParser<Color> {
                    public static bool TryParse(string input, out global::TestNamespace.Color result) => throw new global::System.NotImplementedException();
            }
            """;

        var expected = VerifyStaticAbstractCodeFix.Diagnostic(Rules.StaticAbstractMethodNotImplemented.Id)
            .WithLocation(0)
            .WithArguments("Color", "TryParse", "TryParse<Color>", "IParser<Color>");

        await VerifyStaticAbstractCodeFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), expected);
    }

    [Fact]
    public async Task TestMakeTargetTypePartial_WhenIMPL017() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults), ImplementInTargetTypes = true)]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class {|#0:Color|} : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
            }
            """;

        var fixedTest =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults), ImplementInTargetTypes = true)]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public partial class {|#0:Color|} : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
            }
            """;

        var expected = VerifyStaticAbstractCodeFix.Diagnostic(Rules.StaticVirtualTargetTypeNotPartial.Id)
            .WithLocation(0)
            .WithArguments("Color", "IParser", "Parse");

        var fixedExpected = new[] {
            VerifyStaticAbstractCodeFix.Diagnostic(Rules.StaticVirtualMethodNotImplemented.Id)
                .WithLocation(0)
                .WithArguments("Color", "Parse", "IParser")
                .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden)
        };

        await VerifyStaticAbstractCodeFix.VerifyCodeFixWithFixedStateAsync(CreateTestSource(test), CreateTestSource(fixedTest), 0, fixedExpected, expected);
    }

    [Fact]
    public async Task TestImplementStaticVirtualMethod_WhenIMPL017() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults), ImplementInTargetTypes = true)]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class {|#0:Color|} : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
            }
            """;

        var fixedTest =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults), ImplementInTargetTypes = true)]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class Color : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }

                    public static global::TestNamespace.Color Parse(string input) => global::TestNamespace.ParserDefaults.Parse<global::TestNamespace.Color>(input);
            }
            """;

        var expected = VerifyStaticAbstractCodeFix.Diagnostic(Rules.StaticVirtualTargetTypeNotPartial.Id)
            .WithLocation(0)
            .WithArguments("Color", "IParser", "Parse");

        await VerifyStaticAbstractCodeFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), 1, expected);
    }

    [Fact]
    public async Task TestStaticVirtualRefactoring_SingleMethod() {
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

            public class [|Color|] : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
            }
            """;

        var fixedTest =
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

                    public static global::TestNamespace.Color Parse(string input) => global::TestNamespace.ParserDefaults.Parse<global::TestNamespace.Color>(input);
            }
            """;

        await VerifyStaticVirtualRefactoring.VerifyRefactoringAsync(CreateTestSource(test), CreateTestSource(fixedTest));
    }

    [Fact]
    public async Task TestStaticVirtualRefactoring_MultipleMethods() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);
            public delegate string Format<T>(T value);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
                public static string Format<T>(T value) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))]
            [StaticVirtual("Format", typeof(Format<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class [|Color|] : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
            }
            """;

        var fixedTest =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);
            public delegate string Format<T>(T value);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
                public static string Format<T>(T value) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))]
            [StaticVirtual("Format", typeof(Format<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class Color : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }

                    public static global::TestNamespace.Color Parse(string input) => global::TestNamespace.ParserDefaults.Parse<global::TestNamespace.Color>(input);

                    public static string Format(global::TestNamespace.Color value) => global::TestNamespace.ParserDefaults.Format<global::TestNamespace.Color>(value);
            }
            """;

        // Action index 2 is "Implement all static virtual methods" (index 0 is Parse, 1 is Format, 2 is All)
        await VerifyStaticVirtualRefactoring.VerifyRefactoringAsync(CreateTestSource(test), CreateTestSource(fixedTest), 2);
    }

    [Fact]
    public async Task TestImplementStaticVirtualMethod_WhenIMPL018() {
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

            public class {|#0:Color|} : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
            }
            """;

        var fixedTest =
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

                    public static global::TestNamespace.Color Parse(string input) => global::TestNamespace.ParserDefaults.Parse<global::TestNamespace.Color>(input);
            }
            """;

        var expected = VerifyStaticAbstractCodeFix.Diagnostic(Rules.StaticVirtualMethodNotImplemented.Id)
            .WithLocation(0)
            .WithArguments("Color", "Parse", "IParser")
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden);

        await VerifyStaticAbstractCodeFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), expected);
    }

    [Fact]
    public async Task TestImplementStaticVirtualMethod_WhenPartial_WhenIMPL018() {
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

            public partial class {|#0:Color|} : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
            }
            """;

        var fixedTest =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public partial class Color : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }

                    public static global::TestNamespace.Color Parse(string input) => global::TestNamespace.ParserDefaults.Parse<global::TestNamespace.Color>(input);
            }
            """;

        var expected = VerifyStaticAbstractCodeFix.Diagnostic(Rules.StaticVirtualMethodNotImplemented.Id)
            .WithLocation(0)
            .WithArguments("Color", "Parse", "IParser")
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden);

        await VerifyStaticAbstractCodeFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), expected);
    }

    [Fact]
    public async Task TestImplementStaticVirtualMethod_MultipleMethods_WhenIMPL018() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);
            public delegate string Format<T>(T value);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
                public static string Format<T>(T value) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))]
            [StaticVirtual("Format", typeof(Format<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class {|#0:Color|} : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }
            }
            """;

        var fixedTest =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);
            public delegate string Format<T>(T value);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
                public static string Format<T>(T value) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))]
            [StaticVirtual("Format", typeof(Format<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults))]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public class Color : IParser<Color> {
                public static bool TryParse(string input, out Color result) {
                    result = new Color();
                    return true;
                }

                    public static string Format(global::TestNamespace.Color value) => global::TestNamespace.ParserDefaults.Format<global::TestNamespace.Color>(value);

                    public static global::TestNamespace.Color Parse(string input) => global::TestNamespace.ParserDefaults.Parse<global::TestNamespace.Color>(input);
            }
            """;

        var expectedParse = VerifyStaticAbstractCodeFix.Diagnostic(Rules.StaticVirtualMethodNotImplemented.Id)
            .WithLocation(0)
            .WithArguments("Color", "Parse", "IParser")
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden);

        var expectedFormat = VerifyStaticAbstractCodeFix.Diagnostic(Rules.StaticVirtualMethodNotImplemented.Id)
            .WithLocation(0)
            .WithArguments("Color", "Format", "IParser")
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden);

        await VerifyStaticAbstractCodeFix.VerifyCodeFixIterativeAsync(CreateTestSource(test), CreateTestSource(fixedTest), 2, expectedParse, expectedFormat);
    }

    [Fact]
    public async Task TestImplementStaticVirtualMethod_GenericType_Box() {
        var test =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults), ImplementInTargetTypes = true)]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public partial class {|#0:Box|}<T> : IParser<Box<T>> {
                public static bool TryParse(string input, out Box<T> result) {
                    result = new Box<T>();
                    return true;
                }
            }
            """;

        var fixedTest =
            """
            public delegate bool TryParse<T>(string input, out T result);
            public delegate T Parse<T>(string input);

            public static class ParserDefaults {
                public static T Parse<T>(string input) where T : IParser<T> => throw null!;
            }

            [StaticAbstract("TryParse", typeof(TryParse<object>), new[] { "TSelf", "T" })]
            [StaticVirtual("Parse", typeof(Parse<object>), new[] { "TSelf", "T" }, DefaultType = typeof(ParserDefaults), ImplementInTargetTypes = true)]
            public partial interface IParser<TSelf> where TSelf : IParser<TSelf> {}

            public partial class Box<T> : IParser<Box<T>> {
                public static bool TryParse(string input, out Box<T> result) {
                    result = new Box<T>();
                    return true;
                }

                    public static global::TestNamespace.Box<T> Parse(string input) => global::TestNamespace.ParserDefaults.Parse<global::TestNamespace.Box<T>>(input);
            }
            """;

        var expected = VerifyStaticAbstractCodeFix.Diagnostic(Rules.StaticVirtualMethodNotImplemented.Id)
            .WithLocation(0)
            .WithArguments("Box", "Parse", "IParser")
            .WithSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden);

        await VerifyStaticAbstractCodeFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), expected);
    }
}