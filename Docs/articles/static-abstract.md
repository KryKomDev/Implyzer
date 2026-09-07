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

### 1.2 Named Properties for Default Implementations

-   **`DefaultType`** (`Type?`): Specifies the type (e.g., a static defaults class or another interface) containing the fallback static method implementation. If omitted, defaults to the interface itself.
-   **`DefaultMethod`** (`string?`): The name of the static default method on `DefaultType`. If omitted, defaults to `methodName`.
-   **`ImplementInTargetTypes`** (`bool`): When `true` (or via alias `GenerateInTargetTypes`), Implyzer generates static method implementations directly inside implementing types that do not explicitly provide one. Implementing types must be marked as `partial`.

### 1.3 `[StaticVirtual]` and `[StaticDefault]`

-   **`[StaticVirtualAttribute]`**: Derived from `[StaticAbstractAttribute]`, this attribute makes the intent explicit that a static member is virtual and has a default implementation. In C# 11+, Implyzer emits `public static virtual` on the interface.
-   **`[StaticDefaultAttribute]`**: Placed on a static method to explicitly mark it as the default implementation for a specific static abstract or virtual member name.

### 1.4 `[ImplementInTargetTypes]`

The `[ImplementInTargetTypes]` attribute can be applied at multiple levels to automatically generate static virtual method implementations directly inside implementing target types:
-   **Interface level**: Automatically enables generation for all static virtual methods of that interface.
-   **Target type level**: On a specific implementing `partial class` or `partial struct` to opt that type in.
-   **Assembly level**: Enforces target type implementations across the entire assembly:
    ```csharp
    [assembly: ImplementInTargetTypes]
    ```
You can also explicitly pass `false` (e.g. `[ImplementInTargetTypes(false)]`) to opt a specific interface or type out of an assembly-wide policy.

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

Classes implementing the interface must implement a matching public static method (unless a default implementation is available):

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

### 2.3 Default Implementations & Overrides

You can provide default implementations for static abstract members by specifying `DefaultType` (and optional `DefaultMethod`) or by using `[StaticVirtual]` and `[StaticDefault]`:

```csharp
public delegate T Parse<T>(string input);

public static class ParserDefaults
{
    public static T Parse<T>(string input) where T : IParser<T>
    {
        if (IParser.TryParse<T>(input, out var result))
            return result!;
        throw new FormatException($"Cannot parse '{input}'");
    }
}

[StaticAbstract(nameof(TryParse), typeof(TryParse<>), "TSelf", "T")]
[StaticVirtual(nameof(Parse), typeof(Parse<>), "TSelf", "T", DefaultType = typeof(ParserDefaults))]
public partial interface IParser<TSelf> where TSelf : IParser<TSelf> { }

// 1. Implementing class omits Parse (uses default implementation automatically):
public class Color : IParser<Color>
{
    public static bool TryParse(string input, out Color? result)
    {
        result = new Color();
        return true;
    }
}

// 2. Implementing class overrides Parse with custom logic:
public class CustomColor : IParser<CustomColor>
{
    public static bool TryParse(string input, out CustomColor? result)
    {
        result = new CustomColor();
        return true;
    }

    public static CustomColor Parse(string input) => new CustomColor();
}
```

### 2.4 Generating Implementations in Target Types

By default, static virtual methods with default implementations can be called through the interface (`TSelf.Parse(...)` in generic contexts) or via companion class helpers (`IParser.Parse<Color>(...)`).

If you want callers to be able to call the method directly on the implementing type itself (e.g. `Color.Parse("red")`), enable target type generation using `ImplementInTargetTypes = true` or `[ImplementInTargetTypes]`:

```csharp
[StaticAbstract(nameof(TryParse), typeof(TryParse<>), "TSelf", "T")]
[StaticVirtual(nameof(Parse), typeof(Parse<>), "TSelf", "T", DefaultType = typeof(ParserDefaults), ImplementInTargetTypes = true)]
public partial interface IParser<TSelf> where TSelf : IParser<TSelf> { }

// The target class must be marked as partial:
public partial class Color : IParser<Color>
{
    public static bool TryParse(string input, out Color? result)
    {
        result = new Color();
        return true;
    }
    // Color.Parse is generated automatically by Implyzer:
    // public static Color Parse(string input) => ParserDefaults.Parse<Color>(input);
}

// Can now be called directly on Color!
Color c = Color.Parse("red");
```

> [!NOTE]
> If a target type already defines an explicit method matching the static virtual signature, Implyzer will not generate a duplicate, avoiding CS0111 compiler errors.
> You can also use the IDE Code Fix / Refactoring (`Ctrl+.` / `Alt+Enter`) on any implementing class or struct to implement any static virtual method (or all at once) delegating directly to the default implementation. The fix is always accessible on implementing types until the method is already implemented.

---

## 3 Invoking Generated Static Methods

For every `StaticAbstract` declaration, Implyzer generates two routing overloads on the companion helper class.

### 3.1 Generic Invocation Overload

Invokes the static method using a generic type parameter:

```csharp
// Signature: public static bool TryParse<T>(string input, out T result) where T : IParser<T>
var success = IParser.TryParse<Color>("red", out var color);

// Invoking a method with default implementation:
var color = IParser.Parse<Color>("red"); // calls ParserDefaults.Parse<Color>
```

### 3.2 Non-Generic (Type-Based) Invocation Overload

Invokes the static method dynamically using a `System.Type` parameter. This is useful for reflection-heavy codebases, runtime factories, or deserializers where the type is only known at runtime:

```csharp
// Signature: public static bool TryParse(Type type, string input, out object? result)
var success = IParser.TryParse(typeof(Color), "red", out var colorObj);
```

---

## 4 Registering External & BCL Types (`[StaticRegister]`)

In C#, you cannot retroactively declare that an existing type (such as `int`, `Guid`, or a third-party library class) implements your interface. Implyzer solves this via **structural duck typing** using the `[StaticRegister]` attribute.

If a candidate type has public static methods or properties matching the signatures of all required static members of an interface, you can register it directly to the interface's static registry.

### 4.1 Interface-Level Registration

Decorate the interface directly with the types you want to register:

```csharp
[StaticAbstract("TryParse", typeof(TryParse<>), "TSelf", "T")]
[StaticVirtual("Parse", typeof(Parse<>), "TSelf", "T", DefaultType = typeof(ParserDefaults))]
[StaticRegister(typeof(int), typeof(Guid))]
public partial interface IParser<TSelf> where TSelf : IParser<TSelf>;
```

### 4.2 Assembly-Level Registration

For modular architectures or external interfaces, apply `[StaticRegister]` at the assembly level:

```csharp
// Using named TargetInterface property:
[assembly: StaticRegister(typeof(int), typeof(Guid), TargetInterface = typeof(IParser<>))]

// Or using positional syntax (target interface as first argument):
[assembly: StaticRegister(typeof(IParser<>), typeof(int), typeof(Guid))]
```

### 4.3 Relaxed Companion Constraints & Calling Registered Types

When external types are registered on an interface, Implyzer automatically relaxes the `where T : IParser<T>` constraint on the companion helper methods. This allows you to call companion methods with external types directly:

```csharp
// Generic invocation with BCL type (zero cast overhead):
bool success = IParser.TryParse<int>("42", out int intVal);

// Non-generic invocation with BCL type:
bool guidOk = IParser.TryParse(typeof(Guid), "d3b07384-d113-4f01-9b16-92c25df60e22", out object? guidObj);
```

### 4.4 Open Generic External Types

You can also register external open generic types, such as `[StaticRegister(typeof(Wrapper<>))]`. Implyzer dynamically constructs and caches delegates for closed variants (e.g. `Wrapper<int>`) when requested at runtime.

### 4.5 Strictness Control

By default, `Strict = true` reports compile-time analyzer error `IMPL014` if any registered type does not satisfy the contract. Setting `Strict = false` ignores non-conforming types instead of reporting a compile-time error:

```csharp
[StaticRegister(typeof(int), typeof(string), Strict = false)]
public partial interface IParser<TSelf> where TSelf : IParser<TSelf>;
```

---

## 5 Behind the Scenes

Implyzer automatically adapts its code generation based on the compiler's language version and target framework runtime capabilities.

### 5.1 Native C# 11+ Execution (.NET 7+)

When compiling with C# 11+ on a runtime supporting virtual statics in interfaces:
- **Interface Declarations**: Implyzer generates native `public static abstract` member declarations directly inside the partial interface. When a default implementation is present, it generates `public static virtual` members with method bodies that forward directly to the default implementation.
- **Generic Invocations**: Generic helper calls like `IParser.TryParse<T>(...)` invoke `T.TryParse(...)` directly with **zero dictionary lookup or delegate allocation overhead**. If `T` omits the method, the CLR dispatches to the virtual static default method on the interface. When external types are registered, the companion class provides fast dictionary delegate lookup for registered external types with automatic reflection fallback.
- **Non-Generic Invocations**: Overloads accepting a `System.Type` parameter use reflection (`type.GetMethods(...)`) to locate and invoke the static method dynamically at runtime.

### 5.2 Legacy Target Simulation (C# < 11 / .NET Standard 2.0)

When targeting older frameworks where native `static abstract` interface members are unavailable:
- **Implementation Registration**: Implyzer discovers implementing types at compile-time and generates a `[ModuleInitializer]` method to register implementations into a static registry (`Dictionary<Type, Delegate>`) upon assembly loading. External types registered via `[StaticRegister]` are registered in the same initializer.
- **Open Generic Support**: Open generic types (e.g., `Box<T> : IParser<Box<T>>` or external `Wrapper<T>`) are registered via an open generic factory registry. When a closed generic type like `Box<int>` or `Wrapper<string>` is invoked for the first time, Implyzer dynamically constructs and caches the closed delegate, providing full simulator compatibility for open generics with subsequent O(1) fast-path lookups.
- **Dynamic Overload Routing**: Generic overloads retrieve delegates from the registry, and non-generic overloads utilize `DynamicInvoke` to execute static methods at runtime.

### 5.3 Default Implementation Dispatch

- **C# 11+**: Emitted as `public static virtual` interface members with zero-overhead runtime dispatch.
- **C# < 11**: If an implementing type did not register a custom implementation, companion routing helpers fall back to calling the default implementation directly (in generic overloads) or via reflection fallback (in non-generic overloads).

---

## 6 Diagnostics

Implyzer's static abstract analyzer reports compile-time errors to ensure type safety:

| ID | Title | Description |
|---|---|---|
| `IMPL007` | Method Not Valid Delegate | The signature passed to `[StaticAbstract]` must be a delegate type. |
| `IMPL008` | Interface Not Partial | The interface decorated with `[StaticAbstract]` must be declared as `partial`. |
| `IMPL009` | Static Method Not Implemented | An implementing type must implement the static abstract member matching the delegate signature (unless a default implementation is provided). |
| `IMPL010` | Signature Mismatch | The signature of the static method does not match the delegate signature. |
| `IMPL011` | Default Method Not Found | The default implementation method specified was not found on the target type. |
| `IMPL012` | Default Method Signature Mismatch | The default implementation method parameters or return type do not match the static abstract delegate signature. |
| `IMPL013` | Default Method Must Be Static | The default implementation method must be declared as `static`. |
| `IMPL014` | Static Register Missing Member | A type registered via `[StaticRegister]` does not implement a required static abstract or virtual member. |
| `IMPL015` | Static Register Non-Static-Abstract Interface | Target interface specified in `[StaticRegister]` has no `[StaticAbstract]` or `[StaticVirtual]` attributes. |
| `IMPL016` | Static Register Redundant Registration | A type registered via `[StaticRegister]` already explicitly implements the target interface. |
| `IMPL017` | Target Type Not Partial | Target type must be declared as `partial` when `ImplementInTargetTypes` is enabled for a static virtual method. |
| `IMPL018` | Static Virtual Method Not Implemented | Target type does not implement static virtual method (Hidden diagnostic enabling IDE code fix until implemented). |