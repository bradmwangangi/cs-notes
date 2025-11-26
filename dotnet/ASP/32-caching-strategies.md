# 31. Caching Strategies

## Overview
Caching improves performance by storing frequently accessed data closer to consumers, reducing latency and database load. Strategic caching is essential for enterprise applications handling high traffic and complex computations.

---

## 1. Caching Fundamentals

### 1.1 Cache Levels

```
Application Layer
    ↓ (L1 Cache)
IMemoryCache (in-memory, process-level)
    ↓ (L2 Cache)
Redis (distributed, network accessible)
    ↓ (L3 Cache)
Database
```

```csharp
public class CacheLevelsExample
{
    // L1: In-Memory Cache (process-level)
    private readonly IMemoryCache _memoryCache;
    
    // L2: Distributed Cache (Redis, App Cache)
    private readonly IDistributedCache _distributedCache;
    
    // L3: Database
    private readonly IOrderRepository _repository;
    
    public async Task<Order> GetOrderAsync(int id)
    {
        // Check L1: In-memory (fastest, but limited)
        if (_memoryCache.TryGetValue($"order-{id}", out Order cached))
        {
            return cached;
        }
        
        // Check L2: Distributed (slower, but shared across instances)
        var distributedJson = await _distributedCache.GetStringAsync($"order-{id}");
        if (!string.IsNullOrEmpty(distributedJson))
        {
            var order = JsonConvert.DeserializeObject<Order>(distributedJson);
            
            // Populate L1
            _memoryCache.Set($"order-{id}", order, TimeSpan.FromMinutes(10));
            
            return order;
        }
        
        // Check L3: Database (slowest)
        var dbOrder = await _repository.GetByIdAsync(id);
        
        // Populate both caches
        _memoryCache.Set($"order-{id}", dbOrder, TimeSpan.FromMinutes(10));
        await _distributedCache.SetStringAsync(
            $"order-{id}",
            JsonConvert.SerializeObject(dbOrder),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            }
        );
        
        return dbOrder;
    }
}
```

### 1.2 Caching Trade-offs

| Benefit | Trade-off |
|---------|-----------|
| **Speed** | Stale data |
| **Throughput** | Memory usage |
| **Reduced load** | Complexity |
| **Scaling** | Consistency |

---

## 2. In-Memory Caching

### 2.1 IMemoryCache Implementation

```csharp
// Install: Built-in to ASP.NET Core

public class MemoryCacheExample
{
    private readonly IMemoryCache _cache;
    
    // Basic caching
    public async Task<Customer> GetCustomerAsync(int id)
    {
        const string key = "customer";
        
        // Try to get from cache
        if (!_cache.TryGetValue(key, out Customer customer))
        {
            // Not in cache, get from database
            customer = await _repository.GetByIdAsync(id);
            
            // Store in cache with expiration
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(10))  // Expires after 10 min
                .SetSlidingExpiration(TimeSpan.FromMinutes(5));   // Resets on access
            
            _cache.Set(key, customer, cacheOptions);
        }
        
        return customer;
    }
    
    // Priority-based eviction
    public void CacheWithPriority(string key, object value)
    {
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(10))
            .SetPriority(CacheItemPriority.High);  // Keep if memory pressure
        
        _cache.Set(key, value, options);
    }
    
    // Cache invalidation
    public void InvalidateCustomerCache(int id)
    {
        _cache.Remove($"customer-{id}");
    }
    
    // Size limits
    public static void ConfigureMemoryCacheSize(IServiceCollection services)
    {
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 100 * 1024 * 1024;  // 100MB limit
            options.CompactionPercentage = 0.25;    // Remove 25% when limit hit
        });
    }
}

// Dependency injection
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddMemoryCache();  // Enable in-memory caching
        services.AddScoped<CustomerService>();
    }
}
```

### 2.2 Cache Tags for Invalidation

```csharp
public class CacheTagExample
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _tagCache = new();
    
    // Cache with tags
    public void SetWithTags(string key, object value, params string[] tags)
    {
        // Store in cache
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(10));
        
        _cache.Set(key, value, options);
        
        // Track tags
        foreach (var tag in tags)
        {
            var keys = _tagCache.GetOrAdd(tag, _ => new ConcurrentBag<string>());
            keys.Add(key);
        }
    }
    
    // Invalidate all cache entries by tag
    public void InvalidateByTag(string tag)
    {
        if (_tagCache.TryGetValue(tag, out var keys))
        {
            while (keys.TryTake(out var key))
            {
                _cache.Remove(key);
            }
        }
    }
}

// Usage
public class OrderServiceWithTags
{
    private readonly CacheTagExample _cache;
    
    public async Task<int> PlaceOrderAsync(Order order)
    {
        var orderId = await _repository.SaveAsync(order);
        
        // Cache with tags for easy invalidation
        _cache.SetWithTags(
            $"order-{orderId}",
            order,
            tags: new[] { "orders", $"customer-{order.CustomerId}" }
        );
        
        return orderId;
    }
    
    public async Task<Customer> GetCustomerWithOrdersAsync(int id)
    {
        var customer = await _repository.GetCustomerAsync(id);
        
        _cache.SetWithTags(
            $"customer-{id}",
            customer,
            tags: new[] { "customers" }
        );
        
        return customer;
    }
    
    // When customer updates, invalidate their tag
    public void InvalidateCustomer(int id)
    {
        _cache.InvalidateByTag($"customer-{id}");
    }
}
```

---

## 3. Distributed Caching

### 3.1 Redis Caching

```csharp
// Install: dotnet add package StackExchange.Redis

public class RedisCacheConfiguration
{
    public static IDistributedCache ConfigureRedis(IServiceCollection services)
    {
        // Connection string
        var connectionString = "localhost:6379";
        
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
            options.InstanceName = "Bookstore_";  // Key prefix
        });
        
        return services.BuildServiceProvider()
            .GetRequiredService<IDistributedCache>();
    }
}

public class RedisCacheExample
{
    private readonly IDistributedCache _cache;
    
    // Get from distributed cache
    public async Task<Order> GetOrderAsync(int id)
    {
        const string key = "order";
        
        // Try to get from Redis
        var json = await _cache.GetStringAsync(key);
        
        if (!string.IsNullOrEmpty(json))
        {
            return JsonConvert.DeserializeObject<Order>(json);
        }
        
        // Not in cache, get from database
        var order = await _repository.GetByIdAsync(id);
        
        // Store in Redis
        var options = new DistributedCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromHours(1));
        
        await _cache.SetStringAsync(key, JsonConvert.SerializeObject(order), options);
        
        return order;
    }
    
    // Remove from distributed cache
    public async Task InvalidateAsync(string key)
    {
        await _cache.RemoveAsync(key);
    }
    
    // Bulk operations
    public async Task SetMultipleAsync(Dictionary<string, string> items)
    {
        var batch = await _cache.GetAsync("batch-key");
        // Redis supports pipelining for bulk operations
    }
}
```

### 3.2 Azure Cache for Redis

```csharp
public class AzureCacheConfiguration
{
    public static void ConfigureAzureCache(IServiceCollection services, IConfiguration config)
    {
        var cacheConnection = config.GetConnectionString("AzureCache");
        
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = cacheConnection;
            options.InstanceName = "Bookstore_";
        });
    }
}

// Connection string format:
// {hostname},{port}=ssl=true,password={password},abortConnect=False

// Example in appsettings.json:
/*
{
  "ConnectionStrings": {
    "AzureCache": "bookstore.redis.cache.windows.net:6379,ssl=true,password=your-password,abortConnect=False"
  }
}
*/
```

---

## 4. Caching Patterns

### 4.1 Cache-Aside (Lazy Loading)

```csharp
public class CacheAsidePattern
{
    private readonly IMemoryCache _cache;
    private readonly IOrderRepository _repository;
    
    // Application checks cache, updates cache if miss
    public async Task<Order> GetOrderAsync(int id)
    {
        var key = $"order-{id}";
        
        if (!_cache.TryGetValue(key, out Order order))
        {
            // Cache miss: Load from source
            order = await _repository.GetByIdAsync(id);
            
            if (order != null)
            {
                _cache.Set(key, order, TimeSpan.FromMinutes(10));
            }
        }
        
        return order;
    }
    
    // Advantages:
    // - Simple to implement
    // - Only cache what's needed
    // - Easy to invalidate
    
    // Disadvantages:
    // - Cache miss delay
    // - Potential for thundering herd (multiple processes request same data)
}
```

### 4.2 Write-Through

```csharp
public class WriteThroughPattern
{
    private readonly IMemoryCache _cache;
    private readonly IOrderRepository _repository;
    
    // Update cache when writing to source
    public async Task<int> SaveOrderAsync(Order order)
    {
        // Write to database first (primary concern)
        var id = await _repository.SaveAsync(order);
        
        // Update cache immediately
        _cache.Set($"order-{id}", order, TimeSpan.FromMinutes(10));
        
        return id;
    }
    
    public async Task DeleteOrderAsync(int id)
    {
        // Delete from database
        await _repository.DeleteAsync(id);
        
        // Invalidate cache
        _cache.Remove($"order-{id}");
    }
    
    // Advantages:
    // - Cache always consistent with source
    // - No stale data
    
    // Disadvantages:
    // - Slower writes (update both cache and database)
    // - Unused data takes cache space
    // - Failure handling complexity
}
```

### 4.3 Write-Behind (Write-Back)

```csharp
public class WriteBehindPattern
{
    private readonly IMemoryCache _cache;
    private readonly IOrderRepository _repository;
    private readonly Channel<OrderUpdate> _writeQueue;
    
    // Write to cache immediately, database later (asynchronously)
    public async Task<int> SaveOrderAsync(Order order)
    {
        // Write to cache first (fast)
        var id = order.Id > 0 ? order.Id : GenerateId();
        order.Id = id;
        
        _cache.Set($"order-{id}", order, TimeSpan.FromMinutes(30));
        
        // Queue for database write (asynchronous)
        await _writeQueue.Writer.WriteAsync(new OrderUpdate
        {
            Order = order,
            Operation = "Save"
        });
        
        return id;
    }
    
    // Background service writes to database
    public async Task ProcessWriteQueueAsync(CancellationToken ct)
    {
        await foreach (var update in _writeQueue.Reader.ReadAllAsync(ct))
        {
            try
            {
                // Write to database when ready
                await _repository.SaveAsync(update.Order);
                _cache.Remove($"order-{update.Order.Id}");  // Cache now redundant
            }
            catch (Exception ex)
            {
                // Handle failure: Retry, alert, etc.
                _logger.LogError(ex, "Failed to persist order");
            }
        }
    }
    
    // Advantages:
    // - Very fast writes (only to cache)
    // - Batching opportunities
    // - Better throughput
    
    // Disadvantages:
    // - Data loss risk if cache fails
    // - Increased complexity
    // - Consistency windows
}
```

---

## 5. Cache Invalidation Strategies

### 5.1 TTL (Time-To-Live)

```csharp
public class TTLStrategy
{
    private readonly IDistributedCache _cache;
    
    public async Task SetWithTTLAsync(string key, string value, TimeSpan ttl)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl  // Expires after TTL
        };
        
        await _cache.SetStringAsync(key, value, options);
    }
    
    // Fixed TTL: Always 10 minutes
    public async Task SetFixedTTLAsync(string key, string value)
    {
        await SetWithTTLAsync(key, value, TimeSpan.FromMinutes(10));
    }
    
    // Sliding TTL: Resets on access
    public async Task SetSlidingTTLAsync(string key, string value)
    {
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(5)  // 5 min of inactivity
        };
        
        await _cache.SetStringAsync(key, value, options);
    }
    
    // Combination: Both absolute and sliding
    public async Task SetCombinedTTLAsync(string key, string value)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),  // Max 1 hour
            SlidingExpiration = TimeSpan.FromMinutes(5)  // 5 min inactivity
        };
        
        await _cache.SetStringAsync(key, value, options);
    }
}
```

### 5.2 Event-Based Invalidation

```csharp
public class EventBasedInvalidation
{
    private readonly IDistributedCache _cache;
    private readonly IEventPublisher _eventPublisher;
    
    // When order updated, publish event
    public async Task<int> UpdateOrderAsync(Order order)
    {
        await _repository.UpdateAsync(order);
        
        // Publish cache invalidation event
        await _eventPublisher.PublishAsync(new CacheInvalidationEvent
        {
            Keys = new[] { $"order-{order.Id}", "orders-list" }
        });
        
        return order.Id;
    }
    
    // Subscriber invalidates cache
    public class CacheInvalidationHandler : IEventHandler<CacheInvalidationEvent>
    {
        private readonly IDistributedCache _cache;
        
        public async Task HandleAsync(CacheInvalidationEvent @event)
        {
            foreach (var key in @event.Keys)
            {
                await _cache.RemoveAsync(key);
            }
        }
    }
}
```

---

## 6. Cache Stampede Prevention

### 6.1 Thundering Herd

```csharp
// ❌ PROBLEM: Cache expires, many processes request same data
public class ThunderingHerdProblem
{
    private readonly IDistributedCache _cache;
    private readonly IOrderRepository _repository;
    private readonly ILogger<ThunderingHerdProblem> _logger;
    
    public async Task<Order> GetOrderUnsafeAsync(int id)
    {
        var key = $"order-{id}";
        var cached = await _cache.GetStringAsync(key);
        
        // If cache expired, all waiting requests hit database simultaneously
        if (string.IsNullOrEmpty(cached))
        {
            _logger.LogInformation("Cache miss - potential stampede!");
            var order = await _repository.GetByIdAsync(id);
            
            await _cache.SetStringAsync(key, JsonConvert.SerializeObject(order),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
                });
            
            return order;
        }
        
        return JsonConvert.DeserializeObject<Order>(cached);
    }
}

// ✅ SOLUTION 1: Probabilistic early expiration
public class ProbabilisticEarlyExpiration
{
    private readonly IDistributedCache _cache;
    private readonly IOrderRepository _repository;
    
    public async Task<Order> GetOrderSafeAsync(int id)
    {
        var key = $"order-{id}";
        var ttlKey = $"{key}-ttl";
        
        var cached = await _cache.GetStringAsync(key);
        
        if (!string.IsNullOrEmpty(cached))
        {
            // Check if approaching expiration (70% of TTL elapsed)
            var ttl = await _cache.GetStringAsync(ttlKey);
            
            if (ttl != null && int.TryParse(ttl, out var secondsLeft))
            {
                if (secondsLeft < 300)  // Less than 5 minutes left
                {
                    if (Random.Shared.NextDouble() < 0.1)  // 10% probability
                    {
                        // Refresh cache early
                        var fresh = await _repository.GetByIdAsync(id);
                        await _cache.SetStringAsync(key, JsonConvert.SerializeObject(fresh),
                            new DistributedCacheEntryOptions
                            {
                                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
                            });
                    }
                }
            }
        }
        else
        {
            // Cache miss: Load and cache
            var order = await _repository.GetByIdAsync(id);
            await _cache.SetStringAsync(key, JsonConvert.SerializeObject(order),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
                });
            
            return order;
        }
        
        return JsonConvert.DeserializeObject<Order>(cached);
    }
}

// ✅ SOLUTION 2: Locking during refresh
public class LockedRefresh
{
    private readonly IDistributedCache _cache;
    private readonly IOrderRepository _repository;
    
    public async Task<Order> GetOrderWithLockAsync(int id)
    {
        var key = $"order-{id}";
        var lockKey = $"{key}-lock";
        
        var cached = await _cache.GetStringAsync(key);
        
        if (!string.IsNullOrEmpty(cached))
            return JsonConvert.DeserializeObject<Order>(cached);
        
        // Try to acquire lock
        var lockValue = Guid.NewGuid().ToString();
        var acquired = await _cache.SetStringAsync(lockKey, lockValue,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5)
            });
        
        if (acquired)
        {
            try
            {
                // This process loads from database
                var order = await _repository.GetByIdAsync(id);
                await _cache.SetStringAsync(key, JsonConvert.SerializeObject(order),
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
                    });
                
                return order;
            }
            finally
            {
                await _cache.RemoveAsync(lockKey);
            }
        }
        else
        {
            // Other process is loading, wait and retry
            await Task.Delay(100);
            return await GetOrderWithLockAsync(id);
        }
    }
}
```

---

## 7. Cache Performance and Monitoring

### 7.1 Cache Metrics

```csharp
public class CacheMetricsExample
{
    private readonly IDistributedCache _cache;
    private readonly TelemetryClient _telemetry;
    
    public async Task<Order> GetOrderWithMetricsAsync(int id)
    {
        var stopwatch = Stopwatch.StartNew();
        var key = $"order-{id}";
        
        var cached = await _cache.GetStringAsync(key);
        stopwatch.Stop();
        
        if (!string.IsNullOrEmpty(cached))
        {
            // Cache hit
            _telemetry.TrackEvent("CacheHit", new Dictionary<string, string>
            {
                { "Key", key },
                { "Type", "Order" }
            }, new Dictionary<string, double>
            {
                { "Duration", stopwatch.ElapsedMilliseconds },
                { "HitRate", 1 }
            });
            
            return JsonConvert.DeserializeObject<Order>(cached);
        }
        
        // Cache miss
        stopwatch.Restart();
        var order = await _repository.GetByIdAsync(id);
        stopwatch.Stop();
        
        _telemetry.TrackEvent("CacheMiss", new Dictionary<string, string>
        {
            { "Key", key },
            { "Type", "Order" }
        }, new Dictionary<string, double>
        {
            { "DatabaseDuration", stopwatch.ElapsedMilliseconds },
            { "HitRate", 0 }
        });
        
        await _cache.SetStringAsync(key, JsonConvert.SerializeObject(order),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });
        
        return order;
    }
}
```

---

## Summary

Caching improves performance significantly but requires careful strategy:

1. **Levels**: In-memory → Distributed → Database
2. **Patterns**: Cache-Aside, Write-Through, Write-Behind
3. **Invalidation**: TTL, Event-based, Probabilistic
4. **Tools**: IMemoryCache, Redis, Azure Cache
5. **Monitoring**: Hit rates, latency, memory usage
6. **Pitfalls**: Stampede, stale data, consistency

Strategic caching can reduce database load by 90%+ for read-heavy workloads.

Next topic covers Message Queuing and Event-Driven Architecture.
