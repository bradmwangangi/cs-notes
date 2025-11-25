# Chapter 2: ASP.NET Core Basics for APIs

## 2.1 Project Structure and Setup

### Creating an API Project

ASP.NET Core provides templates optimized for API development:

```bash
dotnet new webapi -n MyApi
```

This creates a minimal API project with:
- **Program.cs** - Entry point, configuration, service registration
- **appsettings.json** - Configuration values
- **Properties/launchSettings.json** - Debug/run configurations
- Sample controller (optionally removed for Minimal APIs)

### Project File Structure (csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>              <!-- Enable nullable reference types -->
    <ImplicitUsings>enable</ImplicitUsings> <!-- Auto-import common namespaces -->
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
  </ItemGroup>

</Project>
```

**Key Notes:**
- `TargetFramework`: .NET version to target (8.0 recommended for latest features)
- `Nullable`: Enables C# nullable reference types (important for null safety)
- `ImplicitUsings`: Auto-imports common namespaces (less boilerplate)

### Directory Organization

Typical API project structure:

```
MyApi/
├── Controllers/          # Controller classes (if using controllers)
├── Middleware/           # Custom middleware
├── Services/             # Business logic, application services
├── Models/               # Domain models and entities
├── Data/                 # DbContext and migrations
├── DTOs/                 # Data transfer objects (request/response)
├── Filters/              # Action filters, exception handlers
├── Utilities/            # Helper functions, extensions
├── Program.cs            # Startup configuration
└── appsettings.json      # Configuration
```

For larger systems, organize by domain:

```
MyApi/
├── Users/
│   ├── Controllers/
│   ├── Services/
│   ├── Models/
│   └── DTOs/
├── Orders/
│   ├── Controllers/
│   ├── Services/
│   ├── Models/
│   └── DTOs/
└── Shared/
```

---

## 2.2 Program.cs and Configuration

### Basic Program.cs Structure

```csharp
var builder = WebApplicationBuilder.CreateBuilder(args);

// 1. Add services to DI container
builder.Services.AddControllers();
builder.Services.AddScoped<IUserService, UserService>();

// 2. Build the app
var app = builder.Build();

// 3. Configure middleware pipeline
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// 4. Run the app
app.Run();
```

This pattern is called the **Startup Pattern** in .NET 6+.

### Service Registration

Services are registered in the DI container:

```csharp
// Transient: new instance every time
builder.Services.AddTransient<IRepository, Repository>();

// Scoped: new instance per request (most common for APIs)
builder.Services.AddScoped<IUserService, UserService>();

// Singleton: one instance for entire application lifetime
builder.Services.AddSingleton<IConfiguration>();
```

**When to use each:**
- **Transient**: Stateless utilities, lightweight objects
- **Scoped**: Database services, request-specific context, business logic services
- **Singleton**: Configuration, loggers, caches shared across requests

Example service registration:

```csharp
builder.Services.AddScoped<IDbContext, AppDbContext>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();
```

### Configuration Management

Configuration comes from multiple sources in priority order:
1. Environment variables (overrides everything)
2. Command-line arguments
3. appsettings.json (or appsettings.{Environment}.json)
4. User secrets (development only)

**appsettings.json:**
```json
{
  "Database": {
    "ConnectionString": "Server=localhost;Database=MyApp;"
  },
  "Jwt": {
    "Secret": "your-secret-key",
    "ExpirationMinutes": 60
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

**Environment-specific file (appsettings.Production.json):**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  }
}
```

**Accessing configuration in code:**

```csharp
// Inject IConfiguration
public class UserService
{
    private readonly IConfiguration _config;
    
    public UserService(IConfiguration config)
    {
        _config = config;
    }
    
    public void DoSomething()
    {
        var connectionString = _config["Database:ConnectionString"];
        var jwtSecret = _config.GetSection("Jwt")["Secret"];
    }
}
```

**Using options pattern (recommended):**

```csharp
// Define options class
public class JwtOptions
{
    public string Secret { get; set; }
    public int ExpirationMinutes { get; set; }
}

// Register in Program.cs
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection("Jwt")
);

// Inject IOptions<JwtOptions> in services
public class JwtService
{
    private readonly JwtOptions _options;
    
    public JwtService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }
}
```

**Options pattern advantages:**
- Type-safe configuration
- Validation support
- Can be reloaded from file changes
- Better testability

---

## 2.3 Dependency Injection

Dependency Injection (DI) is fundamental to ASP.NET Core.

### Purpose

DI decouples components: classes depend on abstractions (interfaces), not concrete implementations.

**Without DI (tightly coupled):**
```csharp
public class UserService
{
    private readonly UserRepository _repo = new UserRepository();
    
    public User GetUser(int id) => _repo.GetById(id);
}
```

Problems:
- Hard to test (can't substitute fake repository)
- Hard to change implementation
- UserService can't exist without UserRepository

**With DI (loosely coupled):**
```csharp
public class UserService
{
    private readonly IUserRepository _repo;
    
    public UserService(IUserRepository repo)
    {
        _repo = repo;
    }
    
    public User GetUser(int id) => _repo.GetById(id);
}
```

Benefits:
- Easy to test (inject mock repository)
- Easy to swap implementations
- Clear dependencies

### Constructor Injection

Preferred method in ASP.NET Core:

```csharp
public class OrderService
{
    private readonly IUserRepository _userRepo;
    private readonly IOrderRepository _orderRepo;
    private readonly ILogger<OrderService> _logger;
    
    public OrderService(
        IUserRepository userRepo,
        IOrderRepository orderRepo,
        ILogger<OrderService> logger)
    {
        _userRepo = userRepo;
        _orderRepo = orderRepo;
        _logger = logger;
    }
    
    public Order CreateOrder(int userId, OrderDto dto)
    {
        var user = _userRepo.GetById(userId);
        if (user == null)
            throw new InvalidOperationException("User not found");
        
        var order = new Order { UserId = userId, /* ... */ };
        _orderRepo.Add(order);
        _logger.LogInformation("Order created: {OrderId}", order.Id);
        return order;
    }
}
```

### Service Lifetime Issues

**Common mistake: Captive dependency**

```csharp
// WRONG: Scoped service injected into Singleton
builder.Services.AddSingleton<ISomeSingleton, SomeSingleton>();
builder.Services.AddScoped<ISomeScoped, SomeScoped>();

public class SomeSingleton
{
    // Singleton depends on Scoped - always sees same instance!
    public SomeSingleton(ISomeScoped scoped) { }
}
```

Singleton's scoped dependency is captured on first request and reused forever. Scoped services are meant to be request-isolated.

**Rule:** Only shorter-lived services can be injected into longer-lived services.
- Singleton → can inject Singleton
- Scoped → can inject Scoped or Singleton
- Transient → can inject anything

---

## 2.4 Middleware Pipeline

Middleware components process HTTP requests and responses. They form a pipeline.

### Middleware Ordering

```csharp
var app = builder.Build();

// Order matters! Earlier middleware runs first for requests,
// but in reverse order for responses.

app.UseHttpsRedirection();      // Redirect HTTP to HTTPS
app.UseAuthentication();        // Authenticate the user
app.UseAuthorization();         // Check if authorized
app.MapControllers();           // Route to controllers

app.Run();
```

**Request flow (left to right):**
```
Request → HttpsRedirection → Authentication → Authorization → Controller → Response
```

**Response flow (right to left):**
```
Response ← HttpsRedirection ← Authentication ← Authorization ← Controller ← Status Code
```

### Common Middleware

**UseHttpsRedirection**
- Redirects HTTP requests to HTTPS
- Essential for production APIs

**UseAuthentication**
- Populates `HttpContext.User` with claims from token/credentials
- Must come before `UseAuthorization`

**UseAuthorization**
- Checks if `HttpContext.User` has required permissions
- Returns 403 Forbidden if not authorized

**UseExceptionHandler**
- Catches unhandled exceptions
- Returns appropriate error response
- Important for consistent error handling

Example:
```csharp
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        
        var exceptionHandlerPathFeature = 
            context.Features.Get<IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;
        
        var response = new { error = exception?.Message };
        await context.Response.WriteAsJsonAsync(response);
    });
});
```

**UseRouting and MapControllers**
- `UseRouting`: Determines which endpoint matches
- `MapControllers`: Maps controller actions to routes

For Minimal APIs, replaces `MapControllers`:
```csharp
app.MapGet("/api/users/{id}", GetUserHandler);
```

---

## 2.5 Request/Response Processing

### Request Binding

ASP.NET Core automatically binds request data to handler parameters:

```csharp
// Query parameter
GET /api/users?name=John
public IActionResult GetUsers(string name) { }

// Route parameter
GET /api/users/123
public IActionResult GetUser(int id) { }

// Request body (POST/PUT)
POST /api/users
public IActionResult CreateUser([FromBody] CreateUserRequest request) { }

// Header value
public IActionResult Something([FromHeader] string authorization) { }
```

**[FromBody]** attribute explicitly indicates body binding (usually implicit for POST/PUT).

**[FromQuery]** explicitly binds query parameters (usually implicit).

**[FromRoute]** explicitly binds route parameters (usually implicit).

### Model Binding and Validation

When a model is bound from request data, validation occurs:

```csharp
public class CreateUserRequest
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100)]
    public string Name { get; set; }
    
    [EmailAddress]
    public string Email { get; set; }
    
    [Range(18, 120)]
    public int Age { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpPost]
    public IActionResult CreateUser(CreateUserRequest request)
    {
        // If validation fails, 400 Bad Request returned automatically
        // ModelState contains validation errors
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        // request is valid here
        return Ok();
    }
}
```

ASP.NET Core validates models and returns `BadRequest` automatically if `[ApiController]` attribute is present. **You should still check `ModelState.IsValid` manually if needed.**

### Content Negotiation in ASP.NET Core

By default, ASP.NET Core APIs return JSON. Configure other formats:

```csharp
builder.Services.AddControllers()
    .AddXmlSerializerFormatters(); // Support XML too

// Or in action:
[Produces("application/json", "application/xml")]
public IActionResult GetUsers() { }
```

The `Accept` header determines format:
```
GET /api/users
Accept: application/xml
```

Returns XML instead of JSON.

---

## 2.6 Error Handling

### Global Exception Handler

Middleware catches all unhandled exceptions:

```csharp
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionHandlerPathFeature = 
            context.Features.Get<IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;
        
        var problemDetails = new ProblemDetails
        {
            Type = "https://api.example.com/errors/server-error",
            Title = "An error occurred",
            Status = StatusCodes.Status500InternalServerError,
            Detail = exception?.Message,
            Instance = context.Request.Path
        };
        
        context.Response.StatusCode = problemDetails.Status.Value;
        context.Response.ContentType = "application/problem+json";
        
        await context.Response.WriteAsJsonAsync(problemDetails);
    });
});
```

### Application-Specific Exceptions

Create custom exceptions for domain logic:

```csharp
public class DomainException : Exception
{
    public int StatusCode { get; set; }
    
    public DomainException(string message, int statusCode = 400) 
        : base(message)
    {
        StatusCode = statusCode;
    }
}

public class UserNotFoundException : DomainException
{
    public UserNotFoundException(int userId)
        : base($"User {userId} not found", 404) { }
}

// Catch in exception handler:
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionHandlerPathFeature = 
            context.Features.Get<IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;
        
        int statusCode = 500;
        string title = "Internal Server Error";
        
        if (exception is UserNotFoundException)
        {
            statusCode = 404;
            title = "Not Found";
        }
        else if (exception is DomainException domainEx)
        {
            statusCode = domainEx.StatusCode;
            title = domainEx.Message;
        }
        
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception?.Message
        };
        
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(problemDetails);
    });
});
```

---

## 2.7 Logging

### Structured Logging

Logging is injected automatically:

```csharp
public class UserService
{
    private readonly ILogger<UserService> _logger;
    
    public UserService(ILogger<UserService> logger)
    {
        _logger = logger;
    }
    
    public void CreateUser(string name, string email)
    {
        _logger.LogInformation(
            "Creating user {UserName} with email {UserEmail}",
            name, email);
        
        // Do work...
        
        _logger.LogError("Failed to create user: {ErrorMessage}", ex.Message);
    }
}
```

**Benefits of structured logging:**
- Logs are queryable (can filter by UserName, UserEmail)
- Easier debugging
- Works well with log aggregation services

### Configuration

In appsettings.json:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.EntityFrameworkCore": "Debug"
    }
  }
}
```

Log levels:
- **Trace** - Very detailed, typically only enabled in development
- **Debug** - Diagnostic info
- **Information** - General information flow
- **Warning** - Warnings about potential problems
- **Error** - Errors that occurred
- **Critical** - Critical failures
- **None** - Disable logging

---

## 2.8 Health Checks

Health checks indicate application status:

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>()
    .AddCheck<CustomHealthCheck>("custom-check");

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/detailed", new HealthCheckOptions
{
    ResponseWriter = WriteResponse
});

public class CustomHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        var isHealthy = CheckDependencies();
        
        return Task.FromResult(
            isHealthy 
                ? HealthCheckResult.Healthy("All dependencies OK")
                : HealthCheckResult.Unhealthy("Database unreachable")
        );
    }
}
```

Responds at `/health`:
```json
{
  "status": "Healthy",
  "checks": {
    "DbContext": { "status": "Healthy" },
    "custom-check": { "status": "Healthy" }
  }
}
```

---

## Summary

ASP.NET Core provides the foundation for API development: dependency injection for loose coupling, middleware pipeline for request processing, configuration management for environment handling, and health checks for monitoring. The next chapter covers the two main approaches to building endpoints: Controllers and Minimal APIs.
