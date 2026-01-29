using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Implyzer.Tests;

public static class VerifyIndirectImpl {
    public static DiagnosticResult Diagnostic(string diagnosticId)
        => CSharpAnalyzerVerifier<IndirectImplAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);

    public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected) {
        var test = new CSharpAnalyzerTest<IndirectImplAnalyzer, DefaultVerifier> {
            TestCode = source,
        };

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}

public class IndirectImplAnalyzerTests {
    private static string CreateTestSource(string testSnippet) {
        return $$"""
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
    }

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
}
