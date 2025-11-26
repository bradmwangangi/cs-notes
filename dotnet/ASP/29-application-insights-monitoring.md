# 28. Application Insights & Monitoring

## Overview
Application Insights (Azure Monitor) provides end-to-end observability for applications running on Azure or hybrid environments. It collects telemetry about performance, availability, and user behavior, enabling data-driven decisions about application health and optimization.

---

## 1. Application Insights Setup

### 1.1 Basic Configuration

```csharp
// Install: dotnet add package Microsoft.ApplicationInsights.AspNetCore

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }
    
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Add Application Insights
                services.AddApplicationInsightsTelemetry();
                
                services.AddControllers();
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });
}

// appsettings.json
{
  "ApplicationInsights": {
    "InstrumentationKey": "your-instrumentation-key-here"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}

// appsettings.Production.json
{
  "ApplicationInsights": {
    "InstrumentationKey": "${APPINSIGHTS_INSTRUMENTATIONKEY}"
  }
}
```

### 1.2 Connection String Configuration

```csharp
// Modern approach: Connection String
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Option 1: From configuration
        services.AddApplicationInsightsTelemetry();
        
        // Option 2: Explicit connection string
        var connectionString = Environment.GetEnvironmentVariable(
            "APPLICATIONINSIGHTS_CONNECTION_STRING"
        );
        
        services.AddApplicationInsightsTelemetry(new ApplicationInsightsServiceOptions
        {
            ConnectionString = connectionString,
            EnableAdaptiveSampling = true,  // Sample high-volume scenarios
            EnableQuickPulseMetricStream = true  // Live metrics
        });
    }
}

// appsettings.json
{
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=...;IngestionEndpoint=..."
  }
}
```

---

## 2. Telemetry Collection

### 2.1 Automatic Telemetry

Application Insights automatically collects:
- HTTP requests and responses
- Dependencies (database, HTTP calls)
- Exceptions
- Performance counters
- Custom events and metrics

```csharp
// No additional code needed - automatic collection
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(int id)
    {
        // Automatically tracked:
        // - Request timing
        // - Database call timing
        // - Response status
        // - Any exceptions
        
        var order = await _repository.GetByIdAsync(id);
        return Ok(order);
    }
}

// Automatic telemetry includes:
/*
{
  "name": "GET /api/orders/123",
  "url": "https://example.com/api/orders/123",
  "source": "PC",
  "performanceBucket": "0-250ms",
  "resultCode": "200",
  "duration": 145,
  "timestamp": "2024-01-15T10:23:45.1234567Z",
  "isSuccessful": true
}
*/
```

### 2.2 Custom Events and Metrics

```csharp
public class CustomTelemetryExample
{
    private readonly TelemetryClient _telemetryClient;
    
    public CustomTelemetryExample(TelemetryClient telemetryClient)
    {
        _telemetryClient = telemetryClient;
    }
    
    // Custom event: Something meaningful happened
    public async Task<int> PlaceOrderAsync(Order order)
    {
        await _repository.SaveAsync(order);
        
        _telemetryClient.TrackEvent("OrderPlaced", new Dictionary<string, string>
        {
            { "OrderId", order.Id.ToString() },
            { "CustomerId", order.CustomerId.ToString() },
            { "CustomerType", order.Customer.IsVIP ? "VIP" : "Regular" }
        }, new Dictionary<string, double>
        {
            { "OrderTotal", (double)order.Total.Amount },
            { "ItemCount", order.Items.Count }
        });
        
        return order.Id;
    }
    
    // Custom metric: Numeric measurement
    public void TrackInventoryLevel(int productId, int quantity)
    {
        _telemetryClient.GetMetric("InventoryLevel").TrackValue(quantity);
        
        // With properties
        _telemetryClient.GetMetric("InventoryLevel")
            .TrackValue(
                quantity,
                new Dictionary<string, object> { { "ProductId", productId } }
            );
    }
    
    // Track availability
    public void TrackExternalServiceAvailability(string service, bool isAvailable)
    {
        var availability = new AvailabilityTelemetry
        {
            Name = $"{service} Availability Check",
            RunLocation = "East US",
            IsSuccessful = isAvailable,
            Duration = TimeSpan.FromMilliseconds(500),
            Timestamp = DateTimeOffset.UtcNow
        };
        
        _telemetryClient.TrackAvailability(availability);
    }
    
    // Track performance operation
    public async Task<T> TrackOperationAsync<T>(
        string operationName,
        Func<Task<T>> operation)
    {
        using (var operation_op = _telemetryClient.StartOperation<DependencyTelemetry>(operationName))
        {
            operation_op.Telemetry.Type = "Operation";
            operation_op.Telemetry.Target = "OrderService";
            
            try
            {
                var result = await operation();
                return result;
            }
            catch (Exception ex)
            {
                operation_op.Telemetry.Success = false;
                _telemetryClient.TrackException(ex);
                throw;
            }
        }
    }
}
```

### 2.3 Dependency Tracking

```csharp
public class DependencyTrackingExample
{
    private readonly TelemetryClient _telemetryClient;
    
    // Automatic dependency tracking (HTTP, SQL, etc.)
    public async Task<Order> GetOrderWithDependencyTrackingAsync(int id)
    {
        var startTime = DateTime.UtcNow;
        var success = false;
        
        try
        {
            // HTTP dependency is automatically tracked
            var order = await _httpClient.GetAsync($"/api/orders/{id}");
            
            // Database call is automatically tracked
            await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == id);
            
            success = true;
            return order;
        }
        finally
        {
            var duration = DateTime.UtcNow - startTime;
            
            // Manual dependency tracking (for non-standard dependencies)
            _telemetryClient.TrackDependency(
                type: "Custom",
                target: "LegacySystem",
                dependencyName: "GetOrder",
                data: id.ToString(),
                startTime: startTime,
                duration: duration,
                resultCode: success ? "200" : "500",
                success: success
            );
        }
    }
}
```

---

## 3. Application Map and Diagnostics

### 3.1 Application Map

Shows component dependencies and health:

```csharp
// Application Insights automatically builds map from telemetry
// Shows:
// - Services and their relationships
// - Performance (response time, failure rate)
// - Throughput (requests/sec)
// - Dependencies between components

// To optimize Application Map:
public class AppMapOptimization
{
    private readonly TelemetryClient _telemetryClient;
    
    public async Task<Order> GetOrderAsync(int id)
    {
        // Ensure correlate operations are tagged
        using (var operation = _telemetryClient.StartOperation<RequestTelemetry>("GetOrder"))
        {
            var order = await _repository.GetByIdAsync(id);
            
            // Dependent operations inherit correlation context
            await _notificationService.SendAsync(order);
            
            return order;
        }
    }
}
```

### 3.2 Performance Diagnostics

```csharp
public class PerformanceDiagnosticsExample
{
    private readonly TelemetryClient _telemetryClient;
    
    // Identify slow operations
    public async Task<List<Order>> GetOrdersAsync()
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            var orders = await _repository.GetAllAsync();
            
            var duration = DateTime.UtcNow.Subtract(startTime);
            
            // Flag slow queries
            if (duration.TotalMilliseconds > 1000)
            {
                _telemetryClient.TrackTrace(
                    "Slow Query Detected",
                    SeverityLevel.Warning,
                    new Dictionary<string, string>
                    {
                        { "Query", "GetOrders" },
                        { "Duration", duration.TotalMilliseconds.ToString() },
                        { "RecordCount", orders.Count.ToString() }
                    }
                );
            }
            
            return orders;
        }
        catch (Exception ex)
        {
            _telemetryClient.TrackException(ex);
            throw;
        }
    }
    
    // Memory usage tracking
    public void MonitorMemoryUsage()
    {
        var memoryUsage = GC.GetTotalMemory(false);
        
        _telemetryClient.GetMetric("MemoryUsageMB")
            .TrackValue(memoryUsage / (1024.0 * 1024.0));
    }
    
    // CPU tracking (if available)
    public void MonitorCpu()
    {
        var cpuCounter = new PerformanceCounter(
            "Processor",
            "% Processor Time",
            "_Total"
        );
        
        var cpuUsage = cpuCounter.NextValue();
        
        _telemetryClient.GetMetric("CpuUsagePercent")
            .TrackValue(cpuUsage);
    }
}
```

---

## 4. Alerts and Notifications

### 4.1 Alert Configuration

```csharp
public class AlertConfigurationExample
{
    // Configure alerts in Azure Portal:
    
    // Alert 1: High error rate
    /*
    Condition: Exception count > 10 in 5 minutes
    Action: Email to team@example.com
    Severity: Critical
    */
    
    // Alert 2: Slow response time
    /*
    Condition: Average response time > 2 seconds
    Action: Page on-call engineer
    Severity: High
    */
    
    // Alert 3: Memory usage
    /*
    Condition: Memory > 80% available
    Action: Email warning
    Severity: Medium
    */
    
    // Alert 4: Availability
    /*
    Condition: Failed requests > 5% in 5 minutes
    Action: Create incident
    Severity: Critical
    */
}
```

### 4.2 Alert Action Groups

```csharp
public class AlertActions
{
    // Action groups define how to respond to alerts
    /*
    Email Notifications:
    - production-alerts@example.com
    - devops@example.com
    
    Webhooks:
    - PagerDuty integration
    - Slack notifications
    - Custom automation
    
    Runbooks:
    - Auto-scale resources
    - Restart service
    - Failover to secondary
    
    ITSM:
    - Create incident in Jira
    - ServiceNow ticket
    */
}
```

### 4.3 Smart Alerts

```csharp
public class SmartAlertingExample
{
    // Anomaly detection (built-in)
    /*
    Smart detection identifies unusual patterns:
    - Sudden spike in exceptions
    - Abnormal response time
    - Memory leak patterns
    - Failed dependency surge
    
    Configured in Application Insights:
    Smart detection rules enable anomaly detection
    */
    
    // Composite alerts
    public class CompositeAlertLogic
    {
        // Alert only if BOTH conditions true:
        // 1. Error rate > 5%
        // 2. AND response time > 1 second
        // (Not alert on either alone)
        
        /*
        This reduces false positives and alerts on meaningful issues.
        */
    }
}
```

---

## 5. Analytics Queries

### 5.1 Kusto Query Language (KQL)

```kusto
// Query: Recent requests by endpoint
requests
| where timestamp > ago(1h)
| summarize count() by name
| order by count_ desc

// Query: Failed requests with exceptions
requests
| where success == false
| join kind=inner (
    exceptions
    | project exceptionType, message
) on operation_Id

// Query: Performance by operation
customMetrics
| where name == "OrderProcessingTime"
| summarize avg(value), p95(value), max(value) by tostring(customDimensions.OrderType)

// Query: Error rate trend
requests
| where timestamp > ago(7d)
| summarize 
    totalRequests = count(),
    failedRequests = sum(toint(success == false))
| extend errorRate = (failedRequests * 100.0) / totalRequests
| order by timestamp desc

// Query: Slow operations
requests
| where duration > 1000  // Longer than 1 second
| summarize 
    count(),
    avg(duration),
    max(duration),
    p95(duration)
    by name
| order by avg_duration desc

// Query: Dependency issues
dependencies
| where success == false
| summarize count() by type, name, resultCode
| order by count_ desc

// Query: Custom events
customEvents
| where name == "OrderPlaced"
| summarize count() by tostring(customDimensions.CustomerType)
```

### 5.2 C# SDK for Analytics

```csharp
public class AnalyticsQueryExample
{
    private readonly ApplicationInsightsDataClient _aiClient;
    
    // Query recent telemetry programmatically
    public async Task<List<OrderEvent>> GetRecentOrdersAsync()
    {
        var query = @"
            customEvents
            | where name == 'OrderPlaced'
            | where timestamp > ago(1d)
            | extend customDimensions
            | project 
                OrderId = customDimensions.OrderId,
                CustomerId = customDimensions.CustomerId,
                Amount = customDimensions.OrderTotal,
                timestamp
            | order by timestamp desc
            | limit 100
        ";
        
        var result = await _aiClient.Query.ExecuteAsync(
            resourceGroupName: "myResourceGroup",
            resourceName: "myAppInsights",
            body: new QueryBody(query)
        );
        
        return result.Tables[0].Rows
            .Select(row => new OrderEvent
            {
                OrderId = row[0].ToString(),
                CustomerId = row[1].ToString(),
                Amount = decimal.Parse(row[2].ToString()),
                Timestamp = DateTime.Parse(row[3].ToString())
            })
            .ToList();
    }
}

public class OrderEvent
{
    public string OrderId { get; set; }
    public string CustomerId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Timestamp { get; set; }
}
```

---

## 6. Dashboards and Workbooks

### 6.1 Custom Dashboard

```csharp
public class DashboardExample
{
    // Create dashboard with JSON
    public class DashboardConfig
    {
        /*
        Dashboard tiles:
        1. Request rate (last 24h)
        2. Error rate trend
        3. Top slow endpoints
        4. Exception breakdown
        5. Dependency health
        6. Custom metrics
        
        JSON structure:
        {
          "id": "dashboard-id",
          "name": "Production Overview",
          "tiles": [
            {
              "type": "chart",
              "query": "requests | summarize count() by bin(timestamp, 1m)",
              "title": "Request Rate"
            },
            {
              "type": "metric",
              "metricName": "RequestsPerSecond",
              "title": "RPS"
            }
          ]
        }
        */
    }
}
```

### 6.2 Workbooks

```csharp
public class WorkbookExample
{
    // Interactive investigation notebooks
    public class OrderInvestigationWorkbook
    {
        /*
        Workbook sections:
        
        1. Order Overview
           - Total orders placed
           - Success rate
           - Average order value
        
        2. Performance Analysis
           - Median response time
           - P95 response time
           - Slow order queries
        
        3. Error Analysis
           - Error count by type
           - Failed orders by stage
           - Exception details
        
        4. Customer Insights
           - Top customers by volume
           - VIP vs regular performance
           - Repeat order rate
        
        5. Dependency Health
           - Database performance
           - Payment service status
           - Email service health
        */
    }
}
```

---

## 7. Cost Optimization

### 7.1 Sampling

```csharp
public class SamplingConfiguration
{
    // Adaptive sampling: Automatically reduces data at high volume
    public static void ConfigureAdaptiveSampling(IServiceCollection services)
    {
        services.AddApplicationInsightsTelemetry(options =>
        {
            options.EnableAdaptiveSampling = true;  // Enabled by default
        });
    }
    
    // Custom sampling rules
    public void ConfigureCustomSampling(TelemetryConfiguration config)
    {
        // Sample 10% of normal traffic
        var processor = new AdaptiveSamplingTelemetryProcessor(initialSamplingPercentage: 10)
        {
            // Sample 100% of requests from specific users (VIP)
            ExcludedTypes = "Trace",  // Always keep traces
            IncludedTypes = "Event"   // Sample events
        };
        
        // But always keep errors
        processor.MaxTelemetryItemsPerSecond = 50;
        
        config.DefaultTelemetrySink.TelemetryProcessors.Add(processor);
    }
}
```

### 7.2 Data Retention and Limits

```csharp
public class DataManagement
{
    // Configure retention
    /*
    Azure Portal:
    - Application Insights resource
    - Usage and estimated costs
    - Daily cap: Set maximum data ingestion
    - Retention: 30-730 days
    */
    
    public class RetentionPolicy
    {
        // Data automatically deleted after retention period
        // Billing based on GB ingested (not stored)
        
        // Cost optimization:
        // 1. Enable sampling for high-volume apps
        // 2. Filter unnecessary events
        // 3. Use daily cap to control costs
        // 4. Archive to long-term storage if needed
    }
}
```

---

## 8. Troubleshooting and Best Practices

### 8.1 Common Issues

```csharp
public class TroubleshootingExample
{
    // Issue: Data not appearing
    // Solution: Check instrumentation key, network connectivity
    
    // Issue: High costs
    // Solution: Enable sampling, filter unnecessary data
    
    // Issue: Missing dependencies
    // Solution: Ensure instrumented libraries are added
    
    // Issue: Performance impact
    // Solution: Use sampling, reduce telemetry verbosity
}
```

### 8.2 Best Practices

| Practice | Benefit |
|----------|---------|
| **Enable sampling at scale** | Reduce costs, improve performance |
| **Set appropriate retention** | Balance cost vs. historical data |
| **Use correlation IDs** | Track requests across services |
| **Create meaningful dashboards** | Quick identification of issues |
| **Configure alerts** | Proactive issue detection |
| **Regular health checks** | Ensure monitoring is working |
| **Document thresholds** | Clear escalation criteria |

---

## Summary

Application Insights provides comprehensive monitoring:

1. **Automatic Telemetry**: Requests, dependencies, exceptions
2. **Custom Metrics**: Track business and technical KPIs
3. **Dashboards**: Real-time health visualization
4. **Alerts**: Proactive problem notification
5. **Analytics**: Deep dive investigation with KQL
6. **Cost Control**: Sampling and data management
7. **Integration**: Works with Azure services

Next topic covers Distributed Tracing for understanding request flow through microservices.
