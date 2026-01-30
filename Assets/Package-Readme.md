# Implyzer

**Implyzer** is a Roslyn source generator and analyzer that enforces implementation constraints on interfaces. 
It allows you to specify whether an interface should be implemented only by `class`es (reference types) or 
`struct`s (value types), and can even enforce a specific base class.

## Features

- **Enforce Implementation Type**: Restrict interface implementations to be strictly `class` or `struct`.
- **Enforce Base Class**: Require implementing classes to inherit from a specific base type.
- **Enforce Indirect Implementation**: Make some interfaces directly unimplementable by classes, while still allowing
  them to be implemented through other interfaces.
- **Zero-Config**: Works out-of-the-box with standard .NET projects.
- **Source Generator**: Automatically injects the necessary attributes into your project—no extra dependencies are 
  required at runtime.

## Installation

### .csproj

```xml
<ItemGroup>
    <PackageReference Include="Implyzer" Version="*" OutputItemType="Analyzer" ReferenceOutputAssembly="false">
        <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
</ItemGroup>
```

### Dotnet CLI

```bash
dotnet add package Implyzer --version 1.1.0
```

### Package Manager Console

```bash
NuGet\Install-Package Implyzer -Version 1.1.0
```

### File-based Reference

```c#
#:package Implyzer@1.1.0
```

## Examples

### 1. Enforce Reference Types (Classes)

Use `[ImplType(ImplKind.ReferenceType)]` to ensure an interface is only implemented by classes.

```csharp
using Implyzer;

[ImplType(ImplKind.ReferenceType)]
public interface IService
{
    void Execute();
}

// VALID: Class implements the interface
public class MyService : IService
{
    public void Execute() { }
}

// ERROR: Struct cannot implement ReferenceType interface
public struct MyStruct : IService
{
    public void Execute() { }
}
```

### 2. Enforce Value Types (Structs)

Use `[ImplType(ImplKind.ValueType)]` to ensure an interface is only implemented by structs.

```csharp
using Implyzer;

[ImplType(ImplKind.ValueType)]
public interface IData
{
    int Value { get; }
}

// VALID: Struct implements the interface
public struct Point : IData
{
    public int Value => 10;
}

// ERROR: Class cannot implement ValueType interface
public class DataObject : IData
{
    public int Value => 10;
}
```

### 3. Enforce Base Class

Use `[ImplType(typeof(MyBaseClass))]` to ensure implementing classes inherit from a specific base class. This 
implicitly enforces `ImplKind.ReferenceType`.

```csharp
using Implyzer;

public class GameEntity { }

[ImplType(typeof(GameEntity))]
public interface IEnemy { }

// VALID: Inherits from GameEntity
public class Zombie : GameEntity, IEnemy { }

// ERROR: Does not inherit from GameEntity
public class Player : IEnemy { }
```

### 4. Enforce Indirect Implementation

Enforce indirect implementation by using `[IndirectImpl]`.

```c#
using Implyzer;

[IndirectImpl(typeof(IGeneric<>))]
public interface INotGeneric { }

public interface IGeneric<T> { }

// VALID: Inherits from IGeneric
public class Generic : IGeneric<int> { }

// ERROR: Inherits from INotGeneric
public class NotGeneric : INotGeneric { }
```

## How It Works

1. The package includes a **Source Generator** that adds the `ImplTypeAttribute` and `ImplKind` enum to your 
   project automatically.
2. The **Analyzer** inspects your code at compile-time and reports errors (`IMPL001` for kind mismatch, `IMPL002` 
   for base type mismatch) if the constraints are violated.

## Building from Source

Requirements: .NET 10.0 SDK or later.

1. Clone the repository.
2. Build the solution:
   ```bash
   dotnet build
   ```
3. Run tests:
   ```bash
   dotnet test
   ```

## License

MIT