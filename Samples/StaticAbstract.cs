using System;

namespace Implyzer.Sample;

public delegate bool TryParse<T>(string input, out T? result);

/// <summary>
/// Illustrates [StaticAbstract]. Simulates C# 11 static abstract interface members.
/// Implyzer generates static companion helper class IParser to route static calls to registered implementations.
/// </summary>
[StaticAbstract("TryParse", typeof(TryParse<>), "TSelf", "T")]
public partial interface IParser<TSelf> where TSelf : IParser<TSelf>;

// VALID: Color implements IParser<Color> and provides the matching static method.
public class Color : IParser<Color> {
    public static bool TryParse(string input, out Color? result) {
        if (input == "red") {
            result = new Color();
            return true;
        }
        result = null;
        return false;
    }
}

// INVALID: Dog implements IParser<Dog> but does NOT provide the static TryParse method.
// Uncomment to see analyzer error IMPL009:
// "Type 'Dog' must implement public static method 'TryParse' matching signature of delegate 'TryParse<Dog>' because it implements interface 'IParser<Dog>'"
/*
public class Dog : IParser<Dog> {
    // Missing TryParse static method
}
*/

public class Program {
    public static void Main(string[] args) {
        // Option 1: Generic companion static method invocation
        var successGeneric = IParser.TryParse<Color>("red", out var colorGeneric);
        Console.WriteLine($"Generic TryParse: {successGeneric}, Result: {colorGeneric}");

        // Option 2: Non-generic Type-based static method invocation (useful for dynamic/runtime resolving)
        var successNonGeneric = IParser.TryParse(typeof(Color), "red", out var colorNonGeneric);
        Console.WriteLine($"Non-generic TryParse: {successNonGeneric}, Result: {colorNonGeneric}");
    }
}
