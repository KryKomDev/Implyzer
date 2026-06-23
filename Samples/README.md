# Implyzer Samples

This project contains code examples demonstrating the implementation constraints and refactoring diagnostics enforced by Implyzer.

Each example illustrates a specific compile-time diagnostic rule, showing both **valid** and **invalid** usages (with instructions on how to trigger the analyzer errors).

---

## Sample Contents

### 1. [ImplTypeExamples.cs](file:///C:/Users/krystof/Desktop/projects/Implyzer/Samples/ImplTypeExamples.cs)
*   **Demonstrates**: `[ImplType]` restrictions for reference/value types, and base class inheritance requirements.
*   **Diagnostic Rules**:
    *   `IMPL001`: Mismatch between implementing type kind (e.g. class vs. struct) and the constraints specified on the interface.
    *   `IMPL002`: Missing inheritance constraint (the implementing class does not inherit from the required base class).

### 2. [EmptyConstructorExample.cs](file:///C:/Users/krystof/Desktop/projects/Implyzer/Samples/EmptyConstructorExample.cs)
*   **Demonstrates**: `ImplKind.ReferenceTypeNew` requiring a public parameterless constructor on any implementing class.
*   **Diagnostic Rules**:
    *   `IMPL004`: Missing public parameterless constructor on a class implementing a `ReferenceTypeNew` interface.

### 3. [IndirectImplExamples.cs](file:///C:/Users/krystof/Desktop/projects/Implyzer/Samples/IndirectImplExamples.cs)
*   **Demonstrates**: `[IndirectImpl]` constraints, preventing developers from implementing a base interface directly, forcing them to implement a derived interface instead.
*   **Diagnostic Rules**:
    *   `IMPL003`: Direct implementation of an interface decorated with `[IndirectImpl]` is forbidden.

### 4. [StaticAbstract.cs](file:///C:/Users/krystof/Desktop/projects/Implyzer/Samples/StaticAbstract.cs)
*   **Demonstrates**: The simulation of C# 11 `static abstract` methods on interfaces for older target frameworks using the `[StaticAbstract]` attribute.
*   **Diagnostic Rules**:
    *   `IMPL006` / `IMPL007` / `IMPL008`: Correctness of partial declarations on target interfaces and registry classes.
    *   `IMPL009`: Implementing class/struct failed to implement the required public static method matching the delegate signature.
    *   `IMPL010`: Mismatch where the signature parameter is not a delegate.

### 5. [UseInsteadExamples.cs](file:///C:/Users/krystof/Desktop/projects/Implyzer/Samples/UseInsteadExamples.cs)
*   **Demonstrates**: Suggesting replacement symbols (classes, methods, fields, properties, constructors) using `[UseInstead]`.
*   **Diagnostic Rules**:
    *   `IMPL005`: Warning/Info suggestion indicating that a newer replacement symbol should be used instead of the annotated one, complete with IDE Code Fix refactoring support.

---

## How to Run & Verify

1.  Open your terminal and navigate to the samples directory:
    ```powershell
    cd Samples
    ```
2.  Run build to verify that everything compiles cleanly out of the box (invalid code is commented out by default):
    ```powershell
    dotnet build
    ```
3.  To trigger and verify diagnostics:
    *   Open any sample file (e.g. [ImplTypeExamples.cs](file:///C:/Users/krystof/Desktop/projects/Implyzer/Samples/ImplTypeExamples.cs)).
    *   Uncomment the block marked as `// Uncomment to see error ...`.
    *   Run `dotnet build` again, or observe the compiler errors right in your IDE.
