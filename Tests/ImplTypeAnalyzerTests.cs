using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Implyzer.Tests;

public static class VerifyCS {
    public static DiagnosticResult Diagnostic(string diagnosticId) => CSharpAnalyzerVerifier<ImplTypeAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);

    public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected) {
        var test = new CSharpAnalyzerTest<ImplTypeAnalyzer, DefaultVerifier> {
            TestCode = source
        };

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}

public class ImplTypeAnalyzerTests {
    private static string CreateTestSource(string testSnippet) =>
        $$"""
          using System;
          using Implyzer;

          namespace Implyzer {
              public enum ImplKind {
                  ReferenceType,
                  ValueType,
                  ReferenceTypeNew
              }

              [AttributeUsage(AttributeTargets.Interface)]
              public class ImplTypeAttribute : Attribute {
                  public ImplKind Kind { get; }
                  public Type? BaseType { get; }

                  public ImplTypeAttribute(ImplKind kind) {
                      Kind = kind;
                  }

                  public ImplTypeAttribute(Type baseType) {
                      Kind = ImplKind.ReferenceType;
                      BaseType = baseType;
                  }
              }
          }

          namespace System.Runtime.CompilerServices {
              internal static class IsExternalInit {}
          }

          namespace TestNamespace
          {
              {{testSnippet}}
          }
          """;

    [Fact]
    public async Task TestValidReferenceType() {
        var test =
            """
            [ImplType(ImplKind.ReferenceType)]
            public interface ITest {}

            public class TestClass : ITest {}
            """;

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestInvalidReferenceType() {
        var test =
            """
            [ImplType(ImplKind.ReferenceType)]
            public interface ITest {}

            public struct {|#0:TestStruct|} : ITest {}
            """;

        var expected = VerifyCS.Diagnostic(Rules.RefVal.Id)
            .WithLocation(0)
            .WithArguments("TestStruct", "reference type (class)", "ITest", "ReferenceType");

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestValidValueType() {
        var test =
            """
            [ImplType(ImplKind.ValueType)]
            public interface ITest {}

            public struct TestStruct : ITest {}
            """;

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestInvalidValueType() {
        var test =
            """
            [ImplType(ImplKind.ValueType)]
            public interface ITest {}

            public class {|#0:TestClass|} : ITest {}
            """;

        var expected = VerifyCS.Diagnostic(Rules.RefVal.Id)
            .WithLocation(0)
            .WithArguments("TestClass", "value type (struct)", "ITest", "ValueType");

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestValidBaseType() {
        var test =
            """
            public class MyBase {}

            [ImplType(typeof(MyBase))]
            public interface ITest {}

            public class TestClass : MyBase, ITest {}
            """;

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestInvalidBaseType() {
        var test =
            """
            public class MyBase {}
            public class OtherBase {}

            [ImplType(typeof(MyBase))]
            public interface ITest {}

            public class {|#0:TestClass|} : OtherBase, ITest {}
            """;

        var expected = VerifyCS.Diagnostic(Rules.Type.Id)
            .WithLocation(0)
            .WithArguments("TestClass", "MyBase", "ITest");

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestBaseTypeImpliesReferenceType() {
        var test =
            """
            public class MyBase {}

            [ImplType(typeof(MyBase))]
            public interface ITest {}

            public struct {|#0:TestStruct|} : ITest {}
            """;

        var expected = VerifyCS.Diagnostic(Rules.RefVal.Id)
            .WithLocation(0)
            .WithArguments("TestStruct", "reference type (class)", "ITest", "ReferenceType");

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestValidValueTypeNew() {
        var test =
            """
            [ImplType(ImplKind.ReferenceTypeNew)]
            public interface ITest {}

            public class TestClass : ITest {}
            """;

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestValidValueTypeNewExplicitCtor() {
        var test =
            """
            [ImplType(ImplKind.ReferenceTypeNew)]
            public interface ITest {}

            public class TestClass : ITest {
                public TestClass() {}
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestInvalidValueTypeNew_Struct() {
        var test =
            """
            [ImplType(ImplKind.ReferenceTypeNew)]
            public interface ITest {}

            public struct {|#0:TestStruct|} : ITest {}
            """;

        var expected = VerifyCS.Diagnostic(Rules.RefVal.Id)
            .WithLocation(0)
            .WithArguments("TestStruct", "reference type (class)", "ITest", "ReferenceTypeNew");

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestInvalidValueTypeNew_NoParameterlessCtor() {
        var test =
            """
            [ImplType(ImplKind.ReferenceTypeNew)]
            public interface ITest {}

            public class {|#0:TestClass|} : ITest {
                public TestClass(int i) {}
            }
            """;

        var expected = VerifyCS.Diagnostic(Rules.Constructor.Id)
            .WithLocation(0)
            .WithArguments("TestClass", "ITest");

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestInvalidValueTypeNew_PrivateCtor() {
        var test =
            """
            [ImplType(ImplKind.ReferenceTypeNew)]
            public interface ITest {}

            public class {|#0:TestClass|} : ITest {
                private TestClass() {}
            }
            """;

        var expected = VerifyCS.Diagnostic(Rules.Constructor.Id)
            .WithLocation(0)
            .WithArguments("TestClass", "ITest");

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestValidReferenceTypeRecord() {
        var test =
            """
            [ImplType(ImplKind.ReferenceType)]
            public interface ITest {}

            public record TestRecord : ITest {}
            """;

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestInvalidReferenceTypeRecord() {
        var test =
            """
            [ImplType(ImplKind.ReferenceType)]
            public interface ITest {}

            public record struct {|#0:TestRecordStruct|} : ITest {}
            """;

        var expected = VerifyCS.Diagnostic(Rules.RefVal.Id)
            .WithLocation(0)
            .WithArguments("TestRecordStruct", "reference type (class)", "ITest", "ReferenceType");

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestValidValueTypeRecord() {
        var test =
            """
            [ImplType(ImplKind.ValueType)]
            public interface ITest {}

            public record struct TestRecordStruct : ITest {}
            """;

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestInvalidValueTypeRecord() {
        var test =
            """
            [ImplType(ImplKind.ValueType)]
            public interface ITest {}

            public record {|#0:TestRecord|} : ITest {}
            """;

        var expected = VerifyCS.Diagnostic(Rules.RefVal.Id)
            .WithLocation(0)
            .WithArguments("TestRecord", "value type (struct)", "ITest", "ValueType");

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestValidReferenceTypeNewRecord() {
        var test =
            """
            [ImplType(ImplKind.ReferenceTypeNew)]
            public interface ITest {}

            public record TestRecord : ITest {}
            """;

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestInvalidReferenceTypeNewRecord_NoParameterlessCtor() {
        var test =
            """
            [ImplType(ImplKind.ReferenceTypeNew)]
            public interface ITest {}

            public record {|#0:TestRecord|}(int X) : ITest {}
            """;

        var expected = VerifyCS.Diagnostic(Rules.Constructor.Id)
            .WithLocation(0)
            .WithArguments("TestRecord", "ITest");

        await VerifyCS.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestMetadataGenericInterface() {
        const string librarySource =
            """
            using System;

            namespace Implyzer {
                public enum ImplKind {
                    ReferenceType,
                    ValueType,
                    ReferenceTypeNew
                }

                [AttributeUsage(AttributeTargets.Interface)]
                public class ImplTypeAttribute : Attribute {
                    public ImplKind Kind { get; }
                    public Type? BaseType { get; }

                    public ImplTypeAttribute(ImplKind kind) {
                        Kind = kind;
                    }

                    public ImplTypeAttribute(Type baseType) {
                        Kind = ImplKind.ReferenceType;
                        BaseType = baseType;
                    }
                }
            }

            namespace LibraryNamespace {
                [Implyzer.ImplType(Implyzer.ImplKind.ValueType)]
                public interface ITest<T> {}
            }
            """;

        var testCode =
            """
            using LibraryNamespace;

            namespace TestNamespace {
                public struct TestStruct : ITest<int> {}
            }
            """;

        var test = new CSharpAnalyzerTest<ImplTypeAnalyzer, DefaultVerifier> {
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

        await test.RunAsync();
    }

    [Fact]
    public async Task TestMetadataGenericInterfaceInvalid() {
        const string librarySource =
            """
            using System;

            namespace Implyzer {
                public enum ImplKind {
                    ReferenceType,
                    ValueType,
                    ReferenceTypeNew
                }

                [AttributeUsage(AttributeTargets.Interface)]
                public class ImplTypeAttribute : Attribute {
                    public ImplKind Kind { get; }
                    public Type? BaseType { get; }

                    public ImplTypeAttribute(ImplKind kind) {
                        Kind = kind;
                    }

                    public ImplTypeAttribute(Type baseType) {
                        Kind = ImplKind.ReferenceType;
                        BaseType = baseType;
                    }
                }
            }

            namespace LibraryNamespace {
                [Implyzer.ImplType(Implyzer.ImplKind.ValueType)]
                public interface ITest<T> {}
            }
            """;

        var testCode =
            """
            using LibraryNamespace;

            namespace TestNamespace {
                public class {|#0:TestClass|} : ITest<int> {}
            }
            """;

        var test = new CSharpAnalyzerTest<ImplTypeAnalyzer, DefaultVerifier> {
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

        var expected = VerifyCS.Diagnostic(Rules.RefVal.Id)
            .WithLocation(0)
            .WithArguments("TestClass", "value type (struct)", "ITest", "ValueType");

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }
}