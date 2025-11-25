# Chapter 9: Async/Await & Performance Optimization

## 9.1 Async/Await Fundamentals for APIs

Async operations allow the thread pool to handle other requests while waiting for I/O (database, HTTP, file system).

### Why Async Matters in APIs

**Synchronous blocking:**
```csharp
// Each request blocks a thread until operation completes
var user = _userService.GetUser(id);  // Blocks thread
// While blocked, thread can't handle other requests
// Under load, thread pool exhausted → request rejection
```

**Asynchronous non-blocking:**
```csharp
// Thread released while waiting for I/O
var user = await _userService.GetUserAsync(id);
// Other requests use the released thread
// Much higher throughput with same hardware
```

**Impact under load:**

With 200 thread pool threads and 1000 concurrent requests:

- **Sync:** 200 threads active, 800 queued, many timeouts
- **Async:** All 1000 requests can run concurrently (awaiting I/O doesn't hold threads)

Async can handle 5-10x more concurrent requests.

### Async All The Way

The async model requires full async chains. If any layer blocks, you lose benefits:

```csharp
// Good: async all the way
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _userService.GetUserAsync(id);  // Async
    return Ok(user);
}

public class UserService
{
    public async Task<User> GetUserAsync(int id)
    {
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);  // Async
    }
}

// Bad: blocking sync call in async context
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _userService.GetUserAsync(id);
    var userDto = _mapper.Map<UserDto>(user);
    
    // Sync call blocks! Defeating async benefits
    var enrichedData = _externalService.GetData(id);
    
    return Ok(userDto);
}

// Better: use async
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _userService.GetUserAsync(id);
    var userDto = _mapper.Map<UserDto>(user);
    
    // Async call, no blocking
    var enrichedData = await _externalService.GetDataAsync(id);
    
    return Ok(userDto);
}
```

### Async Return Types

```csharp
// For controllers
public async Task<ActionResult<T>>       // Recommended
public async Task<IActionResult>        // When type varies

// For minimal APIs
async Task<IResult>                      // Async handler
Task<IResult>                            // Async handler
IResult                                  // Sync handler (only if truly sync)

// For services
public async Task<T>                     // Return value needed
public async Task                        // No return value
```

**Never use void:**
```csharp
// NEVER do this (except event handlers)
public async void GetUserAsync(int id)  // Fire and forget, can't track errors
{
    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
}

// Do this instead
public async Task GetUserAsync(int id)
{
    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
}
```

---

## 9.2 Parallel Operations

Execute multiple async operations concurrently:

### Task.WhenAll

```csharp
[HttpGet("{id}")]
public async Task<ActionResult<UserDetailDto>> GetUserDetail(int id)
{
    // Start all operations simultaneously
    var userTask = _userService.GetUserAsync(id);
    var ordersTask = _orderService.GetUserOrdersAsync(id);
    var preferencesTask = _preferenceService.GetUserPreferencesAsync(id);
    
    // Wait for all to complete
    await Task.WhenAll(userTask, ordersTask, preferencesTask);
    
    // Combine results
    return Ok(new UserDetailDto
    {
        User = await userTask,
        Orders = await ordersTask,
        Preferences = await preferencesTask
    });
}
```

**Response time comparison:**
- Sequential: 100ms (user) + 150ms (orders) + 50ms (prefs) = 300ms
- Parallel: max(100ms, 150ms, 50ms) = 150ms (50% faster)

### Task.WhenAny

Complete when any task finishes (useful for timeouts):

```csharp
public async Task<ActionResult<UserDto>> GetUserWithTimeout(int id)
{
    var userTask = _userService.GetUserAsync(id);
    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
    
    var completedTask = await Task.WhenAny(userTask, timeoutTask);
    
    if (completedTask == timeoutTask)
        return StatusCode(504, "Request timed out");  // Gateway Timeout
    
    return Ok(await userTask);
}
```

### Parallel Collection Processing

```csharp
public async Task EnrichUsersAsync(List<User> users)
{
    // Process multiple items in parallel
    var tasks = users.Select(async user =>
    {
        user.EnrichedData = await _enrichmentService.GetDataAsync(user.Id);
        return user;
    });
    
    await Task.WhenAll(tasks);
}

// Alternative with Parallel.ForEachAsync (more efficient)
await Parallel.ForEachAsync(users, new ParallelOptions { MaxDegreeOfParallelism = 5 },
    async (user, ct) =>
    {
        user.EnrichedData = await _enrichmentService.GetDataAsync(user.Id);
    });
```

---

## 9.3 Caching Strategies

Caching reduces database/external service load dramatically.

### In-Memory Caching

```csharp
// Register in Program.cs
builder.Services.AddMemoryCache();

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly IMemoryCache _cache;
    private readonly ICategoryService _service;
    
    public CategoriesController(IMemoryCache cache, ICategoryService service)
    {
        _cache = cache;
        _service = service;
    }
    
    [HttpGet]
    public async Task<ActionResult<List<CategoryDto>>> GetCategories()
    {
        const string cacheKey = "categories";
        
        // Check cache first
        if (_cache.TryGetValue(cacheKey, out List<CategoryDto> categories))
            return Ok(categories);
        
        // Not in cache, fetch from database
        categories = await _service.GetCategoriesAsync();
        
        // Store in cache for 1 hour
        _cache.Set(cacheKey, categories, TimeSpan.FromHours(1));
        
        return Ok(categories);
    }
    
    [HttpPost]
    public async Task<ActionResult<CategoryDto>> CreateCategory(CreateCategoryRequest request)
    {
        var category = await _service.CreateCategoryAsync(request);
        
        // Invalidate cache on write
        _cache.Remove("categories");
        
        return CreatedAtAction(nameof(GetCategories), category);
    }
}
```

**Cache considerations:**
- TTL (Time-To-Live): How long before refresh
- Cache invalidation: Remove when data changes
- Cache size: Monitor memory usage
- Stampede prevention: Ensure only one refresh when cache expires

### Distributed Caching

For multi-instance deployments, use Redis:

```csharp
// Register in Program.cs
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

public class CachedUserService
{
    private readonly IDistributedCache _cache;
    private readonly IUserRepository _repository;
    
    public CachedUserService(IDistributedCache cache, IUserRepository repository)
    {
        _cache = cache;
        _repository = repository;
    }
    
    public async Task<User> GetUserAsync(int id)
    {
        var cacheKey = $"user:{id}";
        
        // Try cache first
        var cachedData = await _cache.GetStringAsync(cacheKey);
        if (cachedData != null)
            return JsonSerializer.Deserialize<User>(cachedData);
        
        // Fetch from database
        var user = await _repository.GetByIdAsync(id);
        
        // Store in cache for 1 hour
        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(user),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            }
        );
        
        return user;
    }
    
    public async Task InvalidateUserCacheAsync(int id)
    {
        await _cache.RemoveAsync($"user:{id}");
    }
}
```

### Response Caching

Browser/CDN caching via HTTP headers:

```csharp
[HttpGet("{id}")]
[ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _service.GetUserAsync(id);
    return Ok(user);
}

// Multiple endpoints in controller
[ApiController]
[ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
public class PublicDataController : ControllerBase
{
    [HttpGet("statistics")]
    public IActionResult GetStatistics() { }
    
    [HttpGet("settings")]
    [ResponseCache(Duration = 3600)]  // Override: longer TTL
    public IActionResult GetSettings() { }
}

// Disable caching
[HttpPost]
[ResponseCache(NoStore = true)]
public async Task<IActionResult> CreateUser(CreateUserRequest request) { }
```

**HTTP caching headers:**
- `Cache-Control: max-age=3600` - Cache for 1 hour
- `Cache-Control: public` - Share across users
- `Cache-Control: private` - Per-user only
- `Cache-Control: no-store` - Don't cache
- `ETag: "abc123"` - Entity tag for validation
- `Last-Modified: ...` - Last change time

---

## 9.4 Database Query Optimization

### Query Performance

**N+1 Problem (already covered in Chapter 4):**
```csharp
// Bad: multiple queries
var users = await context.Users.ToListAsync();
foreach (var user in users)
{
    user.Orders = await context.Orders.Where(o => o.UserId == user.Id).ToListAsync();
}

// Good: single query with JOIN
var users = await context.Users
    .Include(u => u.Orders)
    .ToListAsync();
```

### Batch Operations

```csharp
// Bad: one query per user
foreach (var userId in userIds)
{
    var user = await context.Users.FindAsync(userId);
    user.Status = "Active";
}

// Good: batch update
await context.Users
    .Where(u => userIds.Contains(u.Id))
    .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "Active"));
```

### Pagination with Large Datasets

```csharp
// Bad: Skip/Take on large result sets
var users = await context.Users
    .Skip((pageNumber - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
// Slow when pageNumber is large

// Better: Use keyset pagination (seek method)
public async Task<List<User>> GetUsersAsync(int lastId, int pageSize)
{
    return await context.Users
        .Where(u => u.Id > lastId)  // Seek to position
        .OrderBy(u => u.Id)
        .Take(pageSize)
        .ToListAsync();
    // Much faster for large pages
}

// URL: /api/users?lastId=1000&pageSize=20
```

### Query Analysis

Use logging to identify slow queries:

```csharp
// Enable EF Core logging
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString)
        .LogTo(Console.WriteLine, LogLevel.Debug)  // Log all queries
        .EnableSensitiveDataLogging();  // Log parameter values
});

// In appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Debug"
    }
  }
}
```

---

## 9.5 Connection Pooling

Database connections are expensive. Connection pooling reuses them:

```csharp
// Configure connection pool
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions
            .CommandTimeout(30)
            .MaxPoolSize(100)  // Max connections in pool
            .MinPoolSize(10)   // Min connections in pool
    );
});
```

**Connection pool sizing:**
- Default: 100 connections
- Minimum: 10 (keep warm)
- Maximum: based on load
- Monitor: `Max Pool Size exceeded` errors indicate undersized pool

---

## 9.6 Output Compression

Compress responses to reduce bandwidth:

```csharp
// Register in Program.cs
builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<GzipCompressionProvider>();
    options.Providers.Add<BrotliCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes
        .Concat(new[] { "application/json", "text/plain" });
});

app.UseResponseCompression();
```

**Compression impact:**
- JSON: typically 70-80% reduction
- Small responses: overhead of compression not worth it
- Large responses: significant savings
- CPU cost: minimal with modern algorithms

---

## 9.7 Monitoring Performance

### Application Insights

```csharp
builder.Services.AddApplicationInsightsTelemetry();

[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(
    int id,
    TelemetryClient telemetry)
{
    using (var operation = telemetry.StartOperation<RequestTelemetry>("GetUser"))
    {
        var user = await _service.GetUserAsync(id);
        
        telemetry.TrackEvent("UserFetched", new Dictionary<string, string>
        {
            { "UserId", id.ToString() }
        });
        
        return Ok(user);
    }
}
```

### Custom Metrics

```csharp
public class PerformanceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PerformanceMiddleware> _logger;
    
    public PerformanceMiddleware(RequestDelegate next, ILogger<PerformanceMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        
        await _next(context);
        
        stopwatch.Stop();
        
        if (stopwatch.ElapsedMilliseconds > 1000)  // Slow requests
        {
            _logger.LogWarning(
                "Slow request: {Method} {Path} took {ElapsedMs}ms",
                context.Request.Method,
                context.Request.Path,
                stopwatch.ElapsedMilliseconds
            );
        }
    }
}
```

---

## 9.8 Benchmarking

Measure performance improvements:

```csharp
[MemoryDiagnoser]
public class QueryBenchmarks
{
    private readonly AppDbContext _context;
    
    [Benchmark]
    public async Task<int> QueryWithInclude()
    {
        return await _context.Users
            .Include(u => u.Orders)
            .CountAsync();
    }
    
    [Benchmark]
    public async Task<int> QueryWithSelect()
    {
        return await _context.Users
            .Select(u => new { u.Id, OrderCount = u.Orders.Count })
            .CountAsync();
    }
}
```

Use BenchmarkDotNet to compare approaches:
```bash
dotnet add package BenchmarkDotNet
```

---

## Summary

Async/await is essential for scalability—use it throughout the stack. Parallel operations with `Task.WhenAll` improve response times for independent operations. Caching dramatically reduces load (in-memory for single instance, distributed for multi-instance). Optimize database queries to avoid N+1 problems and use pagination for large datasets. Monitor performance to identify bottlenecks. The next chapter covers architectural patterns—Domain-Driven Design and Clean Architecture.
