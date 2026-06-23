<div align="center">

# Implyzer

An implementation analyzer and restraing system for C#.

[![License](https://img.shields.io/github/license/KryKomDev/Implyzer?style=for-the-badge&labelColor=%232a313c&color=%2358a6ff)](https://github.com/KryKomDev/Implyzer/blob/main/LICENSE)
[![Latest Stable](https://img.shields.io/github/v/release/KryKomDev/Implyzer?sort=semver&style=for-the-badge&label=Latest%20Stable&labelColor=2a313c&color=e051c6)](https://img.shields.io/github/v/release/KryKomDev/Implyzer?sort=semver&style=for-the-badge&label=Latest%20Stable&labelColor=2a313c&color=e051c6)
[![Latest](https://img.shields.io/github/v/release/KryKomDev/Implyzer?include_prereleases&sort=semver&style=for-the-badge&label=Latest&labelColor=2a313c&color=e051c6)](https://img.shields.io/github/v/release/KryKomDev/Implyzer?include_prereleases&sort=semver&style=for-the-badge&label=Latest&labelColor=2a313c&color=e051c6)
[![Commits per month](https://img.shields.io/github/commit-activity/m/KryKomDev/Implyzer/main?style=for-the-badge&labelColor=%232a313c&color=%23d69a00)](https://img.shields.io/github/commit-activity/m/KryKomDev/Implyzer/main?style=for-the-badge&labelColor=%232a313c&color=%2358a6ff)
![NuGet Version](https://img.shields.io/nuget/v/Implyzer?style=for-the-badge&labelColor=2a313c&color=e051c6&link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FImplyzer)

</div>

**Implyzer** is a Roslyn source generator and analyzer that enforces implementation constraints on interfaces.
It allows you to specify whether an interface should be implemented only by `class`es (reshieldsference types) or
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
dotnet add package Implyzer
```

### Package Manager Console

```bash
NuGet\Install-Package Implyzer
```

### File-based Reference

```c#
#:package Implyzer
```

## Examples

All code examples are available in the [Samples](./Samples) project.

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

### 5. Use Instead

Create a suggestion diagnostic when using a method, class etc.
using `[UseInstead]`

```c#
[UseInstead(nameof(ShouldBeUsed))]
public void ShouldNotBeUsed() { ... }

public void ShouldBeUsed() { ... }

void Main() 
{
    // creates a suggestion alongside with 
    // a code fix when using an IDE
    ShouldNotBeUsed();
}
```

## Configuration

No manual configuration is required. The generator automatically checks if the attributes
(`ImplTypeAttribute`, `IndirectImplAttribute`, `UseInsteadAttribute`) are already defined
in the compilation (either within your own codebase or from a referenced project/assembly).
If they do not exist, they are generated as `public` by default.

### Working in a Multi-project Solution

If you are importing Implyzer in a multi-project solution, the attributes will only be 
generated in the projects where they don't already exist. For example, if a project 
references another project that already generates/declares the attributes, Implyzer will 
automatically detect this and skip generating them in the referencing project to avoid 
duplicate type errors.

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

## Documentation

You can find the full documentation [here](https://krykomdev.github.io/Implyzer/).

## License

MIT