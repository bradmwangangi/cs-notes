# .NET Architecture & Runtime

## .NET Overview

**.NET** is a free, open-source platform for building applications. Modern .NET (6+) is cross-platform and unified.

### .NET History
- **.NET Framework** (Windows only, legacy) - released 2002
- **.NET Core** (cross-platform, modern) - released 2016
- **.NET 5+** (unified platform, current) - released 2020

Use **.NET 6+** for new projects.

## CLR (Common Language Runtime)

The execution engine for .NET programs:

```
Source Code (.cs)
       ↓
C# Compiler
       ↓
MSIL (Microsoft Intermediate Language) / IL
       ↓
JIT Compiler (Just-In-Time)
       ↓
Native Machine Code
       ↓
CPU Execution
```

### What the CLR Provides

| Feature | Purpose |
|---------|---------|
| **JIT Compilation** | Compiles IL to machine code at runtime |
| **Memory Management** | Automatic allocation and garbage collection |
| **Type Safety** | Enforces type checking |
| **Exception Handling** | Unified exception model |
| **Security** | Code access security, sandboxing |

## Assemblies

Compiled units of deployment. An assembly is a `.dll` or `.exe` file containing:
- MSIL code
- Metadata (type information, method signatures)
- Resources (images, strings, etc.)

```bash
# View assembly contents
dotnet --info

# Examine an assembly (requires ildasm tool)
ildasm MyAssembly.dll
```

### Assembly Structure

```csharp
// Your code compiles to MSIL
public class Calculator
{
    public int Add(int a, int b) => a + b;
}

// At runtime, the JIT compiler converts MSIL to native code
// The CLR manages memory and execution
```

### Assembly Versioning

```
Version: Major.Minor.Build.Revision
Example: 1.2.0.0
```

```xml
<!-- In .csproj file -->
<PropertyGroup>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>
</PropertyGroup>
```

## Namespaces & Using Statements

Organize code into logical groups:

```csharp
// Define namespace
namespace MyApp.Core
{
    public class BusinessLogic
    {
        public void Execute() { }
    }
}

// Another file
namespace MyApp.UI
{
    using MyApp.Core;  // Import namespace

    public class UserInterface
    {
        public void Interact()
        {
            var logic = new BusinessLogic();
            logic.Execute();
        }
    }
}

// Global using (C# 10+, import for entire project)
// In GlobalUsings.cs
global using System;
global using System.Collections.Generic;
```

### Standard Namespaces

| Namespace | Purpose |
|-----------|---------|
| `System` | Core types, Console, DateTime, etc. |
| `System.Collections` | Collections (lists, dictionaries) |
| `System.Linq` | LINQ queries |
| `System.IO` | File and stream operations |
| `System.Net` | Networking |
| `System.Text.Json` | JSON serialization |
| `System.Threading.Tasks` | Async operations |

## Managed vs Unmanaged Code

```csharp
// Managed: CLR handles memory and execution
public class ManagedClass
{
    public int Value { get; set; }
}  // CLR automatically cleans up

// Unmanaged: You manage memory (rare in modern C#)
using var handle = GCHandle.Alloc("data");
// Must explicitly free memory
handle.Free();
```

## Memory Management

### Stack vs Heap

```csharp
public void MemoryLayout()
{
    int age = 30;              // VALUE TYPE: stored on STACK
    string name = "Alice";     // REFERENCE TYPE: reference on STACK, data on HEAP
    Person person = new();     // REFERENCE TYPE: reference on STACK, object on HEAP

    var numbers = new int[100];  // REFERENCE TYPE: reference on STACK, array on HEAP
}
// When method exits:
// - All stack variables (age, name, person, numbers references) are freed
// - Heap objects become eligible for garbage collection
```

### Value Types vs Reference Types

```csharp
// VALUE TYPES: stored on stack (copy by value)
struct Point { public int X; public int Y; }
int num = 5;
bool flag = true;

var p1 = new Point { X = 1, Y = 2 };
var p2 = p1;           // Copies the struct
p2.X = 10;
Console.WriteLine(p1.X);  // Still 1 (separate copy)

// REFERENCE TYPES: stored on heap (copy by reference)
class Person { public string Name; }
var person1 = new Person { Name = "Alice" };
var person2 = person1;     // Copies the reference, not the object
person2.Name = "Bob";
Console.WriteLine(person1.Name);  // "Bob" (same object)
```

## Garbage Collection (GC)

Automatic memory cleanup:

```csharp
public class Resource : IDisposable
{
    private bool disposed = false;

    // Use for unmanaged resources or cleanup
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);  // Don't call finalizer
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposed)
        {
            if (disposing)
            {
                // Cleanup managed resources
            }
            // Cleanup unmanaged resources
            disposed = true;
        }
    }

    ~Resource()  // Finalizer (fallback cleanup)
    {
        Dispose(false);
    }
}

// Using statement ensures Dispose() is called
using (var resource = new Resource())
{
    // Use resource
}  // Dispose() called automatically

// C# 8+ using declaration
using var resource2 = new Resource();
// Dispose() called at end of scope
```

### GC Generations

The GC categorizes objects by age:

- **Gen 0**: Recently created objects (collected most frequently)
- **Gen 1**: Survived one collection
- **Gen 2**: Long-lived objects (collected less frequently)

```csharp
// Force garbage collection (rarely needed)
GC.Collect();
GC.WaitForPendingFinalizers();
```

## Type System

Everything in C# derives from `object`:

```csharp
// All types inherit from object
int num = 5;
object o = num;  // Boxing: wrapping value type in object

int extracted = (int)o;  // Unboxing: extracting value (can throw)

// Type checking
if (o is int)
{
    int value = (int)o;  // Safe cast after checking
}

// Pattern matching (safer)
if (o is int intValue)
{
    Console.WriteLine($"Integer: {intValue}");
}

// typeof: get type information
Type type = typeof(string);
Console.WriteLine(type.Name);  // "String"
Console.WriteLine(type.Namespace);  // "System"
```

## Reflection

Inspect and invoke code at runtime:

```csharp
public class Sample
{
    public string Name { get; set; }
    public void PrintInfo() { }
}

var type = typeof(Sample);

// Get properties
var properties = type.GetProperties();
foreach (var prop in properties)
{
    Console.WriteLine($"Property: {prop.Name} ({prop.PropertyType.Name})");
}

// Get methods
var methods = type.GetMethods();
foreach (var method in methods)
{
    Console.WriteLine($"Method: {method.Name}");
}

// Invoke method dynamically
var instance = new Sample();
var method = type.GetMethod("PrintInfo");
method?.Invoke(instance, null);

// Get property value
var nameProp = type.GetProperty("Name");
instance.Name = "Test";
var value = nameProp?.GetValue(instance);
Console.WriteLine(value);  // "Test"
```

## AppDomains (Framework) vs Isolated Processes (.NET Core+)

In **.NET Core/.NET 5+**, appdomains are gone - use separate processes instead:

```csharp
// .NET Framework concept (don't use in modern .NET)
// AppDomain.CreateDomain("SandboxedDomain");

// Modern .NET (.NET 5+): Use separate processes
using var process = new System.Diagnostics.Process
{
    StartInfo = new System.Diagnostics.ProcessStartInfo
    {
        FileName = "otherapp.exe"
    }
};
process.Start();
```

## Versioning & Compatibility

### Target Frameworks

```xml
<!-- .csproj: specify target framework -->
<PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <!-- or multiple:
    <TargetFrameworks>net6.0;net7.0;net8.0</TargetFrameworks>
    -->
</PropertyGroup>
```

### Breaking Changes

```csharp
// Version 1.0
public class MyClass
{
    public int Value { get; set; }
}

// Version 2.0 (potential breaking change)
public class MyClass
{
    public string Value { get; set; }  // Changed type: breaks existing code
}

// Safe change (compatible)
public class MyClass
{
    public int Value { get; set; }
    public string DisplayValue => Value.ToString();  // Add, don't remove
}
```

## Practice Exercises

1. **Assembly Inspection**: Use reflection to inspect a built-in type (e.g., `string`) and list all its public methods
2. **Type Checking**: Write a function that accepts `object` and determines its type safely
3. **Memory Behavior**: Create value and reference types, modify copies, and observe differences
4. **Namespace Organization**: Organize a multi-file project with proper namespaces
5. **Dispose Pattern**: Create a class that owns a resource and implements proper cleanup

## Key Takeaways

- **.NET** is a managed platform; the **CLR** handles compilation, memory, and execution
- **MSIL** is compiled at runtime by the JIT compiler to machine code
- **Assemblies** are the unit of deployment containing compiled code and metadata
- **Stack** holds value types and references; **Heap** holds reference type objects
- **Garbage collection** automatically cleans up unreferenced heap objects
- **Namespaces** organize code logically
- **Reflection** allows runtime inspection and invocation of types
- Use **using statements** to ensure proper resource cleanup
- Understand **value vs reference types** for correct behavior when copying data
