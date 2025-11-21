# Performance & Optimization

Build fast, efficient applications.

## Benchmarking

Measure performance scientifically:

```bash
dotnet add package BenchmarkDotNet
```

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Linq;

[MemoryDiagnoser]
public class StringConcatenationBenchmarks
{
    private const int N = 1000;

    [Benchmark]
    public string Concat()
    {
        string result = "";
        for (int i = 0; i < N; i++)
        {
            result += i.ToString();  // String concatenation
        }
        return result;
    }

    [Benchmark]
    public string StringBuilder()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < N; i++)
        {
            sb.Append(i.ToString());
        }
        return sb.ToString();
    }

    [Benchmark]
    public string StringInterpolation()
    {
        var results = new List<string>();
        for (int i = 0; i < N; i++)
        {
            results.Add($"{i}");
        }
        return string.Concat(results);
    }

    [Benchmark]
    public string Join()
    {
        var items = Enumerable.Range(0, N).Select(i => i.ToString());
        return string.Join("", items);
    }
}

// Run
var summary = BenchmarkRunner.Run<StringConcatenationBenchmarks>();
```

## Common Performance Issues

### String Concatenation

```csharp
// ✗ Slow: creates new string each iteration
string result = "";
for (int i = 0; i < 10000; i++)
{
    result += i.ToString();  // O(n²) complexity
}

// ✓ Fast: reuses buffer
var sb = new StringBuilder();
for (int i = 0; i < 10000; i++)
{
    sb.Append(i.ToString());
}
string result = sb.ToString();
```

### LINQ Performance

```csharp
// ✗ Creates intermediate collections
var result = numbers
    .Where(x => x > 5)
    .ToList()    // Creates list
    .Where(x => x < 10)
    .ToList()    // Creates another list
    .Select(x => x * 2);

// ✓ Lazy evaluation, single pass
var result = numbers
    .Where(x => x > 5)
    .Where(x => x < 10)
    .Select(x => x * 2);

// ✗ N+1 query problem
var users = _context.Users.ToList();
foreach (var user in users)
{
    var posts = _context.Posts.Where(p => p.UserId == user.Id).ToList();
}

// ✓ Single query with include
var users = _context.Users
    .Include(u => u.Posts)
    .ToList();
```

### Collection Initialization

```csharp
// ✗ Allocates default capacity, grows as needed
var list = new List<int>();
for (int i = 0; i < 100000; i++)
{
    list.Add(i);  // Multiple resizes
}

// ✓ Pre-allocate capacity
var list = new List<int>(100000);
for (int i = 0; i < 100000; i++)
{
    list.Add(i);  // No resizes
}

// Benchmark difference
// Without capacity: ~1000 reallocations
// With capacity: ~1 allocation
```

### Boxing/Unboxing

```csharp
// ✗ Boxing: value type wrapped in object
int num = 5;
object obj = num;  // Boxing: allocates heap memory
int value = (int)obj;  // Unboxing

// ✓ Use generics to avoid boxing
List<int> numbers = new();
numbers.Add(5);  // No boxing

// ✗ LINQ boxing
var sum = numbers.Sum();  // May box

// ✓ Generic where possible
T GetFirst<T>(List<T> items) => items[0];
```

## Profiling

Find bottlenecks:

```csharp
using System.Diagnostics;

public class PerformanceProfiler
{
    public static void MeasureExecution(string label, Action action, int iterations = 1)
    {
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            action();
        }

        sw.Stop();
        Console.WriteLine($"{label}: {sw.ElapsedMilliseconds}ms ({iterations} iterations)");
    }

    public static void MeasureMemory(string label, Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var memBefore = GC.GetTotalMemory(false);

        action();

        GC.Collect();
        var memAfter = GC.GetTotalMemory(false);
        var diff = memAfter - memBefore;

        Console.WriteLine($"{label}: {diff / 1024} KB allocated");
    }
}

// Usage
PerformanceProfiler.MeasureExecution("String concat", () =>
{
    string result = "";
    for (int i = 0; i < 1000; i++)
    {
        result += i;
    }
}, 100);

PerformanceProfiler.MeasureMemory("StringBuilder", () =>
{
    var sb = new StringBuilder();
    for (int i = 0; i < 1000; i++)
    {
        sb.Append(i);
    }
    sb.ToString();
});
```

## Async Performance

```csharp
// ✗ Sequential: waits for each operation
public async Task<List<User>> GetUsersSequentialAsync(int[] ids)
{
    var users = new List<User>();
    foreach (var id in ids)
    {
        users.Add(await _apiClient.GetUserAsync(id));
    }
    return users;
}
// Total time: sum of all requests (~5 seconds for 5 requests)

// ✓ Parallel: concurrent operations
public async Task<List<User>> GetUsersParallelAsync(int[] ids)
{
    var tasks = ids.Select(id => _apiClient.GetUserAsync(id));
    var users = await Task.WhenAll(tasks);
    return users.ToList();
}
// Total time: ~1 second (parallel)
```

## Database Optimization

### Query Optimization

```csharp
// ✗ N+1 problem: 1 + n queries
var users = _context.Users.ToList();
foreach (var user in users)
{
    var posts = _context.Posts.Where(p => p.UserId == user.Id).Count();
    Console.WriteLine($"{user.Name}: {posts} posts");
}

// ✓ Single query
var users = _context.Users
    .Include(u => u.Posts)
    .Select(u => new
    {
        u.Name,
        PostCount = u.Posts.Count
    })
    .ToList();
```

### Pagination

```csharp
// ✗ Load all then skip/take
var allUsers = _context.Users.ToList();
var page = allUsers.Skip(1000).Take(50);  // Loaded all 100k users into memory

// ✓ Skip/take at database
var page = _context.Users
    .Skip(1000)
    .Take(50)
    .ToList();  // Only 50 users transferred
```

### Tracking vs No Tracking

```csharp
// ✗ Track entities you don't need to modify
var users = _context.Users.ToList();  // Tracks changes
var list = users.Where(u => u.Active).ToList();

// ✓ AsNoTracking for read-only queries
var users = _context.Users
    .AsNoTracking()  // Doesn't track
    .Where(u => u.Active)
    .ToList();  // Less memory, faster
```

### Bulk Operations

```csharp
// ✗ Individual inserts
foreach (var user in users)
{
    _context.Users.Add(user);
    _context.SaveChanges();  // 1000 database round trips
}

// ✓ Batch inserts
_context.Users.AddRange(users);
_context.SaveChanges();  // Single round trip

// ✓ Bulk operations (EF Core 7+)
_context.Users
    .Where(u => u.Status == "Inactive")
    .ExecuteDelete();  // Single SQL DELETE statement
```

## Caching

```csharp
public class UserService
{
    private readonly IMemoryCache _cache;
    private readonly UserRepository _repository;

    public UserService(IMemoryCache cache, UserRepository repository)
    {
        _cache = cache;
        _repository = repository;
    }

    public async Task<User> GetUserAsync(int id)
    {
        // Check cache first
        if (_cache.TryGetValue($"user_{id}", out User cachedUser))
        {
            return cachedUser;
        }

        // Load from database
        var user = await _repository.GetUserAsync(id);

        // Cache for 5 minutes
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            SlidingExpiration = TimeSpan.FromMinutes(1)
        };

        _cache.Set($"user_{id}", user, cacheOptions);
        return user;
    }

    public async Task UpdateUserAsync(User user)
    {
        await _repository.UpdateUserAsync(user);
        _cache.Remove($"user_{user.Id}");  // Invalidate cache
    }
}

// Register in DI
builder.Services.AddMemoryCache();
```

## HTTP Compression

```csharp
builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<GzipCompressionProvider>();
    options.Providers.Add<BrotliCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/json" });
});

var app = builder.Build();
app.UseResponseCompression();
```

## Connection Pooling

```csharp
// EF Core automatically pools connections
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.CommandTimeout(30);
        sqlOptions.EnableRetryOnFailure();
    })
);

// HttpClient pooling
builder.Services.AddHttpClient<ApiClient>()
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });
```

## Minimal Allocations

```csharp
// ✗ Creates array each call
int Sum(params int[] numbers) => numbers.Sum();

// ✓ Use Span for low-allocation
int SumSpan(ReadOnlySpan<int> numbers)
{
    int sum = 0;
    foreach (var num in numbers)
        sum += num;
    return sum;
}

// Usage
Span<int> nums = stackalloc int[] { 1, 2, 3 };
int result = SumSpan(nums);  // No heap allocation
```

## Lazy Initialization

```csharp
public class ExpensiveService
{
    private Lazy<ExpensiveResource> _resource;

    public ExpensiveService()
    {
        // Doesn't load until accessed
        _resource = new Lazy<ExpensiveResource>(
            () => new ExpensiveResource());
    }

    public void UseResource()
    {
        var resource = _resource.Value;  // Loads on first access
    }
}
```

## Common Bottlenecks

| Area | Problem | Solution |
|------|---------|----------|
| **Strings** | Concatenation in loops | Use StringBuilder |
| **Collections** | No pre-allocated capacity | Allocate size upfront |
| **LINQ** | N+1 queries | Use Include, AsNoTracking |
| **Database** | Individual saves | Batch operations |
| **HTTP** | Sequential calls | Parallel with Task.WhenAll |
| **Memory** | Unnecessary allocations | Use Span, stackalloc |
| **Caching** | No caching strategy | Implement IMemoryCache |

## Profiling Tools

```bash
# dotTrace (JetBrains)
# Profiler (JetBrains Rider integrated)
# Visual Studio Profiler
# PerfView (Windows, advanced)
# dotnet-trace

# Example with dotnet-trace
dotnet tool install -g dotnet-trace
dotnet-trace collect --output trace.nettrace -- dotnet run
```

## Practice Exercises

1. **Benchmark Comparison**: Benchmark string concatenation vs StringBuilder
2. **Query Optimization**: Fix N+1 problems in EF Core queries
3. **Caching**: Implement caching strategy for frequently accessed data
4. **Parallel Operations**: Convert sequential API calls to parallel
5. **Memory Profiling**: Profile and reduce memory allocations

## Key Takeaways

- **Measure** with BenchmarkDotNet before optimizing
- **String concatenation** in loops: use StringBuilder
- **LINQ**: Check for N+1 queries, use Include
- **Database**: Batch operations, use AsNoTracking
- **Async**: Run operations in parallel with Task.WhenAll
- **Caching**: Reduce database/API calls
- **Memory**: Pre-allocate collections, use Span for low-alloc
- **80/20 rule**: 20% of code causes 80% of slowness
- **Premature optimization** is the root of all evil
