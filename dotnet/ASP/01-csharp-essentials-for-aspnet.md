# 1. C# Essentials for ASP.NET

## Overview
This topic provides a high-level recap of C# fundamentals critical for ASP.NET development. For comprehensive coverage of C# concepts, refer to `dotnet/csharp/`.

## Quick Reference: Key C# Concepts

### Types & Collections
```csharp
// Value types: int, double, bool, struct
// Reference types: class, interface, delegate, record
// Strings are immutable reference types

var users = new List<User>();  // Generic collections
var dict = new Dictionary<string, int>();  // Key-value pairs
```

### Properties & Indexers
```csharp
public class User
{
    public string Name { get; set; }  // Auto-property
    public int Age { get; private set; }  // Init-only
    
    // Indexer for collection-like access
    public string this[int index] => Items[index];
}
```

### LINQ (Language Integrated Query)
```csharp
var activeUsers = users
    .Where(u => u.IsActive)
    .OrderBy(u => u.Name)
    .Select(u => u.Email)
    .ToList();

// Query syntax alternative
var query = from user in users
            where user.IsActive
            orderby user.Name
            select user.Email;
```

### Async/Await Patterns
```csharp
public async Task<List<User>> GetUsersAsync()
{
    var data = await httpClient.GetAsync("api/users");
    return await data.Content.ReadAsAsync<List<User>>();
}

// Critical: Always use async/await in ASP.NET to avoid blocking threads
public async Task ProcessAsync()
{
    await Task.Delay(1000);  // Non-blocking
}
```

### Generics
```csharp
public class Repository<T> where T : IEntity
{
    public async Task<T> GetByIdAsync(int id) { }
    public async Task<List<T>> GetAllAsync() { }
}
```

### Records (Reference Types with Value Semantics)
```csharp
// Excellent for DTOs and immutable data
public record CreateUserDto(string Name, string Email, int Age);

// Records provide:
// - Automatic equality by value
// - Immutability by default
// - Concise syntax
var dto1 = new CreateUserDto("John", "john@email.com", 30);
var dto2 = new CreateUserDto("John", "john@email.com", 30);
Console.WriteLine(dto1 == dto2);  // true
```

### Nullable Reference Types
```csharp
// Enable in .csproj: <Nullable>enable</Nullable>
public class User
{
    public string Name { get; set; }  // Cannot be null
    public string? MiddleName { get; set; }  // Can be null
    
    public void PrintName()
    {
        Console.WriteLine(Name);  // Safe
        // Console.WriteLine(MiddleName);  // Warning without null-check
        if (MiddleName != null)
            Console.WriteLine(MiddleName);  // Safe
    }
}
```

### Interfaces & Abstraction
```csharp
public interface IUserRepository
{
    Task<User> GetByIdAsync(int id);
    Task<List<User>> GetAllAsync();
}

public interface IValidator<T>
{
    ValidationResult Validate(T instance);
}

// Implementations must satisfy contract
public class UserRepository : IUserRepository
{
    public async Task<User> GetByIdAsync(int id) { }
    public async Task<List<User>> GetAllAsync() { }
}
```

## ASP.NET-Specific C# Patterns

### Attributes
```csharp
// Used extensively in ASP.NET for metadata and configuration
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<UserDto>> GetById(int id) { }
}
```

### Extension Methods
```csharp
// Commonly used for fluent configuration in ASP.NET
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUserServices(
        this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserService, UserService>();
        return services;
    }
}

// Usage in Startup
services.AddUserServices();
```

### Delegates & Func/Action
```csharp
// Frequently used in middleware and configuration
public delegate void LogDelegate(string message);

// Func: returns a value
Func<int, int, int> add = (x, y) => x + y;

// Action: returns void
Action<string> log = msg => Console.WriteLine(msg);

// Middleware example
app.Use(async (context, next) =>
{
    await next.Invoke();
});
```

## Critical Concepts for ASP.NET

### Immutability & Thread Safety
ASP.NET applications are multithreaded. Prefer immutable objects:

```csharp
// Good: Immutable record
public record UserDto(int Id, string Name, string Email);

// Good: Immutable class
public class Settings
{
    public string ApiKey { get; }
    public int Timeout { get; }
    
    public Settings(string apiKey, int timeout)
    {
        ApiKey = apiKey;
        Timeout = timeout;
    }
}

// Avoid: Mutable static state
// static List<User> Users = new();  // DANGER: Thread-unsafe
```

### Value vs Reference Types Performance
```csharp
// Structs are value types - good for small data
public struct Point
{
    public int X { get; set; }
    public int Y { get; set; }
}

// Classes are reference types - good for larger objects
public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
}

// In ASP.NET: Use classes for domain models
// Use records for DTOs
// Use structs rarely (only for very small, immutable values)
```

## Integration with ASP.NET

ASP.NET heavily relies on:
- **LINQ**: Querying collections and databases
- **Async/Await**: Non-blocking I/O operations
- **Generics**: Type-safe abstractions (Repository<T>, Service<T>)
- **Attributes**: Declaring API contracts and authorization rules
- **Interfaces**: Dependency injection and mocking
- **Records**: DTOs and command/query objects
- **Extension Methods**: Fluent configuration and customization

## Recommended Reading

For comprehensive C# coverage:
- `dotnet/csharp/` - Full C# documentation
- [Microsoft C# Documentation](https://docs.microsoft.com/en-us/dotnet/csharp/)
- [C# Language Features by Version](https://docs.microsoft.com/en-us/dotnet/csharp/whats-new/)

## Key Takeaways

1. Async/await is non-negotiable in ASP.NET - use it everywhere
2. Understand generics for type-safe abstractions
3. Leverage records for immutable DTOs
4. Use interfaces extensively for testability and loose coupling
5. Be aware of thread safety in multithreaded environments
6. Records and immutability are preferred over mutable classes for data transfer
7. Attributes provide metadata that ASP.NET uses for routing, validation, and security
