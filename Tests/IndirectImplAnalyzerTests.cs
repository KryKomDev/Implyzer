using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Implyzer.Tests;

public static class VerifyIndirectImpl {
    public static DiagnosticResult Diagnostic(string diagnosticId) => CSharpAnalyzerVerifier<IndirectImplAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);

    public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected) {
        var test = new CSharpAnalyzerTest<IndirectImplAnalyzer, DefaultVerifier> {
            TestCode = source
        };

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}

public class IndirectImplAnalyzerTests {
    private static string CreateTestSource(string testSnippet) =>
        $$"""
          using System;
          using Implyzer;

          namespace Implyzer {
              [AttributeUsage(AttributeTargets.Interface)]
              public class IndirectImplAttribute : Attribute {
                  public Type? ImplementInstead { get; }
                  public IndirectImplAttribute(Type? implementInstead = null) {
                      ImplementInstead = implementInstead;
                  }
              }
          }

          namespace TestNamespace
          {
              {{testSnippet}}
          }
          """;

    [Fact]
    public async Task TestDirectImplementationError() {
        var test =
            """
            [IndirectImpl]
            public interface IInternal {}

            public class TestClass : {|#0:IInternal|} {}
            """;

        var expected = VerifyIndirectImpl.Diagnostic(Rules.IndirectImpl.Id)
            .WithLocation(0)
            .WithArguments("TestClass", "IInternal", "");

        await VerifyIndirectImpl.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestDirectImplementationErrorOnRecord() {
        var test =
            """
            [IndirectImpl]
            public interface IInternal {}

            public record TestRecord : {|#0:IInternal|} {}
            """;

        var expected = VerifyIndirectImpl.Diagnostic(Rules.IndirectImpl.Id)
            .WithLocation(0)
            .WithArguments("TestRecord", "IInternal", "");

        await VerifyIndirectImpl.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestDirectImplementationErrorWithSuggestion() {
        var test =
            """
            public interface IPublic {}

            [IndirectImpl(typeof(IPublic))]
            public interface IInternal {}

            public class TestClass : {|#0:IInternal|} {}
            """;

        var expected = VerifyIndirectImpl.Diagnostic(Rules.IndirectImpl.Id)
            .WithLocation(0)
            .WithArguments("TestClass", "IInternal", ", implement 'IPublic' instead");

        await VerifyIndirectImpl.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestIndirectImplementationNoDiagnostics() {
        var test =
            """
            [IndirectImpl]
            public interface IInternal {}

            public interface IPublic : IInternal {}

            public class TestClass : IPublic {}
            """;

        await VerifyIndirectImpl.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestMetadataGenericInterface() {
        const string librarySource =
            """
            using System;

            namespace Implyzer {
                [AttributeUsage(AttributeTargets.Interface)]
                public class IndirectImplAttribute : Attribute {
                    public Type? ImplementInstead { get; }
                    public IndirectImplAttribute(Type? implementInstead = null) {
                        ImplementInstead = implementInstead;
                    }
                }
            }

            namespace LibraryNamespace {
                [Implyzer.IndirectImpl]
                public interface IInternal<T> {}
            }
            """;

        var testCode =
            """
            using LibraryNamespace;

            namespace TestNamespace {
                public class TestClass : {|#0:IInternal<int>|} {}
            }
            """;

        var test = new CSharpAnalyzerTest<IndirectImplAnalyzer, DefaultVerifier> {
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

        var expected = VerifyIndirectImpl.Diagnostic(Rules.IndirectImpl.Id)
            .WithLocation(0)
            .WithArguments("TestClass", "IInternal", "");

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }
}