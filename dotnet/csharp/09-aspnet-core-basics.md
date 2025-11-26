# ASP.NET Core Basics

ASP.NET Core is the web framework for building web applications and APIs.

## Project Setup

```bash
# Create ASP.NET Core web app
dotnet new web -n MyWebApp
cd MyWebApp

# Create MVC app
dotnet new mvc -n MyMvcApp

# Create Web API
dotnet new webapi -n MyApi

# Run
dotnet run
```

## Minimal APIs (Simplest)

ASP.NET Core 6+:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Define routes
app.MapGet("/", () => "Hello, World!");

app.MapGet("/users/{id}", (int id) => 
    $"User {id}");

app.MapPost("/users", (User user) =>
{
    // Save user
    return Results.Created($"/users/{user.Id}", user);
});

app.Run();

public record User(int Id, string Name, string Email);
```

## Middleware

Pipeline of components processing requests:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Middleware runs in order
app.Use(async (context, next) =>
{
    // Before request
    context.Response.Headers.Add("X-Custom-Header", "MyValue");
    
    await next();  // Call next middleware
    
    // After request
    Console.WriteLine($"Request completed: {context.Request.Path}");
});

// Built-in middleware
app.UseStaticFiles();  // Serve static files (wwwroot)
app.UseRouting();      // Route incoming requests
app.MapControllers();  // Map controller routes

app.Run();
```

## Controllers

Handles HTTP requests:

```csharp
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    public IActionResult GetUser(int id)
    {
        if (id <= 0)
            return BadRequest("Invalid ID");

        var user = new { Id = id, Name = "Alice" };
        return Ok(user);
    }

    [HttpPost]
    public IActionResult CreateUser([FromBody] CreateUserRequest request)
    {
        var user = new { Id = 1, Name = request.Name };
        return CreatedAtAction(nameof(GetUser), new { id = 1 }, user);
    }

    [HttpPut("{id}")]
    public IActionResult UpdateUser(int id, [FromBody] UpdateUserRequest request)
    {
        return NoContent();
    }

    [HttpDelete("{id}")]
    public IActionResult DeleteUser(int id)
    {
        return Ok();
    }
}

public record CreateUserRequest(string Name, string Email);
public record UpdateUserRequest(string Name, string Email);
```

## Dependency Injection in Controllers

```csharp
public interface IUserService
{
    User GetUser(int id);
    void SaveUser(User user);
}

public class UserService : IUserService
{
    public User GetUser(int id) => new User { Id = id, Name = "Alice" };
    public void SaveUser(User user) { }
}

// Register in Program.cs
builder.Services.AddScoped<IUserService, UserService>();

// Use in controller
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService userService;

    public UsersController(IUserService userService)
    {
        this.userService = userService;
    }

    [HttpGet("{id}")]
    public IActionResult GetUser(int id)
    {
        var user = userService.GetUser(id);
        return Ok(user);
    }
}
```

## Routing

### Route Attributes

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    // GET /api/products
    [HttpGet]
    public IActionResult GetAll() { }

    // GET /api/products/5
    [HttpGet("{id}")]
    public IActionResult GetById(int id) { }

    // GET /api/products/5/reviews
    [HttpGet("{id}/reviews")]
    public IActionResult GetReviews(int id) { }

    // Custom route
    [HttpPost("/products/bulk")]
    public IActionResult BulkCreate() { }
}
```

### Route Constraints

```csharp
[ApiController]
[Route("api/[controller]")]
public class ItemsController : ControllerBase
{
    // Must be integer
    [HttpGet("{id:int}")]
    public IActionResult GetById(int id) { }

    // Must be GUID
    [HttpGet("{id:guid}")]
    public IActionResult GetByGuid(Guid id) { }

    // Must match regex
    [HttpGet("{username:alpha}")]
    public IActionResult GetByUsername(string username) { }

    // Multiple constraints
    [HttpGet("{id:int:min(1)}")]
    public IActionResult GetMinId(int id) { }
}
```

## Request/Response Binding

### FromBody

```csharp
public class OrderRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

[HttpPost]
public IActionResult CreateOrder([FromBody] OrderRequest request)
{
    // Automatically deserializes JSON body
    return Ok();
}
```

### FromRoute, FromQuery, FromHeader

```csharp
[HttpGet("{id}")]
public IActionResult Get(
    [FromRoute] int id,           // From URL path
    [FromQuery] string filter,    // From query string
    [FromHeader] string authorization) // From header
{
    return Ok();
}

// Usage:
// GET /users/5?filter=active
// Header: Authorization: Bearer token
```

### FromServices

```csharp
[HttpGet]
public IActionResult GetData([FromServices] IUserService userService)
{
    var user = userService.GetUser(1);
    return Ok(user);
}
```

## Response Types

```csharp
[ApiController]
[Route("api/[controller]")]
public class ResponsesController : ControllerBase
{
    [HttpGet("ok")]
    public IActionResult Ok() => Ok(new { message = "Success" });

    [HttpGet("created")]
    public IActionResult Created() 
        => CreatedAtAction(nameof(GetById), new { id = 1 }, new { id = 1 });

    [HttpGet("nocontent")]
    public IActionResult NoContent() => NoContent();

    [HttpGet("badrequest")]
    public IActionResult BadRequest() => BadRequest("Invalid input");

    [HttpGet("notfound")]
    public IActionResult NotFound() => NotFound();

    [HttpGet("unauthorized")]
    public IActionResult Unauthorized() => Unauthorized();

    [HttpGet("forbidden")]
    public IActionResult Forbidden() => Forbid();

    [HttpGet("conflict")]
    public IActionResult Conflict() => Conflict();

    [HttpGet("error")]
    public IActionResult InternalError() => StatusCode(500, "Server error");

    [HttpGet("custom")]
    public IActionResult Custom() => StatusCode(418, "I'm a teapot");

    [HttpGet("id")]
    public int GetById() => 42;  // Auto-serializes to JSON
}
```

## Configuration

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "Database": {
    "ConnectionString": "Server=localhost;Database=MyDb"
  },
  "Jwt": {
    "SecretKey": "your-secret-key-here",
    "Issuer": "MyApp",
    "Audience": "MyAppUsers"
  },
  "AllowedHosts": "*"
}
```

### Access Configuration

```csharp
// In Program.cs
var builder = WebApplication.CreateBuilder(args);

// Access configuration
var connectionString = builder.Configuration["Database:ConnectionString"];
var jwtSecret = builder.Configuration["Jwt:SecretKey"];

// Strongly-typed configuration
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>();

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

var app = builder.Build();
```

### Environment-Specific Settings

```bash
# appsettings.json (default)
# appsettings.Development.json
# appsettings.Production.json
```

```csharp
// In Program.cs
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
}
```

## Static Files

```csharp
// Serve files from wwwroot folder
app.UseStaticFiles();

// Serve from custom directory
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "public")),
    RequestPath = "/files"
});

// Usage: GET /files/document.pdf
```

## Error Handling

### Global Exception Handler

```csharp
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exceptionHandlerPathFeature =
            context.Features.Get<IExceptionHandlerPathFeature>();

        var exception = exceptionHandlerPathFeature?.Error;
        
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await context.Response.WriteAsJsonAsync(new
        {
            message = "An error occurred",
            error = exception?.Message
        });
    });
});
```

### Controller Exception Handling

```csharp
[ApiController]
public class DataController : ControllerBase
{
    [HttpGet("{id}")]
    public IActionResult GetData(int id)
    {
        try
        {
            if (id <= 0)
                throw new ArgumentException("Invalid ID");

            var data = FetchData(id);
            return Ok(data);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Internal error" });
        }
    }

    private object FetchData(int id) => new { Id = id };
}
```

## CORS (Cross-Origin Resource Sharing)

Allow requests from different domains:

```csharp
// In Program.cs
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });

    options.AddPolicy("AllowSpecific", policy =>
    {
        policy.WithOrigins("https://example.com", "https://app.example.com")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();
app.UseCors("AllowAll");
```

## Startup Pipeline

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddCors();

var app = builder.Build();

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();

app.Run();
```

## Practice Exercises

1. **Basic API**: Create CRUD endpoints for a resource (e.g., todos, products)
2. **Routing**: Implement nested routes and route constraints
3. **Configuration**: Set up environment-specific settings
4. **Error Handling**: Add global exception handler and validate inputs
5. **Middleware**: Create custom middleware for logging and request timing

## Key Takeaways

- **Minimal APIs** for simple endpoints, **Controllers** for complex applications
- **Middleware** processes requests in order
- **Routing** maps URLs to handler methods
- **Dependency injection** integrates services with controllers
- **Configuration** from appsettings.json, environment-specific overrides
- **Status codes** communicate request results (200, 201, 400, 404, 500, etc.)
- **CORS** manages cross-origin requests
- **Error handling** should catch exceptions and return appropriate responses
