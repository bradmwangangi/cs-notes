# 6. Routing & Endpoints

## Overview

Routing is how ASP.NET Core maps incoming HTTP requests to handler code (controllers, minimal APIs, Razor pages). Endpoints are the final handlers that process requests and return responses. Mastering routing is essential for building organized, maintainable APIs and web applications.

## How Routing Works

```
HTTP Request arrives
    ↓
UseRouting() middleware parses request
    ↓
Route matching against configured patterns
    ↓
Matching endpoint found
    ↓
Endpoint info stored in HttpContext
    ↓
UseEndpoints() executes the matched endpoint
    ↓
Response returned
```

### Routing Phases

```csharp
var app = builder.Build();

// Phase 1: Routing setup (parse URL, match routes)
app.UseRouting();

// ... middleware can run here with route info ...

// Phase 2: Endpoint execution
app.MapControllers();  // Run matched endpoint

app.Run();
```

## Route Matching Process

Routing is **case-insensitive by default** and matches in **registration order**.

```csharp
// These routes are registered in order:
app.MapGet("/users/{id}", GetUserById);              // Route 1
app.MapGet("/users/{id:int}", GetUserByIntId);       // Route 2
app.MapGet("/users/{id:guid}", GetUserByGuidId);     // Route 3
app.MapGet("/users/admin", GetAdminUsers);           // Route 4

// Request: GET /users/123
// Matches: Route 1 (no constraint, matches first)
// id = "123" (string)

// Request: GET /users/123abc  
// Matches: Route 2 (if int constraint, would fail)
// Then Route 1 (fallback)

// Request: GET /users/admin
// Matches: Route 4 (literal match)
// More specific routes should come after less specific
```

## Conventional Routing (MVC/Controllers)

Conventional routing uses URL patterns to infer the controller and action.

### Attribute Routing (Preferred)

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    // GET /api/users
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetUsers()
    {
        return Ok(await _service.GetUsersAsync());
    }
    
    // GET /api/users/123
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _service.GetUserAsync(id);
        return user == null ? NotFound() : Ok(user);
    }
    
    // POST /api/users
    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto dto)
    {
        var user = await _service.CreateUserAsync(dto);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }
    
    // PUT /api/users/123
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, UpdateUserDto dto)
    {
        await _service.UpdateUserAsync(id, dto);
        return NoContent();
    }
    
    // DELETE /api/users/123
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _service.DeleteUserAsync(id);
        return NoContent();
    }
}

// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

var app = builder.Build();
app.UseRouting();
app.MapControllers();  // Map all attribute routes
app.Run();
```

### Token Replacement in Routes

ASP.NET replaces tokens automatically:

```csharp
// [controller] replaced with controller name (without "Controller" suffix)
[Route("api/[controller]")]
public class UsersController { }
// Routes: /api/users

// [action] replaced with action method name
[Route("[controller]/[action]")]
public class UsersController
{
    public IActionResult GetAll() { }  // GET /users/getall
    public IActionResult GetById(int id) { }  // GET /users/getbyid/1
}
```

## Minimal APIs (Modern Approach)

Minimal APIs are a lightweight alternative to controllers, ideal for microservices and API-focused applications.

### Basic Minimal API Routes

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();

// GET endpoint
app.MapGet("/api/users", GetUsers)
    .WithName("GetUsers")
    .WithOpenApi()
    .Produces<List<UserDto>>(StatusCodes.Status200OK);

// GET with id
app.MapGet("/api/users/{id}", GetUser)
    .WithName("GetUser")
    .WithOpenApi()
    .Produces<UserDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

// POST endpoint
app.MapPost("/api/users", CreateUser)
    .WithName("CreateUser")
    .WithOpenApi()
    .Produces<UserDto>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest);

// PUT endpoint
app.MapPut("/api/users/{id}", UpdateUser)
    .WithName("UpdateUser")
    .WithOpenApi()
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound);

// DELETE endpoint
app.MapDelete("/api/users/{id}", DeleteUser)
    .WithName("DeleteUser")
    .WithOpenApi()
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound);

app.Run();

// Handler methods
async Task<IResult> GetUsers(IUserService service)
{
    var users = await service.GetUsersAsync();
    return Results.Ok(users);
}

async Task<IResult> GetUser(int id, IUserService service)
{
    var user = await service.GetUserAsync(id);
    return user == null ? Results.NotFound() : Results.Ok(user);
}

async Task<IResult> CreateUser(CreateUserDto dto, IUserService service)
{
    var user = await service.CreateUserAsync(dto);
    return Results.CreatedAtRoute("GetUser", new { id = user.Id }, user);
}

async Task<IResult> UpdateUser(int id, UpdateUserDto dto, IUserService service)
{
    await service.UpdateUserAsync(id, dto);
    return Results.NoContent();
}

async Task<IResult> DeleteUser(int id, IUserService service)
{
    await service.DeleteUserAsync(id);
    return Results.NoContent();
}
```

### Minimal API with Extension Methods

```csharp
// UserEndpoints.cs
public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app
            .MapGroup("/api/users")
            .WithName("Users")
            .WithOpenApi()
            .WithTags("Users");
        
        group.MapGet("/", GetUsers)
            .WithName("GetUsers")
            .WithSummary("Get all users");
        
        group.MapGet("/{id}", GetUser)
            .WithName("GetUser")
            .WithSummary("Get user by ID");
        
        group.MapPost("/", CreateUser)
            .WithName("CreateUser")
            .WithSummary("Create new user");
        
        group.MapPut("/{id}", UpdateUser)
            .WithName("UpdateUser")
            .WithSummary("Update user");
        
        group.MapDelete("/{id}", DeleteUser)
            .WithName("DeleteUser")
            .WithSummary("Delete user");
    }
    
    private static async Task<IResult> GetUsers(IUserService service)
    {
        var users = await service.GetUsersAsync();
        return Results.Ok(users);
    }
    
    // ... other handlers ...
}

// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();
app.MapUserEndpoints();
app.Run();
```

## Route Constraints

Constraints filter which requests match a route.

### Built-in Constraints

```csharp
// int constraint - only match integer IDs
app.MapGet("/api/users/{id:int}", GetUserById);
// GET /api/users/123 ✓ Matches
// GET /api/users/abc ✗ No match

// guid constraint
app.MapGet("/api/orders/{id:guid}", GetOrderById);
// GET /api/orders/550e8400-e29b-41d4-a716-446655440000 ✓
// GET /api/orders/invalid-guid ✗

// length constraint
app.MapGet("/api/users/{username:length(3,20)}", GetUserByUsername);
// GET /api/users/john ✓ (4 characters)
// GET /api/users/ab ✗ (2 characters, too short)

// regex constraint
app.MapGet("/api/categories/{slug:regex(^[a-z0-9-]+$)}", GetCategory);
// GET /api/categories/tech-news ✓
// GET /api/categories/Tech_News ✗ (has uppercase and underscore)

// range constraint
app.MapGet("/api/pages/{page:range(1,100)}", GetPage);
// GET /api/pages/50 ✓
// GET /api/pages/150 ✗

// alpha constraint
app.MapGet("/api/countries/{code:alpha}", GetCountry);
// GET /api/countries/us ✓
// GET /api/countries/12 ✗

// datetime constraint
app.MapGet("/api/reports/{date:datetime}", GetReportByDate);
// GET /api/reports/2023-12-25 ✓
```

### Custom Route Constraints

```csharp
// Custom constraint
public class SlugConstraint : IRouteConstraint
{
    public bool Match(HttpContext httpContext, IRouter route,
        string routeKey, RouteValueDictionary values,
        RouteDirection routeDirection)
    {
        if (values.TryGetValue(routeKey, out var value))
        {
            var stringValue = value?.ToString();
            return !string.IsNullOrEmpty(stringValue) &&
                System.Text.RegularExpressions.Regex.IsMatch(
                    stringValue, @"^[a-z0-9]+(?:-[a-z0-9]+)*$");
        }
        
        return false;
    }
}

// Register constraint
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRouting(options =>
{
    options.ConstraintMap.Add("slug", typeof(SlugConstraint));
});

var app = builder.Build();

// Use custom constraint
app.MapGet("/api/posts/{slug:slug}", GetPostBySlug);
// GET /api/posts/my-blog-post ✓
// GET /api/posts/my_blog_post ✗
```

## Route Groups (MapGroup)

Group related endpoints with shared configuration:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthorization();

var app = builder.Build();

// Create route group
var apiGroup = app.MapGroup("/api")
    .WithOpenApi()
    .WithTags("API");

var userGroup = apiGroup.MapGroup("/users")
    .RequireAuthorization()
    .WithDescription("User management endpoints");

// Endpoints inherit group configuration
userGroup.MapGet("/", GetUsers)
    .WithName("GetUsers");

userGroup.MapGet("/{id}", GetUser)
    .WithName("GetUser")
    .AllowAnonymous();  // Override authorization for this endpoint

userGroup.MapPost("/", CreateUser)
    .WithName("CreateUser");

// Products group - no authorization required
var productGroup = apiGroup.MapGroup("/products")
    .WithDescription("Product management endpoints");

productGroup.MapGet("/", GetProducts)
    .WithName("GetProducts");

productGroup.MapGet("/{id}", GetProduct)
    .WithName("GetProduct");

app.Run();
```

## Optional Route Parameters

Parameters can be optional:

```csharp
// id is optional (can be null)
app.MapGet("/api/users/{id?}", GetUsers);

// Matches:
// GET /api/users → id = null
// GET /api/users/123 → id = "123"

async Task<IResult> GetUsers(int? id)
{
    if (id.HasValue)
    {
        // Get specific user
        var user = await _service.GetUserAsync(id.Value);
        return user == null ? Results.NotFound() : Results.Ok(user);
    }
    
    // Get all users
    var users = await _service.GetUsersAsync();
    return Results.Ok(users);
}
```

## Catchall Routes

Match remaining path segments:

```csharp
// {*path} captures remaining path
app.MapGet("/files/{*path}", ServeFile);

// GET /files/images/profile.jpg → path = "images/profile.jpg"
// GET /files/documents/invoices/2023/01.pdf → path = "documents/invoices/2023/01.pdf"

async Task<IResult> ServeFile(string path)
{
    var filePath = Path.Combine("/var/files", path);
    
    if (!System.IO.File.Exists(filePath))
        return Results.NotFound();
    
    var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
    return Results.File(bytes, "application/octet-stream");
}
```

## Areas (Multi-Section Organization)

Organize large applications into areas:

```csharp
[Area("Admin")]
[Route("admin/[controller]")]
public class UsersController : ControllerBase
{
    // Routes: /admin/users, /admin/users/{id}, etc.
}

[Area("Admin")]
[Route("admin/[controller]")]
public class DashboardController : ControllerBase
{
    // Routes: /admin/dashboard, etc.
}

[Area("Customer")]
[Route("customer/[controller]")]
public class OrdersController : ControllerBase
{
    // Routes: /customer/orders, /customer/orders/{id}, etc.
}

// Program.cs
app.MapControllers();  // Maps all areas automatically
```

## Query String Binding

Query parameters are automatically bound to handler parameters:

```csharp
// GET /api/users?page=2&limit=10&search=john&sort=name&order=asc
app.MapGet("/api/users", GetUsers);

async Task<IResult> GetUsers(
    [FromQuery] int page = 1,
    [FromQuery] int limit = 10,
    [FromQuery] string? search = null,
    [FromQuery] string? sort = "name",
    [FromQuery] string? order = "asc")
{
    var users = await _service.GetUsersAsync(
        page: page,
        limit: limit,
        search: search,
        sort: sort,
        order: order);
    
    return Results.Ok(users);
}
```

## Route Value vs Query Parameter

```csharp
// Route value - part of URL path structure
app.MapGet("/api/users/{id}", GetUser);
// GET /api/users/123 → id from route

// Query parameter - optional, after ?
app.MapGet("/api/users", GetUsers);
// GET /api/users?page=2&limit=10 → page and limit from query

// Both
app.MapGet("/api/users/{id}/posts", GetUserPosts);
// GET /api/users/123/posts?page=2 → id from route, page from query

async Task<IResult> GetUserPosts(int id, [FromQuery] int page = 1)
{
    var posts = await _service.GetUserPostsAsync(id, page);
    return Results.Ok(posts);
}
```

## Comparing Controllers vs Minimal APIs

| Aspect | Controllers | Minimal APIs |
|--------|-----------|-------------|
| **Code Organization** | Class-based | Method-based |
| **Routing** | Attribute on class/method | Fluent API |
| **Discovery** | Implicit (reflection) | Explicit (registration) |
| **Dependency Injection** | Constructor injection | Method parameters |
| **Use Case** | Large APIs, MVC | Microservices, simple APIs |
| **Learning Curve** | Steeper | Gentler |
| **Type Safety** | Strong | Strong |

### When to Use Each

```csharp
// Use Controllers for:
// - Large, complex APIs
// - Team with MVC experience
// - Need traditional layering
public class UsersController : ControllerBase { }

// Use Minimal APIs for:
// - Microservices
// - Simple, focused APIs
// - Greenfield projects
app.MapGet("/api/users", GetUsers);
```

## Advanced Routing Scenarios

### Dynamic Routing Registration

```csharp
public static class DynamicRouteRegistration
{
    public static void RegisterDynamicRoutes(
        this WebApplication app,
        IEnumerable<IEndpointDefinition> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            endpoint.MapEndpoint(app);
        }
    }
}

public interface IEndpointDefinition
{
    void MapEndpoint(WebApplication app);
}

public class UserEndpointDefinition : IEndpointDefinition
{
    public void MapEndpoint(WebApplication app)
    {
        app.MapGet("/api/users", GetUsers)
            .WithName("GetUsers");
        
        app.MapGet("/api/users/{id}", GetUser)
            .WithName("GetUser");
    }
    
    private static async Task<IResult> GetUsers(IUserService service)
    {
        return Results.Ok(await service.GetUsersAsync());
    }
    
    private static async Task<IResult> GetUser(int id, IUserService service)
    {
        var user = await service.GetUserAsync(id);
        return user == null ? Results.NotFound() : Results.Ok(user);
    }
}

// Program.cs
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var endpoints = new IEndpointDefinition[]
{
    new UserEndpointDefinition(),
    new ProductEndpointDefinition(),
};

app.RegisterDynamicRoutes(endpoints);
app.Run();
```

### Prefix All Routes

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Prefix all routes with /api
var apiGroup = app.MapGroup("/api");

apiGroup.MapGet("/users", GetUsers);
apiGroup.MapPost("/users", CreateUser);
apiGroup.MapGet("/products", GetProducts);

app.Run();
// Routes become: /api/users, /api/products, etc.
```

## Key Takeaways

1. **Routing maps requests to endpoints**: URL → handler
2. **UseRouting() must come before UseEndpoints()**: Two-phase process
3. **Route order matters**: More specific routes before generic ones
4. **Constraints filter matches**: Ensure type safety
5. **Minimal APIs are lightweight**: Better for microservices
6. **Controllers are traditional**: Better for complex applications
7. **Route groups organize endpoints**: Shared configuration
8. **Query parameters vs route values**: Different purposes
9. **Attribute routing is preferred**: Explicit and maintainable
10. **Custom constraints for validation**: Specific matching rules

## Related Topics

- **HTTP Fundamentals** (Topic 4): Understanding requests and methods
- **Middleware & Pipeline** (Topic 5): Where routing fits in request flow
- **Model Binding & Validation** (Topic 7): How parameters are filled from requests

