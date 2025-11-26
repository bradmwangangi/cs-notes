# Web APIs & REST

REST (Representational State Transfer) principles for building scalable APIs.

## REST Fundamentals

### HTTP Methods

| Method | Purpose | Body | Idempotent |
|--------|---------|------|-----------|
| GET | Retrieve resource | No | Yes |
| POST | Create resource | Yes | No |
| PUT | Replace entire resource | Yes | Yes |
| PATCH | Partial update | Yes | Yes |
| DELETE | Remove resource | No | Yes |

### Status Codes

| Code | Meaning | Example |
|------|---------|---------|
| 200 | OK | Successful GET |
| 201 | Created | POST successful |
| 204 | No Content | Successful DELETE |
| 400 | Bad Request | Invalid input |
| 401 | Unauthorized | Missing auth |
| 403 | Forbidden | No permission |
| 404 | Not Found | Resource missing |
| 409 | Conflict | Resource exists |
| 500 | Server Error | Unhandled exception |

## RESTful API Design

### Resource-Based Endpoints

```csharp
[ApiController]
[Route("api/v1/[controller]")]
public class UsersController : ControllerBase
{
    // Collection
    [HttpGet]
    public IActionResult GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        // GET /api/v1/users
        var users = GetUsers(page, pageSize);
        return Ok(new { data = users, page, pageSize });
    }

    // Single resource
    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        // GET /api/v1/users/5
        var user = FindUser(id);
        if (user == null) return NotFound();
        return Ok(user);
    }

    // Create
    [HttpPost]
    public IActionResult Create([FromBody] CreateUserDto dto)
    {
        // POST /api/v1/users
        var user = new User { Name = dto.Name, Email = dto.Email };
        SaveUser(user);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    // Full update
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] UpdateUserDto dto)
    {
        // PUT /api/v1/users/5
        var user = FindUser(id);
        if (user == null) return NotFound();

        user.Name = dto.Name;
        user.Email = dto.Email;
        SaveUser(user);
        return Ok(user);
    }

    // Partial update
    [HttpPatch("{id}")]
    public IActionResult PartialUpdate(int id, [FromBody] PartialUserDto dto)
    {
        // PATCH /api/v1/users/5
        var user = FindUser(id);
        if (user == null) return NotFound();

        if (!string.IsNullOrEmpty(dto.Name))
            user.Name = dto.Name;
        if (!string.IsNullOrEmpty(dto.Email))
            user.Email = dto.Email;

        SaveUser(user);
        return Ok(user);
    }

    // Delete
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        // DELETE /api/v1/users/5
        var user = FindUser(id);
        if (user == null) return NotFound();

        RemoveUser(user);
        return NoContent();  // 204
    }

    private User FindUser(int id) => null;  // Placeholder
    private void SaveUser(User user) { }
    private void RemoveUser(User user) { }
    private List<User> GetUsers(int page, int pageSize) => new();
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}

public record CreateUserDto(string Name, string Email);
public record UpdateUserDto(string Name, string Email);
public record PartialUserDto(string Name = null, string Email = null);
```

## Pagination

```csharp
public class PaginatedResponse<T>
{
    public List<T> Data { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages => (Total + PageSize - 1) / PageSize;
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

[HttpGet]
public IActionResult GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
{
    if (page < 1 || pageSize < 1)
        return BadRequest("Page and pageSize must be positive");

    var totalUsers = GetTotalUserCount();
    var users = FetchUsers((page - 1) * pageSize, pageSize);

    return Ok(new PaginatedResponse<User>
    {
        Data = users,
        Page = page,
        PageSize = pageSize,
        Total = totalUsers
    });
}
```

## Filtering & Sorting

```csharp
[HttpGet]
public IActionResult GetUsers(
    [FromQuery] string search,
    [FromQuery] string sortBy = "name",
    [FromQuery] string sortOrder = "asc",
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 10)
{
    var query = _dbContext.Users.AsQueryable();

    // Filter
    if (!string.IsNullOrEmpty(search))
        query = query.Where(u => u.Name.Contains(search) || u.Email.Contains(search));

    // Sort
    query = sortOrder.ToLower() == "desc"
        ? query.OrderByDescending(u => GetPropertyValue(u, sortBy))
        : query.OrderBy(u => GetPropertyValue(u, sortBy));

    // Pagination
    var total = query.Count();
    var users = query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToList();

    return Ok(new { data = users, total, page, pageSize });
}

private object GetPropertyValue(User user, string propertyName) =>
    propertyName.ToLower() switch
    {
        "email" => user.Email,
        "id" => user.Id,
        _ => user.Name
    };
```

## Nested Resources

```csharp
[ApiController]
[Route("api/v1/[controller]")]
public class PostsController : ControllerBase
{
    // GET /api/v1/users/5/posts
    [HttpGet("/api/v1/users/{userId}/posts")]
    public IActionResult GetUserPosts(int userId)
    {
        var user = FindUser(userId);
        if (user == null) return NotFound();

        var posts = GetPostsByUserId(userId);
        return Ok(posts);
    }

    // GET /api/v1/users/5/posts/3
    [HttpGet("/api/v1/users/{userId}/posts/{postId}")]
    public IActionResult GetUserPost(int userId, int postId)
    {
        var post = GetPost(postId);
        if (post == null) return NotFound();
        if (post.UserId != userId) return NotFound();

        return Ok(post);
    }

    // POST /api/v1/users/5/posts
    [HttpPost("/api/v1/users/{userId}/posts")]
    public IActionResult CreateUserPost(int userId, [FromBody] CreatePostDto dto)
    {
        var user = FindUser(userId);
        if (user == null) return NotFound();

        var post = new Post { UserId = userId, Title = dto.Title };
        SavePost(post);

        return CreatedAtAction(nameof(GetUserPost), 
            new { userId, postId = post.Id }, post);
    }

    private User FindUser(int id) => null;
    private Post GetPost(int id) => null;
    private List<Post> GetPostsByUserId(int userId) => new();
    private void SavePost(Post post) { }
}

public class Post
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; }
}

public record CreatePostDto(string Title);
```

## Request Validation

```csharp
using System.ComponentModel.DataAnnotations;

public record CreateUserDto
{
    [Required(ErrorMessage = "Name is required")]
    [MinLength(3, ErrorMessage = "Name must be at least 3 characters")]
    public string Name { get; set; }

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public string Email { get; set; }

    [Range(18, 100, ErrorMessage = "Age must be between 18 and 100")]
    public int Age { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpPost]
    public IActionResult Create([FromBody] CreateUserDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);  // Returns validation errors

        // Create user...
        return Ok();
    }
}
```

### Custom Validation

```csharp
public class AgeValidator : ValidationAttribute
{
    protected override ValidationResult IsValid(object value, ValidationContext context)
    {
        if (value is int age && (age < 0 || age > 150))
            return new ValidationResult("Age must be between 0 and 150");

        return ValidationResult.Success;
    }
}

public record CreateUserDto
{
    [Required]
    public string Name { get; set; }

    [AgeValidator]
    public int Age { get; set; }
}
```

## API Versioning

### Version in URL

```csharp
[ApiController]
[Route("api/v1/[controller]")]
public class UsersControllerV1 : ControllerBase
{
    [HttpGet("{id}")]
    public IActionResult GetUser(int id)
    {
        // Version 1 implementation
        return Ok();
    }
}

[ApiController]
[Route("api/v2/[controller]")]
public class UsersControllerV2 : ControllerBase
{
    [HttpGet("{id}")]
    public IActionResult GetUser(int id)
    {
        // Version 2 implementation (different response)
        return Ok();
    }
}
```

### Version in Header

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}"), MapToApiVersion("1.0")]
    public IActionResult GetUserV1(int id) => Ok();

    [HttpGet("{id}"), MapToApiVersion("2.0")]
    public IActionResult GetUserV2(int id) => Ok();
}

// Client: Header: Api-Version: 2.0
```

## Response Standardization

```csharp
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T Data { get; set; }
    public string Message { get; set; }
    public List<string> Errors { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    public IActionResult GetUser(int id)
    {
        try
        {
            var user = FindUser(id);
            if (user == null)
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = "User not found"
                });

            return Ok(new ApiResponse<User>
            {
                Success = true,
                Data = user,
                Message = "User retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "An error occurred",
                Errors = new List<string> { ex.Message }
            });
        }
    }

    private User FindUser(int id) => null;
}
```

## Content Negotiation

Serve different formats based on request:

```csharp
[ApiController]
[Route("api/[controller]")]
public class DataController : ControllerBase
{
    [HttpGet("{id}")]
    [Produces("application/json", "application/xml")]
    public IActionResult GetData(int id)
    {
        var data = new { Id = id, Value = "Test" };
        return Ok(data);
    }
}

// Client request:
// Accept: application/xml  -> returns XML
// Accept: application/json -> returns JSON
```

## Rate Limiting

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", policy =>
    {
        policy.PermitLimit = 100;
        policy.Window = TimeSpan.FromMinutes(1);
    });

    options.AddSlidingWindowLimiter("sliding", policy =>
    {
        policy.PermitLimit = 50;
        policy.Window = TimeSpan.FromMinutes(1);
        policy.SegmentsPerWindow = 2;
    });
});

var app = builder.Build();
app.UseRateLimiter();

[ApiController]
[Route("api/[controller]")]
public class DataController : ControllerBase
{
    [HttpGet("{id}")]
    [RequireRateLimiting("fixed")]
    public IActionResult GetData(int id) => Ok();
}
```

## Practice Exercises

1. **Blog API**: Build CRUD endpoints for posts, comments, with pagination
2. **Product API**: Implement filtering, sorting, search on products
3. **Validation**: Add comprehensive input validation with custom rules
4. **Versioning**: Create v1 and v2 endpoints with different response formats
5. **Rate Limiting**: Implement rate limiting on sensitive endpoints

## Key Takeaways

- **REST** uses HTTP methods and status codes meaningfully
- **Resource-based** URLs (not action-based)
- **Pagination** for large collections
- **Filtering & sorting** via query parameters
- **Validation** prevents invalid data
- **Nested resources** represent relationships naturally
- **Versioning** allows API evolution
- **Standard responses** improve client integration
- **Rate limiting** protects from abuse
