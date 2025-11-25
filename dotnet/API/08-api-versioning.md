# Chapter 8: API Versioning & Backwards Compatibility

## 8.1 Why API Versioning Matters

APIs evolve. Changing endpoints breaks existing clients. Versioning allows you to make breaking changes while maintaining backwards compatibility.

**Scenarios requiring versioning:**

- Removing/renaming fields from response
- Changing response format
- Removing endpoints
- Changing parameter types
- Modifying business logic significantly

**Good API design delays breaking changes through versioning.**

---

## 8.2 Versioning Strategies

### URL Path Versioning (Most Common)

Version in the URL path:

```csharp
[ApiController]
[Route("api/v1/[controller]")]
public class UsersV1Controller : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDtoV1>> GetUser(int id)
    {
        var user = await _service.GetUserAsync(id);
        return Ok(new UserDtoV1
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name
        });
    }
}

[ApiController]
[Route("api/v2/[controller]")]
public class UsersV2Controller : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDtoV2>> GetUser(int id)
    {
        var user = await _service.GetUserAsync(id);
        return Ok(new UserDtoV2
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.Name,  // Renamed from Name
            CreatedAt = user.CreatedAt  // New field
        });
    }
}
```

**Requests:**
```
GET /api/v1/users/1  → UserDtoV1
GET /api/v2/users/1  → UserDtoV2
```

**Advantages:**
- Explicit in URL (clear which version being used)
- Easy to test in browser
- Cacheable per version
- Clear for documentation

**Disadvantages:**
- Creates multiple route handlers
- URL bloat
- Duplicated code (need to refactor)

### Header-Based Versioning

Version specified via Accept header:

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(int id, [FromHeader(Name = "api-version")] string version = "1.0")
    {
        var user = await _service.GetUserAsync(id);
        
        return version switch
        {
            "1.0" => Ok(new UserDtoV1 { /* ... */ }),
            "2.0" => Ok(new UserDtoV2 { /* ... */ }),
            _ => BadRequest("Unsupported API version")
        };
    }
}
```

**Requests:**
```
GET /api/users/1
api-version: 1.0
→ UserDtoV1

GET /api/users/1
api-version: 2.0
→ UserDtoV2
```

**Advantages:**
- Cleaner URLs
- Single route handler
- Standard HTTP behavior

**Disadvantages:**
- Not obvious from URL (harder to test in browser)
- Less discoverable
- Not cached per version by default

### Custom Media Type Versioning

Version in Accept header using vendor-specific media types:

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    [Produces("application/vnd.myapi.v1+json")]
    [Produces("application/vnd.myapi.v2+json")]
    public async Task<IActionResult> GetUser(
        int id,
        [FromHeader(Name = "Accept")] string accept)
    {
        var user = await _service.GetUserAsync(id);
        
        if (accept.Contains("v2"))
            return Ok(new UserDtoV2 { /* ... */ });
        
        return Ok(new UserDtoV1 { /* ... */ });
    }
}
```

**Requests:**
```
GET /api/users/1
Accept: application/vnd.myapi.v2+json
→ UserDtoV2
```

**Advantages:**
- Follows HTTP semantics
- Single URL
- Content negotiation

**Disadvantages:**
- Complex format
- Less common
- Harder for clients to understand

### Query Parameter Versioning

Version as query parameter:

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(int id, [FromQuery] int version = 1)
    {
        var user = await _service.GetUserAsync(id);
        
        return version switch
        {
            1 => Ok(new UserDtoV1 { /* ... */ }),
            2 => Ok(new UserDtoV2 { /* ... */ }),
            _ => BadRequest("Unsupported version")
        };
    }
}
```

**Requests:**
```
GET /api/users/1?version=2
→ UserDtoV2
```

**Advantages:**
- Easy for clients
- Visible in URL

**Disadvantages:**
- Mixes API version with query concerns
- Caching issues
- Not standard practice

---

## 8.3 Implementing Versioning at Scale

For larger APIs, implement versioning systematically to reduce code duplication.

### Abstract Controllers

```csharp
// Base controller with shared logic
public abstract class BaseUsersController : ControllerBase
{
    protected readonly IUserService _userService;
    protected readonly IMapper _mapper;
    
    protected BaseUsersController(IUserService userService, IMapper mapper)
    {
        _userService = userService;
        _mapper = mapper;
    }
    
    protected async Task<User> GetUserAsync(int id)
    {
        var user = await _userService.GetUserAsync(id);
        if (user == null)
            return NotFound();
        return user;
    }
}

[ApiController]
[Route("api/v1/[controller]")]
public class UsersV1Controller : BaseUsersController
{
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDtoV1>> GetUser(int id)
    {
        var user = await GetUserAsync(id);
        if (user == null)
            return NotFound();
        
        return Ok(_mapper.Map<UserDtoV1>(user));
    }
}

[ApiController]
[Route("api/v2/[controller]")]
public class UsersV2Controller : BaseUsersController
{
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDtoV2>> GetUser(int id)
    {
        var user = await GetUserAsync(id);
        if (user == null)
            return NotFound();
        
        return Ok(_mapper.Map<UserDtoV2>(user));
    }
}
```

### Unified Routing with Filters

```csharp
[AttributeUsage(AttributeTargets.Class)]
public class ApiVersionAttribute : Attribute
{
    public int[] Versions { get; }
    
    public ApiVersionAttribute(params int[] versions)
    {
        Versions = versions;
    }
}

[ApiController]
[Route("api/users")]
[ApiVersion(1, 2)]  // Supports both v1 and v2
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    
    public UsersController(IUserService userService)
    {
        _userService = userService;
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(
        int id,
        [FromHeader(Name = "api-version")] int version = 1)
    {
        var user = await _userService.GetUserAsync(id);
        if (user == null)
            return NotFound();
        
        return version switch
        {
            1 => Ok(new UserDtoV1 { /* map from user */ }),
            2 => Ok(new UserDtoV2 { /* map from user */ }),
            _ => BadRequest("Unsupported API version")
        };
    }
}
```

### ApiVersioning NuGet Package

Use `Asp.Versioning.Mvc` for professional versioning:

```csharp
// Program.cs
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;  // Return supported versions in header
    options.ApiVersionReader = new UrlSegmentApiVersionReader();  // v{version:apiVersion}
})
.AddMvc()
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<UserDtoV1>> GetUserV1(int id) { }
    
    [HttpGet("{id}")]
    [MapToApiVersion("2.0")]
    public async Task<ActionResult<UserDtoV2>> GetUserV2(int id) { }
}
```

---

## 8.4 Deprecation and Sunset

Plan lifecycle for older API versions.

### Deprecation Policy

```csharp
[ApiController]
[Route("api/v1/[controller]")]
public class UsersV1Controller : ControllerBase
{
    [HttpGet("{id}")]
    [Deprecated("Use /api/v2/users instead. v1 will be sunset on 2024-12-31")]
    public async Task<ActionResult<UserDtoV1>> GetUser(int id)
    {
        // Implementation
    }
}
```

**Add deprecation headers to responses:**

```csharp
public class DeprecationMiddleware
{
    private readonly RequestDelegate _next;
    
    public DeprecationMiddleware(RequestDelegate next)
    {
        _next = next;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/v1"))
        {
            context.Response.Headers.Add("Deprecation", "true");
            context.Response.Headers.Add("Sunset", "Fri, 31 Dec 2024 23:59:59 GMT");
            context.Response.Headers.Add("Link", "</api/v2/users>; rel=\"successor-version\"");
        }
        
        await _next(context);
    }
}

// Register in Program.cs
app.UseMiddleware<DeprecationMiddleware>();
```

**Client receives:**
```
HTTP/1.1 200 OK
Deprecation: true
Sunset: Fri, 31 Dec 2024 23:59:59 GMT
Link: </api/v2/users>; rel="successor-version"

{...}
```

### Version Sunset Timeline

Communicate version lifecycle clearly:

```
v1: Released Jan 2022
    Deprecated: Jan 2024 (2 years of support)
    Sunset: Dec 2024 (12 months notice)

v2: Released Jan 2024
    Current stable version
    Support until Jan 2026

v3: Released Jan 2025
    Latest version
```

---

## 8.5 Managing Breaking Changes

Sometimes breaking changes are necessary. Manage them carefully.

### Semantic Versioning

Use MAJOR.MINOR.PATCH:
- **MAJOR**: Breaking changes (v1 → v2)
- **MINOR**: New features, backwards compatible (v1.0 → v1.1)
- **PATCH**: Bug fixes (v1.0.0 → v1.0.1)

### Breaking Change Examples

**Renaming field:**
```csharp
// v1
{
  "fullName": "John Doe"
}

// v2 (breaking)
{
  "name": "John Doe"  // Renamed
}
```

**Changing field type:**
```csharp
// v1
{
  "price": "19.99"  // String
}

// v2 (breaking)
{
  "price": 19.99  // Number
}
```

**Removing field:**
```csharp
// v1
{
  "id": 1,
  "name": "John",
  "internalNotes": "..."  // Should never have been here
}

// v2 (breaking)
{
  "id": 1,
  "name": "John"
}
```

**Adding required field:**
```csharp
// v1: type optional
public class UserDtoV1
{
    public int Id { get; set; }
    public string Name { get; set; }
}

// v2: new required field (breaking for POST)
public class UserDtoV2
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Department { get; set; }  // Required!
}
```

### Minimizing Breaking Changes

**Make new fields optional:**
```csharp
public class UserDtoV2
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string? Department { get; set; }  // Optional, won't break clients
}
```

**Add fields instead of renaming:**
```csharp
public class UserDtoV2
{
    public int Id { get; set; }
    public string FullName { get; set; }  // Keep old name too
    public string Name { get; set; }
    
    [JsonIgnore]
    public string FullNameDeprecated => FullName;  // For backwards compat
}
```

**Additive versioning (no removal):**
- Never remove fields, mark as deprecated
- Never remove endpoints, mark as deprecated
- Transition clients before actual removal

---

## 8.6 Versioning Strategy Decision

Choose based on your context:

**Use URL versioning (/api/v1/) when:**
- You have many breaking changes
- You need clear separation between versions
- Clients should be aware of version
- You want explicit version documentation

**Use header versioning when:**
- You want to minimize URL changes
- Versions are minor differences
- You want API flexibility
- URLs should be version-agnostic

**Recommendation for enterprise:**
- Start with URL versioning (/api/v1/)
- Clear, explicit, standard
- Easier to document and test
- Clients understand versioning
- Can migrate to header versioning later if needed

---

## 8.7 Version Migration Example

Real-world scenario: migrating from v1 to v2.

**v1 endpoint:**
```csharp
[HttpGet("users/{id}")]
public class UserDtoV1
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
}
```

**v2 endpoint (improved):**
```csharp
[HttpGet("users/{id}")]
public class UserDtoV2
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string FullName { get; set; }  // Consolidated
    public string Status { get; set; }    // New
    public DateTime CreatedAt { get; set; }  // New
}
```

**Both supported together:**
```csharp
[ApiController]
public class UsersController : ControllerBase
{
    [HttpGet("api/v1/users/{id}")]
    public async Task<ActionResult<UserDtoV1>> GetUserV1(int id)
    {
        var user = await _service.GetUserAsync(id);
        return Ok(new UserDtoV1
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName
        });
    }
    
    [HttpGet("api/v2/users/{id}")]
    public async Task<ActionResult<UserDtoV2>> GetUserV2(int id)
    {
        var user = await _service.GetUserAsync(id);
        return Ok(new UserDtoV2
        {
            Id = user.Id,
            Email = user.Email,
            FullName = $"{user.FirstName} {user.LastName}",
            Status = user.Status,
            CreatedAt = user.CreatedAt
        });
    }
}
```

**Migration timeline:**
- Month 1-2: Both v1 and v2 available
- Month 2: Deprecation headers added to v1
- Month 3-8: v1 deprecated, v2 stable
- Month 9: v1 sunset, removed

---

## Summary

Versioning allows APIs to evolve without breaking clients. URL path versioning is clearest for enterprise APIs. Plan version lifecycles with clear deprecation timelines. Minimize breaking changes through thoughtful API design. The next chapter covers performance optimization—critical for scalable APIs.
