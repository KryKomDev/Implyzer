# Getting Started with Implyzer

This guide will walk you through installing Implyzer and setting up your first implementation constraint.

---

## 1 Installation

Implyzer is packaged as a standard Roslyn Analyzer and Source Generator. It doesn't ship external runtime assemblies to keep your binary sizes minimal.

### Option A: via CLI (Recommended)
Run the following command inside your project directory:
```bash
dotnet add package Implyzer
```

### Option B: via .csproj Reference
Add the package reference to your `.csproj` file:
```xml
<ItemGroup>
    <PackageReference Include="Implyzer" Version="*" OutputItemType="Analyzer" ReferenceOutputAssembly="false">
        <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
</ItemGroup>
```

---

## 2 Zero-Configuration Setup

Once the package reference is added, **no configuration is necessary**. 

Upon compilation, the source generator will automatically detect if the Implyzer attributes are present in your codebase. If they are not (which is true on initial install), Implyzer will generate the following attributes directly inside your assembly:
-   `ImplTypeAttribute` and `ImplKind` enum
-   `IndirectImplAttribute`
-   `StaticAbstractAttribute`
-   `UseInsteadAttribute`

These are generated as `public` by default, meaning they are immediately accessible in your project.

### Multi-Project Solutions
In multi-project setups where a library project already references and generates Implyzer attributes, referencing projects will automatically detect the compiled types. Implyzer will skip generation in the referencing projects, preventing duplicate type compiler errors.

---

## 3 Writing Your First Constraint

Let's enforce that a specific service contract interface can only be implemented by reference types (classes).

1.  Declare your interface and apply `[ImplType(ImplKind.ReferenceType)]`:
    ```csharp
    using Implyzer;

    [ImplType(ImplKind.ReferenceType)]
    public interface ILoggingService {
        void Log(string message);
    }
    ```

2.  If you implement this interface using a `struct`, the analyzer will immediately highlight the declaration in your IDE and block compilation with `IMPL001`:
    ```csharp
    // COMPILER ERROR (IMPL001): Type 'StructLogger' must be a class because...
    public struct StructLogger : ILoggingService {
        public void Log(string message) { }
    }
    ```

3.  Implementing it using a `class` compiles cleanly:
    ```csharp
    // VALID
    public class ConsoleLogger : ILoggingService {
        public void Log(string message) { }
    }
    ```