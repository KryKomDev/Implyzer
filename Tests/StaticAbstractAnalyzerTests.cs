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
        test.SolutionTransforms.Add((solution, projectId) => {
            var project = solution.GetProject(projectId);
            if (project == null) return solution;
            var parseOptions = project.ParseOptions as Microsoft.CodeAnalysis.CSharp.CSharpParseOptions;
            if (parseOptions == null) return solution;
            return solution.WithProjectParseOptions(projectId, parseOptions.WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest));
        });

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

            public class {|#0:Color|} : IParser<Color> {
                // Mismatch: input is 'string' instead of 'string?'
                public static bool TryParse(string input, out Color? result) {
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

            public class {|#0:Color|} : IParser<Color> {
                // Mismatch: missing [Custom] attribute on input
                public static bool TryParse(string input, out Color result) {
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
}
