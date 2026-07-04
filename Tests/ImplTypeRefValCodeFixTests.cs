using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Implyzer.Tests;

public static class VerifyRefValFix {
    public static async Task VerifyCodeFixAsync(string source, string fixedSource, params DiagnosticResult[] expected) {
        var test = new CSharpCodeFixTest<ImplTypeAnalyzer, ImplTypeRefValCodeFixProvider, DefaultVerifier> {
            TestCode  = source,
            FixedCode = fixedSource
        };

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    public static DiagnosticResult Diagnostic(string diagnosticId) => CSharpAnalyzerVerifier<ImplTypeAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);
}

public class ImplTypeRefValCodeFixTests {
    private static string CreateTestSource(string testSnippet) {
        var source = $$"""
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

                               public ImplTypeAttribute(ImplKind kind) {
                                   Kind = kind;
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

        return source.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }

    [Fact]
    public async Task TestChangeClassToStruct() {
        var test =
            """
                [ImplType(ImplKind.ValueType)]
                public interface ITest {}

                public class {|#0:TestClass|} : ITest {}
            """;

        var fixedTest =
            """
                [ImplType(ImplKind.ValueType)]
                public interface ITest {}

                public struct TestClass : ITest {}
            """;

        var expected = VerifyRefValFix.Diagnostic(Rules.RefVal.Id)
            .WithLocation(0)
            .WithArguments("TestClass", "value type (struct)", "ITest", "ValueType");

        await VerifyRefValFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), expected);
    }

    [Fact]
    public async Task TestChangeStructToClass() {
        var test =
            """
                [ImplType(ImplKind.ReferenceType)]
                public interface ITest {}

                public struct {|#0:TestStruct|} : ITest {}
            """;

        var fixedTest =
            """
                [ImplType(ImplKind.ReferenceType)]
                public interface ITest {}

                public class TestStruct : ITest {}
            """;

        var expected = VerifyRefValFix.Diagnostic(Rules.RefVal.Id)
            .WithLocation(0)
            .WithArguments("TestStruct", "reference type (class)", "ITest", "ReferenceType");

        await VerifyRefValFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), expected);
    }

    [Fact]
    public async Task TestChangeRecordToRecordStruct() {
        var test =
            """
                [ImplType(ImplKind.ValueType)]
                public interface ITest {}

                public record {|#0:TestRecord|} : ITest {}
            """;

        var fixedTest =
            """
                [ImplType(ImplKind.ValueType)]
                public interface ITest {}

                public record struct TestRecord : ITest {}
            """;

        var expected = VerifyRefValFix.Diagnostic(Rules.RefVal.Id)
            .WithLocation(0)
            .WithArguments("TestRecord", "value type (struct)", "ITest", "ValueType");

        await VerifyRefValFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), expected);
    }

    [Fact]
    public async Task TestChangeRecordStructToRecord() {
        var test =
            """
                [ImplType(ImplKind.ReferenceType)]
                public interface ITest {}

                public record struct {|#0:TestRecordStruct|} : ITest {}
            """;

        var fixedTest =
            """
                [ImplType(ImplKind.ReferenceType)]
                public interface ITest {}

                public record TestRecordStruct : ITest {}
            """;

        var expected = VerifyRefValFix.Diagnostic(Rules.RefVal.Id)
            .WithLocation(0)
            .WithArguments("TestRecordStruct", "reference type (class)", "ITest", "ReferenceType");

        await VerifyRefValFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), expected);
    }
}