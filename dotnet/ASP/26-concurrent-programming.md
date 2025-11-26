# 26. Concurrent Programming

## Overview
Concurrent programming involves coordinating multiple threads or tasks safely. While async/await handles I/O concurrency, explicit concurrent programming is necessary when dealing with shared state, background tasks, and parallel computation in enterprise systems.

---

## 1. Threading Fundamentals

### 1.1 Threads vs Tasks

```csharp
public class ThreadsVsTasks
{
    // Thread: OS-level construct, expensive to create
    public void ExplicitThreadExample()
    {
        var thread = new Thread(() =>
        {
            Console.WriteLine("Running on explicit thread");
            Thread.Sleep(1000);
        });
        
        thread.Start();
        thread.Join();  // Wait for completion
    }
    
    // Task: High-level abstraction over thread pool
    public async Task TaskExample()
    {
        await Task.Run(() =>
        {
            Console.WriteLine("Running on thread pool");
            Thread.Sleep(1000);
        });
    }
    
    // In enterprise code:
    // - Use Task/async for I/O-bound work
    // - Use Task.Run only when CPU-bound work needed
    // - Rarely create Thread explicitly
}
```

### 1.2 Thread Safety Basics

```csharp
public class ThreadSafetyExamples
{
    // ❌ UNSAFE: Multiple threads accessing shared variable
    public class UnsafeCounter
    {
        private int _count = 0;
        
        public void Increment()
        {
            _count++;  // NOT thread-safe!
            // Operation: read → add 1 → write
            // Between steps, another thread could read stale value
        }
        
        public void RunUnsafe()
        {
            var tasks = Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() =>
                {
                    for (int i = 0; i < 1000; i++)
                        Increment();
                }))
                .ToArray();
            
            Task.WaitAll(tasks);
            
            // Expected: 10,000
            // Actual: Usually less due to race conditions
            Console.WriteLine($"Count: {_count}");
        }
    }
    
    // ✅ SAFE: Synchronized access
    public class SafeCounterWithLock
    {
        private int _count = 0;
        private readonly object _lockObject = new();
        
        public void Increment()
        {
            lock (_lockObject)
            {
                // Only one thread at a time
                _count++;
            }
        }
        
        public void RunSafe()
        {
            var tasks = Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() =>
                {
                    for (int i = 0; i < 1000; i++)
                        Increment();
                }))
                .ToArray();
            
            Task.WaitAll(tasks);
            
            // Result: Always 10,000
            Console.WriteLine($"Count: {_count}");
        }
    }
    
    // ✅ BETTER: Immutable design (no synchronization needed)
    public class CounterImmutable
    {
        private int _count = 0;
        
        // Create new instance instead of modifying
        public int Increment() => _count + 1;
        
        // Thread-safe by design
    }
}
```

---

## 2. Synchronization Primitives

### 2.1 Lock and Monitor

```csharp
public class LockExamples
{
    public class BankAccount
    {
        private decimal _balance;
        private readonly object _syncLock = new();
        
        public BankAccount(decimal initialBalance)
        {
            _balance = initialBalance;
        }
        
        // Critical section protected by lock
        public void Deposit(decimal amount)
        {
            lock (_syncLock)
            {
                var newBalance = _balance + amount;
                Thread.Sleep(10);  // Simulate work
                _balance = newBalance;
            }
        }
        
        public void Withdraw(decimal amount)
        {
            lock (_syncLock)
            {
                if (_balance >= amount)
                {
                    _balance -= amount;
                }
            }
        }
        
        // Deadlock risk: Don't take locks in different order
        public void Transfer(BankAccount other, decimal amount)
        {
            lock (_syncLock)
            {
                lock (other._syncLock)  // ❌ Deadlock risk if other also transfers to us
                {
                    Withdraw(amount);
                    other.Deposit(amount);
                }
            }
        }
        
        public decimal GetBalance()
        {
            lock (_syncLock)
            {
                return _balance;
            }
        }
    }
}
```

### 2.2 ReaderWriterLockSlim

When reads vastly outnumber writes:

```csharp
public class ReaderWriterLockExample
{
    public class CachedOrderData
    {
        private Dictionary<int, Order> _cache = new();
        private readonly ReaderWriterLockSlim _lock = new();
        
        // Multiple readers simultaneously
        public Order GetOrder(int id)
        {
            _lock.EnterReadLock();
            try
            {
                if (_cache.TryGetValue(id, out var order))
                    return order;
                
                return null;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
        
        // Single writer (exclusive)
        public void UpdateOrder(int id, Order order)
        {
            _lock.EnterWriteLock();
            try
            {
                _cache[id] = order;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        
        // Upgrade from read to write (risky)
        public void IncrementHitCount(int id)
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                if (_cache.TryGetValue(id, out var order))
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        order.HitCount++;  // Write
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                }
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }
        }
    }
}
```

### 2.3 Semaphore and SemaphoreSlim

Control resource access:

```csharp
public class SemaphoreExamples
{
    // Limit concurrent database connections
    public class ConnectionPoolManager
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly Queue<SqlConnection> _connectionPool;
        
        public ConnectionPoolManager(int maxConnections)
        {
            _semaphore = new SemaphoreSlim(maxConnections);
            _connectionPool = new Queue<SqlConnection>(maxConnections);
        }
        
        public async Task<SqlConnection> AcquireConnectionAsync()
        {
            // Wait if pool exhausted
            await _semaphore.WaitAsync();
            
            lock (_connectionPool)
            {
                if (_connectionPool.TryDequeue(out var conn))
                    return conn;
            }
            
            // Create new connection if needed
            return new SqlConnection("...");
        }
        
        public void ReleaseConnection(SqlConnection conn)
        {
            lock (_connectionPool)
            {
                _connectionPool.Enqueue(conn);
            }
            
            // Signal waiting threads
            _semaphore.Release();
        }
    }
    
    // Usage
    public async Task UseConnectionAsync()
    {
        var pool = new ConnectionPoolManager(maxConnections: 10);
        
        var connection = await pool.AcquireConnectionAsync();
        try
        {
            // Use connection
        }
        finally
        {
            pool.ReleaseConnection(connection);
        }
    }
}
```

### 2.4 Mutex and EventWaitHandle

For inter-process synchronization:

```csharp
public class MutexExamples
{
    // Single instance across entire system
    public class SingleInstanceApplication
    {
        private Mutex _instanceMutex;
        
        public bool TryStartApplication()
        {
            string appName = "MyApplication";
            _instanceMutex = new Mutex(false, appName);
            
            // Try to acquire mutex
            try
            {
                // If acquired, this is first instance
                return _instanceMutex.WaitOne(TimeSpan.Zero, false);
            }
            catch (AbandonedMutexException)
            {
                // Previous instance crashed
                return true;
            }
        }
        
        public void ShutdownApplication()
        {
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
        }
    }
    
    // Event signaling
    public class EventWaitHandleExample
    {
        private ManualResetEvent _signal = new(false);
        
        public void WaitForSignal()
        {
            Console.WriteLine("Waiting for signal...");
            _signal.WaitOne();  // Blocks until signaled
            Console.WriteLine("Signal received!");
        }
        
        public void SendSignal()
        {
            Console.WriteLine("Sending signal");
            _signal.Set();  // Wake up all waiting threads
        }
    }
}
```

---

## 3. Thread-Safe Collections

### 3.1 Concurrent Collections

```csharp
public class ConcurrentCollectionsExample
{
    public class OrderProcessor
    {
        // Thread-safe queue
        private readonly ConcurrentQueue<Order> _orderQueue;
        
        // Thread-safe dictionary
        private readonly ConcurrentDictionary<int, OrderStatus> _orderStatus;
        
        // Thread-safe bag (unordered collection)
        private readonly ConcurrentBag<Log> _logs;
        
        public OrderProcessor()
        {
            _orderQueue = new();
            _orderStatus = new();
            _logs = new();
        }
        
        public void EnqueueOrder(Order order)
        {
            // No locking needed
            _orderQueue.Enqueue(order);
        }
        
        public bool TryGetOrder(out Order order)
        {
            return _orderQueue.TryDequeue(out order);
        }
        
        public void UpdateOrderStatus(int orderId, OrderStatus status)
        {
            // Atomic add or update
            _orderStatus.AddOrUpdate(orderId, status, (key, old) => status);
        }
        
        public void LogOperation(Log log)
        {
            _logs.Add(log);
        }
        
        // Processing multiple orders in parallel
        public async Task ProcessAllOrdersAsync()
        {
            var processingTasks = new List<Task>();
            
            while (_orderQueue.TryDequeue(out var order))
            {
                // Process multiple orders in parallel
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessOrderAsync(order);
                        UpdateOrderStatus(order.Id, OrderStatus.Completed);
                    }
                    catch (Exception ex)
                    {
                        UpdateOrderStatus(order.Id, OrderStatus.Failed);
                        _logs.Add(new Log { Message = ex.Message });
                    }
                });
                
                processingTasks.Add(task);
            }
            
            await Task.WhenAll(processingTasks);
        }
        
        private async Task ProcessOrderAsync(Order order)
        {
            // Simulate work
            await Task.Delay(100);
        }
    }
}

public class OrderStatus { }
public class Order { public int Id { get; set; } }
public class Log { public string Message { get; set; } }
```

### 3.2 Producer-Consumer Pattern

```csharp
public class ProducerConsumerPattern
{
    public class OrderProcessingSystem
    {
        private readonly BlockingCollection<Order> _orderQueue;
        private readonly CancellationTokenSource _cts;
        
        public OrderProcessingSystem()
        {
            _orderQueue = new BlockingCollection<Order>(boundedCapacity: 100);
            _cts = new CancellationTokenSource();
        }
        
        // Producer: Add orders to queue
        public void ProduceOrders()
        {
            var producerTask = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        var order = new Order { Id = i };
                        
                        // Blocks if queue full (backpressure)
                        _orderQueue.Add(order, _cts.Token);
                        
                        Console.WriteLine($"Produced order {i}");
                        Thread.Sleep(100);
                    }
                }
                finally
                {
                    _orderQueue.CompleteAdding();  // Signal no more items
                }
            });
        }
        
        // Consumer: Process orders from queue
        public void ConsumeOrders()
        {
            var consumerTask = Task.Run(() =>
            {
                try
                {
                    // Iterate until CompleteAdding() and queue empty
                    foreach (var order in _orderQueue.GetConsumingEnumerable(_cts.Token))
                    {
                        Console.WriteLine($"Processing order {order.Id}");
                        ProcessOrder(order);
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Consumer cancelled");
                }
            });
        }
        
        private void ProcessOrder(Order order)
        {
            Thread.Sleep(50);  // Simulate work
        }
        
        public void Shutdown()
        {
            _cts.Cancel();
        }
    }
}
```

---

## 4. Parallel Processing

### 4.1 Parallel.For

```csharp
public class ParallelForExample
{
    public class BulkOrderProcessor
    {
        private readonly IOrderRepository _repository;
        
        // Sequential: Slow for large datasets
        public void ProcessOrdersSequential(List<int> orderIds)
        {
            foreach (var id in orderIds)
            {
                var order = _repository.GetById(id);
                ApplyDiscount(order);
                _repository.Save(order);
            }
        }
        
        // Parallel: Use multiple cores
        public void ProcessOrdersParallel(List<int> orderIds)
        {
            Parallel.For(0, orderIds.Count, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, i =>
            {
                var id = orderIds[i];
                var order = _repository.GetById(id);
                ApplyDiscount(order);
                _repository.Save(order);
            });
        }
        
        // Parallel with partitioning (better performance)
        public void ProcessOrdersParallelPartitioned(List<int> orderIds)
        {
            var partitioner = Partitioner.Create(orderIds, loadBalance: true);
            
            Parallel.ForEach(partitioner, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, id =>
            {
                var order = _repository.GetById(id);
                ApplyDiscount(order);
                _repository.Save(order);
            });
        }
        
        // Parallel with break/stop
        public void ProcessOrdersWithBreak(List<int> orderIds)
        {
            Parallel.ForEach(orderIds, (id, loopState) =>
            {
                var order = _repository.GetById(id);
                
                if (order.Amount > 10000)
                {
                    loopState.Stop();  // Stop processing
                    return;
                }
                
                ApplyDiscount(order);
                _repository.Save(order);
            });
        }
        
        private void ApplyDiscount(Order order) { }
    }
}
```

### 4.2 Parallel.ForEach with PLINQ

```csharp
public class PLinqExample
{
    public class DataAnalysis
    {
        public void AnalyzeSequential(List<Order> orders)
        {
            // Single-threaded
            var results = orders
                .Where(o => o.Total > 100)
                .Select(o => new { o.Id, o.Total })
                .ToList();
        }
        
        public void AnalyzeParallel(List<Order> orders)
        {
            // Multi-threaded
            var results = orders
                .AsParallel()
                .Where(o => o.Total > 100)
                .Select(o => new { o.Id, o.Total })
                .ToList();
        }
        
        public void AnalyzeParallelOptimized(List<Order> orders)
        {
            var results = orders
                .AsParallel()
                .WithDegreeOfParallelism(Environment.ProcessorCount)
                .Where(o => o.Total > 100)
                .Select(o => new { o.Id, o.Total })
                .AsSequential()  // Back to sequential for final enumeration
                .ToList();
        }
        
        // Careful: PLINQ has overhead, only use for CPU-intensive operations
        // Not suitable for I/O-bound operations (use async instead)
    }
}
```

---

## 5. Cancellation and Coordination

### 5.1 CancellationToken

```csharp
public class CancellationTokenExample
{
    public class LongRunningOperation
    {
        public async Task ProcessOrdersAsync(
            List<int> orderIds, 
            CancellationToken cancellationToken)
        {
            try
            {
                for (int i = 0; i < orderIds.Count; i++)
                {
                    // Check if cancellation requested
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var id = orderIds[i];
                    var order = await GetOrderAsync(id);
                    await ProcessOrderAsync(order);
                    
                    // Report progress
                    Console.WriteLine($"Processed {i + 1}/{orderIds.Count}");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Operation cancelled");
                throw;
            }
        }
        
        public async Task<Order> GetOrderAsync(int id)
        {
            return await Task.FromResult(new Order { Id = id });
        }
        
        public async Task ProcessOrderAsync(Order order)
        {
            await Task.Delay(100);
        }
    }
    
    // Usage
    public async Task CancellingLongOperationAsync()
    {
        var cts = new CancellationTokenSource();
        var operation = new LongRunningOperation();
        
        var processingTask = operation.ProcessOrdersAsync(
            Enumerable.Range(1, 1000).ToList(),
            cts.Token
        );
        
        // Cancel after 2 seconds
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        
        try
        {
            await processingTask;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Processing was cancelled");
        }
    }
    
    // Timeout pattern
    public async Task TimeoutPatternAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var operation = new LongRunningOperation();
        
        try
        {
            await operation.ProcessOrdersAsync(
                Enumerable.Range(1, 1000).ToList(),
                cts.Token
            );
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Operation timed out");
        }
    }
}
```

### 5.2 Combining Multiple Cancellation Tokens

```csharp
public class CombinedCancellationExample
{
    public async Task ProcessWithMultipleCancellationSourcesAsync()
    {
        // Combine application shutdown token with explicit cancellation
        var userCts = new CancellationTokenSource();
        var shutdownToken = _hostApplicationLifetime.ApplicationStopping;
        
        using var combinedCts = CancellationTokenSource
            .CreateLinkedTokenSource(userCts.Token, shutdownToken);
        
        try
        {
            await ProcessAsync(combinedCts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Processing cancelled");
        }
    }
    
    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        // Processing stops if either source is cancelled
        await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
    }
}
```

---

## 6. Common Concurrency Patterns

### 6.1 Double-Checked Locking

```csharp
public class LazyInitializationPattern
{
    public class DatabaseConnection
    {
        private object _connectionLock = new();
        private Connection _connection;
        
        // ❌ BAD: Always locks even after initialized
        public Connection GetConnectionSlow()
        {
            lock (_connectionLock)
            {
                if (_connection == null)
                {
                    _connection = new Connection();
                }
                return _connection;
            }
        }
        
        // ✅ GOOD: Double-checked locking
        public Connection GetConnectionFast()
        {
            if (_connection == null)  // First check (no lock)
            {
                lock (_connectionLock)
                {
                    if (_connection == null)  // Second check (with lock)
                    {
                        _connection = new Connection();
                    }
                }
            }
            return _connection;
        }
        
        // ✅ BEST: Use Lazy<T>
        private readonly Lazy<Connection> _lazyConnection =
            new(() => new Connection());
        
        public Connection GetConnectionBest => _lazyConnection.Value;
    }
}
```

### 6.2 Thread Pool Management

```csharp
public class ThreadPoolManagement
{
    public void ConfigureThreadPool()
    {
        // Get current thread pool settings
        ThreadPool.GetMinThreads(out var minWorkerThreads, out var minIoThreads);
        ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxIoThreads);
        
        Console.WriteLine($"Min worker threads: {minWorkerThreads}");
        Console.WriteLine($"Max worker threads: {maxWorkerThreads}");
        
        // Set minimum threads (caution!)
        ThreadPool.GetAvailableThreads(out var availWorker, out var availIo);
        
        // Rarely need to modify - .NET handles this well
    }
    
    // Monitor queue length
    public void MonitorThreadPool()
    {
        var timer = new System.Timers.Timer(1000);
        timer.Elapsed += (s, e) =>
        {
            ThreadPool.GetAvailableThreads(out var workerThreads, out var ioThreads);
            ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);
            
            var queueLength = maxWorker - workerThreads;
            Console.WriteLine($"Thread pool queue length: {queueLength}");
        };
        
        timer.Start();
    }
}
```

---

## 7. Avoiding Concurrency Pitfalls

### 7.1 Deadlock Prevention

```csharp
public class DeadlockPrevention
{
    // ❌ DEADLOCK: Different acquisition order
    public class Account
    {
        private decimal _balance;
        private readonly object _lock = new();
        
        public void TransferTo(Account target, decimal amount)
        {
            lock (this._lock)
            {
                lock (target._lock)  // Deadlock if both transfer to each other
                {
                    _balance -= amount;
                    target._balance += amount;
                }
            }
        }
    }
    
    // ✅ SAFE: Consistent acquisition order
    public class SafeAccount
    {
        private decimal _balance;
        private readonly int _id;  // Use ID for ordering
        private readonly object _lock = new();
        
        public SafeAccount(int id)
        {
            _id = id;
        }
        
        public void TransferTo(SafeAccount target, decimal amount)
        {
            // Always acquire in same order (by ID)
            var first = _id < target._id ? this : target;
            var second = _id < target._id ? target : this;
            
            lock (first._lock)
            {
                lock (second._lock)
                {
                    _balance -= amount;
                    target._balance += amount;
                }
            }
        }
    }
}
```

### 7.2 Race Conditions

```csharp
public class RaceConditionPrevention
{
    // ❌ RACE CONDITION: Check-then-act pattern
    public class UnsafeInventory
    {
        private int _stock = 100;
        
        public bool TryPurchase(int quantity)
        {
            if (_stock >= quantity)  // Check
            {
                Thread.Sleep(10);    // Simulate work
                _stock -= quantity;  // Act
                return true;
            }
            return false;
        }
        
        // Two threads can both pass the check!
    }
    
    // ✅ SAFE: Atomic check-and-act
    public class SafeInventory
    {
        private int _stock = 100;
        private readonly object _lock = new();
        
        public bool TryPurchase(int quantity)
        {
            lock (_lock)
            {
                if (_stock >= quantity)
                {
                    Thread.Sleep(10);  // Still safe - holding lock
                    _stock -= quantity;
                    return true;
                }
                return false;
            }
        }
    }
}
```

---

## Summary

Concurrent programming enables safe multi-threaded access:

1. **Synchronization Primitives**: Lock, Monitor, ReaderWriterLock, Semaphore
2. **Thread-Safe Collections**: ConcurrentQueue, ConcurrentDictionary, BlockingCollection
3. **Parallel Processing**: Parallel.For, Parallel.ForEach, PLINQ
4. **Cancellation**: CancellationToken for graceful shutdown
5. **Patterns**: Producer-consumer, lazy initialization, double-checked locking
6. **Pitfalls**: Deadlock, race conditions, priority inversion

**Key principles:**
- Prefer async/await for I/O
- Use concurrent collections
- Keep critical sections small
- Establish ordering for locks
- Test with multiple threads
- Monitor and profile

With async/await (Topic 25) and concurrent programming (Topic 26), you have the tools for building highly scalable, responsive enterprise systems.

Next topics cover Logging & Monitoring (observability) and Background Services.
