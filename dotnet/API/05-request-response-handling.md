# Chapter 5: API Request/Response Handling

## 5.1 Model Binding and Validation

ASP.NET Core automatically binds incoming request data to action parameters.

### Binding Sources

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpPost("{id}/items")]
    public IActionResult AddOrderItem(
        int id,                                    // Route: /api/orders/123
        [FromQuery] int skip = 0,                  // Query: ?skip=10
        [FromQuery] int take = 20,                 // Query: ?take=50
        [FromBody] AddItemRequest request,         // Body: JSON
        [FromHeader] string authorization,        // Header: Authorization
        [FromServices] IOrderService service       // DI
    )
    {
        // All automatically bound
    }
}

public class AddItemRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}
```

**Binding precedence (without explicit attributes):**
1. Route parameters
2. Query parameters
3. Request body (complex types only)

**FromServices:** Inject services without storing in controller constructor. Useful for single-use services.

### Validation Attributes

Data annotations validate models automatically:

```csharp
public class CreateUserRequest
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; }
    
    [EmailAddress]
    public string Email { get; set; }
    
    [Range(18, 120)]
    public int Age { get; set; }
    
    [Phone]
    public string PhoneNumber { get; set; }
    
    [Url]
    public string Website { get; set; }
    
    [RegularExpression(@"^\d{3}-\d{2}-\d{4}$")]
    public string SocialSecurityNumber { get; set; }
}

[HttpPost]
public IActionResult CreateUser(CreateUserRequest request)
{
    // ASP.NET Core validates automatically
    if (!ModelState.IsValid)
        return BadRequest(ModelState);  // Not needed with [ApiController]
}
```

**[ApiController] behavior:**
- Validates model automatically
- Returns 400 with validation errors if invalid
- No need to check `ModelState.IsValid` manually

### Custom Validation

```csharp
public class UpdateUserRequest
{
    public string CurrentPassword { get; set; }
    public string NewPassword { get; set; }
    public string ConfirmPassword { get; set; }
}

// Custom validator
public class PasswordChangeValidator : AbstractValidator<UpdateUserRequest>
{
    public PasswordChangeValidator()
    {
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .Matches(@"[A-Z]").WithMessage("Must contain uppercase")
            .Matches(@"[a-z]").WithMessage("Must contain lowercase")
            .Matches(@"[0-9]").WithMessage("Must contain digit");
        
        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword)
            .WithMessage("Passwords don't match");
    }
}

// Register in Program.cs
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
builder.Services.AddFluentValidationAutoValidation();
```

**Data annotations vs. FluentValidation:**

| Aspect | Data Annotations | FluentValidation |
|--------|------------------|------------------|
| Definition | Attributes on properties | Fluent API in validator class |
| Reusability | One validation per property | One validator per type |
| Complexity | Limited | Excellent |
| Async validation | Limited | Full support |
| Custom messages | Basic | Excellent |

For complex validation, use FluentValidation.

---

## 5.2 Data Transfer Objects (DTOs)

DTOs transfer data between client and API without exposing domain models.

### Why Use DTOs

**Domain Model (internal):**
```csharp
public class User
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }  // Never expose!
    public string InternalNotes { get; set; }  // Internal only
    public decimal Salary { get; set; }        // Don't expose to clients
    public DateTime CreatedAt { get; set; }
}
```

**DTOs (external):**
```csharp
// Sent to clients - safe, controlled projection
public class UserDto
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
}

// Received from clients in requests
public class CreateUserRequest
{
    [Required]
    public string Email { get; set; }
    
    [Required]
    public string Name { get; set; }
    
    [Required]
    public string Password { get; set; }
}

public class UpdateUserRequest
{
    public string Name { get; set; }
    public string Email { get; set; }
    // Password change goes through different endpoint
}
```

**Benefits:**
- Security: Don't expose sensitive fields
- Versioning: Clients depend on DTO contract, not domain model
- Flexibility: Reshape data independently of domain model
- Clarity: API contract is explicit, not inferred from model
- Performance: Select only needed fields

### Mapping DTOs to Domain Models

**Manual mapping:**
```csharp
[HttpPost]
public async Task<ActionResult<UserDto>> CreateUser(CreateUserRequest request)
{
    var user = new User
    {
        Email = request.Email,
        Name = request.Name,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
        CreatedAt = DateTime.UtcNow
    };
    
    await context.Users.AddAsync(user);
    await context.SaveChangesAsync();
    
    return CreatedAtAction(nameof(GetUser), new { id = user.Id },
        new UserDto { Id = user.Id, Email = user.Email, Name = user.Name });
}
```

**Using AutoMapper:**
```csharp
// In Program.cs
builder.Services.AddAutoMapper(typeof(Program));

// Define mappings
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<User, UserDto>();
        CreateMap<CreateUserRequest, User>();
        CreateMap<UpdateUserRequest, User>();
    }
}

// Usage
[HttpPost]
public async Task<ActionResult<UserDto>> CreateUser(
    CreateUserRequest request, 
    IMapper mapper)
{
    var user = mapper.Map<User>(request);
    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
    user.CreatedAt = DateTime.UtcNow;
    
    await context.Users.AddAsync(user);
    await context.SaveChangesAsync();
    
    var userDto = mapper.Map<UserDto>(user);
    return CreatedAtAction(nameof(GetUser), new { id = user.Id }, userDto);
}
```

**LINQ projection (no mapping library):**
```csharp
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var userDto = await context.Users
        .Where(u => u.Id == id)
        .Select(u => new UserDto
        {
            Id = u.Id,
            Email = u.Email,
            Name = u.Name
        })
        .FirstOrDefaultAsync();
    
    if (userDto == null)
        return NotFound();
    
    return Ok(userDto);
}
```

LINQ projection is often preferred for APIs because:
- Only queries needed columns from database
- Explicit about what data is exposed
- No ORM overhead of loading full entity

---

## 5.3 Request/Response Serialization

ASP.NET Core APIs use JSON by default.

### JSON Serialization Configuration

```csharp
// In Program.cs
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;  // Pretty-print in dev
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// Or for traditional controllers:
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });
```

### Custom Serialization

```csharp
public class UserDto
{
    [JsonPropertyName("user_id")]  // Override name in JSON
    public int Id { get; set; }
    
    [JsonIgnore]  // Don't serialize this property
    public string InternalValue { get; set; }
    
    [JsonPropertyOrder(1)]  // Order in JSON output
    public string Email { get; set; }
}

// Conditional serialization
public class OrderDto
{
    public int Id { get; set; }
    public decimal Total { get; set; }
    
    // Only include if not null
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string DiscountCode { get; set; }
}
```

### Date/Time Serialization

```csharp
public class EventDto
{
    public int Id { get; set; }
    
    // ISO 8601 format (default)
    public DateTime CreatedAt { get; set; }
    
    // Custom format
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EventStatus Status { get; set; }
}

// Response:
{
  "id": 1,
  "createdAt": "2023-12-15T10:30:00Z",  // ISO 8601
  "status": "Pending"
}
```

---

## 5.4 Pagination, Filtering, and Sorting

### Pagination

```csharp
public class PagedRequest
{
    [Range(1, int.MaxValue)]
    public int PageNumber { get; set; } = 1;
    
    [Range(1, 100)]
    public int PageSize { get; set; } = 10;
}

public class PagedResponse<T>
{
    public List<T> Items { get; set; }
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    
    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;
    public bool HasNextPage => PageNumber < TotalPages;
    public bool HasPreviousPage => PageNumber > 1;
}

[HttpGet]
public async Task<ActionResult<PagedResponse<UserDto>>> GetUsers(
    [FromQuery] PagedRequest request)
{
    var totalCount = await context.Users.CountAsync();
    
    var items = await context.Users
        .OrderBy(u => u.Id)
        .Skip((request.PageNumber - 1) * request.PageSize)
        .Take(request.PageSize)
        .Select(u => new UserDto { /* ... */ })
        .ToListAsync();
    
    return Ok(new PagedResponse<UserDto>
    {
        Items = items,
        TotalCount = totalCount,
        PageNumber = request.PageNumber,
        PageSize = request.PageSize
    });
}
```

**Usage:**
```
GET /api/users?pageNumber=2&pageSize=20
```

### Filtering

```csharp
public class UserFilterRequest
{
    public string Email { get; set; }
    public string Status { get; set; }
    public DateTime? CreatedAfter { get; set; }
    public DateTime? CreatedBefore { get; set; }
}

[HttpGet]
public async Task<ActionResult<List<UserDto>>> GetUsers(
    [FromQuery] UserFilterRequest filter)
{
    var query = context.Users.AsQueryable();
    
    if (!string.IsNullOrWhiteSpace(filter.Email))
        query = query.Where(u => u.Email.Contains(filter.Email));
    
    if (!string.IsNullOrWhiteSpace(filter.Status))
        query = query.Where(u => u.Status == filter.Status);
    
    if (filter.CreatedAfter.HasValue)
        query = query.Where(u => u.CreatedAt >= filter.CreatedAfter.Value);
    
    if (filter.CreatedBefore.HasValue)
        query = query.Where(u => u.CreatedAt <= filter.CreatedBefore.Value);
    
    var users = await query
        .Select(u => new UserDto { /* ... */ })
        .ToListAsync();
    
    return Ok(users);
}
```

**Usage:**
```
GET /api/users?email=john&status=Active&createdAfter=2023-01-01
```

### Sorting

```csharp
public class SortRequest
{
    public string SortBy { get; set; } = "id";  // Column to sort
    public bool Descending { get; set; } = false;
}

[HttpGet]
public async Task<ActionResult<List<UserDto>>> GetUsers(
    [FromQuery] SortRequest sort)
{
    var query = context.Users.AsQueryable();
    
    query = sort.SortBy.ToLower() switch
    {
        "email" => sort.Descending 
            ? query.OrderByDescending(u => u.Email)
            : query.OrderBy(u => u.Email),
        "createdat" => sort.Descending
            ? query.OrderByDescending(u => u.CreatedAt)
            : query.OrderBy(u => u.CreatedAt),
        _ => sort.Descending
            ? query.OrderByDescending(u => u.Id)
            : query.OrderBy(u => u.Id)
    };
    
    var users = await query
        .Select(u => new UserDto { /* ... */ })
        .ToListAsync();
    
    return Ok(users);
}
```

**Usage:**
```
GET /api/users?sortBy=email&descending=true
```

**Reusable sorting:**
```csharp
public static IQueryable<T> ApplySort<T>(
    IQueryable<T> query, 
    string sortBy, 
    bool descending)
{
    var property = typeof(T).GetProperty(sortBy,
        System.Reflection.BindingFlags.IgnoreCase |
        System.Reflection.BindingFlags.Public);
    
    if (property == null)
        return query;
    
    var parameter = Expression.Parameter(typeof(T));
    var propertyAccess = Expression.MakeMemberAccess(parameter, property);
    var orderByExpression = Expression.Lambda(propertyAccess, parameter);
    
    var methodName = descending ? "OrderByDescending" : "OrderBy";
    var method = typeof(Queryable).GetMethods()
        .First(m => m.Name == methodName && m.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(T), property.PropertyType);
    
    return (IQueryable<T>)method.Invoke(null, new object[] { query, orderByExpression });
}

// Usage
query = ApplySort(query, sort.SortBy, sort.Descending);
```

---

## 5.5 Error Responses

Standardize error format for consistency.

### Problem Details (RFC 7807)

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateUser(CreateUserRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://api.example.com/errors/validation-failed",
                Title = "One or more validation errors occurred",
                Status = StatusCodes.Status400BadRequest,
                Detail = "See errors field for details",
                Extensions = new Dictionary<string, object>
                {
                    ["errors"] = ModelState
                        .Where(x => x.Value.Errors.Any())
                        .ToDictionary(
                            x => x.Key,
                            x => x.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                        )
                }
            });
        }
        
        // Business logic...
    }
}
```

**Response:**
```json
{
  "type": "https://api.example.com/errors/validation-failed",
  "title": "One or more validation errors occurred",
  "status": 400,
  "detail": "See errors field for details",
  "errors": {
    "email": ["Email is required", "Email format is invalid"],
    "name": ["Name is required"]
  }
}
```

### Custom Error Details

```csharp
public class ErrorResponse
{
    public string Type { get; set; }
    public string Title { get; set; }
    public int Status { get; set; }
    public string Detail { get; set; }
    public string TraceId { get; set; }
    public Dictionary<string, object> Extensions { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(int id)
    {
        var user = await context.Users.FindAsync(id);
        
        if (user == null)
        {
            return NotFound(new ErrorResponse
            {
                Type = "https://api.example.com/errors/not-found",
                Title = "User Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = $"User with ID {id} was not found",
                TraceId = HttpContext.TraceIdentifier
            });
        }
        
        return Ok(user);
    }
}
```

### Global Exception Handler

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
            Title = "An unexpected error occurred",
            Status = StatusCodes.Status500InternalServerError,
            Detail = exception?.Message,
            Instance = context.Request.Path
        };
        
        // Log the error
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(exception, "Unhandled exception");
        
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/problem+json";
        
        await context.Response.WriteAsJsonAsync(problemDetails);
    });
});
```

---

## 5.6 Content Negotiation

APIs can serve multiple formats.

### Supporting Multiple Content Types

```csharp
[HttpGet("{id}")]
[Produces("application/json", "application/xml", "text/csv")]
public ActionResult<UserDto> GetUser(int id)
{
    var user = context.Users.Find(id);
    return Ok(user);
}

// Client requests format via Accept header:
// GET /api/users/1
// Accept: application/xml
```

### Custom Formatters

```csharp
public class CsvOutputFormatter : TextOutputFormatter
{
    public CsvOutputFormatter()
    {
        SupportedMediaTypes.Add("text/csv");
    }
    
    public override async Task WriteResponseBodyAsync(
        OutputFormatterWriteContext context, 
        Encoding selectedEncoding)
    {
        var response = context.HttpContext.Response;
        var users = (List<UserDto>)context.Object;
        
        using (var writer = new StreamWriter(response.Body))
        {
            writer.WriteLine("Id,Email,Name");
            foreach (var user in users)
            {
                writer.WriteLine($"{user.Id},{user.Email},{user.Name}");
            }
            await writer.FlushAsync();
        }
    }
}

// Register in Program.cs
builder.Services.AddControllers(options =>
{
    options.OutputFormatters.Add(new CsvOutputFormatter());
});
```

---

## Summary

Proper request/response handling requires DTOs for security and versioning, validation for data integrity, and standardized error responses for predictable client handling. Pagination, filtering, and sorting provide flexibility for large datasets. The next chapter covers authentication and authorization—critical for protecting APIs.
