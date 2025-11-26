# 2. .NET Core Architecture

## Overview

.NET Core is a modern, open-source, cross-platform runtime and framework. Understanding its architecture is essential for building efficient ASP.NET applications.

## .NET Core vs .NET Framework

| Aspect | .NET Core (.NET 5+) | .NET Framework |
|--------|-------------------|-----------------|
| **Platform** | Cross-platform (Windows, Linux, macOS) | Windows only |
| **License** | Open source (MIT) | Proprietary |
| **Performance** | Significantly faster | Legacy baseline |
| **Direction** | Active development | Maintenance mode |
| **Use Case** | New applications | Legacy support only |

**For ASP.NET development**: Always use .NET 6+ (.NET Core evolution).

## Architecture Layers

```
┌─────────────────────────────────────┐
│      Your Application Code          │
│     (ASP.NET, Console, Desktop)     │
└──────────────┬──────────────────────┘
               │
┌──────────────▼──────────────────────┐
│     Base Class Library (BCL)        │
│   (System namespaces, types,        │
│    collections, reflection, etc.)   │
└──────────────┬──────────────────────┘
               │
┌──────────────▼──────────────────────┐
│   Common Language Runtime (CLR)     │
│  - JIT Compilation                  │
│  - Memory Management (GC)           │
│  - Type Safety & Verification       │
│  - Exception Handling               │
│  - Threading & Synchronization      │
└──────────────┬──────────────────────┘
               │
┌──────────────▼──────────────────────┐
│   Operating System & Hardware       │
│   (Windows, Linux, macOS)           │
└─────────────────────────────────────┘
```

## Core Components

### 1. Common Language Runtime (CLR)

The CLR is the execution engine that manages .NET applications.

**Key Responsibilities:**
- **JIT Compilation**: Converts IL (Intermediate Language) to native machine code
- **Garbage Collection**: Automatic memory management
- **Type Safety**: Enforces type checking and memory safety
- **Exception Handling**: Structured exception handling
- **Threading**: Manages thread pools and synchronization

```csharp
// When you write this:
int x = 10;
string name = "John";

// At runtime:
// 1. Code is compiled to IL (Intermediate Language)
// 2. CLR JIT-compiles IL to native code
// 3. CLR manages memory, types, and execution
// 4. GC automatically frees unused objects
```

### 2. Garbage Collection (GC)

.NET uses automatic garbage collection—a critical feature for ASP.NET scalability.

**Generational GC:**
```
Generation 0 (Gen 0): Short-lived objects (collected frequently)
    ├─ Strings, temporary objects, request-scoped data
    └─ Collected most often (minimal pause)
    
Generation 1 (Gen 1): Medium-lived objects (collected occasionally)
    ├─ Some caches, temporary collections
    └─ Collected less frequently
    
Generation 2 (Gen 2): Long-lived objects (collected rarely)
    ├─ Cached data, singleton services, application state
    └─ Collected infrequently (larger pause)
```

**ASP.NET Implications:**
```csharp
// Good: Minimal allocations in request handling
public async Task<IActionResult> GetUser(int id)
{
    var user = await _repository.GetByIdAsync(id);
    return Ok(user);
}

// Bad: Excessive allocations
public async Task<IActionResult> GetUsers()
{
    var users = new List<User>();
    for (int i = 0; i < 1000; i++)
    {
        users.Add(new User());  // Many allocations
    }
    return Ok(users);
}

// Good: Use LINQ or bulk operations
public async Task<IActionResult> GetUsers()
{
    var users = await _repository.GetAllAsync();
    return Ok(users);
}
```

### 3. Base Class Library (BCL)

The BCL provides foundational types and functionality.

```csharp
// System namespace - fundamental types
using System;
int x = 42;
string name = "John";

// System.Collections.Generic - collections
using System.Collections.Generic;
var list = new List<User>();

// System.Linq - LINQ queries
using System.Linq;
var active = list.Where(u => u.IsActive);

// System.Threading.Tasks - async operations
using System.Threading.Tasks;
await Task.Delay(1000);

// System.Reflection - runtime type information
using System.Reflection;
var methods = typeof(User).GetMethods();

// System.Net.Http - HTTP client
using System.Net.Http;
var client = new HttpClient();
```

## Assemblies & Types

### Assemblies
Assemblies are the unit of deployment in .NET. They contain IL code and metadata.

```csharp
// Your project compiles to one or more assemblies (.dll files)
// dotnet build → bin/Debug/net8.0/MyApp.dll

// Assemblies are named and versioned
// Assembly versions: major.minor.build.revision
// Example: MyLibrary, Version=1.0.0.0
```

**Assembly Structure:**
```
MyApp.dll (Assembly)
├── Metadata (type definitions, methods, properties)
├── IL Code (Intermediate Language bytecode)
├── Resource Files (images, config, etc.)
└── Manifest (assembly info, version, dependencies)
```

### Types

.NET has two categories of types:

```csharp
// 1. Value Types (stack allocated)
public struct Point
{
    public int X { get; set; }
    public int Y { get; set; }
}

// 2. Reference Types (heap allocated)
public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public record UserDto(int Id, string Name);  // Also reference type
```

## Namespaces & Organization

Namespaces organize code logically and prevent naming conflicts.

```csharp
// Good: Clear, hierarchical organization
namespace MyCompany.PaymentService.Domain
{
    public class Payment { }
}

namespace MyCompany.PaymentService.Application
{
    public class PaymentService { }
}

namespace MyCompany.PaymentService.Infrastructure
{
    public class PaymentRepository { }
}

// Usage
using MyCompany.PaymentService.Domain;
using MyCompany.PaymentService.Application;

var service = new PaymentService();
```

## NuGet Package Management

NuGet is the package manager for .NET.

```csharp
// In .csproj file:
<ItemGroup>
    <PackageReference Include="Serilog" Version="3.0.1" />
    <PackageReference Include="EntityFrameworkCore" Version="8.0.0" />
    <PackageReference Include="FluentValidation" Version="11.8.0" />
</ItemGroup>

// Or via CLI:
// dotnet add package Serilog
// dotnet add package EntityFrameworkCore

// These packages get restored into:
// ~/.nuget/packages/
```

**Package Structure:**
```
Serilog
├── lib/
│   ├── net6.0/
│   ├── net7.0/
│   └── net8.0/
├── readme.md
├── license.md
└── Serilog.nuspec (metadata)
```

## Project Structure

### Standard ASP.NET Project Layout

```
MyApp/
├── MyApp.csproj                 # Project file (dependencies, settings)
├── Program.cs                   # Entry point, dependency injection setup
├── appsettings.json             # Configuration
├── appsettings.Development.json # Dev-specific config
├── appsettings.Production.json  # Prod-specific config
├── Dockerfile                   # Container configuration
├── .gitignore
├── bin/                         # Build output
├── obj/                         # Intermediate build files
├── Properties/
│   └── launchSettings.json      # Launch profiles (IIS, Kestrel)
├── Features/
│   ├── Users/
│   │   ├── GetUserEndpoint.cs
│   │   └── CreateUserEndpoint.cs
│   └── Products/
├── Domain/                      # Business logic
│   ├── User.cs
│   ├── Product.cs
│   └── Order.cs
├── Application/                 # Use cases, services
│   ├── UserService.cs
│   └── ProductService.cs
├── Infrastructure/              # Database, external services
│   ├── Persistence/
│   │   ├── ApplicationDbContext.cs
│   │   └── UserRepository.cs
│   └── ExternalServices/
└── Presentation/                # API endpoints, controllers
    ├── Controllers/
    └── Middleware/
```

### Project File (.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Serilog.AspNetCore" Version="8.0.0" />
    <PackageReference Include="EntityFrameworkCore" Version="8.0.0" />
  </ItemGroup>

</Project>
```

## .NET CLI Essentials

Essential commands for ASP.NET development:

```bash
# Project creation
dotnet new web -n MyApp                    # New ASP.NET Core app
dotnet new classlib -n MyLibrary           # New class library

# Building
dotnet build                               # Debug build
dotnet build -c Release                    # Release build

# Running
dotnet run                                 # Run application
dotnet run --launch-profile "https"        # Run with specific profile

# Package management
dotnet add package Serilog                 # Add NuGet package
dotnet remove package Serilog              # Remove package
dotnet restore                             # Restore dependencies

# Database (EF Core)
dotnet ef migrations add CreateUserTable   # Create migration
dotnet ef database update                  # Apply migration

# Testing
dotnet test                                # Run all tests
dotnet test --logger "console;verbosity=detailed"

# Publishing
dotnet publish -c Release -o out/          # Publish for deployment

# Information
dotnet --list-sdks                         # List installed SDKs
dotnet --version                           # Show CLI version
```

## Execution Model

```csharp
// 1. Source code (.cs files)
public class User
{
    public int Id { get; set; }
}

// 2. Compiled to IL (Intermediate Language)
// dotnet build creates .dll with IL

// 3. At Runtime: JIT Compilation
// When method is first called, CLR JIT-compiles IL → native code
// JIT'd code is cached in memory for performance

// 4. Native Code Execution
// CPU executes native machine code
// GC monitors heap for unused objects
```

**Startup Sequence:**
```
1. OS loads .NET Runtime
2. Runtime loads .dll assembly
3. Runtime finds entry point (Main in Program.cs)
4. Application code executes
5. For ASP.NET:
   - Dependency Injection Container builds
   - Middleware pipeline configured
   - Kestrel web server starts
   - Listening for HTTP requests
```

## Performance Considerations

### Memory Efficiency

```csharp
// ASP.NET apps run in long-lived processes
// Memory must be managed efficiently

// Problematic pattern:
List<object> cache = new();
for (int i = 0; i < 1_000_000; i++)
{
    cache.Add(new object());  // Gen2 allocation, never freed
}

// Better pattern:
var cache = new MemoryCache(options);  // Built-in caching
var cachedValue = cache.GetOrCreate("key", _ => expensiveOperation());
```

### Allocation Rate

```csharp
// Each allocation stresses GC
// Minimize allocations in hot paths

// Problematic: creates string allocation per request
public string GetUserInfo(User user)
{
    return "User: " + user.Name + " (" + user.Email + ")";
}

// Better: string interpolation (fewer allocations)
public string GetUserInfo(User user)
{
    return $"User: {user.Name} ({user.Email})";
}

// Best: avoid allocation if not needed
public void LogUserInfo(User user)
{
    _logger.LogInformation("User: {Name} ({Email})", user.Name, user.Email);
    // Structured logging - allocation only if level is enabled
}
```

## Key Takeaways

1. **CLR manages execution**: JIT compilation, GC, type safety, threading
2. **Generational GC**: Optimize for short-lived objects
3. **Assemblies are units of deployment**: Reference and version-aware
4. **NuGet for dependencies**: Manage external libraries via .csproj
5. **Project structure matters**: Follow conventions for maintainability
6. **Async is essential**: Non-blocking I/O for scalability
7. **Memory efficiency is critical**: Long-running ASP.NET processes need careful memory management
8. **.NET CLI is powerful**: Build, test, publish, and manage from command line

## Related Topics

- **Dependency Injection** (Next topic): How .NET manages object creation
- **ASP.NET Core Middleware** (Phase 2): How requests flow through the application
- **Entity Framework Core** (Phase 4): How data access integrates with .NET

