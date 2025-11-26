# 9. Request/Response Handling

## Overview

Request/response handling is about processing incoming data and returning results in the correct format. This includes serialization, deserialization, content negotiation, error responses, and streaming. Mastering these patterns ensures robust, performant APIs.

## Serialization & Deserialization

### JSON Serialization (Default)

ASP.NET Core uses `System.Text.Json` by default for JSON serialization/deserialization.

```csharp
// Default configuration
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Default JSON behavior:
// - PascalCase → camelCase (JSON)
// - Null values are included
// - Case-insensitive property matching

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto dto)
    {
        // JSON is automatically deserialized to CreateUserDto
        // {"name": "John", "email": "john@example.com"} → dto
        
        var user = new UserDto 
        { 
            Id = 1, 
            Name = dto.Name, 
            Email = dto.Email 
        };
        
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
        // user is automatically serialized to JSON
        // {"id": 1, "name": "John", "email": "john@example.com"}
    }
}

public class CreateUserDto
{
    public string Name { get; set; }
    public string Email { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}
```

### Custom JSON Serialization Options

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Preserve property casing (PascalCase)
    options.SerializerOptions.PropertyNamingPolicy = null;
    
    // Don't include null values
    options.SerializerOptions.DefaultIgnoreCondition = 
        JsonIgnoreCondition.WhenWritingNull;
    
    // Pretty print (indented)
    options.SerializerOptions.WriteIndented = true;
    
    // Allow trailing commas and comments (non-standard JSON)
    options.SerializerOptions.AllowTrailingCommas = true;
    options.SerializerOptions.ReadCommentHandling = 
        JsonCommentHandling.Skip;
    
    // Custom type handling
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter());
    
    // Handle circular references
    options.SerializerOptions.ReferenceHandler = 
        ReferenceHandler.IgnoreCycles;
});

// Minimal API configuration
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = 
        JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();
```

### Custom JSON Converters

```csharp
// Custom converter for DateTime (ISO 8601 format)
public class CustomDateTimeConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    
    public override DateTime Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return DateTime.ParseExact(
            reader.GetString(),
            Format,
            CultureInfo.InvariantCulture);
    }
    
    public override void Write(
        Utf8JsonWriter writer,
        DateTime value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(Format));
    }
}

// Register converter
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new CustomDateTimeConverter());
});

// Usage
public class EventDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime EventDate { get; set; }  // Uses custom format
}
```

### XML Serialization

```csharp
// Configure XML support
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddXmlSerializerFormatters();  // Add XML formatter

var app = builder.Build();
app.MapControllers();

// Now endpoints support both JSON and XML
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _service.GetUserAsync(id);
    
    // Returns JSON if Accept: application/json
    // Returns XML if Accept: application/xml
    return Ok(user);
}

// Client requesting XML
// GET /api/users/1
// Accept: application/xml
// 
// Response:
// <UserDto>
//   <Id>1</Id>
//   <Name>John</Name>
//   <Email>john@example.com</Email>
// </UserDto>
```

## Response Formats

### Standard Responses

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    // 200 OK with data
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _service.GetUserAsync(id);
        return user == null ? NotFound() : Ok(user);
    }
    
    // 201 Created
    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto dto)
    {
        var user = await _service.CreateUserAsync(dto);
        return CreatedAtAction(nameof(GetUser), 
            new { id = user.Id }, user);
    }
    
    // 202 Accepted (async processing)
    [HttpPost("import")]
    public IActionResult ImportUsers(ImportRequest request)
    {
        _ = Task.Run(() => _service.ImportAsync(request));
        return Accepted(new { message = "Import started" });
    }
    
    // 204 No Content
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _service.DeleteUserAsync(id);
        return NoContent();
    }
}
```

### ProblemDetails Standard Response

RFC 7807 defines a standard error response format:

```csharp
// Automatic ProblemDetails for errors
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _service.GetUserAsync(id);
        
        if (user == null)
        {
            // Problem method creates ProblemDetails response
            return Problem(
                title: "User not found",
                detail: $"User with ID {id} not found",
                statusCode: StatusCodes.Status404NotFound,
                type: "https://api.example.com/errors/not-found");
        }
        
        return Ok(user);
    }
    
    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto dto)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }
        
        var user = await _service.CreateUserAsync(dto);
        return CreatedAtAction(nameof(GetUser), 
            new { id = user.Id }, user);
    }
}

// ProblemDetails response format:
// {
//   "type": "https://api.example.com/errors/not-found",
//   "title": "User not found",
//   "status": 404,
//   "detail": "User with ID 1 not found",
//   "instance": "/api/users/1"
// }
```

### Custom Error Responses

```csharp
// Custom error response format
public class ErrorResponse
{
    public int Code { get; set; }
    public string Message { get; set; }
    public Dictionary<string, string[]> Errors { get; set; }
    public string TraceId { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto dto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Errors
                        .Select(e => e.ErrorMessage)
                        .ToArray());
            
            var response = new ErrorResponse
            {
                Code = 400,
                Message = "Validation failed",
                Errors = errors,
                TraceId = HttpContext.TraceIdentifier
            };
            
            return BadRequest(response);
        }
        
        var user = await _service.CreateUserAsync(dto);
        return CreatedAtAction(nameof(GetUser), 
            new { id = user.Id }, user);
    }
}
```

### Envelope Response Pattern

Wrap all responses in a consistent envelope:

```csharp
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T Data { get; set; }
    public string Message { get; set; }
    public List<string> Errors { get; set; }
}

public class ApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public List<string> Errors { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetUser(int id)
    {
        var user = await _service.GetUserAsync(id);
        
        if (user == null)
        {
            return NotFound(new ApiResponse
            {
                Success = false,
                Message = "User not found",
                Errors = new List<string> { $"No user with ID {id}" }
            });
        }
        
        return Ok(new ApiResponse<UserDto>
        {
            Success = true,
            Data = user,
            Message = "User retrieved successfully"
        });
    }
    
    [HttpPost]
    public async Task<ActionResult<ApiResponse<UserDto>>> CreateUser(
        CreateUserDto dto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            
            return BadRequest(new ApiResponse
            {
                Success = false,
                Message = "Validation failed",
                Errors = errors
            });
        }
        
        var user = await _service.CreateUserAsync(dto);
        
        return CreatedAtAction(nameof(GetUser),
            new { id = user.Id },
            new ApiResponse<UserDto>
            {
                Success = true,
                Data = user,
                Message = "User created successfully"
            });
    }
}
```

## Content Negotiation

### Automatic Content Negotiation

```csharp
// Configure multiple formatters
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = 
            JsonNamingPolicy.CamelCase;
    })
    .AddXmlSerializerFormatters()
    .AddCsvFormatter();  // Custom formatter

var app = builder.Build();
app.MapControllers();

// Client controls format via Accept header
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _service.GetUserAsync(id);
    return user == null ? NotFound() : Ok(user);
}

// GET /api/users/1
// Accept: application/json → JSON response
//
// GET /api/users/1
// Accept: application/xml → XML response
//
// GET /api/users/1
// Accept: text/csv → CSV response
```

### Explicit Content Type Declaration

```csharp
[HttpGet("{id}")]
[Produces("application/json", "application/xml")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _service.GetUserAsync(id);
    return Ok(user);
}

// With custom formatter
[HttpGet("{id}")]
[Produces("application/json", "text/csv", "application/pdf")]
public async Task<IActionResult> GetReportByFormat(int id, string format)
{
    var data = await _service.GetDataAsync(id);
    
    return format.ToLower() switch
    {
        "csv" => File(GenerateCsv(data), "text/csv", "report.csv"),
        "pdf" => File(GeneratePdf(data), "application/pdf", "report.pdf"),
        _ => Ok(data)  // JSON default
    };
}

private byte[] GenerateCsv(ReportData data)
{
    // CSV generation logic
    return Encoding.UTF8.GetBytes("id,name,value\n1,test,100");
}

private byte[] GeneratePdf(ReportData data)
{
    // PDF generation logic
    return new byte[0];
}
```

## Streaming & Large Responses

### Streaming to Client

For large files or data, stream instead of buffering:

```csharp
[ApiController]
[Route("api/[controller]")]
public class ExportsController : ControllerBase
{
    // Stream file to client
    [HttpGet("users/export")]
    public async Task ExportUsers()
    {
        Response.ContentType = "text/csv";
        Response.Headers.Add(
            "Content-Disposition",
            "attachment; filename=\"users.csv\"");
        
        await using var writer = new StreamWriter(Response.Body);
        
        // Write CSV header
        await writer.WriteLineAsync("Id,Name,Email");
        
        // Stream users in chunks
        var users = _service.GetUsersStream();  // IAsyncEnumerable
        
        await foreach (var user in users)
        {
            await writer.WriteLineAsync(
                $"{user.Id},{user.Name},{user.Email}");
        }
    }
    
    // Stream large file
    [HttpGet("download/{fileId}")]
    public async Task<IActionResult> DownloadFile(string fileId)
    {
        var filePath = $"/files/{fileId}";
        
        if (!System.IO.File.Exists(filePath))
            return NotFound();
        
        var fileName = Path.GetFileName(filePath);
        var fileStream = System.IO.File.OpenRead(filePath);
        
        return File(fileStream, "application/octet-stream", fileName);
    }
    
    // Streaming JSON array
    [HttpGet("users/stream")]
    public async Task StreamUsers()
    {
        Response.ContentType = "application/json";
        
        var users = _service.GetUsersStream();
        
        await Response.StartAsync();
        await Response.WriteAsync("[");
        
        var first = true;
        await foreach (var user in users)
        {
            if (!first)
                await Response.WriteAsync(",");
            
            var json = JsonSerializer.Serialize(user);
            await Response.WriteAsync(json);
            
            first = false;
        }
        
        await Response.WriteAsync("]");
    }
}
```

### Streaming from Client

```csharp
[HttpPost("bulk-import")]
public async Task<IActionResult> BulkImportUsers()
{
    var count = 0;
    
    using var reader = new StreamReader(Request.Body);
    using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
    
    csv.Read();
    csv.ReadHeader();
    
    while (csv.Read())
    {
        var user = csv.GetRecord<UserImportDto>();
        await _service.CreateUserAsync(user);
        count++;
    }
    
    return Ok(new { imported = count });
}
```

## File Uploads & Downloads

### File Upload Handling

```csharp
[HttpPost("upload")]
public async Task<IActionResult> Upload([FromForm] FileUploadDto model)
{
    if (model.File == null || model.File.Length == 0)
        return BadRequest("No file provided");
    
    // Validate file type
    var allowedExtensions = new[] { ".pdf", ".doc", ".docx" };
    var extension = Path.GetExtension(model.File.FileName);
    
    if (!allowedExtensions.Contains(extension))
        return BadRequest("File type not allowed");
    
    // Validate file size (max 10MB)
    const long maxSize = 10 * 1024 * 1024;
    if (model.File.Length > maxSize)
        return BadRequest("File too large");
    
    // Save file
    var fileName = Guid.NewGuid().ToString() + extension;
    var filePath = Path.Combine("/uploads", fileName);
    
    using (var stream = System.IO.File.Create(filePath))
    {
        await model.File.CopyToAsync(stream);
    }
    
    return Ok(new { fileName, size = model.File.Length });
}

public class FileUploadDto
{
    public string Title { get; set; }
    public IFormFile File { get; set; }
}
```

### File Download Handling

```csharp
[HttpGet("download/{fileId}")]
public async Task<IActionResult> DownloadFile(string fileId)
{
    var file = await _service.GetFileAsync(fileId);
    
    if (file == null)
        return NotFound();
    
    var bytes = await System.IO.File.ReadAllBytesAsync(file.Path);
    
    return File(
        bytes,
        "application/octet-stream",
        file.OriginalName);
}

// Stream large file
[HttpGet("stream/{fileId}")]
public IActionResult StreamFile(string fileId)
{
    var filePath = $"/files/{fileId}";
    
    if (!System.IO.File.Exists(filePath))
        return NotFound();
    
    var stream = System.IO.File.OpenRead(filePath);
    
    return File(stream, "application/octet-stream", 
        Path.GetFileName(filePath));
}
```

## Caching Responses

### Output Caching Middleware

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOutputCache(options =>
{
    // Cache all GET requests for 60 seconds
    options.AddBasePolicy(builder =>
        builder.Expire(TimeSpan.FromSeconds(60))
            .With(c => c.HttpMethod == HttpMethods.Get));
    
    // Custom policy for users endpoint
    options.AddPolicy("user-cache", builder =>
        builder
            .Expire(TimeSpan.FromMinutes(5))
            .With(c => c.HttpMethod == HttpMethods.Get && 
                c.Path.StartsWithSegments("/api/users"))
            .Tag("users"));
    
    // Never cache POST/PUT/DELETE
    options.AddPolicy("no-cache", builder =>
        builder.NoCache());
});

var app = builder.Build();

app.UseOutputCache();
app.MapControllers();

// Apply caching to endpoints
[HttpGet("{id}")]
[OutputCache(PolicyName = "user-cache")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    return Ok(await _service.GetUserAsync(id));
}

// Invalidate cache when data changes
[HttpPost]
public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto dto)
{
    var user = await _service.CreateUserAsync(dto);
    
    // Invalidate cached users list
    HttpContext.InvalidateOutputCache("users");
    
    return CreatedAtAction(nameof(GetUser), 
        new { id = user.Id }, user);
}
```

### Response Caching Headers

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    // Cache for 1 hour
    [HttpGet("{id}")]
    public async Task<ActionResult<ProductDto>> GetProduct(int id)
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return Ok(await _service.GetProductAsync(id));
    }
    
    // Cache for 24 hours with ETag
    [HttpGet]
    public async Task<ActionResult<List<ProductDto>>> GetProducts()
    {
        var products = await _service.GetProductsAsync();
        
        var etag = GenerateETag(products);
        Response.Headers.ETag = etag;
        
        var ifNoneMatch = Request.Headers["If-None-Match"].ToString();
        if (ifNoneMatch == etag)
            return StatusCode(StatusCodes.Status304NotModified);
        
        Response.Headers.CacheControl = "public, max-age=86400";
        
        return Ok(products);
    }
    
    // Don't cache sensitive data
    [Authorize]
    [HttpGet("profile")]
    public async Task<ActionResult<UserProfile>> GetProfile()
    {
        Response.Headers.CacheControl = 
            "no-cache, no-store, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        
        return Ok(await _service.GetProfileAsync());
    }
    
    private string GenerateETag(object data)
    {
        var json = JsonSerializer.Serialize(data);
        using var hash = System.Security.Cryptography.SHA256.Create();
        var hashBytes = hash.ComputeHash(Encoding.UTF8.GetBytes(json));
        return $"\"{Convert.ToBase64String(hashBytes)}\"";
    }
}
```

## Compression

### Automatic Response Compression

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResponseCompression(options =>
{
    // Enable Gzip and Brotli compression
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
    
    // Minimum response size to compress (1KB)
    options.MinimumCompressionThreshold = 1024;
});

var app = builder.Build();

// Must be before UseRouting
app.UseResponseCompression();
app.MapControllers();
```

## Error Handling with Responses

### Exception Middleware with Proper Responses

```csharp
public class GlobalExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;
    
    public GlobalExceptionHandlingMiddleware(RequestDelegate next,
        ILogger<GlobalExceptionHandlingMiddleware> logger)
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            
            context.Response.ContentType = "application/json";
            
            var response = ex switch
            {
                ValidationException validationEx =>
                    CreateValidationErrorResponse(validationEx, context),
                
                NotFoundException notFoundEx =>
                    CreateNotFoundResponse(notFoundEx, context),
                
                UnauthorizedAccessException unauthorizedEx =>
                    CreateUnauthorizedResponse(unauthorizedEx, context),
                
                _ => CreateInternalErrorResponse(ex, context)
            };
            
            context.Response.StatusCode = response.StatusCode;
            await context.Response.WriteAsJsonAsync(response.Body);
        }
    }
    
    private (int StatusCode, object Body) CreateValidationErrorResponse(
        ValidationException ex, HttpContext context)
    {
        return (400, new
        {
            status = 400,
            message = "Validation failed",
            errors = ex.Errors.GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => 
                    g.Select(e => e.ErrorMessage).ToArray())
        });
    }
    
    private (int StatusCode, object Body) CreateNotFoundResponse(
        NotFoundException ex, HttpContext context)
    {
        return (404, new
        {
            status = 404,
            message = "Resource not found",
            detail = ex.Message
        });
    }
    
    private (int StatusCode, object Body) CreateUnauthorizedResponse(
        UnauthorizedAccessException ex, HttpContext context)
    {
        return (401, new
        {
            status = 401,
            message = "Unauthorized",
            detail = ex.Message
        });
    }
    
    private (int StatusCode, object Body) CreateInternalErrorResponse(
        Exception ex, HttpContext context)
    {
        return (500, new
        {
            status = 500,
            message = "An error occurred",
            traceId = context.TraceIdentifier
        });
    }
}

// Register in Program.cs
app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
```

## Key Takeaways

1. **JSON is default serialization**: System.Text.Json with configurable options
2. **Content negotiation is automatic**: Accept header determines format
3. **ProblemDetails is standard**: RFC 7807 for error responses
4. **Streaming for large data**: Avoid buffering entire responses
5. **File uploads need validation**: Check type, size, and content
6. **Caching improves performance**: Use headers or output cache middleware
7. **Compression reduces bandwidth**: Enable Gzip/Brotli automatically
8. **Custom converters for complex types**: DateTime, enums, etc.
9. **Consistent error responses**: Custom or ProblemDetails format
10. **Response envelopes simplify clients**: Wrap all responses consistently

## Related Topics

- **HTTP Fundamentals** (Topic 4): Status codes, headers, content types
- **Model Binding & Validation** (Topic 7): Request deserialization
- **API Documentation** (Topic 10): Documenting response formats

