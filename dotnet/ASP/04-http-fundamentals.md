# 4. HTTP Fundamentals

## Overview

HTTP (HyperText Transfer Protocol) is the foundation of web communication. Understanding HTTP deeply is critical for building effective ASP.NET applications. HTTP requests flow through ASP.NET's middleware pipeline and are transformed into responses.

## HTTP Protocol Basics

HTTP is a **stateless, request-response protocol** built on top of TCP/IP.

### Request-Response Cycle

```
Client                          Server
  │                              │
  ├─ Send HTTP Request ────────→ │
  │                              │
  │                         Process request
  │                              │
  │ ←──── Send HTTP Response ──┤
  │                              │
```

### HTTP Versions

| Version | Released | Key Features |
|---------|----------|--------------|
| HTTP/1.0 | 1996 | One request per connection |
| HTTP/1.1 | 1997 | Persistent connections, pipelining |
| HTTP/2 | 2015 | Multiplexing, server push, compression |
| HTTP/3 | 2022 | QUIC protocol, faster, more reliable |

**ASP.NET Core supports all versions.** Kestrel automatically negotiates the best version with clients.

## HTTP Requests

An HTTP request consists of:
1. **Request Line** (method, path, version)
2. **Headers** (metadata about request)
3. **Body** (optional data)

### Request Line & Method

```
GET /api/users/123 HTTP/1.1
│   │              │
│   │              └─ Protocol version
│   └─ Request target (path + query)
└─ HTTP method
```

### HTTP Methods (Verbs)

HTTP methods indicate the intended action on a resource.

#### Safe Methods (don't modify server state)

```csharp
// GET: Retrieve data
GET /api/users                   // List all users
GET /api/users/123               // Get specific user
GET /api/users?page=2&limit=10   // With query parameters

// HEAD: Like GET but without response body
HEAD /api/users                  // Check if endpoint exists, get headers

// OPTIONS: Describe communication options
OPTIONS /api/users               // CORS preflight
```

#### Idempotent Methods (safe to repeat)

```csharp
// PUT: Replace entire resource
PUT /api/users/123               // Replace user 123 completely
// Request body must contain complete new user data
{
    "id": 123,
    "name": "John",
    "email": "john@example.com",
    "age": 30
}

// DELETE: Remove resource
DELETE /api/users/123            // Delete user 123

// Can call multiple times safely - same result
```

#### Non-Idempotent Methods

```csharp
// POST: Create new resource or trigger action
POST /api/users                  // Create new user
// Request body contains only fields for new user
{
    "name": "John",
    "email": "john@example.com",
    "age": 30
}
// Each call creates a NEW user - NOT idempotent!

// PATCH: Partial update
PATCH /api/users/123             // Update specific fields of user 123
{
    "age": 31                    // Only age changes, other fields untouched
}
```

### HTTP Headers

Headers provide metadata about the request and expected response.

```
GET /api/users HTTP/1.1
Host: api.example.com
Content-Type: application/json
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
Accept: application/json
Accept-Language: en-US
User-Agent: Mozilla/5.0
Cache-Control: no-cache
X-Custom-Header: custom-value
```

**Common Request Headers:**

| Header | Purpose | Example |
|--------|---------|---------|
| `Host` | Domain name and port | `api.example.com:8080` |
| `Content-Type` | Format of request body | `application/json` |
| `Content-Length` | Size of request body in bytes | `256` |
| `Authorization` | Authentication credentials | `Bearer token123` |
| `Accept` | Desired response format | `application/json` |
| `Accept-Language` | Preferred languages | `en-US,en;q=0.9` |
| `User-Agent` | Client software info | `Mozilla/5.0` |
| `Referer` | Previous page URL | `https://google.com` |
| `Cookie` | Session/auth cookies | `session_id=abc123` |
| `Cache-Control` | Caching directives | `no-cache, no-store` |

**Custom Headers:**

```
X-Request-ID: f8b3c7e2-4d1a-11ed-bdc3-0242ac120002
X-API-Version: 2.0
X-Client-Version: 1.2.3
```

### Query Parameters

Data in the URL string:

```
GET /api/users?page=2&limit=10&sort=name&filter=active

Parsed as:
- page: "2"
- limit: "10"
- sort: "name"
- filter: "active"
```

### Request Body

Data sent with the request (POST, PUT, PATCH):

```
POST /api/users HTTP/1.1
Content-Type: application/json
Content-Length: 47

{
    "name": "John Doe",
    "email": "john@example.com"
}
```

## HTTP Responses

A response includes:
1. **Status Line** (version, status code, reason phrase)
2. **Headers** (metadata about response)
3. **Body** (response data)

### Response Line & Status Codes

```
HTTP/1.1 200 OK
│        │   │
│        │   └─ Reason phrase
│        └─ Status code
└─ Protocol version
```

**Status Codes by Category:**

#### 1xx (Informational)

```
100 Continue        - Send body, server is ready
101 Switching Protocols - Upgrading connection (e.g., WebSocket)
```

#### 2xx (Success)

```
200 OK              - Request succeeded
201 Created         - Resource created (include Location header + body)
202 Accepted        - Request accepted but not processed yet
204 No Content      - Request succeeded, no body to return
```

**Usage in ASP.NET:**

```csharp
// 200 OK - Standard success
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _service.GetUserAsync(id);
    return Ok(user);  // 200 OK
}

// 201 Created - Resource created
[HttpPost]
public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto dto)
{
    var user = await _service.CreateUserAsync(dto);
    return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    // 201 Created with Location: /api/users/123
}

// 202 Accepted - Async processing
[HttpPost("import")]
public async Task<IActionResult> ImportUsers(ImportRequest request)
{
    _ = _service.ImportUsersAsync(request);  // Fire and forget
    return Accepted();  // 202 Accepted
}

// 204 No Content - Success with no body
[HttpDelete("{id}")]
public async Task<IActionResult> DeleteUser(int id)
{
    await _service.DeleteUserAsync(id);
    return NoContent();  // 204 No Content
}
```

#### 3xx (Redirection)

```
301 Moved Permanently    - Resource permanently moved
302 Found                - Temporary redirect
304 Not Modified         - Use cached version
307 Temporary Redirect   - Temporarily moved
```

```csharp
// Redirect to new location
[HttpGet("old-endpoint")]
public IActionResult OldEndpoint()
{
    return RedirectPermanent("api/new-endpoint");  // 301
}

// 304 Not Modified - for caching
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _service.GetUserAsync(id);
    var etag = GenerateETag(user);
    
    var ifNoneMatch = Request.Headers["If-None-Match"].ToString();
    if (ifNoneMatch == etag)
    {
        return StatusCode(304);  // 304 Not Modified - use cache
    }
    
    Response.Headers.Add("ETag", etag);
    return Ok(user);  // 200 OK
}
```

#### 4xx (Client Error)

```
400 Bad Request          - Malformed request
401 Unauthorized         - Authentication required
403 Forbidden            - Authenticated but no permission
404 Not Found            - Resource doesn't exist
409 Conflict             - Request conflicts with state (e.g., duplicate)
422 Unprocessable Entity - Validation failed
429 Too Many Requests    - Rate limited
```

```csharp
// 400 Bad Request
[HttpPost]
public async Task<IActionResult> CreateUser(CreateUserDto dto)
{
    if (!ModelState.IsValid)
    {
        return BadRequest(ModelState);  // 400 Bad Request
    }
    // ... create user
}

// 401 Unauthorized
[Authorize]
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _service.GetUserAsync(id);
    if (user == null)
    {
        return Unauthorized();  // 401 Unauthorized
    }
    return Ok(user);
}

// 403 Forbidden
[Authorize]
[HttpDelete("{id}")]
public async Task<IActionResult> DeleteUser(int id)
{
    var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    
    if (currentUserId != id)
    {
        return Forbid();  // 403 Forbidden - no permission
    }
    
    await _service.DeleteUserAsync(id);
    return NoContent();
}

// 404 Not Found
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _service.GetUserAsync(id);
    if (user == null)
    {
        return NotFound();  // 404 Not Found
    }
    return Ok(user);
}

// 409 Conflict
[HttpPost]
public async Task<IActionResult> CreateUser(CreateUserDto dto)
{
    var exists = await _service.UserExistsAsync(dto.Email);
    if (exists)
    {
        return Conflict("User with this email already exists");  // 409 Conflict
    }
    // ... create user
}

// 422 Unprocessable Entity
[HttpPost]
public async Task<IActionResult> CreateUser(CreateUserDto dto)
{
    var errors = ValidateUser(dto);
    if (errors.Any())
    {
        return UnprocessableEntity(errors);  // 422 Unprocessable Entity
    }
    // ... create user
}
```

#### 5xx (Server Error)

```
500 Internal Server Error       - Unexpected error
501 Not Implemented             - Feature not implemented
502 Bad Gateway                 - Invalid response from upstream
503 Service Unavailable         - Server temporarily down
504 Gateway Timeout             - Upstream server timeout
```

```csharp
// 500 Internal Server Error - automatic on unhandled exceptions
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _service.GetUserAsync(id);  // Exception → 500
    return Ok(user);
}

// Manual 500
[HttpGet]
public IActionResult GetUsers()
{
    try
    {
        return Ok(_service.GetUsers());
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting users");
        return StatusCode(500, "An error occurred");  // Explicit 500
    }
}

// 503 Service Unavailable
[HttpGet]
public IActionResult Health()
{
    if (!_database.IsHealthy())
    {
        return StatusCode(503, "Service unavailable");  // 503
    }
    return Ok("Healthy");
}
```

### Response Headers

```
HTTP/1.1 200 OK
Content-Type: application/json
Content-Length: 256
Cache-Control: max-age=3600
ETag: "33a64df551425fcc55e4d42a148795d9f25f89d4"
Set-Cookie: session_id=abc123; HttpOnly; Secure
Location: https://api.example.com/users/123
Access-Control-Allow-Origin: *
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 42
```

**Common Response Headers:**

| Header | Purpose | Example |
|--------|---------|---------|
| `Content-Type` | Format of response body | `application/json` |
| `Content-Length` | Size of response body | `256` |
| `Content-Encoding` | Compression used | `gzip` |
| `Cache-Control` | Caching instructions | `max-age=3600` |
| `ETag` | Resource version hash | `"abc123"` |
| `Last-Modified` | Last modification date | `Wed, 21 Oct 2023 07:28:00 GMT` |
| `Set-Cookie` | Session cookies | `session_id=xyz; HttpOnly` |
| `Location` | Redirect/created resource URL | `https://api.example.com/users/123` |
| `Access-Control-Allow-Origin` | CORS origin allowed | `*` or specific domain |
| `WWW-Authenticate` | Authentication scheme | `Bearer realm="api"` |
| `X-RateLimit-Limit` | Rate limit cap | `100` |
| `X-RateLimit-Remaining` | Requests remaining | `42` |

## Content Negotiation

Content negotiation allows client and server to agree on data format.

### Accept Header

Client indicates preferred formats (in priority order):

```
GET /api/users HTTP/1.1
Accept: application/json, application/xml;q=0.9, text/plain;q=0.8

Meaning:
- application/json (preferred, implicit q=1.0)
- application/xml (q=0.9)
- text/plain (q=0.8, lowest priority)
```

### ASP.NET Content Negotiation

```csharp
// ASP.NET automatically selects formatter based on Accept header
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _service.GetUserAsync(id);
        
        // If Accept: application/json → returns JSON
        // If Accept: application/xml → returns XML (if configured)
        return Ok(user);
    }
}

// Configure multiple formatters
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddXmlSerializerFormatters();  // Add XML support

// Now endpoint supports both JSON and XML automatically
```

### Explicit Content Negotiation

```csharp
[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    [HttpGet("{id}")]
    [Produces("application/json", "application/xml", "text/csv")]
    public async Task<ActionResult<ReportDto>> GetReport(int id)
    {
        var report = await _service.GetReportAsync(id);
        
        var acceptHeader = Request.Headers["Accept"].ToString();
        
        if (acceptHeader.Contains("text/csv"))
        {
            return File(GenerateCsv(report), "text/csv", "report.csv");
        }
        
        return Ok(report);  // Default to JSON
    }
    
    private byte[] GenerateCsv(ReportDto report)
    {
        // Generate CSV content
        return Encoding.UTF8.GetBytes("...");
    }
}
```

## HTTP Status Code Best Practices

### RESTful API Response Patterns

```csharp
// GET - Retrieve
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _service.GetUserAsync(id);
    return user == null ? NotFound() : Ok(user);
}

// POST - Create
[HttpPost]
public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto dto)
{
    if (!ModelState.IsValid)
        return BadRequest(ModelState);
    
    var user = await _service.CreateUserAsync(dto);
    return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
}

// PUT - Replace
[HttpPut("{id}")]
public async Task<IActionResult> UpdateUser(int id, UpdateUserDto dto)
{
    if (!ModelState.IsValid)
        return BadRequest(ModelState);
    
    var success = await _service.UpdateUserAsync(id, dto);
    return success ? NoContent() : NotFound();
}

// PATCH - Partial update
[HttpPatch("{id}")]
public async Task<IActionResult> PatchUser(int id, JsonPatchDocument<UserDto> patch)
{
    if (patch == null)
        return BadRequest();
    
    var user = await _service.GetUserAsync(id);
    if (user == null)
        return NotFound();
    
    await _service.PatchUserAsync(id, patch);
    return NoContent();
}

// DELETE - Remove
[HttpDelete("{id}")]
public async Task<IActionResult> DeleteUser(int id)
{
    var success = await _service.DeleteUserAsync(id);
    return success ? NoContent() : NotFound();
}
```

## HTTP Caching

### Client-side Caching

Browsers and clients cache responses based on headers.

```csharp
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _service.GetUserAsync(id);
    
    // Cache for 1 hour
    Response.Headers.CacheControl = "public, max-age=3600";
    
    return Ok(user);
}

[HttpGet("public-data")]
public IActionResult GetPublicData()
{
    // Cache for 24 hours
    Response.Headers.CacheControl = "public, max-age=86400";
    
    return Ok(new { data = "..." });
}

[HttpGet("sensitive")]
[Authorize]
public async Task<ActionResult<UserDto>> GetSensitiveData()
{
    var user = await _service.GetCurrentUserAsync();
    
    // Don't cache sensitive data
    Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    Response.Headers.Pragma = "no-cache";
    
    return Ok(user);
}
```

### ETags for Validation

```csharp
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _service.GetUserAsync(id);
    
    // Generate ETag (version identifier)
    var etag = GenerateETag(user);
    Response.Headers.ETag = etag;
    
    // Check If-None-Match
    var ifNoneMatch = Request.Headers["If-None-Match"].ToString();
    if (ifNoneMatch == etag)
    {
        return StatusCode(304);  // Not Modified - use cache
    }
    
    return Ok(user);
}

private string GenerateETag(UserDto user)
{
    var json = JsonConvert.SerializeObject(user);
    var hash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(json));
    return $"\"{Convert.ToBase64String(hash)}\"";
}
```

## HTTP Security

### HTTPS/TLS

Always use HTTPS in production.

```csharp
// Force HTTPS in ASP.NET Core
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

var app = builder.Build();

// Redirect HTTP to HTTPS
app.UseHttpsRedirection();

// Add HSTS header
app.UseHsts();
```

### Secure Headers

```csharp
app.Use(async (context, next) =>
{
    // Prevent clickjacking
    context.Response.Headers.XFrameOptions = "DENY";
    
    // Prevent MIME type sniffing
    context.Response.Headers.XContentTypeOptions = "nosniff";
    
    // Enable XSS protection
    context.Response.Headers.XXSSProtection = "1; mode=block";
    
    // Content Security Policy
    context.Response.Headers.ContentSecurityPolicy = 
        "default-src 'self'; script-src 'self' 'unsafe-inline'";
    
    await next();
});
```

## Compression

HTTP compression reduces bandwidth usage.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<GzipCompressionProvider>();
    options.Providers.Add<BrotliCompressionProvider>();
    
    // Compress these MIME types
    options.MimeTypes = new[]
    {
        "application/json",
        "application/xml",
        "text/plain",
        "text/html",
        "text/css",
        "application/javascript"
    };
});

var app = builder.Build();

// Enable compression middleware
app.UseResponseCompression();
```

## Key Takeaways

1. **HTTP is stateless**: Each request is independent
2. **Methods have semantics**: Use GET for retrieval, POST for creation, PUT for replacement, PATCH for partial update, DELETE for removal
3. **Status codes are critical**: Use appropriate codes for clarity
4. **Headers carry metadata**: Content-Type, Authorization, Cache-Control, etc.
5. **Content negotiation is automatic**: Accept header determines format
6. **Caching improves performance**: Use Cache-Control and ETags wisely
7. **HTTPS is mandatory**: Always use TLS in production
8. **Response design matters**: Include proper headers, status codes, and bodies
9. **Idempotency is important**: PUT and DELETE can be safely repeated
10. **Security headers are essential**: Prevent common attacks with proper headers

## Common HTTP Patterns in ASP.NET

- **Pagination**: Use `?page=2&limit=10` query parameters
- **Filtering**: Use `?status=active&role=admin` query parameters
- **Sorting**: Use `?sort=name&order=asc` query parameters
- **Searching**: Use `?q=searchterm` query parameter
- **Partial responses**: Use `?fields=id,name,email` to limit returned fields
- **API versioning**: Use `/api/v1/`, `/api/v2/` paths or headers

