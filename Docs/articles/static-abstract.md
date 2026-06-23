# `static abstract` Feature Simulation

Implyzer allows for the simulation of the `static abstract` interface members feature (which was added natively in C# 11 / .NET 7) on older target frameworks.

This feature is enabled by applying the `StaticAbstractAttribute` to a partial interface. Implyzer will generate a companion static helper class that automatically routes static method calls to the correct registered implementations.

---

## 1 The `StaticAbstractAttribute` Attribute

The `StaticAbstractAttribute` constructor has the following signatures:

```csharp
// 1. For target frameworks supporting Default Interface Methods (DIM):
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

For every static abstract simulation, Implyzer generates two routing overloads on the companion helper class.

### 3.1 Generic Invocation Overload

Invokes the registered implementation using a generic type parameter:

```csharp
// Signature: public static bool TryParse<T>(string input, out T result)
var success = IParser.TryParse<Color>("red", out var color);
```

### 3.2 Non-Generic (Type-Based) Invocation Overload

Invokes the registered implementation dynamically using a `System.Type` parameter. This is useful for reflection-heavy codebases, runtime factories, or deserializers where the type is only known at runtime:

```csharp
// Signature: public static bool TryParse(Type type, string input, out object? result)
var success = IParser.TryParse(typeof(Color), "red", out var colorObj);
```

---

## 4 Behind the Scenes

### 4.1 Implementation Registration
Implyzer automatically discovers implementing types at compile-time and generates a `ModuleInitializer` method. This initializer registers all implementing types with the static method registry helper upon assembly loading, preventing any manual setup.

### 4.2 Dynamic Overload Routing
The non-generic overload internally utilizes `DynamicInvoke`. It automatically instantiates the parameter arrays, performs the execution on the registered delegate, and copies any changes made to `out` or `ref` parameters back to the caller seamlessly.