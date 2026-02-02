using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Implyzer.Tests;

public static class VerifyCS {
    public static DiagnosticResult Diagnostic(string diagnosticId)
        => CSharpAnalyzerVerifier<ImplTypeAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);

    public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected) {
        var test = new CSharpAnalyzerTest<ImplTypeAnalyzer, DefaultVerifier> {
            TestCode = source,
        };

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}

public class ImplTypeAnalyzerTests {
    private static string CreateTestSource(string testSnippet) {
        return $$"""
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

               namespace TestNamespace
               {
                   {{testSnippet}}
               }
               """;
    }

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
}