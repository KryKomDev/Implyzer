using System;
using System.Diagnostics.CodeAnalysis;

namespace Implyzer.Sample;

/// <summary>
/// Represents a signature for a static abstract method.
/// </summary>
public delegate bool TryParse<T>([NotNullWhen(true)] string? input, [MaybeNullWhen(false)] out T result);

/// <summary>
/// Represents a delegate for parsing a string input into an object of the specified type.
/// </summary>
/// <typeparam name="T">The type of the object to be parsed from the string input.</typeparam>
public delegate T Parse<T>([NotNullWhen(true)] string? input);

/// <summary>
/// Illustrates [StaticAbstract]. Simulates C# 11 static abstract interface members.
/// Implyzer generates static companion helper class IParser to route static calls to registered implementations.
/// </summary>public delegate bool TryParse{T}([NotNullWhen(true)] string? input, [MaybeNullWhen(false)] out T result);
[StaticAbstract("TryParse", typeof(TryParse<>), "TSelf", "T")]
[StaticAbstract("Parse",    typeof(Parse<>),    "TSelf", "T")]
public partial interface ICustomParsable<TSelf> where TSelf : ICustomParsable<TSelf>?;

// VALID: True implements ICustomParsable<True> and provides the matching static method.
public class True : ICustomParsable<True> {

    public static bool TryParse([NotNullWhen(true)] string? input, [MaybeNullWhen(false)] out True result) {
        if (input == "true") {
            result = new True();

            return true;
        }

        result = null;

        return false;
    }

    public static True Parse([NotNullWhen(true)] string? input) =>
        TryParse(input, out var result)
            ? result
            : throw new ArgumentException();

    public override string ToString() => "true";
}

// VALID: False implements ICustomParsable<False> and provides the matching static method.
public class False : ICustomParsable<False> {

    public static bool TryParse([NotNullWhen(true)] string? input, [MaybeNullWhen(false)] out False result) {
        if (input == "false") {
            result = new False();

            return true;
        }

        result = null;

        return false;
    }

    public static False Parse([NotNullWhen(true)] string? input) =>
        TryParse(input, out var result)
            ? result
            : throw new ArgumentException();

    public override string ToString() => "false";
}

// INVALID: Dog implements ICustomParsable<Dog> but does NOT provide the static TryParse method.
// Uncomment to see analyzer error IMPL009:
//     "Type 'Dog' must implement public static method 'TryParse' matching signature of delegate
//     'TryParse<Dog>' because it implements interface 'IParser<Dog>'"
/*
public class Dog : ICustomParsable<Dog> {
    // Missing TryParse static method
}
*/

public class Program {
    public static void Main() {
        // === Option 1: Generic companion static method invocation ===

        // this will succeed
        var genericSuccess = ICustomParsable.TryParse<True>("true", out var genericTrueResult);
        Console.WriteLine($"Generic TryParse ('true' -> Implyzer.Sample.True): {genericSuccess}, Result: {genericTrueResult}");

        // this will fail
        var genericFail = ICustomParsable.TryParse<False>("no", out var genericFalseResult);
        Console.WriteLine($"Generic TryParse ('no' -> Implyzer.Sample.False): {genericFail}, Result: {genericFalseResult}");


        // === Option 2: Non-generic Type-based static method invocation (useful for dynamic/runtime resolving) ===

        // this will succeed
        var nonGenericSuccess = ICustomParsable.TryParse(typeof(True), "true", out var nonGenericTrueResult);
        Console.WriteLine($"Non-generic TryParse ('true' -> Implyzer.Sample.True): {nonGenericSuccess}, Result: {nonGenericTrueResult}");

        // this will fail
        var nonGenericFail = ICustomParsable.TryParse(typeof(False), "no", out var nonGenericFalseResult);
        Console.WriteLine($"Non-generic TryParse ('no' -> Implyzer.Sample.False): {nonGenericFail}, Result: {nonGenericFalseResult}");
    }
}