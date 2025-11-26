# 3. Dependency Injection Fundamentals

## Overview

Dependency Injection (DI) is a fundamental pattern in ASP.NET Core that manages object creation and lifetime. It enables loose coupling, testability, and maintainability at scale.

## The Problem: Tight Coupling

Without DI, code becomes tightly coupled and hard to test.

```csharp
// PROBLEM: Tight coupling
public class UserService
{
    private readonly UserRepository _repository;
    
    public UserService()
    {
        // Hard-coded dependency
        _repository = new UserRepository();
    }
    
    public async Task<User> GetUserAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }
}

// Issues:
// 1. Can't test UserService without real database
// 2. Can't swap UserRepository for a different implementation
// 3. UserService creates the dependency (violates Single Responsibility)
// 4. Difficult to manage object lifetime
```

**Testing becomes impossible:**
```csharp
[Fact]
public async Task GetUser_ShouldReturnUser()
{
    var service = new UserService();  // Uses real database!
    var user = await service.GetUserAsync(1);
    // Slow, brittle test - depends on database
}
```

## The Solution: Dependency Injection

```csharp
// SOLUTION: Dependency injection via constructor
public class UserService
{
    private readonly IUserRepository _repository;
    
    // Dependency is injected
    public UserService(IUserRepository repository)
    {
        _repository = repository;
    }
    
    public async Task<User> GetUserAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }
}

// Interface allows any implementation
public interface IUserRepository
{
    Task<User> GetByIdAsync(int id);
}

// Production implementation
public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;
    
    public UserRepository(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<User> GetByIdAsync(int id)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
    }
}

// Test implementation (mock)
public class MockUserRepository : IUserRepository
{
    public async Task<User> GetByIdAsync(int id)
    {
        // Return test data, no database
        return await Task.FromResult(new User { Id = id, Name = "Test User" });
    }
}
```

**Testing becomes trivial:**
```csharp
[Fact]
public async Task GetUser_ShouldReturnUser()
{
    // Inject mock repository
    var mockRepository = new MockUserRepository();
    var service = new UserService(mockRepository);
    
    var user = await service.GetUserAsync(1);
    
    Assert.NotNull(user);
    Assert.Equal("Test User", user.Name);
    // Fast, reliable test - no database
}
```

## ASP.NET Core's Built-in DI Container

ASP.NET Core includes a lightweight DI container. No external library needed.

### Registration: Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// Service registration (lifetime management)
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ApplicationDbContext>();

var app = builder.Build();
// ... middleware configuration ...
app.Run();
```

### Dependency Resolution: Constructor Injection

```csharp
// ASP.NET automatically resolves dependencies
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    
    // DI container provides IUserService
    public UsersController(IUserService userService)
    {
        _userService = userService;
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _userService.GetUserAsync(id);
        return Ok(user);
    }
}
```

## Service Lifetimes

The lifetime of a service determines how many instances exist and how they're shared.

### 1. Transient Lifetime

**Creates a new instance every time it's requested.**

```csharp
builder.Services.AddTransient<IEmailService, EmailService>();

// Example dependency graph:
Request 1 → New EmailService Instance A
Request 2 → New EmailService Instance B
Request 3 → New EmailService Instance A (in same request, reused)
```

**Use When:**
- Service is stateless and lightweight
- Service has no shared state
- Example: Validators, formatters, mappers

```csharp
// Good use of Transient
builder.Services.AddTransient<IValidator<User>, UserValidator>();
builder.Services.AddTransient<IEmailFormatter, HtmlEmailFormatter>();
```

**Caution:**
```csharp
// PROBLEM: Transient DbContext causes issues
builder.Services.AddTransient<ApplicationDbContext>();
// Each dependency gets new DbContext → Hard to track changes

// SOLUTION: Use Scoped instead
builder.Services.AddScoped<ApplicationDbContext>();
```

### 2. Scoped Lifetime

**Creates one instance per HTTP request/scope.**

```csharp
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ApplicationDbContext>();

// Example:
// Request 1: DbContext Instance A is created
//   ├─ UserRepository uses DbContext A
//   ├─ ProductRepository uses DbContext A
//   └─ All changes tracked by DbContext A
// Request 2: DbContext Instance B is created (new scope)
//   ├─ UserRepository uses DbContext B
//   └─ ProductRepository uses DbContext B
```

**Use When:**
- Service has request-scoped state
- Service needs data consistency within a request
- Example: DbContext, Unit of Work, Request state

```csharp
// Perfect for data access
builder.Services.AddScoped<ApplicationDbContext>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
```

**Lifetime Flow:**
```
HTTP Request arrives
    ↓
Scope is created
    ↓
All scoped services instantiated once
    ↓
Request processed (all services use same instances)
    ↓
Response sent
    ↓
Scope is disposed (DbContext.Dispose() called)
    ↓
GC collects unused objects
```

### 3. Singleton Lifetime

**One instance for the entire application lifetime.**

```csharp
builder.Services.AddSingleton<IApplicationSettings, ApplicationSettings>();
builder.Services.AddSingleton<IMemoryCache, MemoryCache>();

// Example:
// Application Start: AppSettings Instance created
// Request 1: Uses same AppSettings Instance
// Request 2: Uses same AppSettings Instance
// Application End: Instance disposed
```

**Use When:**
- Service is stateless and expensive to create
- Service must be shared across all requests
- Example: Configuration, caches, logging, application state

```csharp
// Good Singletons
builder.Services.AddSingleton<IConfiguration>(configuration);
builder.Services.AddSingleton<ILogger>(logger);
builder.Services.AddSingleton<IMemoryCache, MemoryCache>();

// BAD singleton (creates concurrency issues)
// builder.Services.AddSingleton<List<User>>();
// Static, shared state is dangerous in multithreaded environments
```

**Thread Safety Requirement:**
```csharp
// Singletons must be thread-safe
public class ThreadSafeCache : IMemoryCache
{
    private readonly ConcurrentDictionary<string, object> _cache 
        = new();
    
    // Thread-safe implementation
    public bool TryGetValue(object key, out object value)
    {
        return _cache.TryGetValue((string)key, out value);
    }
}

// NOT thread-safe - NEVER use
public class UnsafeCache : IMemoryCache
{
    private Dictionary<string, object> _cache = new();  // DANGER!
    
    public bool TryGetValue(object key, out object value)
    {
        return _cache.TryGetValue((string)key, out value);  // Race condition
    }
}
```

## Lifetime Comparison Table

| Aspect | Transient | Scoped | Singleton |
|--------|-----------|--------|-----------|
| **Instances** | New each time | One per request | One total |
| **Thread-Safe** | N/A | Yes | Must be thread-safe |
| **Memory** | Higher | Moderate | Lowest |
| **Use Case** | Stateless, lightweight | DbContext, repositories | Config, logging, cache |
| **Example** | Validators | Unit of Work | Application settings |

## Advanced DI Patterns

### Factory Pattern with DI

```csharp
// Register with factory
builder.Services.AddScoped<IUserRepository>(sp =>
{
    var context = sp.GetRequiredService<ApplicationDbContext>();
    return new UserRepository(context);
});

// Or for more complex initialization
builder.Services.AddScoped<IEmailService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var apiKey = config["EmailService:ApiKey"];
    var baseUrl = config["EmailService:BaseUrl"];
    return new EmailService(apiKey, baseUrl);
});
```

### Keyed Services (Multiple Implementations)

```csharp
// .NET 8+: Register multiple implementations
builder.Services.AddScoped<IEmailService, GmailService>("gmail");
builder.Services.AddScoped<IEmailService, SendGridService>("sendgrid");

// Inject with key
public class NotificationService
{
    private readonly IEmailService _emailService;
    
    public NotificationService(
        [FromKeyedServices("gmail")] IEmailService emailService)
    {
        _emailService = emailService;
    }
}
```

### Options Pattern

```csharp
// Configuration class
public class EmailSettings
{
    public string ApiKey { get; set; }
    public string From { get; set; }
}

// Register options
builder.Services
    .Configure<EmailSettings>(
        builder.Configuration.GetSection("Email"));

// Use in service
public class EmailService
{
    private readonly IOptions<EmailSettings> _options;
    
    public EmailService(IOptions<EmailSettings> options)
    {
        _options = options;
    }
    
    public void SendEmail(string to, string subject, string body)
    {
        var apiKey = _options.Value.ApiKey;
        var from = _options.Value.From;
        // Use settings
    }
}

// In appsettings.json
{
    "Email": {
        "ApiKey": "sk-...",
        "From": "noreply@company.com"
    }
}
```

### Extension Methods for Organization

```csharp
// Services/ServiceCollectionExtensions.cs
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Data access
        services.AddScoped<ApplicationDbContext>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        
        // Business logic
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProductService, ProductService>();
        
        // Caching
        services.AddSingleton<IMemoryCache, MemoryCache>();
        
        // External services
        services.AddHttpClient<IPaymentGateway, StripePaymentGateway>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(
                    configuration["PaymentGateway:BaseUrl"]);
                client.DefaultRequestHeaders.Add("Authorization",
                    $"Bearer {configuration["PaymentGateway:ApiKey"]}");
            });
        
        return services;
    }
}

// Program.cs becomes cleaner
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationServices(builder.Configuration);
```

## Common Mistakes

### 1. Captive Dependency

```csharp
// PROBLEM: Singleton depends on Scoped
builder.Services.AddScoped<ApplicationDbContext>();
builder.Services.AddSingleton<IUserRepository, UserRepository>();
// UserRepository (singleton) holds reference to DbContext (scoped)
// DbContext is never disposed properly → Memory leak, stale data

// SOLUTION: Reverse the lifetimes
builder.Services.AddScoped<ApplicationDbContext>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
```

### 2. Service Locator Anti-Pattern

```csharp
// BAD: Using service provider as service locator
public class UserController
{
    private readonly IServiceProvider _serviceProvider;
    
    public UserController(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(int id)
    {
        // Service Locator - hard to test, hides dependencies
        var service = _serviceProvider.GetRequiredService<IUserService>();
        return Ok(await service.GetUserAsync(id));
    }
}

// GOOD: Direct dependency injection
public class UserController
{
    private readonly IUserService _userService;
    
    public UserController(IUserService userService)
    {
        _userService = userService;
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(int id)
    {
        return Ok(await _userService.GetUserAsync(id));
    }
}
```

### 3. Disposing Singletons Incorrectly

```csharp
// PROBLEM: Disposing before application ends
var host = builder.Build();

var settings = host.Services.GetRequiredService<ApplicationSettings>();
settings.Dispose();  // WRONG: Disposed too early

host.Run();  // ApplicationSettings used, but already disposed!

// SOLUTION: Let the host manage singleton lifetimes
var host = builder.Build();
await host.RunAsync();  // Host disposes singletons on shutdown
```

## Complete Example: Layered Architecture with DI

```csharp
// Domain/Interfaces
public interface IUserRepository
{
    Task<User> GetByIdAsync(int id);
}

public interface IUserService
{
    Task<UserDto> GetUserAsync(int id);
}

// Infrastructure
public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;
    
    public UserRepository(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<User> GetByIdAsync(int id)
    {
        return await _context.Users.FindAsync(id);
    }
}

// Application
public class UserService : IUserService
{
    private readonly IUserRepository _repository;
    private readonly IMapper _mapper;
    
    public UserService(IUserRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }
    
    public async Task<UserDto> GetUserAsync(int id)
    {
        var user = await _repository.GetByIdAsync(id);
        return _mapper.Map<UserDto>(user);
    }
}

// API
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    
    public UsersController(IUserService userService)
    {
        _userService = userService;
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _userService.GetUserAsync(id);
        return Ok(user);
    }
}

// Program.cs - Complete setup
var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddScoped<ApplicationDbContext>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();

// Add controllers
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
```

## Key Takeaways

1. **DI enables loose coupling**: Depend on abstractions, not concretions
2. **Lifetimes matter**: Choose Transient, Scoped, or Singleton wisely
3. **Scoped for DbContext**: Always use scoped for data access
4. **Singleton for expensive services**: Config, logging, caches
5. **Transient for stateless helpers**: Validators, formatters, mappers
6. **Thread-safe singletons**: Required for multithreaded ASP.NET
7. **Constructor injection is standard**: Other injection types are anti-patterns
8. **Program.cs is entry point**: Configure all services here
9. **Testability is key**: DI makes mocking and testing trivial
10. **Extension methods for organization**: Keep Program.cs clean and readable

## Further Reading

- [Microsoft DI Documentation](https://docs.microsoft.com/en-us/dotnet/core/extensions/dependency-injection)
- [ASP.NET Core Dependency Injection](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection)
- [SOLID Principles](https://en.wikipedia.org/wiki/SOLID) - DI supports these

