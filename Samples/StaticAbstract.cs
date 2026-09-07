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
/// Defaults for ICustomParsable static virtual members.
/// </summary>
public static class CustomParsableDefaults {
    public static T Parse<T>([NotNullWhen(true)] string? input) {
        if (ICustomParsable.TryParse<T>(input, out var result)) {
            return result;
        }

        throw new FormatException($"Cannot parse '{input}' as {typeof(T).Name}");
    }
}

/// <summary>
/// Illustrates [StaticAbstract], [StaticVirtual], and [StaticRegister].
/// In C# 11+, emits native static abstract/virtual interface members with default implementations.
/// In C# &lt; 11, generates static companion helper class to route static calls to implementations with fallback.
/// [StaticRegister] allows duck typing for external/BCL types that match method/property signatures.
/// </summary>
[StaticAbstract("TryParse", typeof(TryParse<>), "TSelf", "T")]
[StaticVirtual("Parse", typeof(Parse<>), "TSelf", "T", DefaultType = typeof(CustomParsableDefaults), ImplementInTargetTypes = true)]
[StaticRegister(typeof(int), typeof(Guid))]
public partial interface ICustomParsable<TSelf> where TSelf : ICustomParsable<TSelf>;

// VALID: True implements ICustomParsable<True> and provides TryParse.
// Parse is omitted: it uses the default implementation provided by CustomParsableDefaults,
// and because ImplementInTargetTypes is true, True.Parse is generated directly on True!
public partial class True : ICustomParsable<True> {

    public static bool TryParse([NotNullWhen(true)] string? input, [MaybeNullWhen(false)] out True result) {
        if (input == "true") {
            result = new True();

            return true;
        }

        result = null;

        return false;
    }

    public override string ToString() => "true";
}

// VALID: False implements ICustomParsable<False> and provides TryParse.
// Parse is explicitly overridden with custom logic.
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
            : throw new ArgumentException($"Invalid false string: '{input}'");

    public override string ToString() => "false";
}

// VALID: Open generic type Box<T> implements ICustomParsable<Box<T>>.
// Implyzer's static abstract simulator registers and resolves open generic types seamlessly!
public partial class Box<T> : ICustomParsable<Box<T>> {
    public static bool TryParse([NotNullWhen(true)] string? input, [MaybeNullWhen(false)] out Box<T> result) {
        if (!string.IsNullOrEmpty(input)) {
            result = new Box<T>();
            return true;
        }

        result = null;
        return false;
    }

    public override string ToString() => $"Box<{typeof(T).Name}>";
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


        // === Option 3: Default implementation vs Overridden implementation ===

        // True uses the default implementation in CustomParsableDefaults.Parse:
        var trueParsed = ICustomParsable.Parse<True>("true");
        Console.WriteLine($"Default Parse ('true' -> Implyzer.Sample.True): {trueParsed}");

        // Direct target type method call (generated because ImplementInTargetTypes = true):
        var trueDirect = True.Parse("true");
        Console.WriteLine($"Direct target type Parse ('true' -> Implyzer.Sample.True): {trueDirect}");

        // False uses its explicit override:
        var falseParsed = ICustomParsable.Parse<False>("false");
        Console.WriteLine($"Overridden Parse ('false' -> Implyzer.Sample.False): {falseParsed}");


        // === Option 4: Open generic type support in static abstract members & simulator ===

        var boxGenericSuccess = ICustomParsable.TryParse<Box<int>>("box", out var boxIntResult);
        Console.WriteLine($"Open generic TryParse ('box' -> Box<int>): {boxGenericSuccess}, Result: {boxIntResult}");

        var boxNonGenericSuccess = ICustomParsable.TryParse(typeof(Box<string>), "box", out var boxStringResult);
        Console.WriteLine($"Open generic non-generic TryParse ('box' -> Box<string>): {boxNonGenericSuccess}, Result: {boxStringResult}");


        // === Option 5: External BCL type registration via [StaticRegister] (Duck Typing) ===

        // Generic call for int (System.Int32):
        var intSuccess = ICustomParsable.TryParse<int>("42", out var intResult);
        Console.WriteLine($"Registered BCL TryParse ('42' -> int): {intSuccess}, Result: {intResult}");

        var intParsed = ICustomParsable.Parse<int>("420");
        Console.WriteLine($"Registered BCL Parse ('420' -> int): {intParsed}");

        // Non-generic call for Guid (System.Guid):
        var sampleGuidStr = "d3b07384-d113-4f01-9b16-92c25df60e22";
        var guidSuccess = ICustomParsable.TryParse(typeof(Guid), sampleGuidStr, out var guidObjResult);
        Console.WriteLine($"Registered BCL non-generic TryParse ('{sampleGuidStr}' -> Guid): {guidSuccess}, Result: {guidObjResult}");
    }
}