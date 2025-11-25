# Chapter 13: Resilience & Fault Tolerance

## 13.1 Resilience Principles

APIs in distributed systems experience failures: network timeouts, service downtime, resource exhaustion. Resilience patterns handle failures gracefully.

### Failure Modes

**Transient Failures** - Temporary, may succeed if retried
- Network timeout
- Temporary unavailability
- Brief spike in load

**Permanent Failures** - Won't succeed by retrying
- Service permanently down
- Authentication failure
- Invalid input

---

## 13.2 Retry Policies with Polly

Polly is a resilience library for handling transient failures.

### Simple Retry

```csharp
// Retry up to 3 times with exponential backoff
var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .Or<TimeoutException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attemptNumber => 
            TimeSpan.FromSeconds(Math.Pow(2, attemptNumber - 1)),  // 1s, 2s, 4s
        onRetry: (exception, duration, retryCount, context) =>
        {
            _logger.LogWarning(
                "Retry {RetryCount} after {Duration}ms for {ExceptionType}",
                retryCount,
                duration.TotalMilliseconds,
                exception.GetType().Name
            );
        }
    );

// Execute with retry policy
var result = await retryPolicy.ExecuteAsync(async () =>
{
    return await _externalService.GetDataAsync();
});
```

### Retry with Jitter

Prevent thundering herd when multiple clients retry simultaneously:

```csharp
var jitterPolicy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attemptNumber =>
        {
            var baseDelay = Math.Pow(2, attemptNumber - 1);
            var jitter = Random.Shared.Next(0, 1000); // Add randomness
            return TimeSpan.FromSeconds(baseDelay) + TimeSpan.FromMilliseconds(jitter);
        }
    );
```

### Transient Vs. Permanent Failures

```csharp
// Only retry transient failures
var transientPolicy = Policy
    .Handle<HttpRequestException>(ex =>
    {
        // Don't retry authentication failures
        return ex.StatusCode != System.Net.HttpStatusCode.Unauthorized &&
               ex.StatusCode != System.Net.HttpStatusCode.Forbidden;
    })
    .Or<TimeoutException>()
    .Or<OperationCanceledException>()
    .WaitAndRetryAsync(retryCount: 3, /* ... */);
```

---

## 13.3 Circuit Breaker Pattern

Prevent cascading failures by stopping requests to failing services.

```csharp
// Trip circuit after 5 failures in 30 seconds
// Keep circuit open for 1 minute, then half-open to test recovery
var circuitBreakerPolicy = Policy
    .Handle<HttpRequestException>()
    .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromSeconds(60),
        onBreak: (outcome, duration) =>
        {
            _logger.LogWarning(
                "Circuit breaker opened: {Exception}. Will retry after {Duration}",
                outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString(),
                duration.TotalSeconds
            );
        },
        onReset: () =>
        {
            _logger.LogInformation("Circuit breaker reset, service appears healthy");
        }
    );

public class ExternalServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly IAsyncPolicy<HttpResponseMessage> _circuitBreakerPolicy;
    
    public async Task<Data> GetDataAsync()
    {
        var response = await _circuitBreakerPolicy.ExecuteAsync(
            () => _httpClient.GetAsync("https://api.external.com/data")
        );
        
        // Circuit open throws BrokenCircuitException before making request
        // Prevents overwhelming failing service
        return await response.Content.ReadAsAsync<Data>();
    }
}

// Register in DI
builder.Services.AddHttpClient<ExternalServiceClient>()
    .AddTransientHttpErrorPolicy(p => p.CircuitBreakerAsync(5, TimeSpan.FromSeconds(60)));
```

**Circuit States:**
- **Closed** - Requests pass through (normal state)
- **Open** - Requests fail immediately (service down)
- **Half-Open** - Allow limited requests to test recovery

---

## 13.4 Timeout Policies

Prevent indefinite waiting:

```csharp
// Timeout after 5 seconds
var timeoutPolicy = Policy.TimeoutAsync(
    timeout: TimeSpan.FromSeconds(5),
    timeoutStrategy: TimeoutStrategy.Optimistic  // Cancel operation
);

// Timeout per operation
var perOperationTimeoutPolicy = Policy.TimeoutAsync(
    timeout: TimeSpan.FromSeconds(2),
    timeoutStrategy: TimeoutStrategy.Pessimistic  // Let operation complete but don't wait
);

var result = await timeoutPolicy.ExecuteAsync(async ct =>
{
    // This operation gets cancelled if > 5 seconds
    return await _service.LongOperationAsync();
});

// Timeout on HttpClient
var httpPolicy = Policy.TimeoutAsync<HttpResponseMessage>(
    timeout: TimeSpan.FromSeconds(10)
);

builder.Services.AddHttpClient<MyClient>()
    .AddPolicyHandler(httpPolicy);
```

---

## 13.5 Bulkhead Pattern

Isolate resources to prevent total failure:

```csharp
// Only allow 10 concurrent requests to this service
var bulkheadPolicy = Policy.BulkheadAsync(
    maxParallelization: 10,
    maxQueuingActions: 20,  // Queue up to 20 additional requests
    onBulkheadRejectedAsync: context =>
    {
        _logger.LogWarning("Bulkhead rejected request, service overloaded");
        return Task.CompletedTask;
    }
);

try
{
    await bulkheadPolicy.ExecuteAsync(async () =>
    {
        await _expensiveService.DoWorkAsync();
    });
}
catch (BulkheadRejectedException)
{
    return StatusCode(503, "Service temporarily unavailable");  // 503 Service Unavailable
}
```

---

## 13.6 Combining Policies with Policy Wrap

```csharp
// Combine retry, circuit breaker, timeout
var policyWrap = Policy.WrapAsync(
    retryPolicy,
    circuitBreakerPolicy,
    timeoutPolicy
);

var result = await policyWrap.ExecuteAsync(async () =>
{
    return await _externalService.GetDataAsync();
});

// Order matters! Timeout wraps CircuitBreaker wraps Retry
// Requests timeout after 5s, circuit breaks after 5 failures, retries up to 3 times
```

---

## 13.7 Graceful Degradation

Provide reduced functionality when dependencies fail:

```csharp
[HttpGet("products/{id}")]
public async Task<ActionResult<ProductDto>> GetProduct(int id)
{
    var product = await _productService.GetProductAsync(id);
    
    // Get enrichment data (recommendations, reviews, etc.)
    try
    {
        product.Recommendations = await _recommendationService.GetAsync(id);
        product.Reviews = await _reviewService.GetAsync(id);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to load product enrichment data");
        // Continue without enrichment, return basic product
    }
    
    return Ok(product);
}

// Fallback response when service unavailable
var fallbackPolicy = Policy<Data>
    .Handle<HttpRequestException>()
    .FallbackAsync(
        fallbackValue: new Data { IsDefault = true },  // Return default data
        onFallbackAsync: (outcome, context) =>
        {
            _logger.LogWarning("Using fallback data due to service failure");
            return Task.CompletedTask;
        }
    );

var data = await fallbackPolicy.ExecuteAsync(async () =>
{
    return await _externalService.GetDataAsync();
});
```

---

## 13.8 Health Check Integration

```csharp
public class DependencyHealthCheck : IHealthCheck
{
    private readonly ExternalServiceClient _client;
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to reach dependency
            var data = await _client.GetDataAsync();
            return HealthCheckResult.Healthy("External service operational");
        }
        catch (BrokenCircuitException)
        {
            return HealthCheckResult.Unhealthy("Circuit breaker open, service unavailable");
        }
        catch (TimeoutException)
        {
            return HealthCheckResult.Unhealthy("External service timeout");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Health check failed: {ex.Message}");
        }
    }
}

// Don't start application until critical dependencies are healthy
public class StartupHealthCheck
{
    public static async Task EnsureHealthyAsync(IHost app)
    {
        using var scope = app.Services.CreateScope();
        var healthCheckService = scope.ServiceProvider.GetRequiredService<HealthCheckService>();
        
        var report = await healthCheckService.CheckHealthAsync();
        
        if (report.Status != HealthStatus.Healthy)
        {
            throw new InvalidOperationException(
                "Application cannot start, critical dependencies unhealthy"
            );
        }
    }
}

// In Program.cs
var app = builder.Build();
await StartupHealthCheck.EnsureHealthyAsync(app);
app.Run();
```

---

## 13.9 Idempotency

Ensure operations are safe to retry:

```csharp
// Idempotency key prevents duplicate processing
[HttpPost("orders")]
public async Task<ActionResult<OrderDto>> CreateOrder(
    CreateOrderRequest request,
    [FromHeader(Name = "Idempotency-Key")] string idempotencyKey)
{
    // Check if we've already processed this request
    var existing = await _idempotencyService.GetAsync(idempotencyKey);
    if (existing != null)
    {
        _logger.LogInformation("Returning cached response for idempotency key {Key}", idempotencyKey);
        return Ok(existing);  // Return previous response
    }
    
    try
    {
        // Create order
        var order = await _orderService.CreateOrderAsync(request);
        
        // Cache the response
        await _idempotencyService.StoreAsync(idempotencyKey, order);
        
        return Created($"/orders/{order.Id}", order);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Order creation failed");
        
        // Store failure so we don't retry
        await _idempotencyService.StoreErrorAsync(idempotencyKey, ex);
        throw;
    }
}

// Idempotency storage (Redis recommended for distributed systems)
public class IdempotencyService
{
    private readonly IDistributedCache _cache;
    
    public async Task<OrderDto> GetAsync(string key)
    {
        var cached = await _cache.GetStringAsync(key);
        return cached != null ? JsonSerializer.Deserialize<OrderDto>(cached) : null;
    }
    
    public async Task StoreAsync(string key, OrderDto result)
    {
        await _cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(result),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
            }
        );
    }
}
```

---

## 13.10 Rate Limiting

Prevent abuse and overload:

```csharp
// Per-user rate limiting: 100 requests per minute
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("user-limit", policy =>
    {
        policy.PermitLimit = 100;
        policy.Window = TimeSpan.FromMinutes(1);
        policy.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        policy.QueueLimit = 10;  // Queue up to 10 requests
    });
});

app.UseRateLimiter();

[HttpPost("orders")]
[RequireRateLimiting("user-limit")]
public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
{
    // Limited to 100 requests per minute
}

// Custom rate limit key per user
options.AddSlidingWindowLimiter("authenticated-limit", policy =>
{
    policy.PermitLimit = 100;
    policy.Window = TimeSpan.FromMinutes(1);
    policy.SegmentsPerWindow = 2;  // Divide window into 2 segments
});

// Get rate limit status
[HttpGet("usage")]
public async Task<IActionResult> GetUsage()
{
    var limiter = HttpContext.Features.Get<RateLimitLease>();
    
    return Ok(new
    {
        Limit = limiter?.PermitLimit,
        Remaining = limiter?.TryLeaseAtLeastAsync(0).Result,
        Reset = limiter?.TryLeaseAtLeastAsync(0).Result
    });
}
```

---

## 13.11 Load Testing and Capacity Planning

Understand system limits before production:

```bash
# Load test with Apache Bench
ab -n 1000 -c 10 http://localhost:5000/api/users

# n: total requests
# c: concurrent requests

# More sophisticated: k6
k6 run script.js

# Monitor during test:
# - Response time (p95, p99)
# - Error rate
# - Resource usage (CPU, memory, connections)
# - Database connections used
```

**Capacity planning checklist:**
- Expected concurrent users
- Requests per second
- Peak traffic patterns
- Third-party service limits
- Database connection limits
- Memory requirements
- Disk space growth rate

---

## Summary

Resilience patterns make APIs robust to failures. Retry policies handle transient failures. Circuit breakers prevent cascading failures. Timeouts prevent indefinite waiting. Bulkheads isolate resources. Graceful degradation provides reduced functionality when dependencies fail. Idempotency keys enable safe retries. Rate limiting prevents abuse. Health checks indicate system status. The next chapter covers security best practices—essential for protecting production systems.
