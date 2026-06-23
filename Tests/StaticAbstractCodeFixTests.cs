using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Implyzer.Tests;

public static class VerifyStaticAbstractCodeFix {
    public static async Task VerifyCodeFixAsync(string source, string fixedSource, params DiagnosticResult[] expected) {
        var test = new CSharpCodeFixTest<StaticAbstractAnalyzer, StaticAbstractCodeFixProvider, DefaultVerifier> {
            TestCode  = source,
            FixedCode = fixedSource
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

    public static DiagnosticResult Diagnostic(string diagnosticId) => CSharpAnalyzerVerifier<StaticAbstractAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);
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
}
