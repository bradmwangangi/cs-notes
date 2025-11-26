# Caching & State Management

Optimize performance by reducing expensive operations.

## In-Memory Caching

### IMemoryCache

Built-in caching for single-server applications:

```csharp
using Microsoft.Extensions.Caching.Memory;

public class UserService
{
    private readonly IMemoryCache _cache;
    private readonly IUserRepository _repository;
    private const string USER_CACHE_KEY_PREFIX = "user_";
    private const string USERS_CACHE_KEY = "all_users";

    public UserService(IMemoryCache cache, IUserRepository repository)
    {
        _cache = cache;
        _repository = repository;
    }

    // Get with cache
    public async Task<User> GetUserAsync(int id)
    {
        var cacheKey = $"{USER_CACHE_KEY_PREFIX}{id}";

        if (_cache.TryGetValue(cacheKey, out User cachedUser))
        {
            return cachedUser;
        }

        var user = await _repository.GetUserAsync(id);
        if (user != null)
        {
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                SlidingExpiration = TimeSpan.FromMinutes(2)
            };

            _cache.Set(cacheKey, user, cacheOptions);
        }

        return user;
    }

    // Update and invalidate cache
    public async Task UpdateUserAsync(User user)
    {
        await _repository.UpdateUserAsync(user);
        var cacheKey = $"{USER_CACHE_KEY_PREFIX}{user.Id}";
        _cache.Remove(cacheKey);  // Invalidate
    }

    // Cache with refresh
    public async Task<List<User>> GetAllUsersAsync()
    {
        if (_cache.TryGetValue(USERS_CACHE_KEY, out List<User> cachedUsers))
        {
            return cachedUsers;
        }

        var users = await _repository.GetAllUsersAsync();

        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
        };

        _cache.Set(USERS_CACHE_KEY, users, cacheOptions);
        return users;
    }

    // Post-action cache invalidation
    public async Task DeleteUserAsync(int id)
    {
        await _repository.DeleteUserAsync(id);
        _cache.Remove($"{USER_CACHE_KEY_PREFIX}{id}");
        _cache.Remove(USERS_CACHE_KEY);  // Invalidate list
    }
}

// Register in DI
builder.Services.AddMemoryCache();
```

### Cache Options

```csharp
var options = new MemoryCacheEntryOptions();

// Absolute expiration: expires after specific time
options.AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(10);
options.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);

// Sliding expiration: resets on access
options.SlidingExpiration = TimeSpan.FromMinutes(5);
// If accessed within 5 minutes, expiration resets

// Priority (determines what to remove when memory is low)
options.Priority = CacheItemPriority.High;  // Keep longer
options.Priority = CacheItemPriority.Low;   // Remove first

// Callback on removal
options.RegisterPostEvictionCallback((key, value, reason, state) =>
{
    Console.WriteLine($"Cache item {key} removed due to {reason}");
});

_cache.Set("key", value, options);
```

## Distributed Caching

For multi-server setups:

### Redis

```bash
dotnet add package StackExchange.Redis
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis
```

```csharp
// In Program.cs
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    // or
    options.Configuration = "localhost:6379";
});

// Usage
public class UserService
{
    private readonly IDistributedCache _cache;

    public UserService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<User> GetUserAsync(int id)
    {
        var cacheKey = $"user_{id}";
        
        // Try to get from cache
        var cachedJson = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cachedJson))
        {
            return JsonSerializer.Deserialize<User>(cachedJson);
        }

        // Get from database
        var user = await _repository.GetUserAsync(id);

        if (user != null)
        {
            // Cache as JSON
            var json = JsonSerializer.Serialize(user);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            };
            await _cache.SetStringAsync(cacheKey, json, options);
        }

        return user;
    }

    public async Task UpdateUserAsync(User user)
    {
        await _repository.UpdateUserAsync(user);
        await _cache.RemoveAsync($"user_{user.Id}");
    }
}
```

### Docker Redis

```yaml
# docker-compose.yml
services:
  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data
    command: redis-server --appendonly yes

volumes:
  redis_data:
```

## Response Caching

Cache HTTP responses:

```csharp
// In Program.cs
builder.Services.AddResponseCaching();

var app = builder.Build();
app.UseResponseCaching();

// In controller
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    [HttpGet("{id}")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetProduct(int id)
    {
        var product = await _productService.GetProductAsync(id);
        return Ok(product);
    }

    // Cache varies by query parameter
    [HttpGet]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any, 
        VaryByQueryKeys = new[] { "category", "sort" })]
    public async Task<IActionResult> GetProducts(string category, string sort)
    {
        var products = await _productService.GetProductsAsync(category, sort);
        return Ok(products);
    }

    // Don't cache
    [HttpPost]
    [ResponseCache(NoStore = true)]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductDto dto)
    {
        var product = await _productService.CreateProductAsync(dto);
        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
    }

    // Cache-Control header
    [HttpGet("featured")]
    public IActionResult GetFeaturedProducts()
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        var products = _productService.GetFeaturedProducts();
        return Ok(products);
    }
}
```

## Session Management

Store user-specific state:

```csharp
// In Program.cs
builder.Services.AddDistributedMemoryCache();  // Use Redis in production
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.Name = ".MyApp.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();
app.UseSession();

// Use sessions
[ApiController]
[Route("api/[controller]")]
public class ShoppingCartController : ControllerBase
{
    [HttpPost("add")]
    public IActionResult AddToCart([FromBody] int productId)
    {
        var cart = HttpContext.Session.GetObject<List<int>>("cart") ?? new();
        cart.Add(productId);
        HttpContext.Session.SetObject("cart", cart);
        return Ok();
    }

    [HttpGet]
    public IActionResult GetCart()
    {
        var cart = HttpContext.Session.GetObject<List<int>>("cart") ?? new();
        return Ok(cart);
    }

    [HttpPost("clear")]
    public IActionResult ClearCart()
    {
        HttpContext.Session.Remove("cart");
        return Ok();
    }
}

// Extension methods
public static class SessionExtensions
{
    public static void SetObject<T>(this ISession session, string key, T value)
    {
        session.SetString(key, JsonSerializer.Serialize(value));
    }

    public static T GetObject<T>(this ISession session, string key)
    {
        var data = session.GetString(key);
        if (data == null) return default;
        return JsonSerializer.Deserialize<T>(data);
    }
}
```

## Cache-Aside Pattern

```csharp
public class CacheAsideRepository<T> where T : class
{
    private readonly IDistributedCache _cache;
    private readonly IRepository<T> _repository;

    public CacheAsideRepository(IDistributedCache cache, IRepository<T> repository)
    {
        _cache = cache;
        _repository = repository;
    }

    public async Task<T> GetAsync(string id)
    {
        var cacheKey = GetCacheKey(id);

        // Try cache first
        var cached = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            return JsonSerializer.Deserialize<T>(cached);
        }

        // Load from repository
        var item = await _repository.GetAsync(id);
        if (item != null)
        {
            await _cache.SetStringAsync(cacheKey, 
                JsonSerializer.Serialize(item),
                new DistributedCacheEntryOptions 
                { 
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
                });
        }

        return item;
    }

    public async Task UpdateAsync(T item, string id)
    {
        await _repository.UpdateAsync(item);
        await _cache.RemoveAsync(GetCacheKey(id));
    }

    private string GetCacheKey(string id) => $"{typeof(T).Name}_{id}";
}
```

## Write-Through Pattern

Write to cache and database together:

```csharp
public class WriteThoughRepository<T> where T : class
{
    private readonly IDistributedCache _cache;
    private readonly IRepository<T> _repository;

    public async Task CreateAsync(T item, string id)
    {
        // Write to database first
        await _repository.CreateAsync(item);

        // Then cache
        await _cache.SetStringAsync(GetCacheKey(id),
            JsonSerializer.Serialize(item),
            new DistributedCacheEntryOptions 
            { 
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });
    }

    private string GetCacheKey(string id) => $"{typeof(T).Name}_{id}";
}
```

## Cache Invalidation Strategies

### Event-Based

```csharp
public interface ICacheInvalidationService
{
    Task InvalidateUserCacheAsync(int userId);
    Task InvalidateProductCacheAsync(int productId);
}

public class CacheInvalidationService : ICacheInvalidationService
{
    private readonly IDistributedCache _cache;

    public async Task InvalidateUserCacheAsync(int userId)
    {
        var keys = new[]
        {
            $"user_{userId}",
            $"user_profile_{userId}",
            $"user_posts_{userId}"
        };

        foreach (var key in keys)
        {
            await _cache.RemoveAsync(key);
        }
    }

    public async Task InvalidateProductCacheAsync(int productId)
    {
        var keys = new[]
        {
            $"product_{productId}",
            "all_products",  // Invalidate list
            "featured_products"
        };

        foreach (var key in keys)
        {
            await _cache.RemoveAsync(key);
        }
    }
}

// Use in service
public class ProductService
{
    private readonly ICacheInvalidationService _cacheInvalidation;

    public async Task UpdateProductAsync(Product product)
    {
        await _repository.UpdateAsync(product);
        await _cacheInvalidation.InvalidateProductCacheAsync(product.Id);
    }
}
```

## Cache Stampede Prevention

When cache expires, multiple requests hit database:

```csharp
public class CacheStampedeResolver
{
    private readonly IDistributedCache _cache;
    private readonly IRepository _repository;
    private readonly ILogger<CacheStampedeResolver> _logger;

    public async Task<T> GetWithLockAsync<T>(string key, 
        Func<Task<T>> getValue, 
        TimeSpan cacheDuration)
    {
        // Check cache
        var cached = await _cache.GetStringAsync(key);
        if (!string.IsNullOrEmpty(cached))
        {
            return JsonSerializer.Deserialize<T>(cached);
        }

        // Use lock to prevent stampede
        var lockKey = $"{key}_lock";
        var lockAcquired = await _cache.GetStringAsync(lockKey);

        if (string.IsNullOrEmpty(lockAcquired))
        {
            // Acquire lock
            await _cache.SetStringAsync(lockKey, "locked", 
                new DistributedCacheEntryOptions 
                { 
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5)
                });

            try
            {
                // Load value
                var value = await getValue();

                // Cache result
                await _cache.SetStringAsync(key, 
                    JsonSerializer.Serialize(value), 
                    new DistributedCacheEntryOptions 
                    { 
                        AbsoluteExpirationRelativeToNow = cacheDuration
                    });

                return value;
            }
            finally
            {
                // Release lock
                await _cache.RemoveAsync(lockKey);
            }
        }

        // Wait for lock to be released, then retry
        await Task.Delay(100);
        return await GetWithLockAsync(key, getValue, cacheDuration);
    }
}
```

## State Management Best Practices

```csharp
// ✓ Cache frequently accessed, expensive data
var user = await _cache.GetOrSetAsync("user_1", 
    () => _repository.GetUserAsync(1),
    TimeSpan.FromMinutes(10));

// ✗ Don't cache large objects
var allUsers = await _repository.GetAllUsersAsync();
_cache.Set("all_users", allUsers);  // Memory waste

// ✓ Set appropriate expiration
// Short: 1-5 minutes (user profiles, preferences)
// Medium: 10-30 minutes (product lists, recommendations)
// Long: 1+ hours (static data, settings)

// ✓ Use cache keys strategically
$"user_{userId}"
$"product_{productId}:reviews"
$"category_{categoryId}:products"

// ✗ Vague keys
"user"
"cache"

// ✓ Monitor cache hit rates
_logger.LogInformation("Cache hit ratio: {HitRatio}%", hitRatio);

// ✓ Handle cache misses gracefully
var value = await cache.GetAsync(key) ?? await GetFromDatabaseAsync();
```

## Practice Exercises

1. **Memory Cache**: Add caching to a service, measure improvement
2. **Redis**: Set up Redis and use IDistributedCache
3. **Cache Invalidation**: Implement event-based cache invalidation
4. **Session Storage**: Build shopping cart using sessions
5. **Cache Strategy**: Analyze and choose appropriate expiration times

## Key Takeaways

- **In-memory cache** (IMemoryCache) for single-server
- **Redis** (IDistributedCache) for multi-server scenarios
- **Response caching** for static/semi-static HTTP responses
- **Sessions** for user-specific state
- **Cache-aside**: Check cache before loading from database
- **Write-through**: Write to database then cache
- **Cache invalidation** is hardest problem in CS
- **Expiration**: Set appropriate TTL to balance performance/freshness
- **Cache stampede**: Use locks when cache expires under load
- **Monitor**: Track cache hit rates to measure effectiveness
