# Enforcing Indirect Implementation

The `IndirectImplAttribute` is designed to forbid classes/structs from implementing a "marker" or "base" interface directly. Instead, they are forced to implement the interface indirectly via a derived generic interface or a sub-interface. 

This pattern is highly useful in architectural frameworks to prevent developers from bypassing necessary validation boilerplate, generic mappings, or base type contracts.

---

## 1 Signature

```csharp
[IndirectImpl(Type expectedSubInterface)]
```

### 1.1 Constructor Parameters
-   **`expectedSubInterface`**: The type of the interface that developers are expected to implement instead (e.g. `typeof(IRepository<>)`).

---

## 2 Example Usage

### 2.1 Enforcing through Generic Sub-Interfaces

A common use case is forcing developers to implement a generic interface (which has standard generic signatures) rather than directly implementing a non-generic marker interface.

```csharp
using Implyzer;

// We want INotGeneric to only be implemented through IGeneric<>
[IndirectImpl(typeof(IGeneric<>))]
public interface INotGeneric {
    object? GetValue();
}

// The generic sub-interface provides type safety and default mappings:
public interface IGeneric<T> : INotGeneric {
    object? INotGeneric.GetValue() => Get();
    
    T Get();
}
```

### 2.2 Validating implementing classes

If a class implements `IGeneric<T>`, compilation is successful:

```csharp
// VALID: Implements INotGeneric indirectly through IGeneric<string>
public class BookStore : IGeneric<string> {
    public string Get() => "Design Patterns";
}
```

If a class implements `INotGeneric` directly, compilation is blocked with `IMPL003`:

```csharp
// ERROR (IMPL003): Type 'InvalidStore' cannot implement 'INotGeneric' directly
public class InvalidStore : INotGeneric {
    public object? GetValue() => "Refactoring";
}
```
