# Chapter 16: API Documentation & Developer Experience

## 16.1 OpenAPI and Swagger

OpenAPI (formerly Swagger) is the standard for API documentation.

### Adding OpenAPI to ASP.NET Core

```csharp
// Program.cs
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1.0",
        Title = "My API",
        Description = "Enterprise API for managing users and orders",
        TermsOfService = new Uri("https://example.com/terms"),
        Contact = new OpenApiContact
        {
            Name = "API Support",
            Email = "api-support@example.com",
            Url = new Uri("https://example.com/support")
        },
        License = new OpenApiLicense
        {
            Name = "MIT",
            Url = new Uri("https://opensource.org/licenses/MIT")
        }
    });
    
    // Include XML comments in documentation
    var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
    
    // Add security definition for JWT
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using Bearer scheme"
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
});

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "My API v1");
        options.RoutePrefix = string.Empty;  // Swagger at root
    });
}

app.Run();
```

**Project file must enable XML documentation:**
```xml
<PropertyGroup>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

---

## 16.2 XML Comments and Annotations

Document endpoints with XML comments:

```csharp
[ApiController]
[Route("api/v1/[controller]")]
public class UsersController : ControllerBase
{
    /// <summary>
    /// Get user by ID
    /// </summary>
    /// <remarks>
    /// Sample request:
    /// 
    ///     GET /api/v1/users/123
    /// 
    /// Returns the user with specified ID along with their orders.
    /// </remarks>
    /// <param name="id">The user ID</param>
    /// <returns>The user details</returns>
    /// <response code="200">User found</response>
    /// <response code="401">Unauthorized</response>
    /// <response code="403">Insufficient permissions</response>
    /// <response code="404">User not found</response>
    [HttpGet("{id}")]
    [ProduceResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProduceResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _userService.GetUserAsync(id);
        if (user == null)
            return NotFound();
        return Ok(user);
    }
    
    /// <summary>
    /// Create a new user
    /// </summary>
    /// <remarks>
    /// Creates a new user account with the provided details.
    /// 
    /// Requirements:
    /// - Email must be unique
    /// - Password must be at least 12 characters
    /// - Password must contain uppercase, lowercase, digits
    /// 
    /// Sample request:
    /// 
    ///     POST /api/v1/users
    ///     {
    ///         "email": "john@example.com",
    ///         "firstName": "John",
    ///         "lastName": "Doe",
    ///         "password": "SecurePassword123!"
    ///     }
    /// </remarks>
    /// <param name="request">User creation details</param>
    /// <returns>Newly created user</returns>
    /// <response code="201">User created successfully</response>
    /// <response code="400">Invalid request</response>
    /// <response code="409">Email already in use</response>
    [HttpPost]
    [ProduceResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProduceResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProduceResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> CreateUser(CreateUserRequest request)
    {
        var user = await _userService.CreateUserAsync(request);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }
}

/// <summary>
/// Request to create a new user
/// </summary>
public class CreateUserRequest
{
    /// <summary>
    /// User's email address (must be unique)
    /// </summary>
    [EmailAddress]
    public string Email { get; set; }
    
    /// <summary>
    /// User's first name
    /// </summary>
    [StringLength(100, MinimumLength = 2)]
    public string FirstName { get; set; }
    
    /// <summary>
    /// User's last name
    /// </summary>
    [StringLength(100, MinimumLength = 2)]
    public string LastName { get; set; }
    
    /// <summary>
    /// User's password (min 12 chars, must include uppercase, lowercase, digit, special char)
    /// </summary>
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; set; }
}

/// <summary>
/// User details response
/// </summary>
public class UserDto
{
    /// <summary>
    /// Unique user identifier
    /// </summary>
    public int Id { get; set; }
    
    /// <summary>
    /// User's email address
    /// </summary>
    public string Email { get; set; }
    
    /// <summary>
    /// User's full name (firstName lastName)
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// When the user account was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
```

---

## 16.3 OpenAPI Enhancements

### Multiple API Versions

```csharp
builder.Services.AddSwaggerGen(options =>
{
    // Document v1
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "My API v1",
        DeprecationNotice = "This version is deprecated. Use v2 instead."
    });
    
    // Document v2
    options.SwaggerDoc("v2", new OpenApiInfo
    {
        Version = "v2",
        Title = "My API v2"
    });
});

app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1.0");
    options.SwaggerEndpoint("/swagger/v2/swagger.json", "v2.0");
    
    // Set default to latest
    options.DefaultModelsExpandDepth(1);
    options.DefaultModelExpandDepth(1);
});
```

### Filtering Endpoints

Only expose documented endpoints:

```csharp
options.DocumentFilter<HideEndpointsFilter>();

public class HideEndpointsFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var pathsToRemove = swaggerDoc.Paths
            .Where(p => p.Key.Contains("/admin") && !IsAdmin())
            .ToList();
        
        foreach (var path in pathsToRemove)
        {
            swaggerDoc.Paths.Remove(path.Key);
        }
    }
    
    private bool IsAdmin()
    {
        // Check if current user is admin
        return false;
    }
}
```

### Custom Documentation

```csharp
options.OperationFilter<AuthorizationOperationFilter>();

public class AuthorizationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var authAttribute = context.MethodInfo
            .GetCustomAttributes(inherit: true)
            .OfType<AuthorizeAttribute>()
            .FirstOrDefault();
        
        if (authAttribute != null)
        {
            operation.Description += "\n\n**Authorization Required**";
            
            if (!string.IsNullOrEmpty(authAttribute.Roles))
            {
                operation.Description += $"\n\nRequired role(s): {authAttribute.Roles}";
            }
        }
    }
}
```

---

## 16.4 API Documentation Beyond Swagger

### Developer Portal

Create a dedicated documentation site:

```bash
# Generate static docs from OpenAPI
npm install -g @redocly/cli

redocly build-docs openapi.json -o ./dist

# Serve static site
npx http-server ./dist
```

### Postman Collection

Export API definition for Postman:

```csharp
// Generate Postman collection from OpenAPI
// Via: https://www.postman.com/api-platform/api-client/
// Or: npx openapi-to-postman
```

### API Reference Documentation

Host comprehensive documentation:

```markdown
# My API Documentation

## Getting Started

### Authentication
All endpoints (except public ones) require JWT authentication.

```bash
curl -X GET http://localhost:5000/api/users/1 \
  -H "Authorization: Bearer {token}"
```

### Error Handling
All error responses follow RFC 7807 Problem Details format:

```json
{
  "type": "https://api.example.com/errors/validation-failed",
  "title": "Validation Error",
  "status": 422,
  "detail": "One or more validation errors occurred.",
  "errors": {
    "email": ["Email is required"]
  }
}
```

## Endpoints

### Create User
Creates a new user account...
```

---

## 16.5 API Versioning in Documentation

Document version changes clearly:

```markdown
# API Changelog

## v2.0 (2024-01-15)

### Breaking Changes
- Renamed `name` to `fullName` in User response
- Changed `price` type from string to decimal
- Removed `internalNotes` field from User response

### New Features
- Added `createdAt` field to User response
- Added `status` field to Order response

### Deprecations
- `User.firstName` and `User.lastName` deprecated, use `fullName` instead

### Migration Guide
Migrate from v1 to v2:
1. Update response field mappings
2. Test with v2 endpoints
3. Update `Authorization` header to target v2 API
4. Remove usage of deprecated fields

## v1.0 (2023-01-01)
Original release
Sunset: 2024-12-31

### Deprecated Fields
- `firstName` - use `fullName` instead
- `lastName` - use `fullName` instead
```

---

## 16.6 Interactive Documentation Examples

Provide runnable examples:

```csharp
/// <summary>
/// Get user by ID
/// </summary>
/// <example>
/// <code>
/// GET /api/users/123
/// Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
/// 
/// Response:
/// {
///   "id": 123,
///   "email": "john@example.com",
///   "name": "John Doe",
///   "createdAt": "2023-12-15T10:30:00Z"
/// }
/// </code>
/// </example>
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id) { }
```

---

## 16.7 SDK Generation

Generate client libraries from OpenAPI:

```bash
# Generate .NET client SDK
npm install -g autorest
autorest --input-file=openapi.json --csharp --output-folder=./MyApi.Client

# Generate TypeScript client
npx openapi-generator-cli generate -i openapi.json -g typescript-axios -o ./client

# Generate Go client
openapi-generator-cli generate -i openapi.json -g go -o ./go-client
```

**Generated SDK usage:**
```csharp
var client = new MyApiClient(new Uri("https://api.example.com"));
var user = await client.Users.GetAsync(123);
```

---

## 16.8 Rate Limiting Documentation

Document API rate limits:

```markdown
# Rate Limiting

API requests are rate limited based on authentication method.

## Authenticated Requests
- 100 requests per minute per user
- 1000 requests per day per user

## Unauthenticated Requests
- 10 requests per minute per IP address
- 100 requests per day per IP address

## Rate Limit Headers
Responses include rate limit information:

```
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 95
X-RateLimit-Reset: 1692873600
```

When limit is exceeded, API returns 429 Too Many Requests:

```json
{
  "type": "https://api.example.com/errors/rate-limit-exceeded",
  "title": "Rate Limit Exceeded",
  "status": 429,
  "detail": "Rate limit of 100 requests per minute exceeded",
  "retryAfter": 30
}
```
```

---

## 16.9 Webhook Documentation

Document webhooks for event notifications:

```markdown
# Webhooks

Webhooks notify your application of API events.

## Subscribing to Webhooks

```bash
curl -X POST https://api.example.com/webhooks \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://yourapp.com/webhooks/events",
    "events": ["user.created", "order.completed"]
  }'
```

## Webhook Events

### user.created
Triggered when a new user account is created.

**Payload:**
```json
{
  "id": "evt_123",
  "type": "user.created",
  "timestamp": "2023-12-15T10:30:00Z",
  "data": {
    "id": 123,
    "email": "john@example.com",
    "createdAt": "2023-12-15T10:30:00Z"
  }
}
```

## Webhook Signatures

Verify webhook authenticity:

```csharp
public bool VerifyWebhookSignature(string payload, string signature)
{
    var hash = HMAC256(payload, webhookSecret);
    return hash == signature;
}
```
```

---

## 16.10 Developer Experience Checklist

**Documentation:**
- ✓ OpenAPI spec available at `/swagger`
- ✓ Interactive Swagger UI for testing
- ✓ All endpoints documented with examples
- ✓ Error responses documented
- ✓ Rate limits documented
- ✓ Authentication explained
- ✓ Webhooks documented (if applicable)

**SDKs:**
- ✓ Official SDK(s) provided
- ✓ SDK documentation and examples
- ✓ SDK versioning strategy clear
- ✓ Common use cases documented

**API Design:**
- ✓ Consistent endpoint naming
- ✓ Consistent error response format
- ✓ Sensible defaults
- ✓ Intuitive field names
- ✓ Proper HTTP semantics

**Support:**
- ✓ Support channel (email, Slack, forum)
- ✓ FAQ with common issues
- ✓ Code examples in multiple languages
- ✓ Runnable sample projects
- ✓ Migration guides for version updates

---

## 16.11 Changelog and Communication

Keep developers informed of changes:

```markdown
# API Changelog

## 2024-01-15 - v2.0 Release

### Breaking Changes
- User endpoint returns `fullName` instead of `firstName`/`lastName`
- Order `price` field now returns decimal instead of string

### New Features
- Added pagination support to list endpoints
- Added filtering by status on Orders endpoint

### Bug Fixes
- Fixed race condition in concurrent order creation
- Fixed decimal precision issue in calculations

### Deprecations
- `/api/v1` endpoints deprecated, use `/api/v2`

### Timeline
- 2024-01-15: v2.0 released
- 2024-04-15: v1.0 support ends (60 days notice)
- 2024-05-15: v1.0 removed

Visit [Migration Guide](./migration-v1-to-v2.md) for upgrade instructions.

## 2023-12-01 - v1.2 Release

### New Features
- Added health check endpoint `/health`

### Bug Fixes
- Fixed timezone handling in timestamps

---

# Subscribing to Updates

- Email notifications: [Sign up](https://example.com/newsletter)
- Slack channel: #api-announcements
- Twitter: [@myapi](https://twitter.com/myapi)
```

---

## Summary

OpenAPI/Swagger provides machine-readable API documentation. XML comments enable auto-generated documentation. Multiple API versions must be clearly documented. Rate limits, authentication, and error formats need explanation. SDKs and runnable examples improve developer experience. Changelogs and deprecation notices help manage upgrades. A well-documented API is easier to use, reduces support burden, and improves adoption.

## Curriculum Complete

You've covered the full spectrum of enterprise C# API development:

**Foundation (1-3):** HTTP, REST, ASP.NET Core basics  
**Core (4-7):** Data access, requests/responses, authentication, testing  
**Advanced (8-11):** Versioning, performance, DDD, CQRS  
**Enterprise (12-14):** Logging, resilience, security  
**Production (15-16):** Deployment, documentation  

Next steps:
1. Apply this knowledge building real projects
2. Study the referenced libraries and frameworks in depth
3. Learn domain-specific concerns for your industry
4. Contribute to open-source .NET projects
5. Stay updated with C# and ASP.NET Core evolution

Good luck building robust, scalable APIs.
