# 10. API Documentation & Specification

## Overview

API documentation is critical for developer experience and API adoption. Modern API documentation is often generated from specifications like OpenAPI/Swagger. Understanding how to document, generate, and maintain API documentation ensures clear contracts between frontend and backend teams.

## Documentation Importance

### Why API Documentation Matters

```
Well-Documented API:
- Developers understand endpoints quickly
- Reduces support questions
- Enables faster integration
- Better developer experience
- Reduces bugs from misuse

Poorly-Documented API:
- Developers waste time reverse-engineering
- More bugs and support tickets
- Higher integration time
- Frustrated consumers
- API misuse and data corruption
```

## OpenAPI/Swagger

### What is OpenAPI?

OpenAPI is a specification for describing REST APIs in a machine-readable format. Swagger is a set of tools that work with OpenAPI.

```
OpenAPI Specification (OAS):
├── API information
│   ├── Title
│   ├── Version
│   └── Description
├── Servers
├── Paths (endpoints)
│   ├── Operations (GET, POST, etc.)
│   ├── Parameters
│   ├── Request body
│   └── Responses
├── Components
│   ├── Schemas (data models)
│   └── Security schemes
└── External documentation
```

### Setting Up Swagger in ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Swagger/OpenAPI support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // API information
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "User Management API",
        Version = "v1.0.0",
        Description = "API for managing users in the system",
        Contact = new OpenApiContact
        {
            Name = "Support Team",
            Email = "support@example.com",
            Url = new Uri("https://example.com/support")
        },
        License = new OpenApiLicense
        {
            Name = "MIT License",
            Url = new Uri("https://opensource.org/licenses/MIT")
        }
    });
    
    // Include XML comments in documentation
    var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// Enable Swagger UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
        options.RoutePrefix = "";  // Swagger at root
    });
}

app.MapControllers();
app.Run();
```

### Generated Swagger/OpenAPI Endpoints

After setup, ASP.NET generates:

```
/swagger/v1/swagger.json     ← OpenAPI specification (JSON)
/swagger/ui/index.html       ← Interactive Swagger UI
/redoc/index.html            ← ReDoc documentation (if configured)
```

Visit `http://localhost:5000` to see interactive API documentation.

## Documenting Endpoints

### XML Comments Documentation

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    /// <summary>
    /// Get a user by ID
    /// </summary>
    /// <param name="id">The user ID</param>
    /// <returns>User details</returns>
    /// <response code="200">User found</response>
    /// <response code="404">User not found</response>
    [HttpGet("{id}")]
    [ProduceResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProduceResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _service.GetUserAsync(id);
        return user == null ? NotFound() : Ok(user);
    }
    
    /// <summary>
    /// Create a new user
    /// </summary>
    /// <remarks>
    /// Sample request:
    /// 
    ///     POST /api/users
    ///     {
    ///         "name": "John Doe",
    ///         "email": "john@example.com",
    ///         "age": 30
    ///     }
    /// </remarks>
    /// <param name="dto">User data to create</param>
    /// <returns>Created user with ID</returns>
    /// <response code="201">User created successfully</response>
    /// <response code="400">Invalid input data</response>
    [HttpPost]
    [ProduceResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProduceResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        var user = await _service.CreateUserAsync(dto);
        return CreatedAtAction(nameof(GetUser), 
            new { id = user.Id }, user);
    }
    
    /// <summary>
    /// Update a user
    /// </summary>
    /// <param name="id">The user ID to update</param>
    /// <param name="dto">Updated user data</param>
    /// <response code="204">User updated successfully</response>
    /// <response code="404">User not found</response>
    [HttpPut("{id}")]
    [ProduceResponseType(StatusCodes.Status204NoContent)]
    [ProduceResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUser(int id, UpdateUserDto dto)
    {
        await _service.UpdateUserAsync(id, dto);
        return NoContent();
    }
    
    /// <summary>
    /// Delete a user
    /// </summary>
    /// <param name="id">The user ID to delete</param>
    /// <response code="204">User deleted successfully</response>
    /// <response code="404">User not found</response>
    [HttpDelete("{id}")]
    [ProduceResponseType(StatusCodes.Status204NoContent)]
    [ProduceResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _service.DeleteUserAsync(id);
        return NoContent();
    }
}

/// <summary>
/// User data transfer object
/// </summary>
public class UserDto
{
    /// <summary>
    /// Unique user identifier
    /// </summary>
    public int Id { get; set; }
    
    /// <summary>
    /// User's full name
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// User's email address
    /// </summary>
    public string Email { get; set; }
    
    /// <summary>
    /// User's age
    /// </summary>
    public int Age { get; set; }
}

/// <summary>
/// Data required to create a new user
/// </summary>
public class CreateUserDto
{
    /// <summary>
    /// Full name (required, 2-100 characters)
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; }
    
    /// <summary>
    /// Email address (required, must be valid)
    /// </summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; }
    
    /// <summary>
    /// Age (required, 18-120)
    /// </summary>
    [Required]
    [Range(18, 120)]
    public int Age { get; set; }
}
```

### Documenting Minimal APIs

```csharp
app.MapGet("/api/users/{id}", GetUser)
    .WithName("GetUser")
    .WithOpenApi()
    .WithSummary("Retrieve a user by ID")
    .WithDescription("Returns the details of a user by their unique identifier")
    .WithParameter("id", description: "The user's unique identifier")
    .Produces<UserDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .WithTags("Users");

app.MapPost("/api/users", CreateUser)
    .WithName("CreateUser")
    .WithOpenApi()
    .WithSummary("Create a new user")
    .WithDescription("""
        Creates a new user in the system.
        
        Returns the created user with assigned ID.
        """)
    .Accepts<CreateUserDto>("application/json")
    .Produces<UserDto>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .WithTags("Users");

app.MapGroup("/api/users")
    .WithTags("Users")
    .WithOpenApi()
    .MapGet("/", GetUsers)
        .WithName("GetUsers")
        .WithSummary("List all users")
        .Produces<List<UserDto>>()
    .MapDelete("/{id}", DeleteUser)
        .WithName("DeleteUser")
        .WithSummary("Delete a user")
        .Produces(StatusCodes.Status204NoContent);

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

async Task<IResult> GetUsers(IUserService service)
{
    var users = await service.GetUsersAsync();
    return Results.Ok(users);
}

async Task<IResult> DeleteUser(int id, IUserService service)
{
    await service.DeleteUserAsync(id);
    return Results.NoContent();
}
```

## Custom Swagger Documentation

### Advanced Swagger Configuration

```csharp
builder.Services.AddSwaggerGen(options =>
{
    // Multiple API versions
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "User API",
        Version = "1.0",
        Description = "Current stable version"
    });
    
    options.SwaggerDoc("v2", new OpenApiInfo
    {
        Title = "User API",
        Version = "2.0",
        Description = "New version with breaking changes"
    });
    
    // Security scheme for JWT
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKeyInHeader,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        Description = "Enter 'Bearer' followed by your JWT token"
    });
    
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
    
    // Custom operation filter
    options.OperationFilter<AuthorizationOperationFilter>();
    
    // Document file location
    var xmlPath = Path.Combine(AppContext.BaseDirectory, 
        "MyApi.xml");
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

// Custom operation filter
public class AuthorizationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, 
        OperationFilterContext context)
    {
        // Check if endpoint has [Authorize]
        var hasAuthorize = context.MethodInfo
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Length > 0;
        
        if (!hasAuthorize)
            return;
        
        // Add security requirement to operation
        operation.Security = new List<OpenApiSecurityRequirement>
        {
            new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    new string[] { }
                }
            }
        };
    }
}
```

### Schema Documentation

```csharp
// Configure schema generation
builder.Services.AddSwaggerGen(options =>
{
    options.SchemaGeneratorOptions = new SchemaGeneratorOptions
    {
        UseInlineDefinitionsForEnums = true
    };
    
    // Custom schema filter for enums
    options.SchemaFilter<EnumSchemaFilter>();
});

public class EnumSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type.IsEnum)
        {
            schema.Enum.Clear();
            
            // Document enum values with descriptions
            foreach (var value in Enum.GetValues(context.Type))
            {
                schema.Enum.Add(new OpenApiString(value.ToString()));
            }
        }
    }
}

// Enum with documentation
/// <summary>
/// User role enumeration
/// </summary>
public enum UserRole
{
    /// <summary>Administrator with full access</summary>
    Admin = 1,
    
    /// <summary>Manager with limited access</summary>
    Manager = 2,
    
    /// <summary>Regular user with basic access</summary>
    User = 3
}
```

## API Versioning Documentation

### Documenting Multiple API Versions

```csharp
// Version strategy: URL-based
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[ApiVersion("2.0")]
public class UsersController : ControllerBase
{
    /// <summary>
    /// Get user (v1.0 - deprecated)
    /// </summary>
    /// <remarks>
    /// This endpoint is deprecated in v2.0.
    /// Use GET /api/v2/users/{id} instead.
    /// </remarks>
    [HttpGet("{id}")]
    [MapToApiVersion("1.0")]
    [Obsolete("Use v2.0 endpoint instead")]
    public async Task<ActionResult<UserDtoV1>> GetUserV1(int id)
    {
        return Ok(await _service.GetUserV1Async(id));
    }
    
    /// <summary>
    /// Get user (v2.0 - current)
    /// </summary>
    /// <remarks>
    /// New in v2.0: Returns additional fields like last login date.
    /// </remarks>
    [HttpGet("{id}")]
    [MapToApiVersion("2.0")]
    public async Task<ActionResult<UserDtoV2>> GetUserV2(int id)
    {
        return Ok(await _service.GetUserV2Async(id));
    }
}

// Swagger with versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "User API",
        Version = "1.0",
        Description = "Deprecated - use v2.0",
        DeprecationNotice = "This version will be deprecated on 2025-01-01"
    });
    
    options.SwaggerDoc("v2", new OpenApiInfo
    {
        Title = "User API",
        Version = "2.0",
        Description = "Current stable version",
        Contact = new OpenApiContact
        {
            Name = "API Support",
            Email = "api@example.com"
        }
    });
    
    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        var apiVersion = apiDesc.ActionDescriptor
            .GetApiVersion()
            ?.ToString() ?? "1.0";
        
        return docName == $"v{apiVersion.Split('.')[0]}";
    });
});
```

## Alternative Documentation Formats

### ReDoc for Beautiful Documentation

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    
    // Add ReDoc UI
    app.UseReDoc(options =>
    {
        options.RoutePrefix = "api-docs";
        options.SpecUrl = "/swagger/v1/swagger.json";
        options.DocumentTitle = "User API Documentation";
    });
}

app.MapControllers();
app.Run();

// Visit http://localhost:5000/api-docs for ReDoc
```

### Custom HTML Documentation

```html
<!-- Custom documentation page -->
<!DOCTYPE html>
<html>
<head>
    <title>User API Documentation</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        .endpoint { border: 1px solid #ddd; padding: 10px; margin: 10px 0; }
        .method { font-weight: bold; color: #0066cc; }
        .path { font-family: monospace; background: #f5f5f5; padding: 5px; }
    </style>
</head>
<body>
    <h1>User API Documentation</h1>
    
    <div class="endpoint">
        <p><span class="method">GET</span> <span class="path">/api/users/{id}</span></p>
        <p>Retrieve a user by ID</p>
        <h4>Parameters:</h4>
        <ul>
            <li><strong>id</strong> (integer, required): User ID</li>
        </ul>
        <h4>Response:</h4>
        <pre>
{
    "id": 1,
    "name": "John Doe",
    "email": "john@example.com",
    "age": 30
}
        </pre>
    </div>
    
    <div class="endpoint">
        <p><span class="method">POST</span> <span class="path">/api/users</span></p>
        <p>Create a new user</p>
        <h4>Request Body:</h4>
        <pre>
{
    "name": "John Doe",
    "email": "john@example.com",
    "age": 30
}
        </pre>
    </div>
</body>
</html>
```

## Documentation Best Practices

### 1. Keep Documentation In Sync

```csharp
// Good: Documentation close to code
[HttpGet("{id}")]
/// <summary>Get user by ID</summary>
/// <param name="id">The user ID</param>
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    // ...
}

// Bad: Documentation in separate file (becomes outdated)
// (in external README.md that's rarely updated)
```

### 2. Provide Examples

```csharp
/// <summary>
/// Create a new user
/// </summary>
/// <remarks>
/// Sample request:
/// 
///     POST /api/users
///     {
///         "name": "Jane Smith",
///         "email": "jane@example.com",
///         "age": 28
///     }
/// 
/// Sample response (201 Created):
/// 
///     {
///         "id": 42,
///         "name": "Jane Smith",
///         "email": "jane@example.com",
///         "age": 28
///     }
/// </remarks>
[HttpPost]
public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto dto)
{
    // ...
}
```

### 3. Document Error Responses

```csharp
/// <summary>Get a user</summary>
/// <response code="200">User found</response>
/// <response code="400">Invalid user ID</response>
/// <response code="401">Unauthorized - authentication required</response>
/// <response code="404">User not found</response>
/// <response code="500">Server error</response>
[HttpGet("{id}")]
[ProduceResponseType(typeof(UserDto), 200)]
[ProduceResponseType(typeof(ErrorResponse), 400)]
[ProduceResponseType(typeof(ErrorResponse), 401)]
[ProduceResponseType(typeof(ErrorResponse), 404)]
[ProduceResponseType(typeof(ErrorResponse), 500)]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    // ...
}
```

### 4. Document Rate Limits & Quotas

```csharp
/// <summary>
/// Get user data
/// </summary>
/// <remarks>
/// Rate limited to 100 requests per minute per API key.
/// Returned headers:
/// - X-RateLimit-Limit: 100
/// - X-RateLimit-Remaining: Request count remaining
/// - X-RateLimit-Reset: Unix timestamp when limit resets
/// </remarks>
[HttpGet("{id}")]
[RateLimit(perMinute: 100)]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    // ...
}
```

### 5. Document Authentication

```csharp
/// <summary>
/// List all users
/// </summary>
/// <remarks>
/// Requires authentication via JWT bearer token.
/// 
/// Include header:
///     Authorization: Bearer &lt;your-jwt-token&gt;
/// </remarks>
[Authorize]
[HttpGet]
public async Task<ActionResult<List<UserDto>>> GetUsers()
{
    // ...
}
```

## Documentation Generation

### Generate Documentation File

```bash
# Enable XML documentation generation in .csproj
# <PropertyGroup>
#   <GenerateDocumentationFile>true</GenerateDocumentationFile>
# </PropertyGroup>

# Build project to generate XML comments
dotnet build

# XML file appears in output directory
# bin/Debug/net8.0/MyApi.xml
```

### Export OpenAPI as File

```csharp
// Export OpenAPI specification to file
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger(options =>
    {
        options.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
        {
            // Can modify OpenAPI spec before serialization
        });
    });
    
    // Export to file
    var swaggerJson = Path.Combine(AppContext.BaseDirectory, 
        "swagger.json");
    
    app.MapGet("/swagger/export", () =>
    {
        var spec = File.ReadAllText(swaggerJson);
        return Results.File(
            Encoding.UTF8.GetBytes(spec),
            "application/json",
            "swagger.json");
    });
}

app.MapControllers();
app.Run();
```

## Key Takeaways

1. **OpenAPI is the standard**: Machine-readable API specification
2. **Swagger UI provides interactive exploration**: Built-in to ASP.NET Core
3. **XML comments document code**: Developers stay close to implementation
4. **Multiple API versions**: Document separately with breaking changes noted
5. **Examples are essential**: Show request/response patterns
6. **Error responses need documentation**: Clarity on failure modes
7. **Security schemes matter**: JWT, API keys, OAuth explained
8. **Keep docs in sync with code**: Use attributes and comments
9. **ReDoc for beautiful docs**: Alternative to Swagger UI
10. **Versioning strategy impacts docs**: URL-based, header-based, or query-based

## Related Topics

- **Minimal APIs vs Controllers** (Topic 8): How to document each approach
- **Request/Response Handling** (Topic 9): Response format documentation
- **Routing & Endpoints** (Topic 6): Endpoint structure and naming

