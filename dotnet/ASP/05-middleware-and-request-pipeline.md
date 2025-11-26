# 5. ASP.NET Core Middleware & Request Pipeline

## Overview

Middleware is the core of ASP.NET Core's request processing. The middleware pipeline is a chain of components that handle HTTP requests and responses. Understanding middleware is essential for building secure, performant, and maintainable applications.

## The Middleware Pipeline

Every HTTP request flows through a series of middleware components in a specific order.

### Visual Pipeline

```
Request arrives
    ↓
┌─────────────────────────┐
│   Middleware 1 (HTTPS)  │
│  ╔───────────────────╗  │
│  ║   Before logic    ║  │
│  ╚───────────────────╝  │
│           ↓             │
│  ┌─────────────────┐   │
│  │ Middleware 2    │   │
│  │ (Auth)          │   │
│  ├─────────────────┤   │
│  │ (Routing)       │   │
│  │ (Controllers)   │   │  ← Request processing
│  │ (Handlers)      │   │
│  ├─────────────────┤   │
│  │ Middleware 3    │   │
│  │ (Logging)       │   │
│  ├─────────────────┤   │
│  │ Middleware 4    │   │
│  │ (Exception)     │   │
│  └─────────────────┘   │
│           ↓             │
│  ╔───────────────────╗  │
│  ║   After logic     ║  │
│  ╚───────────────────╝  │
└─────────────────────────┘
    ↓
Response sent to client
```

### Request vs Response Flow

```
Incoming Request
    ↓
Middleware 1 (before) ─→
    ↓                   ↑
Middleware 2 (before) ─→ Middleware 2 (after)
    ↓                            ↑
Middleware 3 (before) ─→ Middleware 3 (after)
    ↓                            ↑
Endpoint (controller/handler)    │
    ↓                            │
Response ──────────────────────→ ↑
                   (bubbles back)
```

## Built-in Middleware

ASP.NET Core provides many built-in middleware components.

### Essential Middleware Stack

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddAuthorization();

var app = builder.Build();

// Middleware pipeline (order matters!)

// 1. Exception handling - FIRST!
app.UseExceptionHandler("/error");

// 2. HTTPS redirect
app.UseHttpsRedirection();

// 3. Security headers (custom middleware)
app.UseSecurityHeaders();

// 4. Routing - must come before authorization and endpoints
app.UseRouting();

// 5. Authentication - who are you?
app.UseAuthentication();

// 6. Authorization - what can you do?
app.UseAuthorization();

// 7. Custom middleware
app.UseRequestLogging();

// 8. Endpoints - LAST!
app.MapControllers();

app.Run();
```

### Detailed Middleware Components

#### 1. Exception Handler Middleware

```csharp
// Top of pipeline - catches all exceptions
app.UseExceptionHandler("/error");

// Or with custom handler
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        
        var exceptionHandler = context.Features
            .Get<IExceptionHandlerPathFeature>();
        
        var exception = exceptionHandler?.Error;
        
        var response = new
        {
            error = new
            {
                message = "An error occurred",
                exception = exception?.Message
            }
        };
        
        await context.Response.WriteAsJsonAsync(response);
    });
});

// In controller to access error page
public class ErrorController : ControllerBase
{
    [Route("/error")]
    public IActionResult Error()
    {
        var exceptionHandler = HttpContext.Features
            .Get<IExceptionHandlerPathFeature>();
        
        return Problem(detail: exceptionHandler?.Error.Message);
    }
}
```

#### 2. HTTPS Redirection

```csharp
app.UseHttpsRedirection();  // HTTP → HTTPS
```

Converts:
```
http://example.com/api/users → https://example.com/api/users
```

#### 3. Routing Middleware

```csharp
// Parse route and determine endpoint
app.UseRouting();

// Later...
app.MapControllers();  // Map attribute routes on controllers
app.MapGet("/", () => "Hello");  // Map minimal APIs
```

#### 4. Authentication Middleware

```csharp
// Extract credentials from request
// Validate and create ClaimsPrincipal
app.UseAuthentication();

// Example flow
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        // Request.HttpContext.User is populated by UseAuthentication()
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        return Ok(await _service.GetUserAsync(id));
    }
}
```

#### 5. Authorization Middleware

```csharp
// Check if authenticated principal has required permissions
app.UseAuthorization();

// Usage in controller
[Authorize]  // Must be authenticated
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    return Ok(await _service.GetUserAsync(id));
}

[Authorize(Roles = "Admin")]  // Must be in Admin role
[HttpDelete("{id}")]
public async Task<IActionResult> DeleteUser(int id)
{
    await _service.DeleteUserAsync(id);
    return NoContent();
}

[Authorize(Policy = "AdminOrManager")]  // Policy-based
[HttpPut("{id}")]
public async Task<IActionResult> UpdateUser(int id, UpdateUserDto dto)
{
    await _service.UpdateUserAsync(id, dto);
    return NoContent();
}
```

#### 6. Static Files Middleware

```csharp
// Serve static files (images, CSS, JS, etc.)
app.UseStaticFiles();

// Custom configuration
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "static")),
    RequestPath = "/files",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "public,max-age=3600";
    }
});
```

#### 7. Response Compression Middleware

```csharp
builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<GzipCompressionProvider>();
    options.Providers.Add<BrotliCompressionProvider>();
});

app.UseResponseCompression();
```

## Custom Middleware

Create middleware for cross-cutting concerns: logging, timing, security, transformation.

### Convention-Based Middleware

```csharp
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    
    public RequestLoggingMiddleware(RequestDelegate next, 
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.NewGuid().ToString();
        
        // Before request processing
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation(
            "Request {RequestId} started: {Method} {Path}",
            requestId, context.Request.Method, context.Request.Path);
        
        // Add request ID to response
        context.Response.Headers.Add("X-Request-ID", requestId);
        
        // Continue pipeline
        await _next(context);
        
        // After request processing
        stopwatch.Stop();
        
        _logger.LogInformation(
            "Request {RequestId} completed: {StatusCode} ({ElapsedMs}ms)",
            requestId, context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
    }
}

// Register in Program.cs
app.UseMiddleware<RequestLoggingMiddleware>();
```

### Inline Middleware

```csharp
// Simple inline middleware
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Custom-Header", "value");
    
    await next();  // Continue to next middleware
    
    // Can modify response after endpoint
});

// Middleware that short-circuits pipeline
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/ping")
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsync("pong");
        return;  // Don't call next() - stops pipeline
    }
    
    await next();
});
```

## Common Custom Middleware Examples

### Request/Response Timing

```csharp
public class TimingMiddleware
{
    private readonly RequestDelegate _next;
    
    public TimingMiddleware(RequestDelegate next) => _next = next;
    
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        
        context.Response.OnStarting(() =>
        {
            stopwatch.Stop();
            context.Response.Headers.Add("X-Response-Time-Ms",
                stopwatch.ElapsedMilliseconds.ToString());
            return Task.CompletedTask;
        });
        
        await _next(context);
    }
}
```

### Correlation ID Propagation

```csharp
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    
    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;
    
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers
            .TryGetValue("X-Correlation-ID", out var values)
            ? values.ToString()
            : Guid.NewGuid().ToString();
        
        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers.Add("X-Correlation-ID", correlationId);
        
        await _next(context);
    }
}

// Use in services
public class UserService : IUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    public UserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }
    
    public async Task<User> GetUserAsync(int id)
    {
        var correlationId = _httpContextAccessor.HttpContext?.Items
            ["CorrelationId"];
        
        _logger.LogInformation(
            "Getting user {UserId} [CorrelationId: {CorrelationId}]",
            id, correlationId);
        
        return await _repository.GetByIdAsync(id);
    }
}
```

### API Key Authentication

```csharp
public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private const string ApiKeyHeaderName = "X-API-Key";
    
    public ApiKeyMiddleware(RequestDelegate next) => _next = next;
    
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(
            ApiKeyHeaderName, out var extractedApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("API key missing");
            return;
        }
        
        var configuration = context.RequestServices
            .GetRequiredService<IConfiguration>();
        var apiKey = configuration.GetValue<string>("ApiKey");
        
        if (!apiKey.Equals(extractedApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid API key");
            return;
        }
        
        await _next(context);
    }
}

// Register
app.UseMiddleware<ApiKeyMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
```

### Request/Response Body Logging

```csharp
public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;
    
    public RequestResponseLoggingMiddleware(RequestDelegate next,
        ILogger<RequestResponseLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        // Log request body
        context.Request.EnableBuffering();
        
        var requestBody = await new StreamReader(context.Request.Body)
            .ReadToEndAsync();
        
        context.Request.Body.Position = 0;
        
        _logger.LogInformation("Request body: {Body}", requestBody);
        
        // Capture response body
        var originalResponseBody = context.Response.Body;
        
        using (var memoryStream = new MemoryStream())
        {
            context.Response.Body = memoryStream;
            
            await _next(context);
            
            // Log response body
            memoryStream.Position = 0;
            var responseBody = await new StreamReader(memoryStream)
                .ReadToEndAsync();
            
            _logger.LogInformation("Response body: {Body}", responseBody);
            
            // Copy response to original body
            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(originalResponseBody);
        }
        
        context.Response.Body = originalResponseBody;
    }
}
```

### Error Handling Middleware

```csharp
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    
    public ErrorHandlingMiddleware(RequestDelegate next,
        ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            
            var response = new
            {
                status = context.Response.StatusCode,
                message = "An error occurred",
                details = ex.Message  // Don't expose in production!
            };
            
            await context.Response.WriteAsJsonAsync(response);
        }
    }
}
```

## Middleware Ordering Guidelines

Order matters significantly:

```csharp
var app = builder.Build();

// 1. ERROR HANDLING - Always first
app.UseExceptionHandler("/error");
app.UseStatusCodePages();  // 404, 500, etc.

// 2. SECURITY - Before routing/auth
app.UseHttpsRedirection();
app.UseHsts();

// 3. CORS - Before routing/auth
app.UseCors();

// 4. STATIC FILES - No need for auth/routing
app.UseStaticFiles();

// 5. ROUTING - Required for auth/endpoints
app.UseRouting();

// 6. CUSTOM LOGGING/TIMING
app.UseMiddleware<RequestLoggingMiddleware>();

// 7. AUTHENTICATION - Who are you?
app.UseAuthentication();

// 8. AUTHORIZATION - What can you do?
app.UseAuthorization();

// 9. ENDPOINTS - The actual handlers
app.MapControllers();
app.MapRazorPages();

app.Run();
```

## Context-Specific Middleware

Middleware can be applied to specific routes:

```csharp
var app = builder.Build();

// Global middleware
app.UseHttpsRedirection();
app.UseRouting();

app.UseEndpoints(endpoints =>
{
    // Middleware only for /admin routes
    endpoints.MapControllers()
        .WithMetadata(new RequireAuthorizationAttribute());
    
    // Different middleware chain for specific routes
    endpoints.MapGet("/public", () => "Public")
        .AllowAnonymous();
    
    endpoints.MapGet("/private", () => "Private")
        .RequireAuthorization();
});
```

## Middleware Best Practices

### 1. Keep Middleware Focused
```csharp
// Good - single responsibility
public class LoggingMiddleware { }
public class TimingMiddleware { }
public class SecurityHeadersMiddleware { }

// Bad - too many concerns
public class DoEverythingMiddleware { }
```

### 2. Use Dependency Injection

```csharp
public class MyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMyService _service;
    
    // Constructor injection
    public MyMiddleware(RequestDelegate next, IMyService service)
    {
        _next = next;
        _service = service;
    }
    
    // HttpContext is injected at invocation
    public async Task InvokeAsync(HttpContext context)
    {
        // Use scoped services from HttpContext
        var scopedService = context.RequestServices
            .GetRequiredService<IScopedService>();
        
        await _next(context);
    }
}
```

### 3. Don't Block the Pipeline Unnecessarily

```csharp
// Good - async all the way
public async Task InvokeAsync(HttpContext context)
{
    await _next(context);
}

// Bad - blocks thread
public async Task InvokeAsync(HttpContext context)
{
    Thread.Sleep(1000);  // Blocks thread!
    await _next(context);
}
```

### 4. Use UseWhen for Conditional Middleware

```csharp
// Only apply middleware to certain paths
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    appBuilder =>
    {
        appBuilder.UseMiddleware<ApiKeyMiddleware>();
    });

// Other paths skip this middleware
app.UseRouting();
app.MapControllers();
```

### 5. Avoid Capturing Scoped Services in Constructor

```csharp
// Bad - scoped service captured in constructor
public class BadMiddleware
{
    private readonly IScopedService _service;  // WRONG!
    
    public BadMiddleware(RequestDelegate next, IScopedService service)
    {
        _next = next;
        _service = service;  // Reused across requests
    }
}

// Good - scoped service obtained per request
public class GoodMiddleware
{
    private readonly RequestDelegate _next;
    
    public GoodMiddleware(RequestDelegate next)
    {
        _next = next;
    }
    
    public async Task InvokeAsync(HttpContext context, IScopedService service)
    {
        // Service obtained fresh per request
        await _next(context);
    }
}
```

## Middleware Composition with Extension Methods

```csharp
// Organize middleware registration
public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityMiddleware(
        this IApplicationBuilder app)
    {
        app.UseHttpsRedirection();
        app.UseHsts();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        return app;
    }
    
    public static IApplicationBuilder UseLoggingMiddleware(
        this IApplicationBuilder app)
    {
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseMiddleware<TimingMiddleware>();
        return app;
    }
    
    public static IApplicationBuilder UseApiMiddleware(
        this IApplicationBuilder app)
    {
        app.UseSecurityMiddleware();
        app.UseLoggingMiddleware();
        app.UseCors();
        return app;
    }
}

// Program.cs becomes clean
var app = builder.Build();

app.UseApiMiddleware();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

## Key Takeaways

1. **Pipeline order is critical**: Exceptions first, endpoints last
2. **Middleware is a chain of responsibility**: Each calls next()
3. **Request flows down, response flows up**: Both can be modified
4. **Built-in middleware handles common concerns**: Auth, routing, compression
5. **Custom middleware extends functionality**: Logging, timing, security headers
6. **DI works in middleware**: Inject services for dependency management
7. **Short-circuit carefully**: Only skip next() when appropriate
8. **Keep middleware focused**: Single responsibility principle
9. **Scoped services per request**: Use dependency injection properly
10. **Composition with extensions**: Organize middleware registration for maintainability

