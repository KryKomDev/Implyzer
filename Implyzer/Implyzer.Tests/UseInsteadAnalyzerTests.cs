using System.Threading.Tasks;
using Implyzer.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Implyzer.Tests;

public static class VerifyCSUseInstead {
    public static DiagnosticResult Diagnostic(string diagnosticId)
        => CSharpAnalyzerVerifier<UseInsteadAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);

    public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected) {
        var test = new CSharpAnalyzerTest<UseInsteadAnalyzer, DefaultVerifier> {
            TestCode = source,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}

public static class VerifyCSUseInsteadFix {
    public static async Task VerifyCodeFixAsync(string source, string fixedSource, params DiagnosticResult[] expected) {
        var test = new CSharpCodeFixTest<UseInsteadAnalyzer, UseInsteadCodeFixProvider, DefaultVerifier> {
            TestCode = source,
            FixedCode = fixedSource,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}

public class UseInsteadAnalyzerTests {
    private static string CreateTestSource(string testSnippet) {
        return $$"""
               using System;
               using Implyzer;

               namespace Implyzer {
                   [AttributeUsage(AttributeTargets.All)]
                   public class UseInsteadAttribute : Attribute {
                       public Type? ReplacementType { get; set; }
                       public string? MemberName { get; set; }
                       public Type[]? ParameterTypes { get; set; }
                       public string? ReplacementString { get; set; }

                       public UseInsteadAttribute(Type replacementType) {
                           ReplacementType = replacementType;
                       }

                       public UseInsteadAttribute(Type replacementType, string memberName) {
                           ReplacementType = replacementType;
                           MemberName = memberName;
                       }

                       public UseInsteadAttribute(Type replacementType, Type[] parameterTypes) {
                           ReplacementType = replacementType;
                           ParameterTypes = parameterTypes;
                       }

                       public UseInsteadAttribute(string replacement) {
                           ReplacementString = replacement;
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
    public async Task TestNoAttribute() {
        var test = 
            """
            public class TestClass {
                public void Method() {}
            }

            public class Usage {
                public void Run() {
                    new TestClass().Method();
                }
            }
            """;
        
        await VerifyCSUseInstead.VerifyAnalyzerAsync(CreateTestSource(test));
    }

    [Fact]
    public async Task TestMethodWithStringReplacement() {
        var test = 
            """
            public class TestClass {
                [UseInstead("BetterMethod")]
                public void OldMethod() {}

                public void BetterMethod() {}
            }

            public class Usage {
                public void Run() {
                    var c = new TestClass();
                    c.{|#0:OldMethod|}();
                }
            }
            """;
        
        var expected = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id)
            .WithLocation(0)
            .WithArguments("BetterMethod", "OldMethod");

        await VerifyCSUseInstead.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestMethodWithTypeReplacement() {
        var test = 
            """
            public class NewClass {}

            public class TestClass {
                [UseInstead(typeof(NewClass))]
                public void OldMethod() {}
            }

            public class Usage {
                public void Run() {
                    new TestClass().{|#0:OldMethod|}();
                }
            }
            """;
        
        var expected = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id)
            .WithLocation(0)
            .WithArguments("NewClass", "OldMethod");

        await VerifyCSUseInstead.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestPropertyUsage() {
        var test = 
            """
            public class TestClass {
                [UseInstead("NewProp")]
                public int OldProp { get; set; }
            }

            public class Usage {
                public void Run() {
                    var c = new TestClass();
                    var x = c.{|#0:OldProp|};
                    c.{|#1:OldProp|} = 5;
                }
            }
            """;
        
        // Expecting two diagnostics, one for getter, one for setter usage
        var expected1 = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id).WithLocation(0).WithArguments("NewProp", "OldProp");
        var expected2 = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id).WithLocation(1).WithArguments("NewProp", "OldProp");

        await VerifyCSUseInstead.VerifyAnalyzerAsync(CreateTestSource(test), expected1, expected2);
    }

    [Fact]
    public async Task TestConstructorUsage() {
        var test = 
            """
            public class TestClass {
                [UseInstead("NewClass")]
                public TestClass() {}
            }

            public class Usage {
                public void Run() {
                    var c = new {|#0:TestClass|}();
                }
            }
            """;
        
        var expected = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id)
            .WithLocation(0)
            .WithArguments("NewClass", ".ctor");

        await VerifyCSUseInstead.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    // Extended Tests
    [Fact]
    public async Task TestClassAttributeOnConstructorCall() {
        var test = 
            """
            public class NewClass {}

            [UseInstead(typeof(NewClass))]
            public class OldClass {}

            public class Usage {
                public void Run() {
                    var c = new {|#0:OldClass|}();
                }
            }
            """;
        
        var expected = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id)
            .WithLocation(0)
            .WithArguments("NewClass", "OldClass");

        await VerifyCSUseInstead.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    // Member Ref Tests
    [Fact]
    public async Task TestTypeAndMemberName() {
        var test = 
            """
            public class NewClass {
                public static void NewMethod() {}
            }

            public class OldClass {
                [UseInstead(typeof(NewClass), "NewMethod")]
                public void OldMethod() {}
            }

            public class Usage {
                public void Run() {
                    new OldClass().{|#0:OldMethod|}();
                }
            }
            """;
        
        var expected = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id)
            .WithLocation(0)
            .WithArguments("NewClass.NewMethod", "OldMethod");

        await VerifyCSUseInstead.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestNamedArgumentMemberName() {
        var test = 
            """
            public class NewClass {
                 public static void NewMethod() {}
            }

            public class OldClass {
                [UseInstead(typeof(NewClass), MemberName = "NewMethod")]
                public void OldMethod() {}
            }

            public class Usage {
                public void Run() {
                    new OldClass().{|#0:OldMethod|}();
                }
            }
            """;
        
        var expected = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id)
            .WithLocation(0)
            .WithArguments("NewClass.NewMethod", "OldMethod");

        await VerifyCSUseInstead.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    // Constructor Ref Tests
    [Fact]
    public async Task TestConstructorWithParameters() {
        var test = 
            """
            public class NewClass {
                public NewClass(int i) {}
            }

            public class OldClass {
                [UseInstead(typeof(NewClass), new[] { typeof(int) })]
                public OldClass() {}
            }

            public class Usage {
                public void Run() {
                    new {|#0:OldClass|}();
                }
            }
            """;
        
        var expected = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id)
            .WithLocation(0)
            .WithArguments("NewClass(int)", ".ctor");

        await VerifyCSUseInstead.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestConstructorWithMultipleParameters() {
        var test = 
            """
            public class NewClass {
                public NewClass(int i, string s) {}
            }

            public class OldClass {
                [UseInstead(typeof(NewClass), new[] { typeof(int), typeof(string) })]
                public OldClass() {}
            }

            public class Usage {
                public void Run() {
                    new {|#0:OldClass|}();
                }
            }
            """;
        
        var expected = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id)
            .WithLocation(0)
            .WithArguments("NewClass(int, string)", ".ctor");

        await VerifyCSUseInstead.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    [Fact]
    public async Task TestConstructorWithNamedArgument() {
        var test = 
            """
            public class NewClass {
                public NewClass(int i) {}
            }

            public class OldClass {
                [UseInstead(typeof(NewClass), ParameterTypes = new[] { typeof(int) })]
                public OldClass() {}
            }

            public class Usage {
                public void Run() {
                    new {|#0:OldClass|}();
                }
            }
            """;
        
        var expected = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id)
            .WithLocation(0)
            .WithArguments("NewClass(int)", ".ctor");

        await VerifyCSUseInstead.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }
    
    [Fact]
    public async Task TestConstructorWithEmptyParameters() {
         var test = 
             """
             public class NewClass {
                 public NewClass() {}
             }
 
             public class OldClass {
                 [UseInstead(typeof(NewClass), new Type[0])]
                 public OldClass() {}
             }
 
             public class Usage {
                 public void Run() {
                     new {|#0:OldClass|}();
                 }
             }
             """;
         
         var expected = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id)
             .WithLocation(0)
             .WithArguments("NewClass()", ".ctor");
 
         await VerifyCSUseInstead.VerifyAnalyzerAsync(CreateTestSource(test), expected);
    }

    // Code Fix Tests
    [Fact]
    public async Task TestSimpleTypeReplacement() {
        var test = 
            """
            public class NewClass {}

            [UseInstead(typeof(NewClass))]
            public class OldClass {}

            public class Usage {
                public void Run() {
                    var c = new {|#0:OldClass|}();
                }
            }
            """;
            
        var fixtest = 
            """
            public class NewClass {}

            [UseInstead(typeof(NewClass))]
            public class OldClass {}

            public class Usage {
                public void Run() {
                    var c = new NewClass();
                }
            }
            """;
        
        var expected = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id)
            .WithLocation(0)
            .WithArguments("NewClass", "OldClass");
        
        await VerifyCSUseInsteadFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixtest), expected);
    }

    [Fact]
    public async Task TestMemberReplacement() {
        var test = 
            """
            public class TestClass {
                [UseInstead("NewMethod")]
                public void OldMethod() {}

                public void NewMethod() {}
            }

            public class Usage {
                public void Run() {
                    new TestClass().{|#0:OldMethod|}();
                }
            }
            """;
            
        var fixtest = 
            """
            public class TestClass {
                [UseInstead("NewMethod")]
                public void OldMethod() {}

                public void NewMethod() {}
            }

            public class Usage {
                public void Run() {
                    new TestClass().NewMethod();
                }
            }
            """;
        
        var expected = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id)
            .WithLocation(0)
            .WithArguments("NewMethod", "OldMethod");
        
        await VerifyCSUseInsteadFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixtest), expected);
    }

    [Fact]
    public async Task TestConstructorReplacement() {
        var test = 
            """
            public class NewClass {
                public NewClass() {}
                public NewClass(int i) {}
            }

            public class OldClass {
                [UseInstead(typeof(NewClass), new[] { typeof(int) })]
                public OldClass() {}
            }

            public class Usage {
                public void Run() {
                    new {|#0:OldClass|}();
                }
            }
            """;
            
        var fixtest = 
            """
            public class NewClass {
                public NewClass() {}
                public NewClass(int i) {}
            }

            public class OldClass {
                [UseInstead(typeof(NewClass), new[] { typeof(int) })]
                public OldClass() {}
            }

            public class Usage {
                public void Run() {
                    new NewClass();
                }
            }
            """;
        
        var expected = VerifyCSUseInstead.Diagnostic(Rules.UseInstead.Id)
            .WithLocation(0)
            .WithArguments("NewClass(int)", ".ctor");
        
        await VerifyCSUseInsteadFix.VerifyCodeFixAsync(CreateTestSource(test), CreateTestSource(fixtest), expected);
    }
}