# 29. Distributed Tracing

## Overview
Distributed tracing tracks requests as they flow across multiple services in a microservices architecture. It provides end-to-end visibility into system behavior, enabling rapid issue detection and performance analysis across service boundaries.

---

## 1. Distributed Tracing Fundamentals

### 1.1 Why Distributed Tracing

**Monolithic System: Easy to trace**
```
Request → Service → Database
         Single execution stack
         Easy to understand flow
```

**Microservices: Complex to trace**
```
Request → Order Service → Inventory Service
                       ↓
                    Payment Service
                       ↓
                    Notification Service
                       ↓
                   Email Service

Multiple services
Multiple databases
Async operations
Multiple failure points
```

Distributed tracing enables:
- Visual request flow through services
- Latency identification at each step
- Error propagation tracking
- Performance bottleneck detection

### 1.2 Trace Concepts

```csharp
public class TraceConceptsExample
{
    // Trace: Collection of spans related to single request
    /*
    Trace ID: abc-123-def-456 (generated at entry point)
    
    Root span (Order Service)
    ├─ GetCustomer span (calls Customer Service)
    ├─ CheckInventory span (calls Inventory Service)
    └─ ProcessPayment span (calls Payment Service)
    
    Each service generates spans for its work.
    All spans share same Trace ID.
    Trace ID propagated through headers.
    */
    
    // Span: Single operation within a service
    /*
    Span ID: xyz-789 (unique within trace)
    Parent Span ID: abc-123 (span that called this)
    Operation name: "CheckInventory"
    Start time: 2024-01-15 10:23:45.123
    Duration: 45ms
    Tags: { service: "inventory", result: "success" }
    Logs: [ "Checking stock" ]
    */
}
```

---

## 2. OpenTelemetry Implementation

### 2.1 Setup

```csharp
// Install packages
// dotnet add package OpenTelemetry
// dotnet add package OpenTelemetry.Exporter.Jaeger
// dotnet add package OpenTelemetry.Instrumentation.AspNetCore
// dotnet add package OpenTelemetry.Instrumentation.HttpClient
// dotnet add package OpenTelemetry.Instrumentation.SqlClient

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Add OpenTelemetry
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    // Instrument ASP.NET Core
                    .AddAspNetCoreInstrumentation()
                    
                    // Instrument HTTP clients
                    .AddHttpClientInstrumentation()
                    
                    // Instrument SQL client
                    .AddSqlClientInstrumentation()
                    
                    // Custom instrumentation
                    .AddSource("Bookstore.*")
                    
                    // Export to Jaeger
                    .AddJaegerExporter(options =>
                    {
                        options.AgentHost = "localhost";
                        options.AgentPort = 6831;
                    });
            });
        
        services.AddControllers();
    }
}

// appsettings.json
{
  "OpenTelemetry": {
    "ServiceName": "bookstore-api",
    "ServiceVersion": "1.0.0"
  },
  "Jaeger": {
    "AgentHost": "localhost",
    "AgentPort": 6831
  }
}
```

### 2.2 Creating Spans Manually

```csharp
public class ManualSpanExample
{
    private static readonly ActivitySource OrderActivitySource = 
        new ActivitySource("Bookstore.OrderService");
    
    private readonly ILogger<OrderService> _logger;
    
    public async Task<int> PlaceOrderAsync(int customerId, List<OrderItem> items)
    {
        // Create root activity (span)
        using (var activity = OrderActivitySource.StartActivity("PlaceOrder"))
        {
            activity?.SetAttribute("customerId", customerId);
            activity?.SetAttribute("itemCount", items.Count);
            
            _logger.LogInformation("Starting order placement");
            
            // Get customer
            using (var getCustomerActivity = OrderActivitySource.StartActivity("GetCustomer"))
            {
                getCustomerActivity?.SetAttribute("customerId", customerId);
                var customer = await _customerRepository.GetByIdAsync(customerId);
                getCustomerActivity?.SetAttribute("customerType", customer?.IsVIP ? "VIP" : "Regular");
            }
            
            // Create order
            using (var createOrderActivity = OrderActivitySource.StartActivity("CreateOrder"))
            {
                var order = Order.Create(customerId, items);
                createOrderActivity?.SetAttribute("orderId", order.Id);
                createOrderActivity?.SetAttribute("totalAmount", order.Total.Amount);
            }
            
            // Check inventory
            using (var checkInventoryActivity = OrderActivitySource.StartActivity("CheckInventory"))
            {
                var available = await _inventoryService.CheckAvailabilityAsync(items);
                checkInventoryActivity?.SetAttribute("available", available);
                
                if (!available)
                {
                    checkInventoryActivity?.SetStatus(
                        ActivityStatusCode.Error,
                        "Insufficient inventory"
                    );
                    throw new OutOfStockException();
                }
            }
            
            // Process payment
            using (var paymentActivity = OrderActivitySource.StartActivity("ProcessPayment"))
            {
                try
                {
                    await _paymentService.ProcessAsync(order);
                    paymentActivity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (PaymentException ex)
                {
                    paymentActivity?.SetStatus(
                        ActivityStatusCode.Error,
                        ex.Message
                    );
                    paymentActivity?.RecordException(ex);
                    throw;
                }
            }
            
            // Save order
            using (var saveActivity = OrderActivitySource.StartActivity("SaveOrder"))
            {
                await _repository.SaveAsync(order);
                saveActivity?.SetAttribute("result", "success");
            }
            
            return order.Id;
        }
    }
}

// Register ActivitySource
public class ServiceCollectionExtensions
{
    public static IServiceCollection AddTelemetry(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("Bookstore.*")  // Includes "Bookstore.OrderService"
                    .AddJaegerExporter();
            });
        
        return services;
    }
}
```

### 2.3 Adding Events to Spans

```csharp
public class SpanEventsExample
{
    private static readonly ActivitySource ActivitySource = 
        new ActivitySource("Bookstore.Processing");
    
    public async Task ProcessOrderWithEventsAsync(Order order)
    {
        using (var activity = ActivitySource.StartActivity("ProcessOrder"))
        {
            // Add event when something happens
            activity?.AddEvent(new ActivityEvent("OrderValidated"));
            
            await ValidateAsync(order);
            activity?.AddEvent(new ActivityEvent("PaymentProcessed"));
            
            await ProcessPaymentAsync(order);
            activity?.AddEvent(new ActivityEvent("InventoryReserved"));
            
            await ReserveInventoryAsync(order);
            activity?.AddEvent(new ActivityEvent("OrderConfirmed"));
            
            // Events with tags
            activity?.AddEvent(new ActivityEvent(
                "OrderShipped",
                tags: new ActivityTagsCollection(new Dictionary<string, object>
                {
                    { "carrier", "FedEx" },
                    { "trackingNumber", "1234567890" }
                })
            ));
        }
    }
    
    private async Task ValidateAsync(Order order) { }
    private async Task ProcessPaymentAsync(Order order) { }
    private async Task ReserveInventoryAsync(Order order) { }
}
```

---

## 2.4 Baggage: Propagating Context

```csharp
public class BaggageExample
{
    private readonly ILogger<OrderService> _logger;
    
    // Baggage: Non-sensitive context propagated across service boundaries
    public async Task ProcessOrderAsync(Order order)
    {
        using (var activity = Activity.Current)
        {
            // Set baggage values
            if (activity != null)
            {
                Activity.Current?.SetBaggage("customerId", order.CustomerId.ToString());
                Activity.Current?.SetBaggage("orderId", order.Id.ToString());
                Activity.Current?.SetBaggage("environment", "production");
            }
            
            // Baggage automatically propagated in HTTP headers
            // When this service calls another service:
            // Headers include baggage values
            var result = await _httpClient.GetAsync("/api/inventory/check");
            
            // In the called service:
            // var customerId = Activity.Current?.GetBaggage("customerId");
        }
    }
}

// Baggage in ASP.NET Core controller
[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    [HttpGet("check")]
    public async Task<IActionResult> CheckInventory()
    {
        // Extract baggage from propagated headers
        var customerId = Activity.Current?.GetBaggage("customerId");
        var orderId = Activity.Current?.GetBaggage("orderId");
        var environment = Activity.Current?.GetBaggage("environment");
        
        // Use in logging
        _logger.LogInformation(
            "Checking inventory for order {OrderId}, customer {CustomerId}",
            orderId,
            customerId
        );
        
        // Baggage is available even though called from different service
        return Ok();
    }
}
```

---

## 3. Trace Exporters

### 3.1 Jaeger

```csharp
// Local development with Jaeger
// Docker: docker run -d --name jaeger -p 6831:6831/udp -p 16686:16686 jaegertracing/all-in-one

public class JaegerConfiguration
{
    public static void ConfigureJaeger(IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddJaegerExporter(options =>
                    {
                        options.AgentHost = "localhost";
                        options.AgentPort = 6831;
                        
                        // Production: Use environment variables
                        // options.AgentHost = Environment.GetEnvironmentVariable("JAEGER_AGENT_HOST");
                        // options.AgentPort = int.Parse(Environment.GetEnvironmentVariable("JAEGER_AGENT_PORT"));
                    });
            });
    }
}

// Access Jaeger UI: http://localhost:16686
```

### 3.2 Application Insights

```csharp
public class ApplicationInsightsTracing
{
    public static void ConfigureApplicationInsights(IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("Bookstore.*")
                    // Export to Application Insights
                    .AddAzureMonitorTraceExporter(options =>
                    {
                        options.ConnectionString = 
                            "InstrumentationKey=...;IngestionEndpoint=...";
                    });
            });
    }
}
```

### 3.3 Zipkin

```csharp
public class ZipkinConfiguration
{
    public static void ConfigureZipkin(IServiceCollection services)
    {
        // Install: dotnet add package OpenTelemetry.Exporter.Zipkin
        
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddZipkinExporter(options =>
                    {
                        options.Endpoint = new Uri("http://localhost:9411/api/v2/spans");
                    });
            });
    }
}

// Local development: 
// docker run -d --name zipkin -p 9411:9411 openzipkin/zipkin
// Access UI: http://localhost:9411
```

---

## 4. Correlation Context

### 4.1 W3C Trace Context

```csharp
// Standard format for trace propagation (W3C spec)
public class W3CTraceContextExample
{
    /*
    traceparent header format:
    traceparent: 00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01
    
    Components:
    - 00: Version (00 = current)
    - 0af7651916cd43dd8448eb211c80319c: Trace ID (128-bit)
    - b7ad6b7169203331: Parent Span ID (64-bit)
    - 01: Trace flags (01 = sampled)
    
    tracestate header: Additional vendor-specific info
    */
    
    public async Task PropagateTraceContextAsync()
    {
        using (var activity = Activity.Current)
        {
            // HTTP headers automatically include W3C trace context
            var request = new HttpRequestMessage(HttpMethod.Get, "http://service-b/api/data");
            
            // Activity context automatically added to headers
            // The called service receives and continues the trace
            
            var response = await _httpClient.SendAsync(request);
        }
    }
}

// Configuration
public class TraceContextConfiguration
{
    public static void ConfigureTraceContext(IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService(
                            serviceName: "bookstore-api",
                            serviceVersion: "1.0.0"
                        )
                    )
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddJaegerExporter();
            });
    }
}
```

### 4.2 Baggage vs Trace Context

```csharp
public class ContextPropagationComparison
{
    // Trace Context: Required fields (trace ID, span ID, flags)
    // - Automatically managed
    // - Used to stitch spans together
    // - Not meant for application data
    
    // Baggage: Optional application context
    // - Must be explicitly set
    // - Propagates across service boundaries
    // - For non-sensitive context (tenant ID, environment, etc.)
    
    public void ContextExample()
    {
        using (var activity = Activity.Current)
        {
            // Trace Context (automatic)
            var traceId = Activity.Current?.Id;      // Entire trace
            var spanId = Activity.Current?.SpanId;   // This span
            
            // Baggage (explicit)
            Activity.Current?.SetBaggage("tenantId", "tenant-123");
            Activity.Current?.SetBaggage("requestId", Guid.NewGuid().ToString());
            
            // Both propagate to downstream services
        }
    }
}
```

---

## 5. Instrumentation and Integration

### 5.1 Custom Instrumentation

```csharp
public class CustomInstrumentationExample
{
    private static readonly ActivitySource ActivitySource = 
        new ActivitySource("Bookstore.Domain");
    
    // Instrument domain operations
    public class Order
    {
        public void ApplyDiscount(Money discount)
        {
            using (var activity = ActivitySource.StartActivity("ApplyDiscount"))
            {
                activity?.SetAttribute("discountAmount", discount.Amount);
                
                // Business logic
                Total = Total - discount;
                
                activity?.SetAttribute("newTotal", Total.Amount);
            }
        }
        
        public async Task<bool> ReserveInventoryAsync(IInventoryService inventory)
        {
            using (var activity = ActivitySource.StartActivity("ReserveInventory"))
            {
                activity?.SetAttribute("orderId", Id);
                activity?.SetAttribute("itemCount", Items.Count);
                
                try
                {
                    var result = await inventory.ReserveAsync(Id, Items);
                    activity?.SetAttribute("reserved", result);
                    return result;
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity?.RecordException(ex);
                    throw;
                }
            }
        }
    }
}
```

### 5.2 Database Query Tracing

```csharp
public class DatabaseTracingExample
{
    // SQL Client instrumentation automatically tracks:
    // - Query text
    // - Database name
    // - Server address
    // - Duration
    // - Success/failure
    
    public async Task<Order> GetOrderAsync(int id)
    {
        // Traced automatically
        return await _dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);
        
        // Span created with:
        // - Operation: "SELECT"
        // - Database: "BookstoreDb"
        // - Query (parameterized)
        // - Duration
    }
    
    // For non-instrumented operations, manually create spans
    public async Task<Order> GetOrderFromLegacySystemAsync(int id)
    {
        var activitySource = new ActivitySource("Bookstore.Legacy");
        
        using (var activity = activitySource.StartActivity("LegacySystemQuery"))
        {
            activity?.SetAttribute("system", "legacy");
            activity?.SetAttribute("orderId", id);
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM Orders WHERE Id = @id";
                    command.Parameters.AddWithValue("@id", id);
                    
                    var reader = await command.ExecuteReaderAsync();
                    // Process results
                }
            }
        }
    }
}
```

---

## 6. Sampling Strategies

### 6.1 Trace Sampling

```csharp
public class TraceSamplingExample
{
    // Sampling: Reduce volume of traces while maintaining representativeness
    
    public static void ConfigureSampling(IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService("bookstore-api"))
                    
                    // AlwaysOn: Sample every trace (development)
                    .SetSampler(new AlwaysOnSampler())
                    
                    // AlwaysOff: Sample no traces (testing)
                    // .SetSampler(new AlwaysOffSampler())
                    
                    // Fixed: Sample percentage (production)
                    .SetSampler(new TraceIdRatioBasedSampler(0.1))  // 10%
                    
                    // Parent-based: Respect upstream decision
                    .SetSampler(new ParentBasedSampler(
                        new TraceIdRatioBasedSampler(0.1)  // 10% if no parent
                    ))
                    
                    .AddAspNetCoreInstrumentation()
                    .AddJaegerExporter();
            });
    }
    
    // Custom sampler: Sample errors at 100%, others at 10%
    public class SmartSampler : Sampler
    {
        public override SamplingResult ShouldSample(SamplingParameters samplingParameters)
        {
            // Sample all error spans
            var attributes = samplingParameters.Attributes;
            if (attributes != null && attributes.ContainsKey("http.status_code"))
            {
                if (int.TryParse(attributes["http.status_code"].ToString(), out var code))
                {
                    if (code >= 400)
                    {
                        return new SamplingResult(SamplingDecision.RecordAndSample);
                    }
                }
            }
            
            // Sample 10% of successful requests
            if (samplingParameters.TraceId.Span.Length > 0)
            {
                var lastByte = samplingParameters.TraceId.Bytes[15];
                if (lastByte <= 25)  // ~10%
                {
                    return new SamplingResult(SamplingDecision.RecordAndSample);
                }
            }
            
            return new SamplingResult(SamplingDecision.Drop);
        }
    }
}
```

---

## 7. Observing Traces

### 7.1 Analyzing Traces

```csharp
public class TraceAnalysisExample
{
    // In Jaeger UI (http://localhost:16686):
    
    // 1. View Trace Details
    /*
    - Trace timeline showing all spans
    - Duration of each operation
    - Span relationships
    - Tags and logs
    - Error details
    */
    
    // 2. Identify Bottlenecks
    /*
    Look for:
    - Long-running spans
    - Spans with errors
    - Sequential operations that could be parallel
    - External dependency latency
    */
    
    // 3. Compare Traces
    /*
    - Successful vs failed traces
    - Fast vs slow traces
    - Different environments
    */
}
```

### 7.2 Trace-Based Alerting

```csharp
public class TraceBasedAlertingExample
{
    // Use trace data to trigger alerts
    /*
    1. High error rate in span:
       - If GetPaymentAsync fails > 5%
       - Alert payment team
    
    2. Latency threshold:
       - If ProcessPayment > 5 seconds
       - Page on-call engineer
    
    3. Span absence:
       - If ReserveInventory never called
       - Alert inventory team
    
    4. Propagation failure:
       - If trace not complete after 30 seconds
       - Alert platform team
    */
}
```

---

## 8. Best Practices

| Practice | Benefit |
|----------|---------|
| **Sample traces appropriately** | Balance visibility vs cost |
| **Include business context** | OrderID, CustomerID in spans |
| **Use correlation IDs** | Track across service boundaries |
| **Set status on errors** | Quickly identify failed operations |
| **Record exceptions** | Detailed error information |
| **Use meaningful span names** | Clear operation identification |
| **Monitor trace volume** | Detect unexpected spikes |
| **Regular trace analysis** | Identify performance trends |

---

## Summary

Distributed tracing provides end-to-end visibility:

1. **OpenTelemetry**: Standard for trace collection
2. **Spans**: Individual operations with timing
3. **Trace Context**: W3C standard propagation
4. **Baggage**: Application context across services
5. **Exporters**: Jaeger, Application Insights, Zipkin
6. **Sampling**: Balance visibility vs. volume
7. **Analysis**: Identify bottlenecks and errors

Combined with structured logging (Topic 27) and Application Insights monitoring (Topic 28), distributed tracing completes the observability picture for enterprise systems.

With Topics 27-29 covering Logging & Monitoring, you now have enterprise-grade observability—critical for understanding and optimizing complex systems.
