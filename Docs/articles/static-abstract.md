# `static abstract` Interface Support & Simulation

Implyzer brings `static abstract` interface members to all .NET target frameworks with automatic optimization for modern C#.

This feature is enabled by applying the `StaticAbstractAttribute` to a partial interface. Implyzer generates a companion static helper class that routes static method calls to the appropriate implementations—using **native C# 11 `static abstract` dispatch** on modern .NET, and seamless **simulation mode** on older target frameworks.

---

## 1 The `StaticAbstractAttribute` Attribute

The `StaticAbstractAttribute` constructor has the following signatures:

```csharp
// 1. For target frameworks supporting Default Interface Methods or native static abstract members:
public StaticAbstractAttribute(
    string methodName, 
    Type signature, 
    params string[] typeParams
);

// 2. For target frameworks requiring a separate target class (e.g. .NET Standard 2.0):
public StaticAbstractAttribute(
    string methodName, 
    Type signature, 
    Type targetClass, 
    params string[] typeParams
);
```

### 1.1 Constructor Parameters

-   **`methodName`**: The name of the static method to be implemented.
-   **`signature`**: The type of a delegate that represents the signature of the method (e.g. `typeof(TryParse<>)`).
-   **`targetClass`** (Optional): Configures which partial class the helper methods will be generated in.
    > [!IMPORTANT]
    > When targeting `.NET Standard 2.0` (which does not support Default Interface Methods), the `targetClass` parameter must be provided, and that target class must be marked as `partial`.
    > For newer target frameworks, this parameter is omitted, and helper methods are generated directly within a companion static class named after the partial interface.
-   **`typeParams`** (Optional): A variable-length array of string pairs (`params string[]`) that maps the generic type parameters of the interface to the generic type parameters of the delegate (e.g., `"TSelf", "T"`).

---

## 2 Example Usage

### 2.1 Interface Decoration

```csharp
using Implyzer;

public delegate bool TryParse<T>(string input, out T? result); 

// Decorating the interface: TryParse method, TryParse delegate signature, mapping TSelf to T
[StaticAbstract(nameof(TryParse), typeof(TryParse<>), "TSelf", "T")]
public partial interface IParser<TSelf> where TSelf : IParser<TSelf> { }
```

### 2.2 Providing the Implementation

Classes implementing the interface must implement a matching public static method:

```csharp
public class Color : IParser<Color> 
{
    public static bool TryParse(string input, out Color? result)
    {
        if (input == "red")
        {
            result = new Color();
            return true;
        }
        result = null;
        return false;
    }
}
```

---

## 3 Invoking Generated Static Methods

For every `StaticAbstract` declaration, Implyzer generates two routing overloads on the companion helper class.

### 3.1 Generic Invocation Overload

Invokes the static method using a generic type parameter:

```csharp
// Signature: public static bool TryParse<T>(string input, out T result) where T : IParser<T>
var success = IParser.TryParse<Color>("red", out var color);
```

### 3.2 Non-Generic (Type-Based) Invocation Overload

Invokes the static method dynamically using a `System.Type` parameter. This is useful for reflection-heavy codebases, runtime factories, or deserializers where the type is only known at runtime:

```csharp
// Signature: public static bool TryParse(Type type, string input, out object? result)
var success = IParser.TryParse(typeof(Color), "red", out var colorObj);
```

---

## 4 Behind the Scenes

Implyzer automatically adapts its code generation based on the compiler's language version and target framework runtime capabilities.

### 4.1 Native C# 11+ Execution (.NET 7+)

When compiling with C# 11+ on a runtime supporting virtual statics in interfaces:
- **Interface Declarations**: Implyzer generates native `public static abstract` member declarations directly inside the partial interface.
- **Generic Invocations**: Generic helper calls like `IParser.TryParse<T>(...)` invoke `T.TryParse(...)` directly with **zero dictionary lookup or delegate allocation overhead**.
- **Non-Generic Invocations**: Overloads accepting a `System.Type` parameter use reflection (`type.GetMethods(...)`) to locate and invoke the static method dynamically at runtime.

### 4.2 Legacy Target Simulation (C# < 11 / .NET Standard 2.0)

When targeting older frameworks where native `static abstract` interface members are unavailable:
- **Implementation Registration**: Implyzer discovers implementing types at compile-time and generates a `[ModuleInitializer]` method to register implementations into a static registry (`Dictionary<Type, Delegate>`) upon assembly loading.
- **Dynamic Overload Routing**: Generic overloads retrieve delegates from the registry, and non-generic overloads utilize `DynamicInvoke` to execute static methods at runtime.