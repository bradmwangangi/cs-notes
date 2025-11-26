# 8. Minimal APIs vs MVC Controllers

## Overview

ASP.NET Core provides two primary approaches to building HTTP endpoints: Minimal APIs (modern, lightweight) and MVC Controllers (traditional, feature-rich). Understanding the trade-offs between them is crucial for architectural decisions in new projects.

## Historical Context

**MVC Controllers** (introduced in .NET Core 1.0):
- Class-based, attribute-decorated approach
- Follows traditional MVC (Model-View-Controller) pattern
- Reflective registration of routes
- Heavy on ceremony but feature-rich

**Minimal APIs** (.NET 6+):
- Method-based, fluent API approach
- Explicit route registration
- Lower ceremony, easier to understand
- Designed for microservices and simple APIs

## Minimal APIs Detailed

### Basic Minimal API Structure

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ApplicationDbContext>();

var app = builder.Build();

// Map endpoints
app.MapGet("/api/users", GetUsers)
    .WithName("GetUsers")
    .WithOpenApi()
    .Produces<List<UserDto>>(StatusCodes.Status200OK);

app.MapGet("/api/users/{id}", GetUser)
    .WithName("GetUser")
    .WithOpenApi()
    .Produces<UserDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

app.MapPost("/api/users", CreateUser)
    .WithName("CreateUser")
    .WithOpenApi()
    .Accepts<CreateUserDto>("application/json")
    .Produces<UserDto>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest);

app.MapPut("/api/users/{id}", UpdateUser)
    .WithName("UpdateUser")
    .WithOpenApi()
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound);

app.MapDelete("/api/users/{id}", DeleteUser)
    .WithName("DeleteUser")
    .WithOpenApi()
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound);

app.Run();

// Handler methods (can be declared in-line or separately)
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
    if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Email))
        return Results.BadRequest("Name and email are required");
    
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

### Minimal API with Extensions (Organized)

```csharp
// UserEndpoints.cs
public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users")
            .WithName("Users")
            .WithOpenApi()
            .WithTags("Users")
            .RequireAuthorization();  // Authorization for group
        
        group.MapGet("/", GetUsers)
            .WithName("GetUsers")
            .WithSummary("List all users")
            .Produces<List<UserDto>>();
        
        group.MapGet("/{id}", GetUser)
            .WithName("GetUser")
            .WithSummary("Get user by ID")
            .Produces<UserDto>()
            .Produces(StatusCodes.Status404NotFound);
        
        group.MapPost("/", CreateUser)
            .WithName("CreateUser")
            .WithSummary("Create new user")
            .AllowAnonymous()  // Override group authorization
            .Accepts<CreateUserDto>("application/json")
            .Produces<UserDto>(StatusCodes.Status201Created);
        
        group.MapPut("/{id}", UpdateUser)
            .WithName("UpdateUser")
            .WithSummary("Update user")
            .Produces(StatusCodes.Status204NoContent);
        
        group.MapDelete("/{id}", DeleteUser)
            .WithName("DeleteUser")
            .WithSummary("Delete user")
            .Produces(StatusCodes.Status204NoContent);
    }
    
    private static async Task<IResult> GetUsers(IUserService service)
    {
        var users = await service.GetUsersAsync();
        return Results.Ok(users);
    }
    
    private static async Task<IResult> GetUser(int id, IUserService service)
    {
        var user = await service.GetUserAsync(id);
        return user == null ? Results.NotFound() : Results.Ok(user);
    }
    
    private static async Task<IResult> CreateUser(
        CreateUserDto dto,
        IUserService service)
    {
        var user = await service.CreateUserAsync(dto);
        return Results.CreatedAtRoute("GetUser", new { id = user.Id }, user);
    }
    
    private static async Task<IResult> UpdateUser(
        int id,
        UpdateUserDto dto,
        IUserService service)
    {
        await service.UpdateUserAsync(id, dto);
        return Results.NoContent();
    }
    
    private static async Task<IResult> DeleteUser(int id, IUserService service)
    {
        await service.DeleteUserAsync(id);
        return Results.NoContent();
    }
}

// ProductEndpoints.cs (similar structure)
public static class ProductEndpoints
{
    public static void MapProductEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/products")
            .WithName("Products")
            .WithOpenApi()
            .WithTags("Products");
        
        group.MapGet("/", GetProducts)
            .WithName("GetProducts");
        
        // ... more endpoints
    }
    
    // ... handlers
}

// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthorization();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IProductService, ProductService>();

var app = builder.Build();

app.UseRouting();
app.UseAuthorization();

app.MapUserEndpoints();
app.MapProductEndpoints();

app.Run();
```

### Minimal API Features

```csharp
// Dependency Injection
app.MapGet("/api/data", GetData);

IResult GetData(
    IUserService userService,
    ILogger<Program> logger,
    HttpContext context)
{
    logger.LogInformation("Getting data for user {UserId}", 
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    
    return Results.Ok();
}

// Route Groups with nesting
var apiGroup = app.MapGroup("/api");
var usersGroup = apiGroup.MapGroup("/users");

usersGroup.MapGet("/", GetUsers);
usersGroup.MapPost("/", CreateUser);

// Results object for responses
app.MapGet("/test", () =>
{
    return Results.Ok(new { message = "success" });
});

app.MapPost("/error", () =>
{
    return Results.BadRequest("Invalid input");
});

app.MapGet("/notfound", () =>
{
    return Results.NotFound();
});

app.MapGet("/custom", () =>
{
    return Results.Json(
        new { data = "value" },
        statusCode: 200,
        options: new JsonSerializerOptions { PropertyNamingPolicy = null });
});

// Filters (run before/after handler)
app.MapGet("/filtered", GetFiltered)
    .AddEndpointFilter(LoggingFilter);

IResult GetFiltered() => Results.Ok("filtered");

async ValueTask<object> LoggingFilter(
    EndpointFilterInvocationContext context,
    EndpointFilterDelegate next)
{
    var logger = context.HttpContext.RequestServices
        .GetRequiredService<ILogger<Program>>();
    
    logger.LogInformation("Before handler");
    
    var result = await next(context);
    
    logger.LogInformation("After handler");
    
    return result;
}

// Metadata for documentation
app.MapGet("/documented", GetDocumented)
    .WithOpenApi()
    .WithName("GetDocumented")
    .WithSummary("A documented endpoint")
    .WithDescription("This endpoint is documented for OpenAPI")
    .Produces<DataDto>(StatusCodes.Status200OK)
    .WithTags("Documentation");

IResult GetDocumented() => Results.Ok(new DataDto());
```

## MVC Controllers Detailed

### Traditional Controller Structure

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;
    
    // Constructor injection
    public UsersController(IUserService userService, 
        ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }
    
    // GET /api/users
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetUsers()
    {
        var users = await _userService.GetUsersAsync();
        return Ok(users);
    }
    
    // GET /api/users/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _userService.GetUserAsync(id);
        if (user == null)
            return NotFound();
        
        return Ok(user);
    }
    
    // POST /api/users
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        var user = await _userService.CreateUserAsync(dto);
        
        return CreatedAtAction(nameof(GetUser), 
            new { id = user.Id }, user);
    }
    
    // PUT /api/users/{id}
    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> UpdateUser(int id, UpdateUserDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        var success = await _userService.UpdateUserAsync(id, dto);
        if (!success)
            return NotFound();
        
        return NoContent();
    }
    
    // DELETE /api/users/{id}
    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var success = await _userService.DeleteUserAsync(id);
        if (!success)
            return NotFound();
        
        return NoContent();
    }
}

// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseRouting();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

### Controller Features

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    
    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }
    
    // Multiple route attributes
    [HttpGet]
    [Route("")]
    [Route("all")]
    public async Task<ActionResult<List<OrderDto>>> GetOrders()
    {
        return Ok(await _orderService.GetOrdersAsync());
    }
    
    // Route constraints
    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDto>> GetOrder(int id)
    {
        var order = await _orderService.GetOrderAsync(id);
        return order == null ? NotFound() : Ok(order);
    }
    
    // Complex route matching
    [HttpGet("{id:int}/items/{itemId:int}")]
    public async Task<ActionResult<OrderItemDto>> GetOrderItem(int id, int itemId)
    {
        var item = await _orderService.GetOrderItemAsync(id, itemId);
        return item == null ? NotFound() : Ok(item);
    }
    
    // Custom action name
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> CancelOrder(int id)
    {
        await _orderService.CancelOrderAsync(id);
        return NoContent();
    }
    
    // Attribute routing with token replacement
    [Route("user/{userId:int}/[action]")]
    [HttpGet]
    public async Task<ActionResult<List<OrderDto>>> GetByUser(int userId)
    {
        return Ok(await _orderService.GetUserOrdersAsync(userId));
    }
    
    // Accept multiple content types
    [HttpPost]
    [Consumes("application/json", "application/xml")]
    public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderDto dto)
    {
        var order = await _orderService.CreateOrderAsync(dto);
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }
    
    // Custom response type
    [HttpGet("{id}/report")]
    [Produces("application/pdf")]
    public async Task<IActionResult> GetOrderReport(int id)
    {
        var pdf = await _orderService.GenerateReportPdfAsync(id);
        return File(pdf, "application/pdf", $"order-{id}.pdf");
    }
}
```

## Comparison: Minimal APIs vs Controllers

### Feature Comparison Table

| Feature | Minimal APIs | Controllers |
|---------|-------------|-----------|
| **Setup Complexity** | Low | Medium |
| **Code Organization** | Method-based | Class-based |
| **Route Registration** | Explicit, fluent | Implicit, attribute-based |
| **Dependency Injection** | Method parameters | Constructor injection |
| **Discoverability** | Explicit in code | Reflection-based |
| **Performance** | Slightly faster | Negligible difference |
| **Swagger/OpenAPI** | Explicit metadata | Automatic from attributes |
| **Authorization** | Per-endpoint | Per-controller/action |
| **Model Validation** | Manual | Automatic via attributes |
| **Learning Curve** | Gentle | Steeper |
| **Microservices** | Excellent | Good |
| **Large APIs** | Less ideal | Excellent |
| **Team Size** | Better for small | Better for large |
| **Testing** | Easy (isolated functions) | Easy (constructor injection) |

### Side-by-Side Comparison

**Minimal API Approach:**
```csharp
// Simple, explicit, few lines
app.MapGet("/api/users/{id}", GetUser)
    .WithName("GetUser")
    .Produces<UserDto>();

async Task<IResult> GetUser(int id, IUserService service)
{
    var user = await service.GetUserAsync(id);
    return user == null ? Results.NotFound() : Results.Ok(user);
}
```

**Controller Approach:**
```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _service;
    
    public UsersController(IUserService service) => _service = service;
    
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _service.GetUserAsync(id);
        return user == null ? NotFound() : Ok(user);
    }
}
```

**Minimal API with authorization:**
```csharp
app.MapGet("/api/users/{id}", GetUser)
    .RequireAuthorization();

async Task<IResult> GetUser(int id, IUserService service)
{
    var user = await service.GetUserAsync(id);
    return user == null ? Results.NotFound() : Results.Ok(user);
}
```

**Controller with authorization:**
```csharp
[Authorize]
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _service.GetUserAsync(id);
    return user == null ? NotFound() : Ok(user);
}
```

## When to Use Minimal APIs

```csharp
// ✓ Microservices - lightweight, focused
// ✓ Simple REST APIs - few endpoints
// ✓ GraphQL APIs - wrapper around resolvers
// ✓ Serverless/Functions - explicit endpoint definition
// ✓ CRUD operations - straightforward handlers
// ✓ Rapid prototyping - quick setup
// ✓ Single-developer projects - simple structure

// Example: Microservice
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IPaymentService, PaymentService>();

var app = builder.Build();

app.MapPost("/process", ProcessPayment)
    .WithName("ProcessPayment");

app.MapGet("/status/{id}", GetPaymentStatus)
    .WithName("GetPaymentStatus");

async Task<IResult> ProcessPayment(ProcessPaymentDto dto, IPaymentService service)
{
    var result = await service.ProcessAsync(dto);
    return Results.Ok(result);
}

async Task<IResult> GetPaymentStatus(string id, IPaymentService service)
{
    var status = await service.GetStatusAsync(id);
    return status == null ? Results.NotFound() : Results.Ok(status);
}

app.Run();
```

## When to Use Controllers

```csharp
// ✓ Large APIs - 50+ endpoints
// ✓ Complex domain - sophisticated business logic
// ✓ Team collaboration - clear structure for teams
// ✓ Feature-rich applications - many related endpoints
// ✓ Legacy .NET Core 3.1 - controllers are standard
// ✓ Multi-tenant applications - organized by feature/tenant

// Example: Large API with multiple controllers
[ApiController]
[Route("api/v1/[controller]")]
public class UsersController : ControllerBase { }

[ApiController]
[Route("api/v1/[controller]")]
public class ProductsController : ControllerBase { }

[ApiController]
[Route("api/v1/[controller]")]
public class OrdersController : ControllerBase { }

[ApiController]
[Route("api/v1/[controller]")]
public class InvoicesController : ControllerBase { }
```

## Hybrid Approach

ASP.NET Core allows mixing both approaches:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();

// Traditional controllers
app.MapControllers();

// Plus minimal APIs for simple endpoints
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .WithOpenApi();

app.MapPost("/webhook/github", HandleGithubWebhook)
    .AllowAnonymous()
    .WithName("GithubWebhook");

async Task<IResult> HandleGithubWebhook(HttpContext context)
{
    var body = await context.Request.BodyReader.ReadAsync();
    // Process webhook
    return Results.Accepted();
}

app.Run();
```

## Migration Between Approaches

### Converting Controller to Minimal API

```csharp
// Original Controller
[ApiController]
[Route("api/[controller]")]
public class CalculatorController : ControllerBase
{
    [HttpGet("add")]
    public IActionResult Add(int a, int b)
    {
        return Ok(new { result = a + b });
    }
    
    [HttpGet("multiply")]
    public IActionResult Multiply(int a, int b)
    {
        return Ok(new { result = a * b });
    }
}

// Converted to Minimal API
app.MapGroup("/api/calculator")
    .MapGet("/add", (int a, int b) => 
        Results.Ok(new { result = a + b }))
    .WithName("Add");

app.MapGroup("/api/calculator")
    .MapGet("/multiply", (int a, int b) => 
        Results.Ok(new { result = a * b }))
    .WithName("Multiply");
```

### Converting Minimal API to Controller

```csharp
// Original Minimal API
app.MapGet("/api/users", GetUsers)
    .WithName("GetUsers");

async Task<IResult> GetUsers(IUserService service)
{
    var users = await service.GetUsersAsync();
    return Results.Ok(users);
}

// Converted to Controller
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _service;
    
    public UsersController(IUserService service) => _service = service;
    
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetUsers()
    {
        var users = await _service.GetUsersAsync();
        return Ok(users);
    }
}
```

## Testing Considerations

### Minimal API Testing

```csharp
public class UserEndpointTests
{
    [Fact]
    public async Task GetUser_WithValidId_ReturnsOkWithUser()
    {
        // Arrange
        var serviceStub = new Mock<IUserService>();
        var user = new UserDto { Id = 1, Name = "John" };
        serviceStub.Setup(s => s.GetUserAsync(1))
            .ReturnsAsync(user);
        
        var handler = new GetUserHandler(serviceStub.Object);
        
        // Act
        var result = await handler(1);
        
        // Assert
        Assert.NotNull(result);
    }
}
```

### Controller Testing

```csharp
public class UserControllerTests
{
    [Fact]
    public async Task GetUser_WithValidId_ReturnsOkWithUser()
    {
        // Arrange
        var serviceMock = new Mock<IUserService>();
        var user = new UserDto { Id = 1, Name = "John" };
        serviceMock.Setup(s => s.GetUserAsync(1))
            .ReturnsAsync(user);
        
        var controller = new UsersController(serviceMock.Object);
        
        // Act
        var result = await controller.GetUser(1);
        
        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedUser = Assert.IsType<UserDto>(okResult.Value);
        Assert.Equal(1, returnedUser.Id);
    }
}
```

## Key Takeaways

1. **Minimal APIs are simpler**: Lower ceremony, explicit routing
2. **Controllers are structured**: Better for large, complex applications
3. **Performance is similar**: Choose based on design needs, not performance
4. **Mixing both is possible**: Hybrid approach for transition or special cases
5. **Minimal APIs suit microservices**: Simple, focused APIs
6. **Controllers suit monoliths**: Organized, feature-rich applications
7. **Both support DI**: Different injection patterns (method vs constructor)
8. **Swagger works with both**: Explicit metadata vs automatic generation
9. **Choose based on team experience**: Controllers for larger teams
10. **Scalability is organizational**: Code organization matters more than technology choice

## Recommendation

- **New greenfield projects**: Start with Minimal APIs unless you have 50+ endpoints
- **Microservices**: Use Minimal APIs
- **Enterprise systems**: Use Controllers with clear layering
- **Teams <5 people**: Minimal APIs
- **Teams >10 people**: Controllers for structure
- **Mixed architecture**: Possible but keep consistent per API/service

