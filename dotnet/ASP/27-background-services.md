# 27a. Background Services

## Overview

Background services in ASP.NET Core enable long-running, asynchronous operations that execute independently of HTTP requests. In enterprise systems, background tasks are essential for scheduled jobs, message processing, file operations, and other work that shouldn't block request handling.

---

## 1. Hosted Services Fundamentals

### 1.1 IHostedService Interface

The fundamental contract for background work:

```csharp
public interface IHostedService
{
    // Called when application starts
    Task StartAsync(CancellationToken cancellationToken);
    
    // Called when application stops
    Task StopAsync(CancellationToken cancellationToken);
}

public class SimpleBackgroundService : IHostedService
{
    private readonly ILogger<SimpleBackgroundService> _logger;
    private CancellationTokenSource _cts;
    private Task _executingTask;
    
    public SimpleBackgroundService(ILogger<SimpleBackgroundService> logger)
    {
        _logger = logger;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SimpleBackgroundService started");
        
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        // Start background work (don't await, let it run independently)
        _executingTask = ExecuteAsync(_cts.Token);
        
        // Prevent this method from blocking startup
        if (_executingTask.IsCompleted)
            await _executingTask;
    }
    
    private async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Background work executing at {Time}", DateTime.UtcNow);
                
                // Do work here
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SimpleBackgroundService cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SimpleBackgroundService error");
            throw;
        }
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SimpleBackgroundService stopping");
        
        if (_executingTask == null)
            return;
        
        try
        {
            _cts?.Cancel();
            
            // Wait for graceful shutdown with timeout
            await Task.WaitAsync(_executingTask, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("SimpleBackgroundService stop timeout");
        }
        finally
        {
            _cts?.Dispose();
        }
    }
}

// Register in Program.cs
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Register background service
        builder.Services.AddHostedService<SimpleBackgroundService>();
        
        var app = builder.Build();
        app.Run();
    }
}
```

### 1.2 BackgroundService Abstract Class

Recommended base class for most background services:

```csharp
public abstract class BackgroundService : IHostedService, IDisposable
{
    private CancellationTokenSource _stoppingCts;
    
    protected abstract Task ExecuteAsync(CancellationToken stoppingToken);
    
    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        // Fire and forget - non-blocking
        _ = ExecuteAsync(_stoppingCts.Token);
        
        return Task.CompletedTask;
    }
    
    public virtual async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _stoppingCts?.Cancel();
        }
        finally
        {
            await Task.WhenAny(Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }
    
    public virtual void Dispose() => _stoppingCts?.Dispose();
}

// Implementation
public class OrderProcessingService : BackgroundService
{
    private readonly ILogger<OrderProcessingService> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    public OrderProcessingService(
        ILogger<OrderProcessingService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrderProcessingService started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Create scope for each iteration (important for EF Core)
                using var scope = _serviceProvider.CreateScope();
                var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
                
                var pendingOrders = await orderRepository.GetPendingOrdersAsync(stoppingToken);
                
                foreach (var order in pendingOrders)
                {
                    await ProcessOrderAsync(order, stoppingToken);
                }
                
                // Wait before next iteration
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("OrderProcessingService stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OrderProcessingService");
                
                // Don't let exceptions kill the service
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
    
    private async Task ProcessOrderAsync(Order order, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing order {OrderId}", order.Id);
        
        try
        {
            // Simulate processing
            await Task.Delay(1000, cancellationToken);
            
            order.Status = OrderStatus.Processed;
            
            _logger.LogInformation("Order {OrderId} processed successfully", order.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process order {OrderId}", order.Id);
            order.Status = OrderStatus.ProcessingFailed;
        }
    }
}

// Register
builder.Services.AddHostedService<OrderProcessingService>();
```

---

## 2. Common Background Service Patterns

### 2.1 Periodic Timer-Based Service

```csharp
public class HealthCheckService : BackgroundService
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly IHealthRepository _healthRepository;
    private Timer _timer;
    
    public HealthCheckService(
        ILogger<HealthCheckService> logger,
        IHealthRepository healthRepository)
    {
        _logger = logger;
        _healthRepository = healthRepository;
    }
    
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HealthCheckService started");
        
        // Using Timer for periodic execution
        _timer = new Timer(
            async _ => await PerformHealthCheckAsync(),
            null,
            TimeSpan.Zero,                      // First execution immediately
            TimeSpan.FromMinutes(1)             // Then every minute
        );
        
        return Task.CompletedTask;
    }
    
    private async Task PerformHealthCheckAsync()
    {
        try
        {
            _logger.LogDebug("Running health check");
            
            var health = new SystemHealth
            {
                Timestamp = DateTime.UtcNow,
                MemoryUsage = GC.GetTotalMemory(false),
                ProcessorUsage = GetProcessorUsage(),
                Status = "Healthy"
            };
            
            await _healthRepository.RecordHealthAsync(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
        }
    }
    
    private double GetProcessorUsage()
    {
        // Implementation for processor monitoring
        return 0;
    }
    
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("HealthCheckService stopping");
        _timer?.Change(Timeout.Infinite, 0);
        return base.StopAsync(cancellationToken);
    }
    
    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }
}
```

### 2.2 Queue-Based Processing Service

```csharp
public class MessageQueueProcessorService : BackgroundService
{
    private readonly ILogger<MessageQueueProcessorService> _logger;
    private readonly IMessageQueue _messageQueue;
    private readonly IMessageHandler _messageHandler;
    
    public MessageQueueProcessorService(
        ILogger<MessageQueueProcessorService> logger,
        IMessageQueue messageQueue,
        IMessageHandler messageHandler)
    {
        _logger = logger;
        _messageQueue = messageQueue;
        _messageHandler = messageHandler;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MessageQueueProcessorService started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Get message from queue (blocking call)
                var message = await _messageQueue.DequeueAsync(stoppingToken);
                
                if (message != null)
                {
                    try
                    {
                        _logger.LogInformation(
                            "Processing message {MessageId} of type {MessageType}",
                            message.Id,
                            message.Type
                        );
                        
                        await _messageHandler.HandleAsync(message, stoppingToken);
                        
                        // Acknowledge message after successful processing
                        await _messageQueue.AcknowledgeAsync(message.Id);
                        
                        _logger.LogInformation("Message {MessageId} processed successfully", message.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process message {MessageId}", message.Id);
                        
                        // Move to dead letter queue or retry
                        await _messageQueue.NackAsync(message.Id, ex.Message);
                    }
                }
                else
                {
                    // No message available, wait briefly before polling again
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("MessageQueueProcessorService stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in MessageQueueProcessorService");
                
                // Wait before retrying to avoid tight loop
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}

public interface IMessageQueue
{
    Task<Message> DequeueAsync(CancellationToken cancellationToken);
    Task AcknowledgeAsync(string messageId);
    Task NackAsync(string messageId, string reason);
}

public interface IMessageHandler
{
    Task HandleAsync(Message message, CancellationToken cancellationToken);
}

public class Message
{
    public string Id { get; set; }
    public string Type { get; set; }
    public object Payload { get; set; }
}
```

### 2.3 Long-Running Database Cleanup Service

```csharp
public class DatabaseCleanupService : BackgroundService
{
    private readonly ILogger<DatabaseCleanupService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly DatabaseCleanupOptions _options;
    
    public DatabaseCleanupService(
        ILogger<DatabaseCleanupService> logger,
        IServiceProvider serviceProvider,
        IOptions<DatabaseCleanupOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DatabaseCleanupService started");
        
        // Wait before first run (avoid startup contention)
        await Task.Delay(_options.InitialDelay, stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                // Clean old logs
                var deletedLogs = await DeleteOldLogsAsync(dbContext, stoppingToken);
                
                // Clean expired sessions
                var deletedSessions = await DeleteExpiredSessionsAsync(dbContext, stoppingToken);
                
                // Clean temporary files
                var deletedFiles = await DeleteTemporaryFilesAsync(stoppingToken);
                
                var duration = DateTime.UtcNow - startTime;
                
                _logger.LogInformation(
                    "Database cleanup completed in {Duration}ms. " +
                    "Deleted: {LogCount} logs, {SessionCount} sessions, {FileCount} files",
                    duration.TotalMilliseconds,
                    deletedLogs,
                    deletedSessions,
                    deletedFiles
                );
                
                // Wait for next cleanup
                await Task.Delay(_options.CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("DatabaseCleanupService stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DatabaseCleanupService");
                
                // Wait before retry
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
    
    private async Task<int> DeleteOldLogsAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_options.LogRetentionDays);
        
        var count = await dbContext.Logs
            .Where(l => l.Timestamp < cutoffDate)
            .ExecuteDeleteAsync(cancellationToken);
        
        return count;
    }
    
    private async Task<int> DeleteExpiredSessionsAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        
        var count = await dbContext.Sessions
            .Where(s => s.ExpiresAt < now)
            .ExecuteDeleteAsync(cancellationToken);
        
        return count;
    }
    
    private async Task<int> DeleteTemporaryFilesAsync(CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(AppContext.BaseDirectory, "temp");
        
        if (!Directory.Exists(tempDir))
            return 0;
        
        var cutoffTime = DateTime.UtcNow.AddHours(-_options.TempFileRetentionHours);
        var files = new DirectoryInfo(tempDir).GetFiles();
        var deletedCount = 0;
        
        foreach (var file in files)
        {
            if (file.LastWriteTimeUtc < cutoffTime)
            {
                try
                {
                    file.Delete();
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary file {FileName}", file.Name);
                }
            }
        }
        
        return deletedCount;
    }
}

public class DatabaseCleanupOptions
{
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
    public int LogRetentionDays { get; set; } = 30;
    public int TempFileRetentionHours { get; set; } = 24;
}

// Configure in Program.cs
builder.Services.Configure<DatabaseCleanupOptions>(
    builder.Configuration.GetSection("DatabaseCleanup")
);
builder.Services.AddHostedService<DatabaseCleanupService>();
```

---

## 3. Scheduling Solutions

### 3.1 Using Quartz.NET

For complex scheduling requirements:

```csharp
// Install: dotnet add package Quartz.Extensions.Hosting

// Define job
public class SendDailyReportJob : IJob
{
    private readonly ILogger<SendDailyReportJob> _logger;
    private readonly IReportService _reportService;
    
    public SendDailyReportJob(
        ILogger<SendDailyReportJob> logger,
        IReportService reportService)
    {
        _logger = logger;
        _reportService = reportService;
    }
    
    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Starting daily report generation");
        
        try
        {
            var report = await _reportService.GenerateDailyReportAsync();
            await _reportService.SendReportAsync(report);
            
            _logger.LogInformation("Daily report sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate or send daily report");
            throw;
        }
    }
}

// Configure in Program.cs
builder.Services.AddQuartz(q =>
{
    // Register job
    var jobKey = new JobKey("SendDailyReport");
    
    q.AddJob<SendDailyReportJob>(opts =>
        opts.WithIdentity(jobKey)
    );
    
    // Schedule job to run daily at 2 AM
    q.AddTrigger(opts =>
        opts
            .ForJob(jobKey)
            .WithIdentity("SendDailyReportTrigger")
            .WithCronSchedule("0 2 * * ?")  // CRON expression
            .StartNow()
    );
});

// Add Quartz hosted service
builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;  // Wait on shutdown
    options.AwaitApplicationStarted = true; // Wait for app to start
});

// More complex scheduling
q.AddJob<ProcessOrdersJob>(opts =>
    opts.WithIdentity("ProcessOrders")
);

// Run every 5 minutes
q.AddTrigger(opts =>
    opts
        .ForJob("ProcessOrders")
        .WithIdentity("ProcessOrdersTrigger")
        .WithSimpleSchedule(schedule =>
            schedule
                .WithIntervalInMinutes(5)
                .RepeatForever()
        )
        .StartNow()
);
```

### 3.2 Using Hangfire

For reliable, persistent job scheduling:

```csharp
// Install: dotnet add package Hangfire.AspNetCore

// Configure in Program.cs
builder.Services.AddHangfire(configuration =>
    configuration
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.AddHangfireServer();

var app = builder.Build();

// Use Hangfire dashboard
app.UseHangfireDashboard();

// Schedule background jobs
public class HangfireJobService
{
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IRecurringJobManager _recurringJobManager;
    
    public HangfireJobService(
        IBackgroundJobClient backgroundJobClient,
        IRecurringJobManager recurringJobManager)
    {
        _backgroundJobClient = backgroundJobClient;
        _recurringJobManager = recurringJobManager;
    }
    
    // Fire and forget job
    public string EnqueueBackgroundJob()
    {
        var jobId = _backgroundJobClient.Enqueue(() => SendNotificationEmail());
        return jobId;
    }
    
    // Delayed job
    public string ScheduleDelayedJob(TimeSpan delay)
    {
        var jobId = _backgroundJobClient.Schedule(
            () => ProcessOrderAsync(123),
            delay
        );
        return jobId;
    }
    
    // Recurring job (runs periodically)
    public void ScheduleRecurringJob()
    {
        _recurringJobManager.AddOrUpdate(
            "daily-report",
            () => GenerateDailyReportAsync(),
            Cron.Daily(2)  // Every day at 2 AM
        );
    }
    
    [AutomaticRetry(Attempts = 3)]  // Retry up to 3 times
    public async Task SendNotificationEmail()
    {
        await Task.Delay(100);  // Simulate work
    }
    
    public async Task ProcessOrderAsync(int orderId)
    {
        await Task.Delay(100);  // Simulate work
    }
    
    public async Task GenerateDailyReportAsync()
    {
        await Task.Delay(100);  // Simulate work
    }
}

// Register in DI
builder.Services.AddScoped<HangfireJobService>();

// Usage in controller
[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly HangfireJobService _jobService;
    
    public JobsController(HangfireJobService jobService)
    {
        _jobService = jobService;
    }
    
    [HttpPost("send-email")]
    public IActionResult SendEmail()
    {
        var jobId = _jobService.EnqueueBackgroundJob();
        return Ok(new { jobId });
    }
}
```

---

## 4. Best Practices for Background Services

### 4.1 Error Handling and Resilience

```csharp
public class ResilientBackgroundService : BackgroundService
{
    private readonly ILogger<ResilientBackgroundService> _logger;
    private readonly int _maxRetries = 3;
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(5);
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ResilientBackgroundService started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            int retryCount = 0;
            
            while (retryCount < _maxRetries && !stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DoWorkAsync(stoppingToken);
                    break;  // Success, exit retry loop
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    
                    if (retryCount >= _maxRetries)
                    {
                        _logger.LogError(ex, "Work failed after {Retries} retries", _maxRetries);
                        break;
                    }
                    
                    _logger.LogWarning(
                        ex,
                        "Work failed, retry {RetryCount}/{MaxRetries} in {Delay}",
                        retryCount,
                        _maxRetries,
                        _retryDelay
                    );
                    
                    await Task.Delay(_retryDelay, stoppingToken);
                }
            }
            
            // Wait before next iteration
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
    
    private async Task DoWorkAsync(CancellationToken cancellationToken)
    {
        // Actual work
        await Task.Delay(100, cancellationToken);
    }
}
```

### 4.2 Graceful Shutdown

```csharp
public class GracefulShutdownService : BackgroundService
{
    private readonly ILogger<GracefulShutdownService> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    
    public GracefulShutdownService(
        ILogger<GracefulShutdownService> logger,
        IHostApplicationLifetime applicationLifetime)
    {
        _logger = logger;
        _applicationLifetime = applicationLifetime;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GracefulShutdownService started");
        
        // Monitor application stopping signal
        _applicationLifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation("Application stopping signal received");
        });
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DoLongRunningWorkAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Background work cancelled, shutting down gracefully");
                
                // Cleanup resources
                await CleanupAsync();
                break;
            }
        }
    }
    
    private async Task DoLongRunningWorkAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(5000, cancellationToken);
    }
    
    private async Task CleanupAsync()
    {
        _logger.LogInformation("Cleaning up resources...");
        await Task.Delay(1000);
        _logger.LogInformation("Cleanup complete");
    }
}
```

---

## 5. Testing Background Services

### 5.1 Unit Testing

```csharp
public class OrderProcessingServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ProcessesOrders_WhenAvailable()
    {
        // Arrange
        var logger = new Mock<ILogger<OrderProcessingService>>();
        var repository = new Mock<IOrderRepository>();
        var serviceProvider = new Mock<IServiceProvider>();
        var scope = new Mock<IServiceScope>();
        
        var orders = new List<Order>
        {
            new Order { Id = 1, Status = OrderStatus.Pending },
            new Order { Id = 2, Status = OrderStatus.Pending }
        };
        
        repository
            .Setup(r => r.GetPendingOrdersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);
        
        scope
            .Setup(s => s.ServiceProvider.GetService(typeof(IOrderRepository)))
            .Returns(repository.Object);
        
        serviceProvider
            .Setup(sp => sp.CreateScope())
            .Returns(scope.Object);
        
        var service = new OrderProcessingService(logger.Object, serviceProvider.Object);
        var cts = new CancellationTokenSource();
        
        // Act
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);
        
        // Assert
        await Task.Delay(TimeSpan.FromSeconds(1));
        
        repository.Verify(
            r => r.GetPendingOrdersAsync(It.IsAny<CancellationToken>()),
            Times.AtLeastOnce
        );
    }
}
```

### 5.2 Integration Testing

```csharp
public class BackgroundServiceIntegrationTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private IHost _host;
    
    public BackgroundServiceIntegrationTests()
    {
        _factory = new WebApplicationFactory<Program>();
    }
    
    public async Task InitializeAsync()
    {
        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddHostedService<TestBackgroundService>();
            });
        
        _host = hostBuilder.Build();
        await _host.StartAsync();
    }
    
    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host?.Dispose();
    }
    
    [Fact]
    public async Task BackgroundServiceExecutes()
    {
        // Service is running in _host
        await Task.Delay(TimeSpan.FromSeconds(2));
        
        // Verify service did work
        // (implementation depends on how you expose results)
    }
}
```

---

## 6. Monitoring Background Services

```csharp
public class MonitoredBackgroundService : BackgroundService
{
    private readonly ILogger<MonitoredBackgroundService> _logger;
    private readonly TelemetryClient _telemetryClient;
    
    public MonitoredBackgroundService(
        ILogger<MonitoredBackgroundService> logger,
        TelemetryClient telemetryClient)
    {
        _logger = logger;
        _telemetryClient = telemetryClient;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MonitoredBackgroundService started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                await DoWorkAsync(stoppingToken);
                stopwatch.Stop();
                
                // Log metrics
                _telemetryClient.TrackEvent(
                    "BackgroundServiceExecuted",
                    properties: new Dictionary<string, string>
                    {
                        { "ServiceName", "MonitoredBackgroundService" },
                        { "Status", "Success" }
                    },
                    metrics: new Dictionary<string, double>
                    {
                        { "DurationMs", stopwatch.ElapsedMilliseconds }
                    }
                );
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                _logger.LogError(ex, "Error in MonitoredBackgroundService");
                
                _telemetryClient.TrackEvent(
                    "BackgroundServiceFailed",
                    properties: new Dictionary<string, string>
                    {
                        { "ServiceName", "MonitoredBackgroundService" },
                        { "ErrorMessage", ex.Message }
                    },
                    metrics: new Dictionary<string, double>
                    {
                        { "DurationMs", stopwatch.ElapsedMilliseconds }
                    }
                );
            }
            
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
    
    private async Task DoWorkAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
    }
}
```

---

## 7. Common Pitfalls

| Pitfall | Problem | Solution |
|---------|---------|----------|
| **Blocking StartAsync** | Application startup delays | Don't await work, fire and forget |
| **Missing cancellation** | Ungraceful shutdown | Always check CancellationToken |
| **No scopes for EF Core** | DbContext tracking issues | Create scope per iteration |
| **Silent failures** | Service dies unnoticed | Proper logging and error handling |
| **Tight loops** | CPU spike on errors | Add delays between retries |
| **Shared mutable state** | Race conditions | Use proper synchronization |
| **No monitoring** | Unknown service health | Add metrics and alerting |

---

## Summary

Background services enable asynchronous, independent work:

1. **IHostedService**: Core interface for background work
2. **BackgroundService**: Recommended base class
3. **Common Patterns**: Timers, queues, cleanup, health checks
4. **Scheduling**: Quartz.NET for complex schedules, Hangfire for persistence
5. **Reliability**: Error handling, retries, graceful shutdown
6. **Monitoring**: Track execution, performance, and errors
7. **Testing**: Unit and integration testing patterns

Background services combined with concurrent programming (Topic 26) enable scalable, responsive enterprise systems that handle both synchronous requests and asynchronous work efficiently.

Next topics cover structured logging (Topic 27) and monitoring with Application Insights (Topic 28).
