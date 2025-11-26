# 27. Structured Logging

## Overview
Structured logging captures logs in a machine-readable format with context, enabling powerful querying and analysis. In enterprise systems, comprehensive logging is essential for debugging, monitoring, and understanding user behavior.

---

## 1. Logging Fundamentals

### 1.1 Why Structured Logging

**Traditional Logging: Unstructured**
```
2024-01-15 10:23:45 ERROR Order processing failed for order 12345 with error: Timeout
2024-01-15 10:23:46 ERROR Customer 5678 experienced payment error: Invalid card
2024-01-15 10:23:47 INFO Order 12345 completed successfully
```

Problems:
- Logs are strings—hard to parse and filter
- No consistent format across application
- Context scattered across message
- Difficult to correlate related events

**Structured Logging: Machine-Readable**
```json
{
  "timestamp": "2024-01-15T10:23:45Z",
  "level": "Error",
  "message": "Order processing failed",
  "orderId": 12345,
  "customerId": 5678,
  "errorType": "TimeoutException",
  "duration_ms": 5000,
  "source": "OrderService"
}
```

Benefits:
- Each field searchable/filterable
- Consistent structure
- Context embedded in each log
- Easy to correlate and aggregate

### 1.2 Serilog Setup

```csharp
// Install: dotnet add package Serilog
// dotnet add package Serilog.AspNetCore
// dotnet add package Serilog.Sinks.Console
// dotnet add package Serilog.Sinks.File

public class Program
{
    public static void Main(string[] args)
    {
        // Configure Serilog BEFORE building host
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File(
                "logs/application-.txt",
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .Enrich.FromLogContext()  // Add context data
            .Enrich.WithProperty("Application", "Bookstore.Api")
            .CreateLogger();
        
        try
        {
            CreateHostBuilder(args).Build().Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
    
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()  // Use Serilog
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });
}
```

---

## 2. Structured Logging Patterns

### 2.1 Contextual Logging

```csharp
public class StructuredLoggingExamples
{
    private readonly ILogger<OrderService> _logger;
    
    // Structured properties in logs
    public async Task<Order> PlaceOrderAsync(int customerId, List<OrderItem> items)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            { "CustomerId", customerId },
            { "ItemCount", items.Count }
        }))
        {
            _logger.LogInformation(
                "Starting order placement for customer {CustomerId} with {ItemCount} items",
                customerId,
                items.Count
            );
            
            var order = Order.Create(customerId, items);
            
            _logger.LogInformation(
                "Order created with ID {OrderId}, total amount {OrderTotal:C}",
                order.Id,
                order.Total.Amount
            );
            
            try
            {
                await _repository.SaveAsync(order);
                
                _logger.LogInformation(
                    "Order {OrderId} successfully persisted to database",
                    order.Id
                );
            }
            catch (RepositoryException ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to save order {OrderId} for customer {CustomerId}. Error: {ErrorMessage}",
                    order.Id,
                    customerId,
                    ex.Message
                );
                throw;
            }
            
            return order;
        }
    }
}

// Output (structured):
/*
{
  "Timestamp": "2024-01-15T10:23:45.1234567Z",
  "Level": "Information",
  "MessageTemplate": "Starting order placement for customer {CustomerId} with {ItemCount} items",
  "Properties": {
    "CustomerId": 123,
    "ItemCount": 3,
    "SourceContext": "Bookstore.Application.Services.OrderService"
  }
}
*/
```

### 2.2 Log Levels and Severity

```csharp
public class LogLevelGuidelines
{
    private readonly ILogger<Example> _logger;
    
    public void LoggingExamples()
    {
        // DEBUG: Detailed diagnostic information for developers
        _logger.LogDebug(
            "Processing order {OrderId} with items {@Items}",
            orderId,
            items  // @ denotes destructuring
        );
        
        // INFORMATION: General flow, important business events
        _logger.LogInformation(
            "Order {OrderId} placed successfully by customer {CustomerId}",
            orderId,
            customerId
        );
        
        // WARNING: Potential issues that don't prevent operation
        _logger.LogWarning(
            "Order {OrderId} exceeds usual amount of {Amount}, manual review recommended",
            orderId,
            amount
        );
        
        // ERROR: Recoverable errors that prevented specific operation
        _logger.LogError(
            ex,
            "Failed to process payment for order {OrderId}, retrying",
            orderId
        );
        
        // CRITICAL: System in unrecoverable state
        _logger.LogCritical(
            "Database connection pool exhausted, cannot process new orders"
        );
    }
}
```

### 2.3 Destructuring and JSON Serialization

```csharp
public class DestructuringExample
{
    private readonly ILogger<OrderService> _logger;
    
    public async Task ProcessOrderAsync(Order order)
    {
        // Without destructuring: Complex object as string
        _logger.LogInformation("Processing order: {Order}", order);
        // Output: "Processing order: Bookstore.Domain.Order"
        
        // With destructuring: Object properties captured
        _logger.LogInformation("Processing order {@Order}", order);
        // Output:
        /*
        {
          "Order": {
            "Id": 123,
            "CustomerId": 456,
            "Total": {
              "Amount": 99.99,
              "Currency": "USD"
            },
            "Status": "Pending",
            "Items": [...]
          }
        }
        */
        
        // Named properties are best
        _logger.LogInformation(
            "Processing order {OrderId} for customer {CustomerId} with total {Amount}",
            order.Id,
            order.CustomerId,
            order.Total.Amount
        );
    }
}
```

---

## 3. Enterprise Logging Patterns

### 3.1 Correlation IDs for Distributed Tracing

```csharp
// Track requests through multiple services
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;
    
    public CorrelationIdMiddleware(RequestDelegate next, 
        ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        const string correlationIdHeader = "X-Correlation-ID";
        
        // Get or create correlation ID
        var correlationId = context.Request.Headers[correlationIdHeader]
            .FirstOrDefault() ?? Guid.NewGuid().ToString();
        
        // Store in context for later retrieval
        context.Items["CorrelationId"] = correlationId;
        
        // Add to response header
        context.Response.Headers.Add(correlationIdHeader, correlationId);
        
        // Add to log context
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            { "CorrelationId", correlationId },
            { "RequestPath", context.Request.Path },
            { "Method", context.Request.Method }
        }))
        {
            _logger.LogInformation(
                "Incoming request {Method} {Path}",
                context.Request.Method,
                context.Request.Path
            );
            
            await _next(context);
            
            _logger.LogInformation(
                "Request completed with status {StatusCode}",
                context.Response.StatusCode
            );
        }
    }
}

// Use correlation ID in services
public class OrderService
{
    private readonly ILogger<OrderService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    public async Task<int> PlaceOrderAsync(int customerId, List<OrderItem> items)
    {
        var correlationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString()
            ?? Guid.NewGuid().ToString();
        
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            { "CorrelationId", correlationId },
            { "CustomerId", customerId }
        }))
        {
            _logger.LogInformation("Processing order placement");
            
            var order = Order.Create(customerId, items);
            await _repository.SaveAsync(order);
            
            // All logs within this scope include CorrelationId and CustomerId
            _logger.LogInformation("Order {OrderId} created", order.Id);
            
            return order.Id;
        }
    }
}

// All logs related to same request have same CorrelationId
/*
{
  "Timestamp": "2024-01-15T10:23:45.1234567Z",
  "CorrelationId": "abc-123-def-456",
  "CustomerId": 789,
  "Level": "Information",
  "Message": "Processing order placement"
}
*/
```

### 3.2 Enrichment Filters

```csharp
public class EnrichmentExample
{
    public static LoggerConfiguration ConfigureEnrichment()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            
            // Add application-wide properties
            .Enrich.WithProperty("Application", "Bookstore.Api")
            .Enrich.WithProperty("Environment", GetEnvironment())
            .Enrich.WithProperty("Version", GetVersion())
            
            // Add machine/deployment info
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentUserName()
            .Enrich.WithProcessId()
            
            // Add timing info
            .Enrich.WithElapsedMilliseconds()
            
            // Custom enricher for user context
            .Enrich.With(new UserContextEnricher())
            
            // Enrich specific to operation
            .Enrich.When(le => le.Level == LogEventLevel.Error,
                e => e.WithProperty("AlertSeverity", "High"))
            
            .WriteTo.Console()
            .WriteTo.File("logs/app-.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }
    
    // Custom enricher
    public class UserContextEnricher : ILogEventEnricher
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        
        public UserContextEnricher(IHttpContextAccessor httpContextAccessor = null)
        {
            _httpContextAccessor = httpContextAccessor;
        }
        
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var userId = _httpContextAccessor?.HttpContext?.User?.FindFirst("sub")?.Value;
            
            if (!string.IsNullOrEmpty(userId))
            {
                var property = propertyFactory.CreateProperty("UserId", userId);
                logEvent.AddPropertyIfAbsent(property);
            }
        }
    }
    
    private static string GetEnvironment() =>
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown";
    
    private static string GetVersion() =>
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "Unknown";
}
```

### 3.3 Conditional Logging and Performance

```csharp
public class PerformanceLoggingPatterns
{
    private readonly ILogger<OrderService> _logger;
    
    // Expensive logging - check level first
    public async Task<Order> GetOrderAsync(int id)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var order = await _repository.GetByIdAsync(id);
            stopwatch.Stop();
            
            // Only deserialize complex object if debug enabled
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Retrieved order {@Order} in {ElapsedMs}ms",
                    order,
                    stopwatch.ElapsedMilliseconds
                );
            }
            else
            {
                _logger.LogInformation(
                    "Retrieved order {OrderId} in {ElapsedMs}ms",
                    order.Id,
                    stopwatch.ElapsedMilliseconds
                );
            }
            
            return order;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Failed to retrieve order {OrderId} after {ElapsedMs}ms",
                id,
                stopwatch.ElapsedMilliseconds
            );
            throw;
        }
    }
    
    // Performance timing decorator
    public async Task<T> LogExecutionTimeAsync<T>(
        Func<Task<T>> operation,
        string operationName)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var result = await operation();
            stopwatch.Stop();
            
            if (stopwatch.ElapsedMilliseconds > 1000)
            {
                _logger.LogWarning(
                    "{Operation} took {Duration}ms (slow)",
                    operationName,
                    stopwatch.ElapsedMilliseconds
                );
            }
            else
            {
                _logger.LogInformation(
                    "{Operation} completed in {Duration}ms",
                    operationName,
                    stopwatch.ElapsedMilliseconds
                );
            }
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "{Operation} failed after {Duration}ms",
                operationName,
                stopwatch.ElapsedMilliseconds
            );
            throw;
        }
    }
}
```

---

## 4. Log Sinks and Outputs

### 4.1 Multiple Sinks

```csharp
public class MultiSinkConfiguration
{
    public static LoggerConfiguration Configure()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            
            // Console: Real-time monitoring
            .WriteTo.Console(
                theme: AnsiConsoleTheme.Code,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            
            // File: Daily rolling
            .WriteTo.File(
                "logs/application-.txt",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,  // Keep 30 days
                fileSizeLimitBytes: 104857600  // 100MB per file
            )
            
            // Error file: Only errors
            .WriteTo.File(
                "logs/errors-.txt",
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: LogEventLevel.Error
            )
            
            // Async file sink: Better performance
            .WriteTo.Async(a => a.File(
                "logs/async-.txt",
                rollingInterval: RollingInterval.Day
            ))
            
            // Sequence: JSON format for structured analysis
            .WriteTo.Seq("http://localhost:5341")  // Seq server
            
            // Application Insights
            .WriteTo.ApplicationInsights(
                telemetryClient,
                TelemetryConverter.Traces
            )
            
            // Conditional sink based on level
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(le => le.Level >= LogEventLevel.Error)
                .WriteTo.Email(
                    fromEmail: "alerts@example.com",
                    toEmail: "admin@example.com",
                    mailServer: "smtp.example.com",
                    subjectLineFormatter: le =>
                        $"[{le.Level}] {le.MessageTemplate.Text}"
                ))
            
            .Enrich.FromLogContext()
            .CreateLogger();
    }
}
```

### 4.2 File Rotation and Retention

```csharp
public class FileRotationConfiguration
{
    public static void ConfigureFileLogging(LoggerConfiguration config)
    {
        config.WriteTo.File(
            "logs/app-.txt",
            
            // Rotation strategy
            rollingInterval: RollingInterval.Day,  // Daily rotation
            // RollingInterval.Hour, RollingInterval.Month, etc.
            
            // File naming pattern
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
            
            // File size limits
            fileSizeLimitBytes: 104857600,  // 100MB
            rollOnFileSizeLimit: true,      // New file when size exceeded
            
            // Retention
            retainedFileCountLimit: 30,     // Keep 30 files
            retainedDateTimeFormat: "yyyyMMdd",  // Filename format
            
            // Performance
            buffered: true,
            flushToDiskInterval: TimeSpan.FromSeconds(1),
            
            // Encoding
            encoding: Encoding.UTF8,
            
            // Shared access
            shared: true  // Allow multiple processes to write
        );
    }
}
```

---

## 5. Testing Logging

### 5.1 Unit Testing Logging

```csharp
public class LoggingTestExample
{
    [Fact]
    public async Task PlaceOrder_LogsOrderCreation()
    {
        // Arrange
        var logger = new Mock<ILogger<OrderService>>();
        var repository = new Mock<IOrderRepository>();
        
        var service = new OrderService(logger.Object, repository.Object);
        
        // Act
        await service.PlaceOrderAsync(1, new List<OrderItem>
        {
            new OrderItem { BookId = 1, Quantity = 2 }
        });
        
        // Assert: Verify logging occurred
        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString().Contains("Order") &&
                    v.ToString().Contains("placed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once
        );
    }
    
    [Fact]
    public async Task PlaceOrder_LogsError_WhenRepositoryFails()
    {
        // Arrange
        var logger = new Mock<ILogger<OrderService>>();
        var repository = new Mock<IOrderRepository>();
        
        repository.Setup(r => r.SaveAsync(It.IsAny<Order>()))
            .ThrowsAsync(new RepositoryException("Database error"));
        
        var service = new OrderService(logger.Object, repository.Object);
        
        // Act & Assert
        await Assert.ThrowsAsync<RepositoryException>(() =>
            service.PlaceOrderAsync(1, new List<OrderItem>())
        );
        
        // Verify error was logged
        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString().Contains("Failed")),
                It.IsAny<RepositoryException>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once
        );
    }
}
```

### 5.2 Integration Testing with Serilog

```csharp
public class LoggingIntegrationTest : IAsyncLifetime
{
    private readonly List<LogEvent> _capturedLogs;
    private readonly TestSink _testSink;
    
    public LoggingIntegrationTest()
    {
        _capturedLogs = new List<LogEvent>();
        _testSink = new TestSink();
    }
    
    public async Task InitializeAsync()
    {
        // Create logger with test sink
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Sink(_testSink)
            .CreateLogger();
    }
    
    public async Task DisposeAsync()
    {
        Log.CloseAndFlush();
    }
    
    [Fact]
    public async Task OrderProcessing_LogsAllSteps()
    {
        // Arrange
        var service = new OrderService();
        
        // Act
        await service.PlaceOrderAsync(123, new List<OrderItem>
        {
            new OrderItem { BookId = 1, Quantity = 2 }
        });
        
        // Assert
        var logs = _testSink.Events.ToList();
        
        logs.Should().Contain(e =>
            e.MessageTemplate.Text.Contains("order")
        );
        
        logs.Should().Contain(e =>
            e.Properties.ContainsKey("OrderId")
        );
    }
    
    // Test sink for capturing logs
    public class TestSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        
        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }
}
```

---

## 6. Best Practices

| Practice | Example |
|----------|---------|
| **Use structured properties** | `_logger.LogInformation("Order {OrderId}", id)` |
| **Include context** | Use BeginScope for correlation IDs |
| **Appropriate levels** | Info for business events, Debug for diagnostics |
| **Avoid logging sensitive data** | Don't log passwords, PII, card numbers |
| **Use correlation IDs** | Track requests through services |
| **Monitor log volume** | High volume impacts performance |
| **Retention policy** | Delete old logs to manage storage |
| **Centralize logs** | Use Seq, Elastic, Application Insights |

---

## 7. Common Pitfalls

| Pitfall | Problem | Solution |
|---------|---------|----------|
| **Unstructured messages** | Can't search/filter | Use named properties |
| **Logging in loops** | Performance issue | Batch log or use sampling |
| **Synchronous file I/O** | Blocks threads | Use async sinks |
| **Too many logs** | Storage and cost | Appropriate log levels |
| **Missing context** | Hard to correlate | Use correlation IDs |
| **Logging sensitive data** | Security risk | Sanitize or redact |

---

## Summary

Structured logging is critical for enterprise systems:

1. **Structured Properties**: Machine-readable logs with context
2. **Log Levels**: Debug, Info, Warning, Error, Critical
3. **Correlation IDs**: Track requests through services
4. **Enrichment**: Add context to all logs
5. **Multiple Sinks**: File, Console, Cloud services
6. **Sampling**: Manage log volume at scale
7. **Centralization**: Aggregate logs for analysis

Serilog provides powerful, extensible structured logging for ASP.NET Core applications.

Next topic covers Application Insights for monitoring and alerting.
