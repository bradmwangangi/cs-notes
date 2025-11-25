# Chapter 12: Logging, Monitoring & Observability

## 12.1 Structured Logging

Structured logging captures events as machine-readable data, not just text strings.

### Traditional Logging

```csharp
// Bad: Unstructured message, hard to query
logger.LogInformation("User 123 logged in from 192.168.1.1 at 2023-12-15 10:30:00");

// Difficult to search: How many logins from IP 192.168.1.1?
// Difficult to parse: Extracting IP address requires regex
// Difficult to alert: Can't easily create alerts on failed logins
```

### Structured Logging

```csharp
// Good: Structured data, easy to query and analyze
logger.LogInformation(
    "User {UserId} logged in from {IpAddress}",
    userId,
    ipAddress);

// Properties:
// - UserId: "123"
// - IpAddress: "192.168.1.1"
// - Timestamp: "2023-12-15T10:30:00Z"
// - Level: "Information"

// Now you can:
// - Query: logs | where UserId == "123"
// - Alert: logs | where LoginFailed == true | count > 5
// - Aggregate: logs | summarize count by IpAddress
```

### Serilog Configuration

```csharp
// Program.cs
using Serilog;

var logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/app-.txt", rollingInterval: RollingInterval.Day)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "MyApi")
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(logger);
```

**Enrichers** add context to all logs:

```csharp
.Enrich.FromLogContext()           // From LogContext
.Enrich.WithProperty("Version", "1.0")
.Enrich.WithMachineName()
.Enrich.WithEnvironmentUserName()
.Enrich.WithThreadId()
```

**Sinks** define where logs go:

```csharp
.WriteTo.Console()                 // Console output
.WriteTo.File("logs/app-.txt")     // File (rolling daily)
.WriteTo.Seq("http://localhost:5341")  // Log aggregator
.WriteTo.ApplicationInsights(new TelemetryConfiguration(...))
```

### Structured Logging in Code

```csharp
[HttpPost("login")]
public async Task<IActionResult> Login(LoginRequest request)
{
    _logger.LogInformation(
        "Login attempt for user {Email}",
        request.Email);
    
    var user = await _userService.GetUserByEmailAsync(request.Email);
    
    if (user == null)
    {
        _logger.LogWarning(
            "Login failed: user not found {Email} from {IpAddress}",
            request.Email,
            HttpContext.Connection.RemoteIpAddress);
        
        return Unauthorized();
    }
    
    if (!user.VerifyPassword(request.Password))
    {
        _logger.LogWarning(
            "Login failed: invalid password for user {UserId} from {IpAddress}",
            user.Id,
            HttpContext.Connection.RemoteIpAddress);
        
        return Unauthorized();
    }
    
    var token = _jwtService.GenerateToken(user);
    
    _logger.LogInformation(
        "User {UserId} logged in successfully from {IpAddress}",
        user.Id,
        HttpContext.Connection.RemoteIpAddress);
    
    return Ok(new { token });
}
```

### Log Levels

Use appropriate levels to control verbosity:

```csharp
// Trace: Very detailed, mostly for debugging
_logger.LogTrace("Processing item {ItemId}", itemId);

// Debug: Diagnostic information
_logger.LogDebug("Query executed: {Query}", sqlQuery);

// Information: General information about application flow
_logger.LogInformation("User {UserId} created", userId);

// Warning: Something unexpected but not critical
_logger.LogWarning("User login failed: {Email}", email);

// Error: Error that occurred but application continues
_logger.LogError(ex, "Failed to send email to {Email}", email);

// Critical: Critical failure, application may not continue
_logger.LogCritical("Database connection lost");
```

---

## 12.2 Correlation IDs and Request Tracing

Track related operations across services using correlation IDs:

```csharp
// Middleware to add correlation ID
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    
    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        const string correlationIdHeader = "X-Correlation-ID";
        
        // Get correlation ID from request or generate new one
        if (!context.Request.Headers.TryGetValue(correlationIdHeader, out var correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
        }
        
        // Add to response so client can track the request
        context.Response.Headers.Add(correlationIdHeader, correlationId);
        
        // Add to logging context (enriches all logs for this request)
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}

// Register in Program.cs
app.UseMiddleware<CorrelationIdMiddleware>();

// Now all logs automatically include CorrelationId
// Logs from same request share same CorrelationId
// Easy to query: logs | where CorrelationId == "abc-123"
```

### Distributed Tracing

Use distributed tracing to follow requests across multiple services:

```csharp
// OpenTelemetry setup
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSqlClientInstrumentation()
        .AddOtlpExporter(options => 
        {
            options.Endpoint = new Uri("http://localhost:4317");
        })
    );

// Automatic tracing of:
// - HTTP requests (incoming and outgoing)
// - Database queries
// - Message queue operations
// - Custom spans

public class OrderService
{
    private readonly ActivitySource _activitySource;
    
    public OrderService()
    {
        _activitySource = new ActivitySource("OrderService");
    }
    
    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        using var activity = _activitySource.StartActivity("CreateOrder");
        activity?.SetTag("OrderAmount", request.Total);
        
        // CreateOrder logic
        
        using var paymentActivity = _activitySource.StartActivity("ProcessPayment");
        // Payment processing...
        
        return order;
    }
}
```

---

## 12.3 Health Checks

Health checks indicate API availability and dependencies status:

```csharp
// Register health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(
        name: "Database",
        failureStatus: HealthStatus.Unhealthy
    )
    .AddUrlGroup(
        new Uri("https://api.external-service.com/health"),
        name: "ExternalApi",
        failureStatus: HealthStatus.Degraded  // Non-critical
    )
    .AddCheck<CustomHealthCheck>("CustomCheck");

// Custom health check
public class CustomHealthCheck : IHealthCheck
{
    private readonly IService _service;
    
    public CustomHealthCheck(IService service)
    {
        _service = service;
    }
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await _service.IsHealthyAsync();
            
            return isHealthy
                ? HealthCheckResult.Healthy("Service is operational")
                : HealthCheckResult.Unhealthy("Service not responding");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Service check failed", ex);
        }
    }
}

// Map health check endpoints
app.MapHealthChecks("/health");  // Simple: healthy/unhealthy

app.MapHealthChecks("/health/detailed", new HealthCheckOptions
{
    ResponseWriter = WriteDetailedResponse
});

private static async Task WriteDetailedResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    
    var response = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description
        })
    };
    
    await context.Response.WriteAsJsonAsync(response);
}
```

**Response examples:**

```json
GET /health → 200 OK
```

```json
GET /health/detailed → 200 OK
{
  "status": "Unhealthy",
  "checks": [
    { "name": "Database", "status": "Healthy", "description": "Connected" },
    { "name": "ExternalApi", "status": "Unhealthy", "description": "Timeout" }
  ]
}
```

### Using health checks

```csharp
// Kubernetes probes
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => true  // Always live
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Docker health check
// HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
//   CMD curl -f http://localhost:8080/health || exit 1
```

---

## 12.4 Application Insights Integration

Azure Application Insights provides comprehensive monitoring:

```csharp
// Installation
builder.Services.AddApplicationInsightsTelemetry();

// In appsettings.json
{
  "ApplicationInsights": {
    "InstrumentationKey": "your-key-here"
  }
}

// Automatic tracking:
// - HTTP requests (latency, success rate)
// - Exceptions
// - Dependencies (database, HTTP calls)
// - Custom events and metrics

[HttpPost("users")]
public async Task<IActionResult> CreateUser(CreateUserRequest request)
{
    try
    {
        var user = await _userService.CreateUserAsync(request);
        
        // Track custom event
        _telemetryClient.TrackEvent("UserCreated", new Dictionary<string, string>
        {
            { "UserId", user.Id.ToString() },
            { "Email", user.Email }
        }, new Dictionary<string, double>
        {
            { "ProcessingTime", sw.ElapsedMilliseconds }
        });
        
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }
    catch (Exception ex)
    {
        // Exceptions automatically tracked
        _telemetryClient.TrackException(ex);
        throw;
    }
}

// Custom metrics
var metric = new MetricTelemetry("ApiResponseTime", sw.ElapsedMilliseconds)
{
    Properties = { { "Endpoint", "CreateUser" } }
};
_telemetryClient.TrackMetric(metric);
```

---

## 12.5 Metrics and KPIs

Track important business and technical metrics:

```csharp
public class MetricsService
{
    private readonly IMetricsCollector _metrics;
    
    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var order = new Order { /* ... */ };
            await _orderRepository.AddAsync(order);
            
            stopwatch.Stop();
            
            // Track success
            _metrics.RecordOrderCreation(
                Duration: stopwatch.ElapsedMilliseconds,
                Amount: order.Total,
                Success: true
            );
            
            return order;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            // Track failure
            _metrics.RecordOrderCreation(
                Duration: stopwatch.ElapsedMilliseconds,
                Amount: 0,
                Success: false,
                ErrorType: ex.GetType().Name
            );
            
            throw;
        }
    }
}

// Key metrics to track:
// - Response time (p50, p95, p99)
// - Request rate (requests/second)
// - Error rate (errors/total requests)
// - Business metrics (orders/day, revenue, etc.)
// - Infrastructure (CPU, memory, connections)
```

---

## 12.6 Logging Best Practices

### What to Log

**DO LOG:**
- Application events (startup, shutdown)
- Authentication/authorization events
- Errors and exceptions
- Performance issues
- Business events (order created, payment processed)

**DON'T LOG:**
- Passwords, API keys, tokens
- Personal information (unless required by law)
- Large payloads (log summaries instead)
- Too much detail in production (adjust log level)

### Sensitive Data Masking

```csharp
public class SensitiveDataMaskingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Mask passwords, tokens, credit cards
        var properties = logEvent.Properties.ToDictionary(p => p.Key, p => p.Value);
        
        foreach (var kvp in properties)
        {
            if (kvp.Key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.Contains("token", StringComparison.OrdinalIgnoreCase))
            {
                properties[kvp.Key] = new ScalarValue("***MASKED***");
            }
        }
        
        logEvent.RemovePropertyIfPresent("password");
        logEvent.RemovePropertyIfPresent("token");
    }
}
```

### Log Aggregation

```csharp
// Send logs to centralized service
.WriteTo.Seq("http://localhost:5341")  // Seq
.WriteTo.Splunk(...)                    // Splunk
.WriteTo.ElasticSearch(...)             // ELK Stack
.WriteTo.AzureAnalytics(...)            // Log Analytics
```

---

## 12.7 Alerting

Set up alerts for critical issues:

```csharp
// Alert thresholds
// - Error rate > 5%
// - Response time p95 > 1 second
// - Database connection failures
// - Disk space < 10%
// - Memory usage > 80%

public class AlertingService
{
    private readonly INotificationService _notificationService;
    
    public async Task CheckMetricsAsync()
    {
        var errorRate = await GetErrorRateAsync();
        
        if (errorRate > 0.05)  // 5%
        {
            await _notificationService.NotifyAsync(
                AlertSeverity.Critical,
                $"Error rate is {errorRate:P}, threshold is 5%"
            );
        }
    }
}
```

---

## Summary

Structured logging with Serilog captures machine-readable events. Correlation IDs enable request tracing across services. Health checks indicate dependency status. Application Insights provides automatic telemetry. Log aggregation centralizes logs for analysis. Sensitive data must be masked. Alerting on critical metrics enables rapid response to issues. The next chapter covers resilience patterns—essential for reliable distributed systems.
