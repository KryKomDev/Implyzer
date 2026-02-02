using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Implyzer.Tests;

public static class VerifyConstructorFix {
    public static async Task VerifyCodeFixAsync(string source, string fixedSource, params DiagnosticResult[] expected) {
        var test = new CSharpCodeFixTest<ImplTypeAnalyzer, ImplTypeConstructorCodeFixProvider, DefaultVerifier> {
            TestCode = source,
            FixedCode = fixedSource,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
    
    public static DiagnosticResult Diagnostic(string diagnosticId)
            => CSharpAnalyzerVerifier<ImplTypeAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);
}

public class ImplTypeConstructorCodeFixTests {
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

               namespace TestNamespace
               {
               {{testSnippet}}
               }
               """;
        return source.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }

    [Fact]
    public async Task TestAddConstructor() {
        var test = 
            """
                [ImplType(ImplKind.ReferenceTypeNew)]
                public interface ITest {}

                public class {|#0:TestClass|} : ITest {
                    public TestClass(int i) {}
                }
            """;
        
        var fixedTest = 
            """
                [ImplType(ImplKind.ReferenceTypeNew)]
                public interface ITest {}

                public class TestClass : ITest {
                    public TestClass()
                    {
                    }

                    public TestClass(int i) {}
                }
            """;
        
        var expected = VerifyConstructorFix.Diagnostic(Rules.Constructor.Id)
            .WithLocation(0)
            .WithArguments("TestClass", "ITest");

        await VerifyConstructorFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), expected);
    }

    [Fact]
    public async Task TestMakeConstructorPublic() {
        var test = 
            """
                [ImplType(ImplKind.ReferenceTypeNew)]
                public interface ITest {}

                public class {|#0:TestClass|} : ITest {
                    private TestClass() {}
                }
            """;
        
        var fixedTest = 
            """
                [ImplType(ImplKind.ReferenceTypeNew)]
                public interface ITest {}

                public class TestClass : ITest {
                    public TestClass() {}
                }
            """;
        
        var expected = VerifyConstructorFix.Diagnostic(Rules.Constructor.Id)
            .WithLocation(0)
            .WithArguments("TestClass", "ITest");

        await VerifyConstructorFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), expected);
    }

    [Fact]
    public async Task TestAddConstructorWithStaticConstructor() {
        var test = 
            """
                [ImplType(ImplKind.ReferenceTypeNew)]
                public interface ITest {}

                public class {|#0:TestClass|} : ITest {
                    static TestClass() {}
                    public TestClass(int i) {}
                }
            """;
        
        var fixedTest = 
            """
                [ImplType(ImplKind.ReferenceTypeNew)]
                public interface ITest {}

                public class TestClass : ITest {
                    public TestClass()
                    {
                    }

                    static TestClass() {}
                    public TestClass(int i) {}
                }
            """;
        
        var expected = VerifyConstructorFix.Diagnostic(Rules.Constructor.Id)
            .WithLocation(0)
            .WithArguments("TestClass", "ITest");

        await VerifyConstructorFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixedTest), expected);
    }
}
