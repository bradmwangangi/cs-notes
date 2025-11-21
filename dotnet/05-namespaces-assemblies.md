# Namespaces & Assemblies

## Namespaces

Organize code into logical hierarchical groups to avoid name conflicts:

```csharp
// Define namespace
namespace MyCompany.ProductA.Core
{
    public class Engine
    {
        public void Start() { }
    }
}

// Another namespace
namespace MyCompany.ProductA.UI
{
    using MyCompany.ProductA.Core;  // Import namespace

    public class Dashboard
    {
        private Engine engine = new Engine();
    }
}
```

### Nested Namespaces

```csharp
// File structure and namespaces
// Folder: Features/Users
// File: User.cs
namespace MyApp.Features.Users
{
    public class User
    {
        public int Id { get; set; }
    }
}

// File: UserService.cs
namespace MyApp.Features.Users
{
    public class UserService
    {
        public User GetUser(int id) { }
    }
}
```

### Using Directives

```csharp
using System;
using System.Collections.Generic;
using MyCompany.Core;
using MyCompany.Utils;

// Alias for disambiguation
using StringDict = System.Collections.Generic.Dictionary<string, string>;

class Program
{
    static void Main()
    {
        var dict = new StringDict();  // Shorter than full name
    }
}
```

### File-Scoped Namespaces (C# 10+)

Modern way to declare a single namespace per file:

```csharp
namespace MyApp.Features.Users;

public class User
{
    public int Id { get; set; }
}

public class UserService
{
    public User GetUser(int id) { }
}
// No braces needed - entire file is in this namespace
```

### Global Usings (C# 10+)

Avoid repeating common imports:

```csharp
// GlobalUsings.cs
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using MyApp.Core;

// Other files automatically have these imports
```

## Assemblies

Compiled units of code distribution:

```
MyApplication.dll
├── MSIL Code
├── Metadata
│   ├── Type definitions
│   ├── Method signatures
│   └── Assembly references
└── Resources
    ├── Images
    ├── Strings
    └── Other data
```

### Assembly Project Structure

```bash
# Create a library
dotnet new classlib -n MyLibrary

# Create a console app that uses the library
dotnet new console -n MyApp
cd MyApp
dotnet add reference ../MyLibrary/MyLibrary.csproj
```

### .csproj (Project File)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>  <!-- dll, exe, etc -->
    <AssemblyName>MyLibrary</AssemblyName>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>
    <InformationalVersion>1.0.0-beta</InformationalVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>

</Project>
```

### Publishing Assemblies

```bash
# Build (creates bin/Debug/net8.0/MyApp.dll)
dotnet build

# Publish (optimized release build)
dotnet publish -c Release -o ./publish

# Self-contained executable
dotnet publish -c Release --self-contained -r win-x64
```

## Assembly Visibility & Access

### Internal Access

```csharp
// Assembly A
namespace MyCompany.LibA
{
    public class PublicClass
    {
        public void PublicMethod() { }
    }

    internal class InternalClass  // Only visible within this assembly
    {
        internal void InternalMethod() { }
    }
}

// Assembly B (referencing Assembly A)
using MyCompany.LibA;

class Program
{
    static void Main()
    {
        var pub = new PublicClass();  // OK
        pub.PublicMethod();            // OK

        // var inter = new InternalClass();  // Compiler error: not visible
    }
}
```

### Internal Visible To (InternalsVisibleTo)

Allow another assembly to see internal members:

```csharp
// In AssemblyInfo.cs or .csproj of Assembly A
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MyCompany.Tests")]

namespace MyCompany.LibA
{
    internal class InternalClass { }
}

// In Assembly MyCompany.Tests
using MyCompany.LibA;

class Tests
{
    void TestInternalClass()
    {
        var inter = new InternalClass();  // OK for tests
    }
}
```

## Package Management (NuGet)

### Adding Packages

```bash
# From command line
dotnet add package Newtonsoft.Json
dotnet add package Serilog --version 2.10.0

# Remove package
dotnet remove package OldPackage
```

### .csproj References

```xml
<ItemGroup>
  <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  <PackageReference Include="Serilog" Version="2.10.0" />
  <PackageReference Include="Microsoft.EntityFrameworkCore" Version="7.0.0" />
</ItemGroup>
```

### Creating a NuGet Package

```xml
<PropertyGroup>
    <PackageId>MyCompany.Core</PackageId>
    <Version>1.0.0</Version>
    <Authors>Your Name</Authors>
    <Description>Core utilities for MyCompany</Description>
    <PackageProjectUrl>https://github.com/mycompany/core</PackageProjectUrl>
    <RepositoryUrl>https://github.com/mycompany/core.git</RepositoryUrl>
</PropertyGroup>
```

```bash
# Build the package
dotnet pack -c Release

# Output: bin/Release/MyCompany.Core.1.0.0.nupkg
```

## Assembly Metadata

### Using Attributes

```csharp
// In AssemblyInfo.cs or .csproj
[assembly: System.Reflection.AssemblyTitle("My Application")]
[assembly: System.Reflection.AssemblyDescription("A sample application")]
[assembly: System.Reflection.AssemblyCompany("MyCompany")]
[assembly: System.Reflection.AssemblyProduct("MyProduct")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyCopyright("Copyright © 2024")]

namespace MyApp { }
```

### Reading Metadata

```csharp
using System.Reflection;

var assembly = Assembly.GetExecutingAssembly();
var version = assembly.GetName().Version;
Console.WriteLine($"Version: {version}");

var attributes = assembly.GetCustomAttributes();
foreach (var attr in attributes)
{
    Console.WriteLine(attr.GetType().Name);
}
```

## Dependency Injection

Modern approach to manage dependencies between assemblies/types:

```csharp
// Define interface in Assembly A
namespace MyCompany.Interfaces
{
    public interface ILogger
    {
        void Log(string message);
    }
}

// Implementation in Assembly B
namespace MyCompany.Implementation
{
    using MyCompany.Interfaces;

    public class ConsoleLogger : ILogger
    {
        public void Log(string message) => Console.WriteLine(message);
    }
}

// Usage in Assembly C (depends on A and B)
namespace MyCompany.App
{
    using MyCompany.Interfaces;
    using MyCompany.Implementation;

    public class Application
    {
        private ILogger logger;

        public Application(ILogger logger)
        {
            this.logger = logger;  // Injected dependency
        }

        public void Run()
        {
            logger.Log("Application started");
        }
    }

    class Program
    {
        static void Main()
        {
            ILogger logger = new ConsoleLogger();
            var app = new Application(logger);
            app.Run();
        }
    }
}
```

## DI Container (ServiceCollection)

Modern .NET uses DI containers:

```csharp
using Microsoft.Extensions.DependencyInjection;

// Setup DI
var services = new ServiceCollection();
services.AddSingleton<ILogger, ConsoleLogger>();
services.AddTransient<IDataService, DataService>();

var serviceProvider = services.BuildServiceProvider();

// Resolve and use
var logger = serviceProvider.GetRequiredService<ILogger>();
logger.Log("Hello from DI");
```

### Lifetime Options

```csharp
// Transient: new instance every time
services.AddTransient<IService, Service>();
var s1 = provider.GetService<IService>();
var s2 = provider.GetService<IService>();
// s1 != s2

// Scoped: new instance per request (common for web)
services.AddScoped<IRepository, Repository>();

// Singleton: single instance for entire application
services.AddSingleton<IConfiguration, Configuration>();
var s1 = provider.GetService<IConfiguration>();
var s2 = provider.GetService<IConfiguration>();
// s1 == s2
```

## Versioning Strategies

### Semantic Versioning

```
Version: Major.Minor.Patch
1.2.3

- Major: Breaking changes
- Minor: New features (backward compatible)
- Patch: Bug fixes
```

### NuGet Versioning in .csproj

```xml
<PropertyGroup>
    <Version>1.2.3</Version>
    <!-- Pre-release -->
    <Version>1.2.3-beta.1</Version>
    <Version>1.2.3-rc.1</Version>
</PropertyGroup>
```

## Best Practices

1. **Namespace Naming**: `Company.Product.Feature` (avoid generic names)
2. **Assembly Grouping**: One logical concept per assembly
3. **Circular Dependencies**: Avoid - refactor with interfaces
4. **Internal by Default**: Make things internal, expose only what's needed
5. **Version Compatibility**: Document breaking changes

## Practice Exercises

1. **Multi-Project Solution**: Create 3 projects (Core, Services, App) with proper dependencies
2. **DI Setup**: Build a console app using ServiceCollection for dependency injection
3. **Package Creation**: Create a simple NuGet package and publish locally
4. **Namespace Organization**: Reorganize existing code with proper namespacing
5. **Assembly Visibility**: Create internal classes and test InternalsVisibleTo

## Key Takeaways

- **Namespaces** organize code logically; follow naming conventions
- **Assemblies** are compiled deployable units containing code and metadata
- **Access modifiers** (public, internal, private) control visibility across assemblies
- **NuGet** manages external dependencies via packages
- **Dependency Injection** decouples assemblies and makes code testable
- **Semantic versioning** communicates compatibility guarantees
- Use **internal** by default, expose only public APIs intentionally
