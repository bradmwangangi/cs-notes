# Chapter 3: Controllers & Minimal APIs

## 3.1 Controller-Based APIs

Controller-based APIs use MVC pattern: controllers handle requests, call services, return responses.

### Basic Controller Structure

```csharp
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
        if (user == null)
            return NotFound();
        return Ok(user);
    }
    
    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser(CreateUserRequest request)
    {
        var user = await _userService.CreateUserAsync(request);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }
}
```

**Key attributes:**

**[ApiController]**
- Adds API-specific behaviors
- Automatic model validation (400 on invalid)
- Automatic binding of body, query, route params
- Problem details responses for errors

**[Route("api/[controller]")]**
- Base route for all actions in controller
- `[controller]` replaced with controller name (Users → /api/users)
- Can be overridden per action

**[HttpGet], [HttpPost], etc.**
- Specify HTTP method for action
- Optional route parameter: `[HttpGet("{id}")]`

### Action Results

Controllers return `ActionResult<T>` to indicate response:

**Ok()**
```csharp
return Ok(user);  // 200 with user data
```

**Created/CreatedAtAction**
```csharp
return Created("/api/users/123", user);  // 201 with Location header
return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
```

**Accepted**
```csharp
return Accepted(ProcessTask(request));  // 202 for async operations
```

**BadRequest**
```csharp
return BadRequest("Invalid request");  // 400
return BadRequest(ModelState);        // Include validation errors
```

**NotFound**
```csharp
return NotFound();        // 404 with no body
return NotFound(details); // 404 with body
```

**Forbidden/Unauthorized**
```csharp
return Forbid();       // 403
return Unauthorized(); // 401
```

**NoContent**
```csharp
return NoContent();  // 204 (no body)
```

**StatusCode**
```csharp
return StatusCode(422, errors);  // Custom status with body
```

### Routing

**Route templates:**

```csharp
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    // GET /api/users
    [HttpGet]
    public IActionResult GetAll() { }
    
    // GET /api/users/123
    [HttpGet("{id}")]
    public IActionResult GetById(int id) { }
    
    // GET /api/users/123/orders
    [HttpGet("{userId}/orders")]
    public IActionResult GetUserOrders(int userId) { }
    
    // POST /api/users
    [HttpPost]
    public IActionResult Create(CreateUserRequest request) { }
    
    // PUT /api/users/123
    [HttpPut("{id}")]
    public IActionResult Update(int id, UpdateUserRequest request) { }
    
    // DELETE /api/users/123
    [HttpDelete("{id}")]
    public IActionResult Delete(int id) { }
    
    // Custom route (overrides class route)
    [HttpPost("register")]
    public IActionResult Register(RegisterRequest request) { }
}
```

**Route constraints:**

```csharp
// Only match numeric IDs
[HttpGet("{id:int}")]
public IActionResult GetUser(int id) { }

// Only match GUID IDs
[HttpGet("{id:guid}")]
public IActionResult GetOrder(Guid id) { }

// Custom constraint
[HttpGet("posts/{slug:minlength(3)}")]
public IActionResult GetPost(string slug) { }
```

**Attribute routing vs. conventional routing:**

ASP.NET Core APIs use **attribute routing** (routes defined on actions with attributes). Conventional routing (legacy) is not recommended for APIs.

### Parameter Binding

Controllers automatically bind parameters from different sources:

```csharp
[HttpPost("api/users/{id}/orders")]
public IActionResult GetUserOrders(
    int id,                                    // From route {id}
    [FromQuery] string status,                 // From query: ?status=pending
    [FromQuery] int limit = 10,                // Default value if not provided
    [FromHeader] string authorization,        // From Authorization header
    [FromBody] OrderRequest request            // From request body
)
{
    // All parameters automatically bound and validated
}
```

**Implicit binding without attributes:**
- Route parameters: inferred from `{param}` in route
- Query parameters: inferred from method signature
- Request body: inferred as single complex type

**Explicit binding with attributes:**
- `[FromRoute]` - Route parameter
- `[FromQuery]` - Query string parameter
- `[FromBody]` - Request body (usually implicit)
- `[FromHeader]` - HTTP header
- `[FromServices]` - Dependency injection (inject services)

Example of injecting service:
```csharp
[HttpPost]
public async Task<IActionResult> CreateUser(
    CreateUserRequest request,
    [FromServices] IUserService userService)
{
    var user = await userService.CreateUserAsync(request);
    return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
}
```

---

## 3.2 Minimal APIs

Minimal APIs (introduced in .NET 6) offer lightweight alternative to controllers for simple endpoints.

### Basic Minimal API

```csharp
var builder = WebApplicationBuilder.CreateBuilder(args);

builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();

app.MapGet("/api/users/{id}", GetUserHandler)
    .WithName("GetUser")
    .WithOpenApi()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

app.Run();

async Task<IResult> GetUserHandler(int id, IUserService userService)
{
    var user = await userService.GetUserAsync(id);
    return user != null 
        ? Results.Ok(user)
        : Results.NotFound();
}
```

**Minimal API methods:**

```csharp
// Map HTTP methods
app.MapGet(pattern, handler);      // GET
app.MapPost(pattern, handler);     // POST
app.MapPut(pattern, handler);      // PUT
app.MapPatch(pattern, handler);    // PATCH
app.MapDelete(pattern, handler);   // DELETE
app.MapMethods(pattern, methods, handler);  // Custom methods

// Pattern is the route template
// Handler is the method to execute
```

### Endpoint Metadata

Minimal APIs support chainable methods to describe endpoints:

```csharp
app.MapGet("/api/users/{id}", GetUserHandler)
    .WithName("GetUser")                    // Named route for linking
    .WithOpenApi()                          // Include in OpenAPI docs
    .WithDescription("Get user by ID")      // Description for docs
    .WithSummary("Retrieve user")           // Summary for docs
    .Produces<UserDto>(200)                 // Success response type
    .Produces(404)                          // Not found response
    .RequireAuthorization()                 // Require authentication
    .WithTags("Users");                     // OpenAPI grouping
```

### Route and Query Parameters

```csharp
// Route parameter (in path)
app.MapGet("/api/users/{id:int}", (int id) => Results.Ok(id));

// Multiple route parameters
app.MapGet("/api/users/{userId}/orders/{orderId}", 
    (int userId, int orderId) => Results.Ok());

// Query parameters (from query string)
app.MapGet("/api/users", (string name, int limit = 10) =>
{
    // name comes from ?name=value
    // limit comes from ?limit=5 or defaults to 10
});

// Mix route and query
app.MapGet("/api/users/{userId}/orders",
    (int userId, int limit = 10, string status = "all") =>
    {
        // /api/users/123/orders?limit=20&status=pending
    });
```

### Request Body

```csharp
app.MapPost("/api/users", 
    (CreateUserRequest request) => 
    {
        // request automatically bound from JSON body
        // validation errors return 400 automatically
        return Results.Created($"/api/users/{id}", user);
    }
);
```

### Dependency Injection

Services are injected into handler parameters:

```csharp
app.MapPost("/api/users",
    async (CreateUserRequest request, IUserService userService) =>
    {
        var user = await userService.CreateUserAsync(request);
        return Results.Created($"/api/users/{user.Id}", user);
    }
);
```

Any registered service can be injected.

### Validation

Validation happens automatically for bound models:

```csharp
public class CreateUserRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; }
    
    [EmailAddress]
    public string Email { get; set; }
}

app.MapPost("/api/users",
    async (CreateUserRequest request, IUserService userService) =>
    {
        // If invalid, returns 400 automatically
        // Access errors via context if needed:
        return Results.Created($"/api/users/{userId}", user);
    }
).WithOpenApi();
```

Manual validation checking:

```csharp
app.MapPost("/api/users",
    async (HttpContext context, CreateUserRequest request, IUserService userService) =>
    {
        // Validate manually if needed
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest("Name required");
            
        var user = await userService.CreateUserAsync(request);
        return Results.Created($"/api/users/{user.Id}", user);
    }
);
```

### Results

Results control response:

```csharp
Results.Ok(user)                      // 200
Results.Created("/api/users/1", user) // 201
Results.Accepted()                     // 202
Results.NoContent()                    // 204
Results.BadRequest()                   // 400
Results.BadRequest("Error message")    // 400 with message
Results.Unauthorized()                 // 401
Results.Forbid()                       // 403
Results.NotFound()                     // 404
Results.StatusCode(422)                // Custom status
Results.Json(data)                     // JSON response
Results.Text("Hello")                  // Text response
Results.File(bytes, "application/pdf") // File download
Results.Redirect("/api/users/1")       // Redirect
```

### Grouping Related Endpoints

```csharp
var usersGroup = app.MapGroup("/api/users")
    .WithTags("Users")
    .RequireAuthorization();

usersGroup.MapGet("", GetAllUsers);
usersGroup.MapGet("{id}", GetUserById);
usersGroup.MapPost("", CreateUser);
usersGroup.MapPut("{id}", UpdateUser);
usersGroup.MapDelete("{id}", DeleteUser);

async Task<IResult> GetAllUsers() { /* ... */ }
async Task<IResult> GetUserById(int id) { /* ... */ }
// etc.
```

Benefits:
- Shared metadata (authorization, tags)
- DRY - don't repeat route prefix
- Logical grouping

---

## 3.3 Controllers vs. Minimal APIs

### When to Use Controllers

**Use controllers when:**
- Building larger APIs with many endpoints
- Need object-oriented organization
- Team familiar with MVC pattern
- Complex logic and filters per action
- Multiple related endpoints

**Controller advantages:**
- Class-based organization (groups related operations)
- Attribute-based routing is explicit
- Easier to apply filters and middleware to whole controller
- Better for large teams (familiar pattern)
- Easier to test (inject dependencies into class)

### When to Use Minimal APIs

**Use Minimal APIs when:**
- Building simple APIs or microservices
- Few endpoints
- Starting new project with modern C#
- Want minimal boilerplate
- Building lambda-heavy code

**Minimal API advantages:**
- Less boilerplate
- Lightweight
- Functional programming style
- Easy for small services
- Newer pattern, more modern

### Hybrid Approach

Mix both in same application:

```csharp
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();  // Map controller-based routes

// Add minimal endpoints alongside
app.MapGet("/api/health", () => Results.Ok("healthy"));
app.MapPost("/api/webhooks/github", HandleGitHubWebhook);

app.Run();
```

This is practical: use controllers for main API, minimal endpoints for simple operations.

---

## 3.4 Filters and Middleware in Controllers

Controllers can apply filters to handle cross-cutting concerns.

### Action Filters

Run before and after controller action:

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ValidateModelAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            context.Result = new BadRequestObjectResult(context.ModelState);
        }
    }
}

[ApiController]
[Route("api/[controller]")]
[ValidateModel]  // Apply to entire controller
public class UsersController : ControllerBase
{
    // All actions automatically validate model
}
```

### Exception Filters

Handle exceptions per action or controller:

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is DomainException domainEx)
        {
            context.Result = new ObjectResult(new { 
                error = domainEx.Message 
            })
            {
                StatusCode = domainEx.StatusCode
            };
            context.ExceptionHandled = true;
        }
    }
}

[ApiController]
[ExceptionFilter]
public class UsersController : ControllerBase
{
    // Exceptions handled by filter
}
```

### Authorization Filters

Authorize before action executes:

```csharp
[Authorize]  // Require authentication
[Authorize(Roles = "Admin")]  // Require Admin role
[Authorize(Policy = "Premium")]  // Require custom policy
public class AdminController : ControllerBase
{
    // All actions require auth
}

public class UsersController : ControllerBase
{
    [AllowAnonymous]  // Override: allow without auth
    [HttpPost("register")]
    public IActionResult Register(RegisterRequest request) { }
    
    [Authorize]  // Only on this action
    [HttpGet("profile")]
    public IActionResult GetProfile() { }
}
```

---

## 3.5 HTTP Semantics in Responses

Proper use of status codes and headers crucial for clients.

### Creating Resources

```csharp
[HttpPost]
public async Task<ActionResult<UserDto>> CreateUser(CreateUserRequest request)
{
    var user = await _userService.CreateUserAsync(request);
    
    // 201 Created is mandatory here
    // Location header tells client where to access new resource
    // Body contains the created resource
    return CreatedAtAction(
        actionName: nameof(GetUser),
        routeValues: new { id = user.Id },
        value: user
    );
}
```

**CreatedAtAction** does:
1. Returns 201 status
2. Sets Location header to route generated from action name and route values
3. Returns created resource in body

Alternative using Minimal APIs:

```csharp
app.MapPost("/api/users", async (CreateUserRequest request, IUserService service) =>
{
    var user = await service.CreateUserAsync(request);
    return Results.Created($"/api/users/{user.Id}", user);
});
```

### Updating Resources

```csharp
// PUT: Replace entire resource
[HttpPut("{id}")]
public async Task<IActionResult> UpdateUser(int id, UpdateUserRequest request)
{
    await _userService.UpdateUserAsync(id, request);
    return NoContent();  // 204: no content to return
    // or return Ok(updatedUser); for 200 with updated data
}

// PATCH: Partial update
[HttpPatch("{id}")]
public async Task<IActionResult> PatchUser(int id, JsonPatchDocument<User> patch)
{
    var user = await _userService.GetUserAsync(id);
    if (user == null)
        return NotFound();
    
    patch.ApplyTo(user);
    await _userService.UpdateUserAsync(id, user);
    return Ok(user);  // Return updated resource
}
```

### Deleting Resources

```csharp
[HttpDelete("{id}")]
public async Task<IActionResult> DeleteUser(int id)
{
    var success = await _userService.DeleteUserAsync(id);
    if (!success)
        return NotFound();
    
    return NoContent();  // 204: deletion confirmed, nothing to return
}
```

### Handling Conflicts

```csharp
[HttpPost("users/123/lock")]
public async Task<IActionResult> LockUser(int id)
{
    try
    {
        await _userService.LockUserAsync(id);
        return Ok();
    }
    catch (UserAlreadyLockedException)
    {
        return Conflict("User is already locked");  // 409
    }
}
```

---

## 3.6 Async/Await in Endpoints

All endpoints should be async to handle concurrent requests:

```csharp
// Good: async all the way
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _userService.GetUserAsync(id);  // Async
    return Ok(user);
}

// Bad: synchronous blocking
[HttpGet("{id}")]
public ActionResult<UserDto> GetUser(int id)
{
    var user = _userService.GetUser(id);  // Blocks thread
    return Ok(user);
}
```

**Blocking is problematic:**
- Thread pool exhaustion under load
- Reduced scalability
- Context switching overhead

**Return types:**
```csharp
// Traditional controller
public async Task<ActionResult<UserDto>>
public async Task<IActionResult>

// Minimal API
async Task<IResult>
Task<IResult>
IResult  // Synchronous if no await needed
```

---

## 3.7 Content Negotiation in Endpoints

Configure response format per endpoint:

```csharp
[HttpGet("{id}")]
[Produces("application/json")]  // Only JSON
public ActionResult<UserDto> GetUser(int id) { }

[HttpGet("export")]
[Produces("application/csv")]  // Custom format
public ActionResult ExportUsers() { }

[HttpGet("{id}")]
[Produces("application/json", "application/xml")]  // Multiple formats
public ActionResult<UserDto> GetUser(int id) { }
```

The `Accept` header in request determines which format is returned.

---

## Summary

Controllers provide object-oriented, structured API development suitable for larger applications. Minimal APIs offer lightweight alternatives for simpler services. Both support async operations, parameter binding, validation, and proper HTTP semantics. The next chapters cover the data access layer with Entity Framework Core and handling request/response details.
