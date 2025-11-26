# 33. Resilience Patterns

## Overview
Resilience patterns ensure applications gracefully handle failures—network timeouts, service outages, rate limiting, and resource exhaustion. The Polly library in .NET provides sophisticated patterns to build fault-tolerant systems that recover automatically and degrade gracefully.

---

## 1. Resilience Fundamentals

### 1.1 Failure Types

```
TRANSIENT FAILURES (Temporary)
- Network timeout
- Service temporarily down
- Rate limit (429 status)
- Resource temporarily unavailable

Recovery: Retry after brief delay

PERMANENT FAILURES (Persistent)
- Service not found (404)
- Authentication failed (401)
- Invalid request (400)
- Service permanently down

Recovery: Fail fast, inform caller
```

### 1.2 Resilience Patterns Hierarchy

```
Level 1: RETRY
└─ Handle transient failures

Level 2: CIRCUIT BREAKER
└─ Prevent cascade failures

Level 3: TIMEOUT
└─ Avoid hanging requests

Level 4: BULKHEAD
└─ Isolate resource pools

Level 5: FALLBACK
└─ Degrade gracefully

Typical Stack:
Timeout → Retry → Circuit Breaker → Bulkhead → Fallback
```

---

## 2. Retry Pattern

### 2.1 Basic Retry

```csharp
// Install: dotnet add package Polly

using Polly;
using Polly.Retry;

public class RetryPolicyExample
{
    public static void ConfigureRetry(IServiceCollection services)
    {
        services.AddScoped<IInventoryService>(provider =>
        {
            var httpClient = new HttpClient();
            
            // Simple retry: 3 attempts with 1 second delay
            var retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or(r => !r.IsSuccessStatusCode)
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt =>
                        TimeSpan.FromSeconds(1),
                    onRetry: (outcome, timespan, retryCount, context) =>
                    {
                        Console.WriteLine(
                            $"Retry {retryCount} after {timespan.TotalSeconds}s"
                        );
                    }
                );
            
            return new ResilientInventoryService(httpClient, retryPolicy);
        });
    }
}

public class ResilientInventoryService : IInventoryService
{
    private readonly HttpClient _httpClient;
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;
    
    public async Task<bool> CheckAvailabilityAsync(int productId, int quantity)
    {
        var response = await _retryPolicy.ExecuteAsync(async () =>
            await _httpClient.GetAsync($"http://inventory/check?id={productId}&qty={quantity}")
        );
        
        return response.IsSuccessStatusCode;
    }
}

// Execution flow:
/*
Attempt 1 → FAIL (transient error)
         ↓ [Wait 1s]
Attempt 2 → FAIL (transient error)
         ↓ [Wait 1s]
Attempt 3 → FAIL (transient error)
         ↓
         THROW EXCEPTION (after all retries exhausted)
*/
```

### 2.2 Exponential Backoff

```csharp
public class ExponentialBackoffExample
{
    public static void ConfigureExponentialBackoff(IServiceCollection services)
    {
        services.AddScoped<IPaymentService>(provider =>
        {
            var httpClient = new HttpClient();
            
            // Exponential backoff: 1s, 2s, 4s, 8s
            var retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or(r => (int)r.StatusCode >= 500)
                .WaitAndRetryAsync(
                    retryCount: 4,
                    sleepDurationProvider: retryAttempt =>
                        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                );
            
            return new ResilientPaymentService(httpClient, retryPolicy);
        });
    }
}

// Pattern: 1s, 2s, 4s, 8s = 15 seconds total
// Useful for: Heavy background jobs, database migrations
// Avoid for: User-facing requests (too slow)
```

### 2.3 Jitter (Randomized Backoff)

```csharp
public class JitteredBackoffExample
{
    public static void ConfigureJitteredBackoff(IServiceCollection services)
    {
        services.AddHttpClient<IOrderService, OrderService>()
            .AddTransientHttpErrorPolicy(p =>
                p.WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt =>
                    {
                        // Exponential backoff with jitter
                        var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        var jitter = TimeSpan.FromMilliseconds(
                            Random.Shared.Next(0, 1000)
                        );
                        
                        return baseDelay.Add(jitter);
                    }
                )
            );
    }
}

// Why jitter?
// Problem: Thundering herd - all clients retry simultaneously
// Solution: Add randomness to stagger retry waves
//
// Without jitter:    With jitter:
// [Retry 1]          [Retry 1]
// [Retry 2]          [Retry 2]  [Retry 2]
// [Retry 3]      →   [Retry 3]      [Retry 3]
// [Retry 4]                     [Retry 4]
```

---

## 3. Circuit Breaker Pattern

### 3.1 Circuit Breaker States

```
CLOSED (Normal Operation)
│ Requests pass through
│ Failures tracked
│
├─ Failure threshold reached
↓
OPEN (Rejecting)
│ Requests fail immediately
│ Prevents cascade
│ Waits for cooldown
│
├─ Cooldown elapsed
↓
HALF-OPEN (Testing)
│ Limited requests allowed
│ Testing if service recovered
│
├─ Success?      → CLOSED (circuit reset)
├─ Failure?      → OPEN (back to failing)
```

### 3.2 Circuit Breaker Implementation

```csharp
public class CircuitBreakerPolicyExample
{
    public static void ConfigureCircuitBreaker(IServiceCollection services)
    {
        services.AddHttpClient<IShippingService, ShippingService>()
            .AddTransientHttpErrorPolicy(p =>
                p.CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 5,  // Break after 5 failures
                    durationOfBreak: TimeSpan.FromSeconds(30),  // Wait 30s before testing
                    onBreak: (outcome, duration) =>
                    {
                        Console.WriteLine(
                            $"Circuit breaker opened for {duration.TotalSeconds}s"
                        );
                    },
                    onReset: () =>
                    {
                        Console.WriteLine("Circuit breaker closed - service recovered");
                    },
                    onHalfOpen: () =>
                    {
                        Console.WriteLine("Circuit breaker half-open - testing service");
                    }
                )
            );
    }
}

// Typical flow:
public class ResilientShippingService : IShippingService
{
    private readonly HttpClient _httpClient;
    
    public async Task<ShippingQuote> GetQuoteAsync(Order order)
    {
        try
        {
            // Circuit breaker wraps this call
            var response = await _httpClient.PostAsJsonAsync(
                "http://shipping/quote",
                order
            );
            
            return await response.Content.ReadAsAsync<ShippingQuote>();
        }
        catch (BrokenCircuitException ex)
        {
            // Circuit is open
            Console.WriteLine("Shipping service unavailable - using fallback");
            return GetFallbackQuote(order);
        }
    }
    
    private ShippingQuote GetFallbackQuote(Order order)
    {
        return new ShippingQuote
        {
            StandardRate = 10.00m,
            EstimatedDays = 5,
            IsEstimate = true
        };
    }
}
```

### 3.3 Advanced Circuit Breaker

```csharp
public class AdvancedCircuitBreakerExample
{
    public static void ConfigureAdvanced(IServiceCollection services)
    {
        services.AddHttpClient<IPaymentGateway, PaymentGateway>()
            .AddTransientHttpErrorPolicy(p =>
                p.CircuitBreakerAsync<HttpResponseMessage>(
                    handledEventsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    
                    // Predicate: What counts as failure
                    failureThreshold: 0.5,  // Break at 50% failure rate
                    samplingDuration: TimeSpan.FromSeconds(10)  // Over last 10 seconds
                )
            );
    }
    
    // Context: Store state across retries/circuit breaker
    public async Task<PaymentResult> ProcessPaymentAsync(
        Payment payment,
        Context context)
    {
        var policy = Policy
            .Handle<HttpRequestException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30)
            );
        
        var response = await policy.ExecuteAsync(
            async (ctx) =>
            {
                // Access context within policy execution
                ctx["PaymentId"] = payment.Id;
                ctx["AttemptedAt"] = DateTime.UtcNow;
                
                return await _httpClient.PostAsJsonAsync("/payment/process", payment);
            },
            context
        );
        
        return await response.Content.ReadAsAsync<PaymentResult>();
    }
}
```

---

## 4. Timeout Pattern

### 4.1 Request Timeout

```csharp
public class TimeoutPolicyExample
{
    public static void ConfigureTimeout(IServiceCollection services)
    {
        services.AddHttpClient<IReportService, ReportService>()
            .ConfigureHttpClient(client =>
            {
                // HttpClient timeout (default: 100 seconds)
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddTransientHttpErrorPolicy(p =>
                p.TimeoutAsync<HttpResponseMessage>(
                    TimeSpan.FromSeconds(5)  // Polly timeout
                )
            );
    }
}

public class ResilientReportService : IReportService
{
    private readonly HttpClient _httpClient;
    private readonly IAsyncPolicy<HttpResponseMessage> _timeoutPolicy;
    
    public async Task<Report> GenerateAsync(ReportRequest request)
    {
        try
        {
            var response = await _timeoutPolicy.ExecuteAsync(async () =>
            {
                // If this takes > 5 seconds, TimeoutRejectedException thrown
                return await _httpClient.PostAsJsonAsync(
                    "http://reporting/generate",
                    request
                );
            });
            
            return await response.Content.ReadAsAsync<Report>();
        }
        catch (TimeoutRejectedException ex)
        {
            Console.WriteLine("Report generation timed out");
            throw new ReportGenerationFailedException("Timeout", ex);
        }
    }
}

// Timeout strategy:
// - Short timeouts: Fail fast, good for user-facing requests
// - Long timeouts: Accept longer waits, good for background jobs
//
// Typical values:
// API to external service: 3-5 seconds
// Database query: 10-30 seconds  
// Batch job: 5-10 minutes
```

---

## 5. Bulkhead Pattern

### 5.1 Resource Isolation

```csharp
public class BulkheadPolicyExample
{
    public static void ConfigureBulkhead(IServiceCollection services)
    {
        // Isolate Payment operations into separate thread pool
        var paymentBulkhead = Policy.BulkheadAsync(
            maxParallelization: 10,  // Max 10 concurrent calls
            maxQueuingActions: 20,   // Queue up to 20 more
            onBulkheadRejectedAsync: context =>
            {
                Console.WriteLine("Payment service overloaded - rejecting request");
                return Task.CompletedTask;
            }
        );
        
        // Isolate Shipping operations
        var shippingBulkhead = Policy.BulkheadAsync(
            maxParallelization: 5,
            maxQueuingActions: 15
        );
        
        services.AddScoped<IPaymentService>(provider =>
            new ResilientPaymentService(_httpClient, paymentBulkhead)
        );
        
        services.AddScoped<IShippingService>(provider =>
            new ResilientShippingService(_httpClient, shippingBulkhead)
        );
    }
}

public class ResilientPaymentService : IPaymentService
{
    private readonly HttpClient _httpClient;
    private readonly IAsyncPolicy _bulkheadPolicy;
    
    public async Task<PaymentResult> ProcessAsync(Payment payment)
    {
        try
        {
            return await _bulkheadPolicy.ExecuteAsync(async () =>
            {
                var response = await _httpClient.PostAsJsonAsync(
                    "http://payment-service/process",
                    payment
                );
                
                return await response.Content.ReadAsAsync<PaymentResult>();
            });
        }
        catch (BulkheadRejectedException ex)
        {
            // Service overloaded
            throw new ServiceOverloadedException(
                "Payment service at capacity. Please retry.",
                ex
            );
        }
    }
}

// Visualization:
/*
Without Bulkhead:          With Bulkhead:
All operations compete     Operations isolated

Payment: 50 threads   →    Payment: 10 max
Shipping: 40 threads  →    Shipping: 5 max
         │                       │
      One queue              Separate queues
     (Exhaustion)           (Protection)

If payment overloaded → shipping still responsive
*/
```

### 5.2 Semaphore Bulkhead

```csharp
public class SemaphoreBulkheadExample
{
    // For CPU-bound operations within same process
    
    public static void ConfigureSemaphore(IServiceCollection services)
    {
        var importBulkhead = Policy.BulkheadAsync(
            maxParallelization: 3,
            maxQueuingActions: 10
        );
        
        services.AddScoped<IDataImportService>(provider =>
            new DataImportService(importBulkhead)
        );
    }
}

public class DataImportService : IDataImportService
{
    private readonly IAsyncPolicy _bulkhead;
    
    public async Task<ImportResult> ImportAsync(string filePath)
    {
        return await _bulkhead.ExecuteAsync(async () =>
        {
            // Only 3 imports running simultaneously
            var data = await ParseLargeFileAsync(filePath);
            return await ProcessDataAsync(data);
        });
    }
}
```

---

## 6. Fallback Pattern

### 6.1 Graceful Degradation

```csharp
public class FallbackPolicyExample
{
    public static void ConfigureFallback(IServiceCollection services)
    {
        services.AddHttpClient<IProductService, ProductService>()
            .AddTransientHttpErrorPolicy(p =>
                p.FallbackAsync<HttpResponseMessage>(
                    fallbackAction: async () =>
                    {
                        // Return cached/degraded response
                        var cachedProducts = await GetCachedProductsAsync();
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                JsonConvert.SerializeObject(cachedProducts),
                                Encoding.UTF8,
                                "application/json"
                            )
                        };
                    }
                )
            );
    }
}

public class ResilientProductService : IProductService
{
    private readonly IMemoryCache _cache;
    private readonly HttpClient _httpClient;
    
    public async Task<List<Product>> GetProductsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("http://catalog/products");
            return await response.Content.ReadAsAsync<List<Product>>();
        }
        catch (HttpRequestException)
        {
            // Return stale cached data
            if (_cache.TryGetValue("products", out List<Product> cached))
            {
                return cached;
            }
            
            // Return empty list as last resort
            return new List<Product>();
        }
    }
}
```

### 6.2 Fallback with Different Data

```csharp
public class RecommendationService : IRecommendationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RecommendationService> _logger;
    
    public async Task<List<Product>> GetRecommendationsAsync(int customerId)
    {
        try
        {
            // Try personalized recommendations
            var response = await _httpClient.GetAsync(
                $"http://ml-service/recommendations/{customerId}"
            );
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsAsync<List<Product>>();
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "ML service unavailable");
        }
        
        // Fallback: Popular products
        return await GetPopularProductsAsync();
    }
    
    private async Task<List<Product>> GetPopularProductsAsync()
    {
        // Cached, fast, always available
        return await _cache.GetOrCreateAsync(
            "popular-products",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                return await _repository.GetPopularAsync();
            }
        );
    }
}
```

---

## 7. Policy Composition

### 7.1 Wrapping Multiple Policies

```csharp
public class PolicyCompositionExample
{
    public static void ConfigureComposedPolicies(IServiceCollection services)
    {
        services.AddHttpClient<IOrderService, OrderService>()
            .AddTransientHttpErrorPolicy(p =>
                p.WrapAsync(
                    // Layer 1: Timeout (fail fast)
                    Policy.TimeoutAsync<HttpResponseMessage>(
                        TimeSpan.FromSeconds(10)
                    ),
                    // Layer 2: Retry (handle transients)
                    Policy.Handle<TimeoutRejectedException>()
                        .Or<HttpRequestException>()
                        .WaitAndRetryAsync(
                            retryCount: 3,
                            sleepDurationProvider: attempt =>
                                TimeSpan.FromSeconds(Math.Pow(2, attempt))
                        ),
                    // Layer 3: Circuit breaker (prevent cascade)
                    Policy.Handle<Exception>()
                        .CircuitBreakerAsync(
                            handledEventsAllowedBeforeBreaking: 5,
                            durationOfBreak: TimeSpan.FromSeconds(30)
                        ),
                    // Layer 4: Fallback (degrade gracefully)
                    Policy.Handle<Exception>()
                        .FallbackAsync<HttpResponseMessage>(
                            fallbackAction: async () =>
                            {
                                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                                {
                                    Content = new StringContent(
                                        JsonConvert.SerializeObject(new { message = "Service unavailable" })
                                    )
                                };
                            }
                        )
                )
            );
    }
}

// Execution flow:
/*
Request
  ↓
[Timeout: 10s]
  ↓
[Retry x3]
  ↓
[Circuit Breaker]
  ↓
[Fallback]
  ↓
Response/Exception
*/
```

### 7.2 Policy Registry

```csharp
public class PolicyRegistry
{
    public static void RegisterPolicies(IServiceCollection services)
    {
        var registry = services.AddPolicyRegistry();
        
        // API timeout policy
        registry.Add("api-timeout", Policy.TimeoutAsync<HttpResponseMessage>(
            TimeSpan.FromSeconds(10)
        ));
        
        // Database timeout policy
        registry.Add("db-timeout", Policy.TimeoutAsync(
            TimeSpan.FromSeconds(30)
        ));
        
        // Retry policy
        registry.Add("default-retry", Policy.Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt))
            ));
        
        // Circuit breaker
        registry.Add("default-circuit-breaker", Policy.Handle<Exception>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30)
            ));
    }
}

// Usage
public class ServiceWithRegistry
{
    private readonly IReadOnlyPolicyRegistry<IAsyncPolicy> _registry;
    
    public async Task<Result> ExecuteAsync()
    {
        var timeoutPolicy = _registry.Get<IAsyncPolicy>("api-timeout");
        var retryPolicy = _registry.Get<IAsyncPolicy>("default-retry");
        
        var combinedPolicy = Policy.WrapAsync(timeoutPolicy, retryPolicy);
        
        return await combinedPolicy.ExecuteAsync(async () =>
        {
            return await MakeRequestAsync();
        });
    }
}
```

---

## 8. Testing Resilience

### 8.1 Resilience Testing

```csharp
public class ResilienceTests
{
    [Fact]
    public async Task ShouldRetryOnTransientFailure()
    {
        // Arrange
        var handler = new MockHttpMessageHandler()
            .RespondWith(HttpStatusCode.ServiceUnavailable)  // Attempt 1
            .RespondWith(HttpStatusCode.ServiceUnavailable)  // Attempt 2
            .RespondWith(HttpStatusCode.OK);                 // Attempt 3 - success
        
        var httpClient = new HttpClient(handler);
        var retryPolicy = Policy.Handle<HttpRequestException>()
            .Or(r => (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(100));
        
        var service = new ResilientOrderService(httpClient, retryPolicy);
        
        // Act
        var result = await service.GetOrderAsync(123);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, handler.Attempts);
    }
    
    [Fact]
    public async Task ShouldOpenCircuitBreakerOnMultipleFailures()
    {
        // Arrange
        var handler = new MockHttpMessageHandler()
            .RespondWith(HttpStatusCode.ServiceUnavailable)
            .RespondWith(HttpStatusCode.ServiceUnavailable)
            .RespondWith(HttpStatusCode.ServiceUnavailable);
        
        var httpClient = new HttpClient(handler);
        var circuitBreaker = Policy.Handle<HttpRequestException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 2,
                durationOfBreak: TimeSpan.FromMilliseconds(500)
            );
        
        var service = new ResilientOrderService(httpClient, circuitBreaker);
        
        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetOrderAsync(123)
        );
        
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => service.GetOrderAsync(456)  // Circuit open
        );
        
        // Wait for circuit to reset
        await Task.Delay(600);
        
        handler.RespondWith(HttpStatusCode.OK);
        
        var result = await service.GetOrderAsync(789);
        Assert.NotNull(result);
    }
}
```

---

## Summary

Resilience patterns ensure systems survive failures:

1. **Retry**: Handle transient failures with exponential backoff and jitter
2. **Circuit Breaker**: Prevent cascade failures by failing fast
3. **Timeout**: Avoid hanging requests
4. **Bulkhead**: Isolate resource pools to prevent exhaustion
5. **Fallback**: Degrade gracefully with cached/alternative data

Key principles:
- Compose patterns in layers (Timeout → Retry → CB → Fallback)
- Monitor policy execution for insights
- Test resilience with controlled failures
- Use appropriate timeouts for context
- Add jitter to prevent thundering herds

Common mistakes:
- Retrying permanent failures (404, 401)
- Timeout too short/long
- Not monitoring circuit breaker state
- Ignoring bulkhead capacity
- Fallback to unavailable resource

Next topic covers Deployment Strategies for production systems.
