# Background Jobs & Services

Execute long-running operations outside the HTTP request cycle.

## Hosted Services

Execute code in the background while the application runs:

```csharp
using Microsoft.Extensions.Hosting;

public class EmailBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmailBackgroundService> _logger;

    public EmailBackgroundService(IServiceProvider serviceProvider, 
        ILogger<EmailBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Email background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var emailService = scope.ServiceProvider
                        .GetRequiredService<IEmailService>();

                    await emailService.ProcessPendingEmailsAsync(stoppingToken);
                }

                // Run every 30 seconds
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing emails");
            }
        }

        _logger.LogInformation("Email background service stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Email background service is stopping");
        await base.StopAsync(cancellationToken);
    }
}

// Register in Program.cs
builder.Services.AddHostedService<EmailBackgroundService>();
```

## Scheduled Services (Cron)

Execute tasks on schedule:

```bash
dotnet add package NCronTab
```

```csharp
public class ScheduledCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduledCleanupService> _logger;

    public ScheduledCleanupService(IServiceProvider serviceProvider,
        ILogger<ScheduledCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var cleanupService = scope.ServiceProvider
                        .GetRequiredService<ICleanupService>();

                    _logger.LogInformation("Running cleanup task");
                    await cleanupService.DeleteExpiredDataAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleanup task failed");
            }
        }
    }
}

// For cron-like scheduling
using NCronTab;

public class CronScheduledService : BackgroundService
{
    private readonly CrontabSchedule _schedule;
    private DateTime _nextRun;

    public CronScheduledService()
    {
        // Run at 2 AM daily
        _schedule = CrontabSchedule.Parse("0 2 * * *");
        _nextRun = _schedule.GetNextOccurrence(DateTime.Now);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            if (now > _nextRun)
            {
                await RunTaskAsync();
                _nextRun = _schedule.GetNextOccurrence(DateTime.Now);
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task RunTaskAsync()
    {
        Console.WriteLine("Running scheduled task at " + DateTime.Now);
        await Task.Delay(1000);
    }
}
```

## Hangfire (Job Queue)

Reliable job scheduling and execution:

```bash
dotnet add package Hangfire.AspNetCore
dotnet add package Hangfire.SqlServer
```

```csharp
// In Program.cs
builder.Services.AddHangfire(config =>
{
    config.UseSqlServerStorage(builder.Configuration["ConnectionStrings:Hangfire"]);
});

builder.Services.AddHangfireServer();

var app = builder.Build();
app.UseHangfireDashboard();  // Dashboard at /hangfire

// Define jobs
public class EmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    public async Task SendWelcomeEmailAsync(int userId)
    {
        _logger.LogInformation("Sending welcome email to user {UserId}", userId);
        await Task.Delay(1000);  // Simulate sending
    }

    public async Task SendResetPasswordEmailAsync(string email)
    {
        _logger.LogInformation("Sending password reset to {Email}", email);
        await Task.Delay(1000);
    }
}

// Queue jobs from controller
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IBackgroundJobClient _backgroundJobClient;

    public AuthController(IBackgroundJobClient backgroundJobClient)
    {
        _backgroundJobClient = backgroundJobClient;
    }

    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest request)
    {
        var userId = SaveUser(request);

        // Queue email to be sent asynchronously
        _backgroundJobClient.Enqueue<EmailService>(
            service => service.SendWelcomeEmailAsync(userId));

        return CreatedAtAction(nameof(GetUser), new { id = userId });
    }

    [HttpPost("forgot-password")]
    public IActionResult ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        // Queue email with delay (send after 5 minutes)
        _backgroundJobClient.Schedule<EmailService>(
            service => service.SendResetPasswordEmailAsync(request.Email),
            TimeSpan.FromMinutes(5));

        return Ok();
    }

    [HttpPost("process-bulk")]
    public IActionResult ProcessBulkData([FromBody] BulkDataRequest request)
    {
        // Queue recurring job
        RecurringJob.AddOrUpdate<DataProcessingService>(
            "bulk-process",
            service => service.ProcessDataAsync(),
            Cron.Daily);

        return Ok();
    }
}
```

## Hangfire Recurring Jobs

```csharp
public class RecurringJobSetup
{
    public static void ConfigureRecurringJobs(IRecurringJobManager recurringJobManager)
    {
        // Daily at 2 AM
        recurringJobManager.AddOrUpdate<ReportService>(
            "daily-report",
            service => service.GenerateDailyReportAsync(),
            Cron.Daily(2));

        // Every hour
        recurringJobManager.AddOrUpdate<SyncService>(
            "hourly-sync",
            service => service.SyncDataAsync(),
            Cron.Hourly);

        // Every 30 minutes
        recurringJobManager.AddOrUpdate<CacheService>(
            "refresh-cache",
            service => service.RefreshAsync(),
            "*/30 * * * *");

        // Every Monday at 9 AM
        recurringJobManager.AddOrUpdate<MaintenanceService>(
            "weekly-maintenance",
            service => service.RunMaintenanceAsync(),
            Cron.Weekly(DayOfWeek.Monday, 9));
    }
}

// In Program.cs
app.Services.GetRequiredService<IRecurringJobManager>();
RecurringJobSetup.ConfigureRecurringJobs(
    app.Services.GetRequiredService<IRecurringJobManager>());
```

## Job Monitoring & Retry

```csharp
public class OrderProcessingService
{
    private readonly ILogger<OrderProcessingService> _logger;

    public OrderProcessingService(ILogger<OrderProcessingService> logger)
    {
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task ProcessOrderAsync(int orderId)
    {
        try
        {
            _logger.LogInformation("Processing order {OrderId}", orderId);
            
            // Process logic that might fail
            var success = await CallExternalPaymentApiAsync(orderId);
            
            if (!success)
            {
                throw new InvalidOperationException("Payment processing failed");
            }

            _logger.LogInformation("Order {OrderId} processed successfully", orderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process order {OrderId}", orderId);
            throw;  // Hangfire will retry
        }
    }

    private async Task<bool> CallExternalPaymentApiAsync(int orderId)
    {
        await Task.Delay(100);
        return true;
    }
}

// Queue with context
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IBackgroundJobClient _backgroundJobClient;

    [HttpPost]
    public IActionResult CreateOrder([FromBody] CreateOrderRequest request)
    {
        var orderId = SaveOrder(request);

        var jobId = _backgroundJobClient.Enqueue<OrderProcessingService>(
            service => service.ProcessOrderAsync(orderId));

        return CreatedAtAction(nameof(GetOrder), 
            new { id = orderId, jobId },
            orderId);
    }
}
```

## Azure Service Bus / Message Queues

For distributed job processing:

```bash
dotnet add package Azure.Messaging.ServiceBus
```

```csharp
public class OrderPublisher
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;

    public OrderPublisher(ServiceBusClient client)
    {
        _client = client;
        _sender = _client.CreateSender("orders-queue");
    }

    public async Task PublishOrderAsync(Order order)
    {
        var json = JsonSerializer.Serialize(order);
        var message = new ServiceBusMessage(json)
        {
            ContentType = "application/json",
            MessageId = order.Id.ToString()
        };

        await _sender.SendMessageAsync(message);
    }
}

// Consumer
public class OrderProcessor : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusProcessor _processor;

    public OrderProcessor(ServiceBusClient client, ILogger<OrderProcessor> logger)
    {
        _client = client;
        _processor = _client.CreateProcessor("orders-queue");
        _processor.ProcessMessageAsync += ProcessOrderAsync;
        _processor.ProcessErrorAsync += ErrorHandler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _processor.StartProcessingAsync(stoppingToken);
    }

    private async Task ProcessOrderAsync(ProcessMessageEventArgs args)
    {
        var json = args.Message.Body.ToString();
        var order = JsonSerializer.Deserialize<Order>(json);

        // Process order
        await Task.Delay(100);
        
        await args.CompleteMessageAsync(args.Message);
    }

    private Task ErrorHandler(ProcessErrorEventArgs args)
    {
        Console.WriteLine(args.Exception);
        return Task.CompletedTask;
    }
}
```

## Polling Pattern

Periodically check and process work:

```csharp
public interface IWorkQueue
{
    Task<WorkItem> GetNextAsync();
    Task CompleteAsync(WorkItem item);
    Task FailAsync(WorkItem item);
}

public class WorkQueueProcessor : BackgroundService
{
    private readonly IWorkQueue _queue;
    private readonly ILogger<WorkQueueProcessor> _logger;

    public WorkQueueProcessor(IWorkQueue queue, ILogger<WorkQueueProcessor> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Poll for work
                var item = await _queue.GetNextAsync();
                
                if (item == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Processing work item {Id}", item.Id);

                try
                {
                    await ProcessWorkAsync(item);
                    await _queue.CompleteAsync(item);
                    _logger.LogInformation("Completed work item {Id}", item.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process work item {Id}", item.Id);
                    await _queue.FailAsync(item);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in work queue processor");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task ProcessWorkAsync(WorkItem item)
    {
        // Do the work
        await Task.Delay(1000);
    }
}

public class WorkItem
{
    public string Id { get; set; }
    public string Payload { get; set; }
    public int RetryCount { get; set; }
}
```

## Task Scheduler Pattern

```csharp
public class TaskScheduler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TaskScheduler> _logger;
    private readonly Dictionary<string, ScheduledTask> _tasks;

    public TaskScheduler(IServiceProvider serviceProvider, ILogger<TaskScheduler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _tasks = new();
    }

    public void ScheduleRecurring<T>(string name, 
        Func<T, Task> action, 
        TimeSpan interval) where T : class
    {
        _tasks[name] = new ScheduledTask(action, interval);
    }

    public async Task StartAsync()
    {
        foreach (var (name, task) in _tasks)
        {
            _ = RunTaskAsync(name, task);
        }

        await Task.CompletedTask;
    }

    private async Task RunTaskAsync(string name, ScheduledTask task)
    {
        var timer = new PeriodicTimer(task.Interval);

        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                await task.ExecuteAsync(scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Task {Name} failed", name);
            }
        }
    }
}

public class ScheduledTask
{
    private readonly Delegate _action;
    public TimeSpan Interval { get; }

    public ScheduledTask(Delegate action, TimeSpan interval)
    {
        _action = action;
        Interval = interval;
    }

    public async Task ExecuteAsync(IServiceProvider serviceProvider)
    {
        var parameters = _action.Method.GetParameters();
        var args = parameters.Select(p => serviceProvider.GetService(p.ParameterType)).ToArray();
        var result = _action.DynamicInvoke(args);

        if (result is Task task)
        {
            await task;
        }
    }
}
```

## Best Practices

```csharp
// ✓ Use scoped services in background tasks
using (var scope = _serviceProvider.CreateScope())
{
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    await service.DoWorkAsync();
}

// ✗ Use singleton services that depend on scoped services
// Causes issues - the singleton captures the singleton scope

// ✓ Handle cancellation gracefully
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await Task.Delay(1000, stoppingToken);
    }
}

// ✗ Don't catch OperationCanceledException
// Let it propagate for graceful shutdown

// ✓ Log what you're doing
_logger.LogInformation("Starting background task");
_logger.LogInformation("Completed background task");

// ✓ Set up retry logic
[AutomaticRetry(Attempts = 3)]
public async Task MyJobAsync() { }

// ✗ Don't create infinite loops without delays
while (true)
{
    // CPU 100%
}

// ✓ Add delays between iterations
while (!stoppingToken.IsCancellationRequested)
{
    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
}
```

## Practice Exercises

1. **Hosted Service**: Create a background service that processes pending items
2. **Hangfire**: Set up job scheduling and monitor via dashboard
3. **Scheduled Task**: Implement daily report generation
4. **Message Queue**: Process work items from a queue
5. **Task Scheduler**: Build a generic task scheduler with multiple jobs

## Key Takeaways

- **BackgroundService** for simple polling/scheduled work
- **Hangfire** for reliable job scheduling with dashboard
- **Recurring jobs** for cron-like scheduling
- **Message queues** for distributed, scalable work
- **Retry logic** for resilient background jobs
- **Proper logging** for monitoring and debugging
- **Scoped services** in background tasks (not singletons)
- **Graceful shutdown** via CancellationToken
- **Health checks** for production monitoring
