# 7. Model Binding & Validation

## Overview

Model binding is the process of mapping HTTP request data (query strings, route values, headers, body) to handler method parameters. Validation ensures the bound data meets business requirements. Together, they form the critical input processing layer for any ASP.NET application.

## Model Binding Sources

ASP.NET searches for data in this order (by default):

1. **Form data** (POST body, application/x-www-form-urlencoded)
2. **Route values** (from URL path)
3. **Query string** (after ?)

You can explicitly specify the source using attributes.

### Source: Route Values

Route values are part of the URL path:

```csharp
// Controller-based
[HttpGet("api/users/{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    // id is bound from route value automatically
    var user = await _service.GetUserAsync(id);
    return Ok(user);
}

// Minimal API
app.MapGet("/api/users/{id}", GetUser);

async Task<IResult> GetUser(int id)
{
    // id is bound from route value automatically
    var user = await _service.GetUserAsync(id);
    return Results.Ok(user);
}

// Explicit with [FromRoute]
[HttpGet("api/users/{id}")]
public async Task<ActionResult<UserDto>> GetUser([FromRoute] int id)
{
    return Ok(await _service.GetUserAsync(id));
}
```

### Source: Query String

Query parameters come after the ? in the URL:

```csharp
// GET /api/users?page=2&limit=10&search=john&role=admin

// Implicit binding
[HttpGet("api/users")]
public async Task<ActionResult<List<UserDto>>> GetUsers(
    int page = 1,
    int limit = 10,
    string search = null,
    string role = null)
{
    var users = await _service.GetUsersAsync(page, limit, search, role);
    return Ok(users);
}

// Explicit with [FromQuery]
[HttpGet("api/users")]
public async Task<ActionResult<List<UserDto>>> GetUsers(
    [FromQuery] int page = 1,
    [FromQuery] int limit = 10,
    [FromQuery] string search = null,
    [FromQuery] string role = null)
{
    return Ok(await _service.GetUsersAsync(page, limit, search, role));
}

// Minimal API with [FromQuery]
app.MapGet("/api/users", GetUsers);

async Task<IResult> GetUsers(
    [FromQuery] int page = 1,
    [FromQuery] int limit = 10,
    [FromQuery] string search = null)
{
    var users = await _service.GetUsersAsync(page, limit, search);
    return Results.Ok(users);
}
```

### Source: Request Body

Body contains JSON, XML, or form data:

```csharp
// POST /api/users
// Content-Type: application/json
// {
//   "name": "John Doe",
//   "email": "john@example.com",
//   "age": 30
// }

// Implicit binding from body (POST/PUT/PATCH)
[HttpPost("api/users")]
public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto dto)
{
    // CreateUserDto is bound from request body automatically
    var user = await _service.CreateUserAsync(dto);
    return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
}

// Explicit with [FromBody]
[HttpPost("api/users")]
public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserDto dto)
{
    return CreatedAtAction(nameof(GetUser), 
        new { id = (await _service.CreateUserAsync(dto)).Id }, 
        await _service.CreateUserAsync(dto));
}

// Minimal API (body is implicit for non-primitive types)
app.MapPost("/api/users", CreateUser);

async Task<IResult> CreateUser(CreateUserDto dto)
{
    var user = await _service.CreateUserAsync(dto);
    return Results.CreatedAtRoute("GetUser", new { id = user.Id }, user);
}
```

### Source: Headers

Extract custom headers:

```csharp
// GET /api/data
// X-Custom-Header: custom-value
// Authorization: Bearer token123

[HttpGet("api/data")]
public IActionResult GetData([FromHeader] string xCustomHeader)
{
    // xCustomHeader contains the value from X-Custom-Header
    return Ok(new { header = xCustomHeader });
}

// Multiple headers
[HttpGet("api/data")]
public IActionResult GetData(
    [FromHeader(Name = "X-Request-ID")] string requestId,
    [FromHeader] string authorization)
{
    return Ok(new { requestId, authorization });
}

// Minimal API
app.MapGet("/api/data", GetData);

IResult GetData([FromHeader(Name = "X-Custom-Header")] string customHeader)
{
    return Results.Ok(new { header = customHeader });
}
```

### Source: Form Data

HTML form submissions:

```csharp
// Multipart form data
// Content-Type: multipart/form-data

[HttpPost("upload")]
public async Task<IActionResult> Upload([FromForm] FileUploadDto dto)
{
    // dto.File is an IFormFile
    if (dto.File == null || dto.File.Length == 0)
        return BadRequest("No file provided");
    
    var fileName = Path.GetFileName(dto.File.FileName);
    var filePath = Path.Combine("/uploads", fileName);
    
    using (var stream = new FileStream(filePath, FileMode.Create))
    {
        await dto.File.CopyToAsync(stream);
    }
    
    return Ok(new { fileName, size = dto.File.Length });
}

public class FileUploadDto
{
    public string Title { get; set; }
    public IFormFile File { get; set; }
}

// Multiple files
[HttpPost("upload-multiple")]
public async Task<IActionResult> UploadMultiple(
    [FromForm] string title,
    [FromForm] IFormFileCollection files)
{
    var uploadedFiles = new List<string>();
    
    foreach (var file in files)
    {
        var fileName = Path.GetFileName(file.FileName);
        var filePath = Path.Combine("/uploads", fileName);
        
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
            uploadedFiles.Add(fileName);
        }
    }
    
    return Ok(uploadedFiles);
}
```

### Source: Custom Binding

Specify sources explicitly:

```csharp
// Route + Query mixed
[HttpGet("api/users/{userId}/posts/{postId}")]
public async Task<IActionResult> GetUserPost(
    [FromRoute] int userId,
    [FromRoute] int postId,
    [FromQuery] bool includeComments = false)
{
    var post = await _service.GetUserPostAsync(userId, postId, includeComments);
    return Ok(post);
}

// Body + Route mixed
[HttpPut("api/users/{id}")]
public async Task<IActionResult> UpdateUser(
    [FromRoute] int id,
    [FromBody] UpdateUserDto dto)
{
    await _service.UpdateUserAsync(id, dto);
    return NoContent();
}

// Complex example
[HttpPost("api/orders/{orderId}/items")]
public async Task<IActionResult> AddOrderItem(
    [FromRoute] int orderId,
    [FromBody] AddOrderItemDto dto,
    [FromHeader(Name = "X-Idempotency-Key")] string idempotencyKey,
    [FromServices] IOrderService orderService)
{
    var item = await orderService.AddItemAsync(orderId, dto, idempotencyKey);
    return CreatedAtAction(nameof(AddOrderItem), 
        new { orderId, itemId = item.Id }, item);
}
```

### Source: Services (Dependency Injection)

Inject services into handlers:

```csharp
// Controllers - use constructor injection
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;
    
    public UsersController(IUserService userService, 
        ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        _logger.LogInformation("Getting user {UserId}", id);
        var user = await _userService.GetUserAsync(id);
        return Ok(user);
    }
}

// Minimal APIs - use method parameters
app.MapGet("/api/users/{id}", GetUser);

async Task<IResult> GetUser(
    int id,
    IUserService userService,
    ILogger<Program> logger)
{
    logger.LogInformation("Getting user {UserId}", id);
    var user = await userService.GetUserAsync(id);
    return user == null ? Results.NotFound() : Results.Ok(user);
}

// Or explicit with [FromServices]
app.MapGet("/api/users/{id}", GetUser);

async Task<IResult> GetUser(
    int id,
    [FromServices] IUserService userService)
{
    var user = await userService.GetUserAsync(id);
    return Results.Ok(user);
}
```

## Complex Type Binding

When binding complex types from request body, ASP.NET uses JSON deserialization:

```csharp
// Request body
{
    "user": {
        "name": "John",
        "email": "john@example.com"
    },
    "metadata": {
        "source": "web",
        "ipAddress": "192.168.1.1"
    }
}

// Binding to complex type
[HttpPost("api/users")]
public async Task<IActionResult> CreateUser(CreateUserRequest request)
{
    // request.User contains user data
    // request.Metadata contains metadata
    var user = await _service.CreateUserAsync(request.User);
    return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
}

public class CreateUserRequest
{
    public UserData User { get; set; }
    public MetadataData Metadata { get; set; }
}

public class UserData
{
    public string Name { get; set; }
    public string Email { get; set; }
}

public class MetadataData
{
    public string Source { get; set; }
    public string IpAddress { get; set; }
}
```

## Model Validation

Validation ensures input data meets business requirements. ASP.NET provides both data annotations and custom validators.

### Data Annotations (Attributes)

```csharp
public class CreateUserDto
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, MinimumLength = 2,
        ErrorMessage = "Name must be between 2 and 100 characters")]
    public string Name { get; set; }
    
    [Required]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; }
    
    [Range(18, 120, ErrorMessage = "Age must be between 18 and 120")]
    public int Age { get; set; }
    
    [Phone]
    public string Phone { get; set; }
    
    [Url]
    public string Website { get; set; }
    
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$",
        ErrorMessage = "Password must contain uppercase, lowercase, digit, and be at least 8 characters")]
    public string Password { get; set; }
    
    [Compare(nameof(Password), ErrorMessage = "Passwords don't match")]
    public string ConfirmPassword { get; set; }
    
    [Range(typeof(DateTime), "2000-01-01", "2024-12-31",
        ErrorMessage = "Date must be between 2000 and 2024")]
    public DateTime DateOfBirth { get; set; }
}

// In controller/handler
[HttpPost]
public async Task<IActionResult> CreateUser(CreateUserDto dto)
{
    // ModelState.IsValid is false if any validation fails
    if (!ModelState.IsValid)
    {
        // Return validation errors
        return BadRequest(ModelState);
        
        // Or return custom format
        // var errors = ModelState.Values.SelectMany(v => v.Errors);
        // return BadRequest(errors);
    }
    
    var user = await _service.CreateUserAsync(dto);
    return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
}
```

### Custom Validation Attributes

```csharp
// Custom attribute
[AttributeUsage(AttributeTargets.Property)]
public class NoFutureDate : ValidationAttribute
{
    public override bool IsValid(object value)
    {
        if (value == null)
            return true;
        
        if (value is not DateTime dateTime)
            return false;
        
        return dateTime <= DateTime.Today;
    }
    
    public override string FormatErrorMessage(string name)
    {
        return $"{name} cannot be in the future";
    }
}

// Usage
public class CreateEventDto
{
    [Required]
    public string Title { get; set; }
    
    [NoFutureDate]
    public DateTime Date { get; set; }
}
```

### FluentValidation (Advanced)

FluentValidation provides a more fluent, testable validation approach:

```csharp
// Install: dotnet add package FluentValidation

public class CreateUserDtoValidator : AbstractValidator<CreateUserDto>
{
    public CreateUserDtoValidator()
    {
        RuleFor(u => u.Name)
            .NotEmpty().WithMessage("Name is required")
            .Length(2, 100).WithMessage("Name must be between 2 and 100 characters");
        
        RuleFor(u => u.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email address");
        
        RuleFor(u => u.Age)
            .InclusiveBetween(18, 120).WithMessage("Age must be between 18 and 120");
        
        RuleFor(u => u.Phone)
            .Matches(@"^\+?1?\d{9,15}$")
            .When(u => !string.IsNullOrEmpty(u.Phone))
            .WithMessage("Invalid phone number");
        
        RuleFor(u => u.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .Matches("[A-Z]").WithMessage("Password must contain uppercase")
            .Matches("[a-z]").WithMessage("Password must contain lowercase")
            .Matches("[0-9]").WithMessage("Password must contain digit");
        
        RuleFor(u => u.ConfirmPassword)
            .Equal(u => u.Password)
            .WithMessage("Passwords don't match");
        
        RuleFor(u => u.DateOfBirth)
            .LessThan(DateTime.Today)
            .WithMessage("Date of birth must be in the past");
    }
}

// Register in Program.cs
builder.Services.AddValidatorsFromAssemblyContaining<CreateUserDtoValidator>();
builder.Services.AddFluentValidationAutoValidation();

// In handler - validation happens automatically
[HttpPost]
public async Task<IActionResult> CreateUser(CreateUserDto dto)
{
    // FluentValidation is invoked before handler is called
    // If invalid, 400 Bad Request returned automatically
    
    var user = await _service.CreateUserAsync(dto);
    return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
}
```

### Validation Error Response Handling

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateUser(CreateUserDto dto)
    {
        if (!ModelState.IsValid)
        {
            // Format 1: Return ModelState (ASP.NET default)
            return BadRequest(ModelState);
            
            // Format 2: Custom error response
            var errors = ModelState
                .Where(x => x.Value.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray());
            
            return BadRequest(new { errors });
            
            // Format 3: ProblemDetails (RFC 7807)
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Failed",
                Detail = "One or more validation errors occurred",
                Type = "https://api.example.com/errors/validation-error"
            };
            
            // Add validation errors to extensions
            foreach (var (key, value) in ModelState
                .Where(x => x.Value.Errors.Count > 0))
            {
                problemDetails.Extensions[$"errors.{key}"] = 
                    value.Errors.Select(e => e.ErrorMessage).ToArray();
            }
            
            return BadRequest(problemDetails);
        }
        
        var user = await _service.CreateUserAsync(dto);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }
}
```

### Global Validation Error Handling

```csharp
// Middleware for consistent error responses
public class ValidationExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ValidationExceptionMiddleware> _logger;
    
    public ValidationExceptionMiddleware(RequestDelegate next,
        ILogger<ValidationExceptionMiddleware> logger)
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
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation error: {Message}", ex.Message);
            
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            
            var errors = ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());
            
            var response = new
            {
                status = 400,
                message = "Validation failed",
                errors
            };
            
            await context.Response.WriteAsJsonAsync(response);
        }
    }
}

// Register in Program.cs
app.UseMiddleware<ValidationExceptionMiddleware>();
```

## Custom Model Binders

For complex binding scenarios, create custom model binders:

```csharp
// Custom model binder for specific type
public class DateTimeModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var modelName = bindingContext.ModelName;
        var valueProviderResult = bindingContext.ValueProvider
            .GetValue(modelName);
        
        if (valueProviderResult == ValueProviderResult.None)
            return Task.CompletedTask;
        
        bindingContext.ModelState.SetModelValue(modelName, valueProviderResult);
        
        var value = valueProviderResult.FirstValue;
        
        if (string.IsNullOrEmpty(value))
            return Task.CompletedTask;
        
        // Custom parsing logic
        if (DateTime.TryParseExact(value, "dd/MM/yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None,
            out var date))
        {
            bindingContext.Result = ModelBindingResult.Success(date);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(modelName,
                "Date format must be dd/MM/yyyy");
        }
        
        return Task.CompletedTask;
    }
}

// Register custom binder
builder.Services.AddControllers()
    .AddModelBinderProvider(typeof(DateTime),
        new BinderTypeModelBinderProvider(typeof(DateTimeModelBinder)));

// Usage
[HttpGet]
public IActionResult GetReportsByDate(
    [ModelBinder(BinderType = typeof(DateTimeModelBinder))]
    DateTime date)
{
    // date is parsed using custom format dd/MM/yyyy
    return Ok();
}
```

## Model Binding Best Practices

### 1. Use Explicit [FromX] Attributes

```csharp
// Good - clear where data comes from
[HttpPost]
public async Task<IActionResult> CreateUser(
    [FromBody] CreateUserDto dto,
    [FromHeader(Name = "X-Request-ID")] string requestId)
{
    return Ok();
}

// Avoid - implicit and confusing
[HttpPost]
public async Task<IActionResult> CreateUser(
    CreateUserDto dto,
    string requestId)
{
    return Ok();
}
```

### 2. Validate Early

```csharp
// Good - validate before processing
[HttpPost]
public async Task<IActionResult> CreateUser(CreateUserDto dto)
{
    if (!ModelState.IsValid)
        return BadRequest(ModelState);
    
    await _service.CreateUserAsync(dto);
    return Ok();
}

// Avoid - process without validation
[HttpPost]
public async Task<IActionResult> CreateUser(CreateUserDto dto)
{
    await _service.CreateUserAsync(dto);  // May throw
    return Ok();
}
```

### 3. Use DTOs for Binding

```csharp
// Good - separate DTO for request binding
public class CreateUserDto
{
    public string Name { get; set; }
    public string Email { get; set; }
}

public class UserDto  // Response DTO
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}

// Avoid - binding directly to domain model
public class User  // Domain model
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### 4. Handle Binding Errors Gracefully

```csharp
[HttpPost]
public async Task<IActionResult> CreateUser(CreateUserDto dto)
{
    // If binding fails (invalid JSON, type mismatch), 
    // ASP.NET automatically returns 400 with error details
    
    if (!ModelState.IsValid)
    {
        var errors = ModelState
            .Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage);
        
        return BadRequest(new { errors });
    }
    
    return Ok();
}
```

## Key Takeaways

1. **Model binding maps request data to parameters**: Automatic process
2. **Sources include routes, query, headers, body**: Specify with [FromX]
3. **Default source order matters**: Form > Route > Query
4. **Validation is critical**: Use data annotations or FluentValidation
5. **DTOs separate concerns**: Request, response, domain models
6. **Custom binders for complex types**: When defaults aren't enough
7. **Error handling is important**: Return clear validation error messages
8. **FluentValidation is powerful**: More readable than attributes
9. **Services can be injected**: Use [FromServices] or dependency injection
10. **Complex types require JSON deserialization**: Ensure proper Content-Type headers

## Related Topics

- **Routing & Endpoints** (Topic 6): How parameters are extracted
- **HTTP Fundamentals** (Topic 4): Request structure
- **Controllers & Minimal APIs** (Phase 3): Where binding is used

