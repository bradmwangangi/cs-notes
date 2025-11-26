# 25. Async/Await Deep Dive

## Overview
Asynchronous programming in .NET allows applications to efficiently handle I/O-bound operations (database, network, file I/O) without blocking threads. For enterprise systems serving thousands of concurrent users, mastery of async patterns is critical.

---

## 1. Async Fundamentals

### 1.1 The Problem Async Solves

**Synchronous I/O: Blocking threads**
```csharp
// ❌ Synchronous: Thread blocks while waiting for I/O
public class OrderService
{
    public Order GetOrder(int id)
    {
        // Thread sits idle waiting for database response
        var order = _database.GetOrder(id);
        
        // Thread sits idle waiting for email to send
        _emailService.SendConfirmation(order);
        
        return order;
    }
}

// Scenario: 1000 concurrent requests
// Need 1000 threads in pool
// Threads blocked for 500ms each = 500ms total time
// CPU switches between idle threads (expensive context switching)
```

**Asynchronous I/O: Non-blocking operations**
```csharp
// ✅ Asynchronous: Thread not blocked
public class OrderService
{
    public async Task<Order> GetOrderAsync(int id)
    {
        // Thread returns to pool; operation continues on I/O completion thread
        var order = await _database.GetOrderAsync(id);
        
        // Same thread or different thread, but from pool
        await _emailService.SendConfirmationAsync(order);
        
        return order;
    }
}

// Scenario: 1000 concurrent requests
// Can use ~10-20 threads (from thread pool)
// Operations overlap (email while database responding)
// Efficient resource utilization
```

### 1.2 Understanding Task and Task<T>

```csharp
// Task represents an operation (work)
// Task<T> represents an operation that returns T

public class TaskExamples
{
    // No return value
    public async Task DelayAsync(int milliseconds)
    {
        await Task.Delay(milliseconds);
        Console.WriteLine("Done waiting");
    }
    
    // Returns int
    public async Task<int> GetOrderCountAsync()
    {
        var count = await _database.CountOrdersAsync();
        return count;
    }
    
    // Task vs void return type
    public async void BadAsyncMethod()  // ❌ Don't do this
    {
        // If exception thrown, can't catch it
        // Caller can't await completion
        // Hard to test
        await Task.Delay(100);
    }
    
    public async Task GoodAsyncMethod()  // ✅ Always return Task
    {
        // Exceptions propagate properly
        // Caller can await completion
        // Easy to test
        await Task.Delay(100);
    }
}
```

### 1.3 Async All the Way

The cardinal rule: **async should propagate up the call stack**.

```csharp
// ❌ BAD: Blocking on async operation (deadlock risk)
public class OrderService
{
    private readonly IOrderRepository _repository;
    
    public OrderService(IOrderRepository repository)
    {
        _repository = repository;
    }
    
    public Order GetOrder(int id)
    {
        // BLOCKING: Waits for async operation synchronously
        var order = _repository.GetByIdAsync(id).Result;  // ❌ Can deadlock
        return order;
    }
    
    public Order GetOrderSync(int id)
    {
        // BLOCKING: Using sync over async
        var order = _repository.GetByIdAsync(id).GetAwaiter().GetResult();  // ❌ Bad
        return order;
    }
}

// ✅ GOOD: Async all the way
public class OrderService
{
    private readonly IOrderRepository _repository;
    
    public OrderService(IOrderRepository repository)
    {
        _repository = repository;
    }
    
    public async Task<Order> GetOrderAsync(int id)
    {
        // ASYNC: Returns control, allows other work
        var order = await _repository.GetByIdAsync(id);  // ✅ Correct
        return order;
    }
}

// API controller also async
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _service;
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(int id)
    {
        // ✅ Async propagates to ASP.NET framework
        var order = await _service.GetOrderAsync(id);
        return Ok(order);
    }
}
```

---

## 2. Task Composition and Coordination

### 2.1 Sequential Operations

```csharp
public class SequentialAsyncPatterns
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IInventoryService _inventory;
    
    // Sequential: Wait for one, then next
    public async Task<OrderDetails> PlaceOrderAsync(int customerId, List<OrderItem> items)
    {
        // 1. Get customer (wait for completion)
        var customer = await _customerRepository.GetByIdAsync(customerId);
        
        // 2. Create order (wait for completion)
        var order = Order.Create(customer, items);
        
        // 3. Check inventory (wait for completion)
        var available = await _inventory.CheckAvailabilityAsync(items);
        if (!available)
            throw new OutOfStockException();
        
        // 4. Reserve inventory (wait for completion)
        await _inventory.ReserveAsync(order.Id, items);
        
        // 5. Save order (wait for completion)
        await _orderRepository.SaveAsync(order);
        
        return new OrderDetails { OrderId = order.Id };
    }
    
    // Total time: ~1000ms (sum of all operations)
    // (100ms + 50ms + 75ms + 100ms + 200ms = 525ms minimum)
}
```

### 2.2 Parallel Operations

```csharp
public class ParallelAsyncPatterns
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IInventoryService _inventory;
    private readonly IEmailService _emailService;
    
    // Parallel: Operations that don't depend on each other
    public async Task<OrderDetails> PlaceOrderAsync(int customerId, List<OrderItem> items)
    {
        // Sequential: Must wait for customer
        var customer = await _customerRepository.GetByIdAsync(customerId);
        var order = Order.Create(customer, items);
        
        // Parallel: Check inventory and save order simultaneously
        var checkTask = _inventory.CheckAvailabilityAsync(items);
        var saveTask = _orderRepository.SaveAsync(order);
        
        // Wait for both to complete
        await Task.WhenAll(checkTask, saveTask);
        
        // After save, reserve inventory (depends on inventory check)
        if (!checkTask.Result)
            throw new OutOfStockException();
        
        await _inventory.ReserveAsync(order.Id, items);
        
        return new OrderDetails { OrderId = order.Id };
    }
    
    // Total time: ~300ms (longest operation, not sum)
}
```

### 2.3 Task.WhenAll vs Task.WhenAny

```csharp
public class TaskCoordinationPatterns
{
    private readonly IOrderRepository _orderRepository;
    private readonly IBackupOrderRepository _backup;
    private readonly IEmailService _emailService;
    
    // WhenAll: Wait for ALL operations to complete
    public async Task<List<Order>> LoadOrdersWithDetails(int customerId)
    {
        // Get orders AND customer details AND inventory in parallel
        var ordersTask = _orderRepository.GetByCustomerAsync(customerId);
        var detailsTask = _orderRepository.GetCustomerDetailsAsync(customerId);
        var inventoryTask = _orderRepository.GetInventoryAsync();
        
        // All three must complete successfully
        await Task.WhenAll(ordersTask, detailsTask, inventoryTask);
        
        var orders = ordersTask.Result;
        var details = detailsTask.Result;
        var inventory = inventoryTask.Result;
        
        return orders;
    }
    
    // WhenAny: Wait for FIRST operation to complete
    public async Task<Order> LoadOrderWithFallback(int orderId)
    {
        var primaryTask = _orderRepository.GetByIdAsync(orderId);
        var backupTask = _backup.GetByIdAsync(orderId);
        
        // Whichever completes first
        var completedTask = await Task.WhenAny(primaryTask, backupTask);
        
        if (completedTask == primaryTask)
        {
            return primaryTask.Result;
        }
        else
        {
            return backupTask.Result;
        }
    }
    
    // Send multiple notifications in parallel
    public async Task NotifyCustomersAsync(List<int> customerIds)
    {
        var emailTasks = customerIds.Select(id =>
            _emailService.SendNotificationAsync(id)
        );
        
        // All emails must be sent (or fail fast on first error)
        await Task.WhenAll(emailTasks);
    }
    
    // Send emails but don't wait for all to complete
    public async Task NotifyCustomersFireAndForget(List<int> customerIds)
    {
        var emailTasks = customerIds.Select(id =>
            _emailService.SendNotificationAsync(id)
        );
        
        // Don't await - fire and forget
        _ = Task.WhenAll(emailTasks);  // Still might want error handling
    }
}
```

---

## 3. Exception Handling in Async

### 3.1 Exception Propagation

```csharp
public class AsyncExceptionHandling
{
    private readonly IOrderRepository _repository;
    
    // Exception in async method propagates to awaiter
    public async Task<Order> GetOrderAsync(int id)
    {
        try
        {
            // Exception inside async method is wrapped in Task
            var order = await _repository.GetByIdAsync(id);
            return order;
        }
        catch (RepositoryException ex)
        {
            // Catch async exceptions just like synchronous
            Console.WriteLine($"Error: {ex.Message}");
            return null;
        }
    }
    
    // Multiple exception handling
    public async Task<Order> GetOrderWithMultipleSourcesAsync(int id)
    {
        try
        {
            var order = await _repository.GetByIdAsync(id);
            var details = await _repository.GetDetailsAsync(order.Id);
            return order;
        }
        catch (TimeoutException ex)
        {
            Console.WriteLine("Database timeout");
            throw;
        }
        catch (RepositoryException ex)
        {
            Console.WriteLine($"Repository error: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error: {ex.Message}");
            throw;
        }
    }
    
    // Exception in parallel tasks
    public async Task LoadOrdersInParallelAsync(List<int> orderIds)
    {
        var tasks = orderIds.Select(id =>
            _repository.GetByIdAsync(id)
        );
        
        try
        {
            // If ANY task throws, WhenAll throws AggregateException
            var results = await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            // Could be exception from any of the tasks
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
    
    // Handle multiple exceptions from parallel tasks
    public async Task HandleMultipleExceptionsAsync(List<int> orderIds)
    {
        var tasks = orderIds.Select(id =>
            _repository.GetByIdAsync(id)
        );
        
        try
        {
            var results = await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            if (ex is AggregateException agg)
            {
                // Multiple exceptions occurred
                foreach (var inner in agg.InnerExceptions)
                {
                    Console.WriteLine($"Error: {inner.Message}");
                }
            }
            else
            {
                // Single exception
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
```

### 3.2 Best Practices for Exception Handling

```csharp
public class ExceptionHandlingBestPractices
{
    // ✅ GOOD: Use try-catch in async methods
    public async Task<Order> GetOrderAsync(int id)
    {
        try
        {
            return await _repository.GetByIdAsync(id);
        }
        catch (RepositoryException ex)
        {
            throw new ApplicationException("Failed to get order", ex);
        }
    }
    
    // ❌ BAD: Using Result/GetAwaiter().GetResult() (can deadlock)
    public Order GetOrder(int id)
    {
        try
        {
            return _repository.GetByIdAsync(id).Result;  // ❌ Avoid
        }
        catch (AggregateException ex)
        {
            // Has to unwrap AggregateException
            throw ex.InnerException;
        }
    }
    
    // ❌ BAD: Async void without error handling
    public async void HandleOrderAsync(int id)  // ❌ Never use async void
    {
        // If exception thrown, no way to catch it
        var order = await _repository.GetByIdAsync(id);
    }
    
    // ✅ GOOD: Fire and forget with error handling
    public void HandleOrderFireAndForget(int id)
    {
        _ = HandleOrderAsync(id);  // Still problematic
        
        async Task HandleOrderAsync(int orderId)
        {
            try
            {
                var order = await _repository.GetByIdAsync(orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order");
            }
        }
    }
    
    // ✅ GOOD: Background task with error handling
    public async Task EnqueueOrderProcessingAsync(int orderId)
    {
        try
        {
            var order = await _repository.GetByIdAsync(orderId);
            await ProcessOrderAsync(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order {OrderId}", orderId);
            // Could retry, queue for dead-letter, etc.
        }
    }
}
```

---

## 4. Synchronization Context

### 4.1 Understanding SynchronizationContext

```csharp
public class SynchronizationContextExamples
{
    // SynchronizationContext: Determines where awaited code resumes
    
    // UI Context Example (WPF, WinForms)
    public class UIAsyncExample
    {
        // UI thread has specific SynchronizationContext
        public async void LoadDataButton_Click(object sender, EventArgs e)
        {
            // Code runs on UI thread
            var data = await FetchDataAsync();
            
            // Resume on UI thread (safe to update UI)
            TextBox.Text = data;  // ✅ Safe
        }
    }
    
    // ASP.NET Core: No SynchronizationContext
    public class WebAsyncExample
    {
        [HttpGet]
        public async Task<IActionResult> GetOrder(int id)
        {
            // No SynchronizationContext in ASP.NET Core
            var order = await _repository.GetByIdAsync(id);
            
            // Continue on thread pool (fine for web)
            return Ok(order);
        }
    }
}
```

### 4.2 ConfigureAwait Pattern

```csharp
public class ConfigureAwaitPatterns
{
    private readonly IOrderRepository _repository;
    
    // Default: Resume on same SynchronizationContext
    public async Task<Order> GetOrderDefault(int id)
    {
        var order = await _repository.GetByIdAsync(id);
        return order;  // Resumes on original context
    }
    
    // ConfigureAwait(false): Resume on any thread
    public async Task<Order> GetOrderOptimized(int id)
    {
        // Tells .NET: I don't care which thread resumes
        var order = await _repository.GetByIdAsync(id).ConfigureAwait(false);
        
        // Slightly more efficient (one less context switch)
        return order;
    }
    
    // When to use ConfigureAwait(false):
    // - Library code (doesn't have specific context needs)
    // - Async all the way down (no UI updates)
    // - ASP.NET Core (no context anyway)
    
    // When to NOT use ConfigureAwait(false):
    // - UI code that updates UI controls
    // - Code that needs specific context
    
    // Best practice: Use ConfigureAwait(false) in libraries
    public async Task<Order> LibraryMethod(int id)
    {
        var customer = await _customerRepository.GetAsync(id)
            .ConfigureAwait(false);
        
        var orders = await _orderRepository.GetByCustomerAsync(customer.Id)
            .ConfigureAwait(false);
        
        return orders.First();
    }
}
```

---

## 5. ValueTask Optimization

### 5.1 Understanding ValueTask

```csharp
public class ValueTaskExamples
{
    // Task: Always allocates heap memory
    // ValueTask: Only allocates if async
    
    // ❌ Unnecessary allocation
    public async Task<int> CountOrdersSlow()
    {
        return await _repository.CountAsync();  // Always Task
    }
    
    // ✅ Optimized: ValueTask only allocates if needed
    public async ValueTask<int> CountOrdersFast()
    {
        return await _repository.CountAsync();  // ValueTask
    }
    
    // ValueTask is valuable when:
    // - Synchronous path is common (already in cache)
    // - Method is called millions of times
    // - Library/core code
    
    // Example: IRepository returning ValueTask
    public interface ICachedOrderRepository
    {
        // If order in cache: synchronous (no allocation)
        // If order needs fetching: asynchronous (allocates)
        ValueTask<Order> GetByIdAsync(int id);
    }
    
    public class CachedOrderRepository : ICachedOrderRepository
    {
        private readonly Dictionary<int, Order> _cache = new();
        private readonly IOrderRepository _inner;
        
        public async ValueTask<Order> GetByIdAsync(int id)
        {
            if (_cache.TryGetValue(id, out var order))
            {
                // Synchronous path (no allocation)
                return order;
            }
            
            // Asynchronous path (allocates ValueTask)
            order = await _inner.GetByIdAsync(id);
            _cache[id] = order;
            return order;
        }
    }
    
    // Usage
    public async Task TestValueTask()
    {
        var repo = new CachedOrderRepository(null);
        
        // First call: async, allocates
        var order1 = await repo.GetByIdAsync(1);
        
        // Second call: sync from cache, NO allocation
        var order2 = await repo.GetByIdAsync(1);
    }
}
```

### 5.2 When to Use ValueTask

```csharp
public class ValueTaskGuidelines
{
    // Use ValueTask when:
    // 1. Synchronous completion is COMMON
    // 2. Method is performance-critical
    // 3. Library/infrastructure code
    
    // ✅ Good use case: Cache lookup
    public async ValueTask<CachedValue> GetFromCacheAsync(string key)
    {
        if (_cache.TryGetValue(key, out var value))
            return value;  // Sync path
        
        return await FetchAsync(key);  // Async path
    }
    
    // ✅ Good use case: Simple synchronous operation
    public async ValueTask<int> GetCountAsync()
    {
        return _items.Count;  // Always synchronous
    }
    
    // ❌ Don't use: When async is always needed
    public async ValueTask<Order> GetOrderAsync(int id)
    {
        return await _database.GetAsync(id);  // Almost always async
    }
    
    // ❌ Don't use: Fire-and-forget operations
    public async ValueTask ProcessAsync()
    {
        await Task.Delay(1000);  // Always async
    }
    
    // ❌ Don't use: Just to reduce allocations (not worth complexity)
    // Profile first! Premature optimization is evil
}
```

---

## 6. Common Pitfalls and Solutions

### 6.1 Deadlocks

```csharp
public class DeadlockExamples
{
    // DEADLOCK: Blocking on async call
    public class DeadlockScenario
    {
        public void LoadOrder(int id)
        {
            // Thread waits for task
            var order = GetOrderAsync(id).Result;  // ❌ Deadlock possible
        }
        
        public async Task<Order> GetOrderAsync(int id)
        {
            // Tries to resume on original thread
            var order = await _repository.GetByIdAsync(id);
            return order;
        }
    }
    
    // SOLUTION: Don't block on async
    public class DeadlockSolution
    {
        public async Task LoadOrder(int id)
        {
            var order = await GetOrderAsync(id);  // ✅ Correct
        }
        
        public async Task<Order> GetOrderAsync(int id)
        {
            return await _repository.GetByIdAsync(id);
        }
    }
    
    // SOLUTION: If you MUST block (rare edge cases)
    public class UnavoidableBlocking
    {
        public Order LoadOrderBlocking(int id)
        {
            // Explicitly handle potential deadlock
            try
            {
                return GetOrderAsync(id)
                    .ConfigureAwait(false)  // Avoid context switch
                    .GetAwaiter()
                    .GetResult();  // Blocks but avoids deadlock
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading order");
                throw;
            }
        }
    }
}
```

### 6.2 Forgotten Awaits

```csharp
public class ForgottenAwaitExamples
{
    private readonly IOrderRepository _repository;
    
    // ❌ BAD: Forgot await - returns unawaited Task
    public async Task<Order> GetOrderBad(int id)
    {
        return _repository.GetByIdAsync(id);  // ❌ Returns Task, not Order
    }
    
    // ✅ GOOD: Properly awaited
    public async Task<Order> GetOrderGood(int id)
    {
        return await _repository.GetByIdAsync(id);  // ✅ Awaits Task
    }
    
    // ❌ BAD: Calling but not awaiting fire-and-forget
    public async Task ProcessOrdersAsync(List<int> ids)
    {
        foreach (var id in ids)
        {
            ProcessOrderAsync(id);  // ❌ Not awaited, no error tracking
        }
    }
    
    // ✅ GOOD: Explicitly fire-and-forget with error handling
    public async Task ProcessOrdersAsync(List<int> ids)
    {
        foreach (var id in ids)
        {
            _ = ProcessOrderAsync(id);  // ✅ Explicitly ignored
        }
    }
    
    // ✅ BETTER: Actually await
    public async Task ProcessOrdersAsync(List<int> ids)
    {
        var tasks = ids.Select(id => ProcessOrderAsync(id));
        await Task.WhenAll(tasks);  // ✅ Wait for all
    }
    
    private async Task ProcessOrderAsync(int id)
    {
        try
        {
            var order = await _repository.GetByIdAsync(id);
            // ... process
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order {Id}", id);
        }
    }
}
```

### 6.3 Async IDisposable Pattern

```csharp
public class AsyncDisposableExamples
{
    // Resource that needs async cleanup
    public class AsyncResource : IAsyncDisposable
    {
        private DatabaseConnection _connection;
        
        public async ValueTask DisposeAsync()
        {
            if (_connection != null)
            {
                // Async cleanup (flush buffers, close connection)
                await _connection.FlushAsync();
                await _connection.CloseAsync();
                _connection = null;
            }
        }
    }
    
    // Using async disposable
    public async Task ProcessOrdersAsync()
    {
        await using var resource = new AsyncResource();
        
        // Use resource
        // Automatically disposed when exiting block
    }
    
    // Combining sync and async cleanup
    public class HybridResource : IAsyncDisposable, IDisposable
    {
        private Stream _stream;
        
        public void Dispose()
        {
            // Sync cleanup
            _stream?.Close();
        }
        
        public async ValueTask DisposeAsync()
        {
            // Async cleanup
            if (_stream != null)
            {
                await _stream.FlushAsync();
                _stream.Dispose();
            }
        }
    }
}
```

---

## 7. Async Patterns for Common Scenarios

### 7.1 Retry with Exponential Backoff

```csharp
public class RetryPatterns
{
    private readonly IOrderRepository _repository;
    private readonly ILogger<RetryPatterns> _logger;
    
    public async Task<Order> GetOrderWithRetryAsync(int id)
    {
        const int maxRetries = 3;
        TimeSpan delay = TimeSpan.FromMilliseconds(100);
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                return await _repository.GetByIdAsync(id);
            }
            catch (TransientException ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning($"Attempt {attempt + 1} failed, retrying in {delay.TotalMilliseconds}ms");
                
                await Task.Delay(delay);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);  // Exponential backoff
            }
        }
        
        throw new Exception("Max retries exceeded");
    }
}
```

### 7.2 Timeout Pattern

```csharp
public class TimeoutPatterns
{
    private readonly IOrderRepository _repository;
    
    public async Task<Order> GetOrderWithTimeoutAsync(int id)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        
        try
        {
            return await _repository.GetByIdAsync(id, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Order retrieval timed out");
        }
    }
}
```

---

## Summary

Async/await enables efficient resource utilization in enterprise systems:

1. **Task and Task<T>**: Represent asynchronous operations
2. **Async all the way**: Propagate async through the call stack
3. **Parallel composition**: Use WhenAll for independent operations
4. **Exception handling**: Works like synchronous code
5. **SynchronizationContext**: Matters for UI, not web
6. **ValueTask**: Optimization for performance-critical code
7. **Patterns**: Retry, timeout, cancellation

Proper async implementation is essential for building scalable enterprise systems that handle thousands of concurrent requests efficiently.

Next topic covers Concurrent Programming for multi-threaded scenarios.
