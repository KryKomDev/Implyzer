# Introduction to Implyzer

**Implyzer** is a static analysis and compilation toolset for C#. It integrates Roslyn Analyzers and Source Generators to provide compile-time implementation enforcement and API refactoring guides.

In modern C# and .NET (C# 11+, .NET 7+), features like `static abstract` members on interfaces and Default Interface Methods (DIM) have introduced powerful new ways to structure and constrain code. However, teams working on older runtime targets (like `.NET Standard 2.0`, `.NET Core 3.1`, or `.NET Framework`) are often locked out of these capabilities.

Implyzer bridges this gap by simulating these runtime-constrained features using static analysis checks and injected code generation, all resolved at compile time with **zero runtime dependencies**.

---

## What Implyzer Does

### 1. Implementation Type Restrictions (`[ImplType]`)
Enforces whether an interface can be implemented strictly as a class (`ReferenceType`), a class with a parameterless constructor (`ReferenceTypeNew`), or a struct (`ValueType`).

### 2. Base Class Constraints (`[ImplType(typeof(BaseClass))]`)
Guarantees that any class implementing the constrained interface also inherits from a specific base class, bridging interface contracts with base class behaviors.

### 3. Indirect Implementation Enforcement (`[IndirectImpl]`)
Forbids implementing specific interfaces directly on classes or structs, ensuring that the interface is only implemented indirectly through designated sub-interfaces. This prevents developers from bypassing key domain boilerplate or generic contract validation.

### 4. Static Abstract Simulation (`[StaticAbstract]`)
Simulates `static abstract` interface methods, automatically generating type-safe generic and non-generic companion routing classes that direct calls to registered static methods.

### 5. API Refactoring Suggestions (`[UseInstead]`)
Generates warning or information diagnostics suggesting replacement APIs to developers, complete with automated IDE Quick Actions (Code Fixes) to rewrite the code.