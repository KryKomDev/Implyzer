# Enforcing Implementation Kinds and Base Types

The `ImplTypeAttribute` enables developers to restrict how interfaces can be implemented. It enforces whether implementing types must be reference types (classes), value types (structs), or must inherit from a specific base class.

---

## 1 Implementation Kinds

To restrict implementing types to specific kinds, pass the `ImplKind` enum to the constructor:

```csharp
[ImplType(ImplKind kind)]
```

### 1.1 `ImplKind.ReferenceType`
Restricts implementation strictly to reference types (classes). Any struct implementing this interface will trigger analyzer error `IMPL001`.

```csharp
[ImplType(ImplKind.ReferenceType)]
public interface IService { }

// VALID
public class ServiceImpl : IService { }

// ERROR (IMPL001)
public struct ServiceStruct : IService { }
```

### 1.2 `ImplKind.ValueType`
Restricts implementation strictly to value types (structs). Any class implementing this interface will trigger analyzer error `IMPL001`.

```csharp
[ImplType(ImplKind.ValueType)]
public interface IDataPayload { }

// VALID
public struct PayloadStruct : IDataPayload { }

// ERROR (IMPL001)
public class PayloadClass : IDataPayload { }
```

### 1.3 `ImplKind.ReferenceTypeNew`
Like `ImplKind.ReferenceType`, but additionally requires the implementing class to expose a **public parameterless constructor**. This is useful for generic dynamic instantiation frameworks or serializers. Missing parameterless constructors trigger error `IMPL004`.

```csharp
[ImplType(ImplKind.ReferenceTypeNew)]
public interface IComponent { }

// VALID
public class ValidComponent : IComponent { } // Implicit parameterless constructor

// ERROR (IMPL004)
public class InvalidComponent : IComponent {
    public InvalidComponent(string setting) { } // Hides parameterless constructor
}
```

---

## 2 Base Class Constraints

You can guarantee that all implementing types inherit from a specific base class by passing the base class type directly to `[ImplType]`:

```csharp
[ImplType(Type baseClass)]
```

This implicitly enforces `ImplKind.ReferenceType` as well. Implementing types that do not inherit from the specified base class will trigger analyzer error `IMPL002`.

```csharp
public abstract class EntityBase {
    public int Id { get; set; }
}

[ImplType(typeof(EntityBase))]
public interface IEntity { }

// VALID: Inherits from EntityBase
public class CustomerEntity : EntityBase, IEntity { }

// ERROR (IMPL002): Does not inherit from EntityBase
public class LogRecord : IEntity { }
```
