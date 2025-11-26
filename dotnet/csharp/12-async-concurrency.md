# Async & Concurrency

Write scalable, responsive applications with asynchronous programming.

## Async Basics

### async/await Pattern

```csharp
// Synchronous (blocks)
public int FetchData()
{
    Thread.Sleep(2000);  // Block for 2 seconds
    return 42;
}

// Asynchronous (non-blocking)
public async Task<int> FetchDataAsync()
{
    await Task.Delay(2000);  // Don't block, yield control
    return 42;
}

// Consuming async method
var result = await FetchDataAsync();  // Wait for result, don't block thread

// Fire and forget (not recommended)
FetchDataAsync();  // Don't await
```

### Task vs Task<T>

```csharp
// Task: void-returning async operation
public async Task PrintMessageAsync(string message)
{
    await Task.Delay(100);
    Console.WriteLine(message);
}

// Task<T>: returns value of type T
public async Task<string> GetMessageAsync()
{
    await Task.Delay(100);
    return "Hello";
}

// Void async (only for event handlers, avoid otherwise)
public async void OnButtonClick()
{
    await FetchDataAsync();  // No way to know when it completes
}
```

## HTTP Calls (Most Common Use Case)

```csharp
using System.Net.Http;

public class ApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // GET request
    public async Task<User> GetUserAsync(int id)
    {
        var response = await _httpClient.GetAsync($"https://api.example.com/users/{id}");
        
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed with status {response.StatusCode}");

        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<User>(content);
    }

    // POST request
    public async Task<User> CreateUserAsync(CreateUserDto dto)
    {
        var json = JsonSerializer.Serialize(dto);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("https://api.example.com/users", content);
        
        if (!response.IsSuccessStatusCode)
            throw new Exception("Failed to create user");

        var result = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<User>(result);
    }

    // PUT request
    public async Task<User> UpdateUserAsync(int id, UpdateUserDto dto)
    {
        var json = JsonSerializer.Serialize(dto);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PutAsync(
            $"https://api.example.com/users/{id}", content);

        if (!response.IsSuccessStatusCode)
            throw new Exception("Failed to update user");

        var result = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<User>(result);
    }

    // DELETE request
    public async Task DeleteUserAsync(int id)
    {
        var response = await _httpClient.DeleteAsync(
            $"https://api.example.com/users/{id}");

        if (!response.IsSuccessStatusCode)
            throw new Exception("Failed to delete user");
    }
}

// Register HttpClient in DI
builder.Services.AddHttpClient<ApiClient>();

// Usage
public class UserService
{
    private readonly ApiClient _apiClient;

    public UserService(ApiClient apiClient) => _apiClient = apiClient;

    public async Task<User> GetUserAsync(int id) => await _apiClient.GetUserAsync(id);
}
```

## Parallel Operations

Execute multiple operations concurrently:

```csharp
// Sequential (slow)
public async Task<(User, List<Post>, List<Comment>)> GetUserDataSequentialAsync(int userId)
{
    var user = await GetUserAsync(userId);
    var posts = await GetPostsAsync(userId);
    var comments = await GetCommentsAsync(userId);
    return (user, posts, comments);
}
// Total time: 3 seconds

// Parallel (fast)
public async Task<(User, List<Post>, List<Comment>)> GetUserDataParallelAsync(int userId)
{
    var userTask = GetUserAsync(userId);
    var postsTask = GetPostsAsync(userId);
    var commentsTask = GetCommentsAsync(userId);

    await Task.WhenAll(userTask, postsTask, commentsTask);

    return (userTask.Result, postsTask.Result, commentsTask.Result);
}
// Total time: ~1 second

// Cleaner syntax
public async Task<(User, List<Post>, List<Comment>)> GetUserDataParallelAsync(int userId)
{
    var (user, posts, comments) = await (
        GetUserAsync(userId),
        GetPostsAsync(userId),
        GetCommentsAsync(userId)
    );
    return (user, posts, comments);
}

private async Task<User> GetUserAsync(int id) { await Task.Delay(1000); return new User(); }
private async Task<List<Post>> GetPostsAsync(int id) { await Task.Delay(1000); return new(); }
private async Task<List<Comment>> GetCommentsAsync(int id) { await Task.Delay(1000); return new(); }
```

## Error Handling in Async

```csharp
public async Task<User> GetUserWithErrorHandlingAsync(int id)
{
    try
    {
        var response = await _httpClient.GetAsync($"https://api.example.com/users/{id}");
        
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new UserNotFoundException($"User {id} not found");

            throw new ApiException($"API error: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<User>(content);
    }
    catch (HttpRequestException ex)
    {
        throw new Exception("Network error", ex);
    }
    catch (JsonException ex)
    {
        throw new Exception("Invalid response format", ex);
    }
}

// When awaiting multiple tasks
public async Task<List<User>> GetUsersAsync(int[] ids)
{
    var tasks = ids.Select(id => GetUserWithErrorHandlingAsync(id)).ToList();

    try
    {
        var users = await Task.WhenAll(tasks);
        return users.ToList();
    }
    catch (Exception ex)
    {
        // If any task fails, WhenAll throws
        Console.WriteLine($"Failed to fetch users: {ex.Message}");
        
        // Or handle partial failures
        return tasks
            .Where(t => t.IsCompletedSuccessfully)
            .Select(t => t.Result)
            .ToList();
    }
}
```

## Cancellation

Stop long-running operations:

```csharp
public async Task<User> GetUserWithCancellationAsync(
    int id,
    CancellationToken cancellationToken)
{
    var response = await _httpClient.GetAsync(
        $"https://api.example.com/users/{id}",
        cancellationToken);  // Pass token to async operation

    if (!response.IsSuccessStatusCode)
        throw new Exception("Failed");

    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    return JsonSerializer.Deserialize<User>(content);
}

// Usage
var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(5));  // Auto-cancel after 5 seconds

try
{
    var user = await GetUserWithCancellationAsync(1, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Request cancelled");
}

// In ASP.NET Core controller
[HttpGet("{id}")]
public async Task<IActionResult> GetUser(int id, CancellationToken cancellationToken)
{
    try
    {
        var user = await _userService.GetUserAsync(id, cancellationToken);
        return Ok(user);
    }
    catch (OperationCanceledException)
    {
        return StatusCode(499);  // Client closed connection
    }
}
```

## Async Iteration

Process async collections:

```csharp
public async IAsyncEnumerable<User> GetUsersStreamAsync(
    CancellationToken cancellationToken = default)
{
    var page = 1;
    while (true)
    {
        var users = await FetchPageAsync(page, cancellationToken);
        if (users.Count == 0) break;

        foreach (var user in users)
            yield return user;

        page++;
    }
}

// Consume async enumerable
await foreach (var user in GetUsersStreamAsync())
{
    Console.WriteLine(user.Name);
    // Process each user as it arrives
}

// With cancellation
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await foreach (var user in GetUsersStreamAsync(cts.Token))
{
    Console.WriteLine(user.Name);
}
```

## Threading

Manual thread creation (rare, use async instead):

```csharp
// Create thread
var thread = new Thread(() =>
{
    Console.WriteLine($"Running on thread {Thread.CurrentThread.ManagedThreadId}");
    Thread.Sleep(1000);
    Console.WriteLine("Done");
});

thread.Start();
thread.Join();  // Wait for completion

// Thread pool (prefer async/Task)
ThreadPool.QueueUserWorkItem(_ =>
{
    Console.WriteLine("Running on thread pool");
});

// Monitor threads
Console.WriteLine($"Current thread: {Thread.CurrentThread.ManagedThreadId}");
Console.WriteLine($"Active threads: {Process.GetCurrentProcess().Threads.Count}");
```

## Task Scheduling

```csharp
// Run on thread pool thread
var task = Task.Run(async () =>
{
    await Task.Delay(1000);
    return "Result";
});

var result = await task;

// Schedule after delay
var delayed = Task.Delay(2000).ContinueWith(async t =>
{
    return await FetchDataAsync();
});

// Combine multiple tasks
var task1 = Task.Delay(1000).ContinueWith(_ => 1);
var task2 = Task.Delay(2000).ContinueWith(_ => 2);

var results = await Task.WhenAll(task1, task2);
// results = [1, 2]
```

## Deadlock Prevention

Avoid blocking on async:

```csharp
// WRONG: Blocks the thread (can cause deadlock)
var user = GetUserAsync(1).Result;

// WRONG: Blocks with thread pool starvation
public void BadMethod()
{
    var user = GetUserAsync(1).Result;  // Blocks current thread
}

// CORRECT: Async all the way down
public async Task<User> GoodMethod()
{
    var user = await GetUserAsync(1);  // Doesn't block
    return user;
}

// CORRECT: Use ConfigureAwait to avoid context switching
public async Task<User> OptimizedMethod()
{
    var response = await _httpClient.GetAsync("...");
    var content = await response.Content.ReadAsStringAsync()
        .ConfigureAwait(false);  // Don't need UI context
    return JsonSerializer.Deserialize<User>(content);
}
```

## BackgroundService

Long-running background tasks:

```csharp
public class EmailBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmailBackgroundService> _logger;

    public EmailBackgroundService(IServiceProvider serviceProvider, ILogger<EmailBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    await emailService.ProcessQueueAsync(stoppingToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing email queue");
            }
        }
    }
}

// Register in Program.cs
builder.Services.AddHostedService<EmailBackgroundService>();
```

## Timeouts

```csharp
// Timeout on single operation
public async Task<User> GetUserWithTimeoutAsync(int id)
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    return await GetUserAsync(id, cts.Token);
}

// Timeout on multiple operations
public async Task<List<User>> GetUsersWithTimeoutAsync(int[] ids)
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var tasks = ids.Select(id => GetUserAsync(id, cts.Token));

    try
    {
        return (await Task.WhenAll(tasks)).ToList();
    }
    catch (OperationCanceledException)
    {
        return new List<User>();
    }
}
```

## Best Practices

```csharp
// ✓ Async all the way
public async Task<Result> ProcessAsync() => await GetDataAsync();

// ✗ Mixing sync and async (deadlock risk)
public void Process()
{
    var data = GetDataAsync().Result;  // WRONG
}

// ✓ Proper cancellation token usage
public async Task<Data> FetchAsync(CancellationToken ct)
{
    return await _client.GetAsync(url, ct);
}

// ✓ ConfigureAwait in libraries
public async Task<Result> LibraryMethodAsync()
{
    var response = await _http.GetAsync(url).ConfigureAwait(false);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    return Parse(content);
}

// ✓ Proper error handling
try
{
    await LongRunningOperationAsync(cancellationToken);
}
catch (OperationCanceledException)
{
    // Handle cancellation
}
catch (Exception ex)
{
    // Handle other errors
}
```

## Practice Exercises

1. **Parallel API Calls**: Fetch data from multiple endpoints concurrently
2. **Cancellation**: Implement cancellable long-running operation
3. **Error Handling**: Handle network timeouts and retry logic
4. **Background Service**: Create worker that processes a queue
5. **Streaming**: Implement async enumerable for large dataset

## Key Takeaways

- **async/await** makes non-blocking code look like synchronous code
- **Task.WhenAll** runs operations in parallel
- **CancellationToken** allows graceful cancellation
- **ConfigureAwait(false)** avoids unnecessary context switching
- **Never block** on async (e.g., .Result causes deadlocks)
- **HttpClient** is async-first
- **BackgroundService** for long-running operations
- **Always provide cancellation token** for timeout support
- Async enables writing **scalable, responsive applications**
