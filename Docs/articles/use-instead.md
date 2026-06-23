# Recommending Alternative APIs with UseInstead

The `UseInsteadAttribute` provides warning or information diagnostics to developers when they invoke older or deprecated APIs (classes, methods, fields, properties, constructors), suggesting a replacement symbol. 

Additionally, Implyzer includes **IDE Quick Action Code Fixes** that allow developers to automatically refactor their code to use the suggested replacement with a single click.

---

## 1 Constructor Signatures

```csharp
// 1. Suggest a replacement symbol in the same type:
public UseInsteadAttribute(string replacementSymbolName);

// 2. Suggest a replacement symbol in a different type:
public UseInsteadAttribute(Type replacementType);

// 3. Suggest a replacement constructor overload (by passing parameter types of the target constructor):
public UseInsteadAttribute(Type replacementType, Type[] parameterTypes);
```

---

## 2 Example Usage

### 2.1 Replacing Methods or Properties
You can suggest replacing a method or property with a newer one in the same class:

```csharp
using Implyzer;

public class Calculator {
    [UseInstead(nameof(Add))]
    public int ComputeSum(int a, int b) => Add(a, b);

    public int Add(int a, int b) => a + b;
}
```
*   **Result**: Invoking `ComputeSum` triggers an `IMPL005` info diagnostic: *"Use 'Add' instead of 'ComputeSum'"*. Applying the IDE Code Fix automatically rewrites the invocation to call `Add`.

### 2.2 Replacing Classes
You can suggest replacing a whole class with a modern implementation:

```csharp
[UseInstead(typeof(ModernLogger))]
public class LegacyLogger { }

public class ModernLogger { }
```
*   **Result**: Instantiating or referencing `LegacyLogger` triggers `IMPL005`: *"Use 'ModernLogger' instead of 'LegacyLogger'"*.

### 2.3 Replacing Constructors
You can suggest replacing a constructor overload with another:

```csharp
public class Connection {
    [UseInstead(typeof(Connection), [typeof(string)])]
    public Connection() { } // Deprecated empty connection

    public Connection(string connectionString) { }
}
```
*   **Result**: Instantiating `new Connection()` triggers `IMPL005` recommending the parameterized constructor.
