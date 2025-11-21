# Logging, Configuration, & Environment Management

Build observable, configurable applications.

## Structured Logging

### Microsoft.Extensions.Logging

Built into ASP.NET Core:

```csharp
using Microsoft.Extensions.Logging;

public class UserService
{
    private readonly ILogger<UserService> _logger;

    public UserService(ILogger<UserService> logger)
    {
        _logger = logger;
    }

    public async Task<User> GetUserAsync(int id)
    {
        _logger.LogInformation("Fetching user with ID: {UserId}", id);

        try
        {
            var user = await _repository.GetUserAsync(id);

            if (user == null)
            {
                _logger.LogWarning("User with ID {UserId} not found", id);
                return null;
            }

            _logger.LogDebug("Successfully retrieved user {UserName}", user.Name);
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user {UserId}", id);
            throw;
        }
    }
}

// In Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(config =>
{
    config.ClearProviders();
    config.AddConsole();
    config.AddDebug();
});
```

### Log Levels

```csharp
public class LoggingExamples
{
    public void LoggingLevels(ILogger<LoggingExamples> logger)
    {
        logger.LogTrace("Detailed diagnostic info");
        logger.LogDebug("Debug information for developers");
        logger.LogInformation("General information about application flow");
        logger.LogWarning("Something unexpected happened");
        logger.LogError(new Exception(), "An error occurred");
        logger.LogCritical("System is failing");
    }
}
```

### Serilog (Recommended)

Professional structured logging:

```bash
dotnet add package Serilog
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Sinks.File
```

```csharp
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/app-.txt",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

var app = builder.Build();

app.MapGet("/users/{id}", async (int id, ILogger<Program> logger) =>
{
    logger.LogInformation("Getting user {UserId}", id);
    return Results.Ok();
});

app.Run();
```

### Structured Data

Log with properties for filtering:

```csharp
public class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public async Task ProcessOrderAsync(Order order)
    {
        using var activity = LogActivity.StartActivity("ProcessOrder");

        _logger.LogInformation(
            "Processing order {OrderId} for customer {CustomerId}",
            order.Id,
            order.CustomerId);

        try
        {
            await _paymentService.ProcessAsync(order.Amount);

            _logger.LogInformation(
                "Order {OrderId} completed successfully in {Duration}ms",
                order.Id,
                activity.Elapsed.TotalMilliseconds);
        }
        catch (PaymentFailedException ex)
        {
            _logger.LogError(
                ex,
                "Payment failed for order {OrderId} with error {ErrorCode}",
                order.Id,
                ex.ErrorCode);
            throw;
        }
    }
}
```

## Configuration Management

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyDb;User Id=sa;"
  },
  "Database": {
    "Host": "localhost",
    "Port": 5432,
    "Name": "mydb"
  },
  "Jwt": {
    "SecretKey": "your-secret-key-here",
    "Issuer": "MyApp",
    "ExpirationMinutes": 60
  },
  "EmailSettings": {
    "SmtpServer": "smtp.gmail.com",
    "SmtpPort": 587,
    "SenderEmail": "app@example.com",
    "SenderPassword": "password"
  },
  "Features": {
    "EnableNewUI": true,
    "MaxUploadSizeMB": 100
  }
}
```

### Environment-Specific Settings

```json
// appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyDb_Dev;..."
  }
}

// appsettings.Production.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=prod.db;Database=MyDb;..."
  }
}
```

### Access Configuration

```csharp
// Simple access
var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"];
var jwtSecret = builder.Configuration["Jwt:SecretKey"];

// Type-safe configuration classes
public class DatabaseSettings
{
    public string Host { get; set; }
    public int Port { get; set; }
    public string Name { get; set; }
}

public class JwtSettings
{
    public string SecretKey { get; set; }
    public string Issuer { get; set; }
    public int ExpirationMinutes { get; set; }
}

// Bind to classes
var databaseSettings = new DatabaseSettings();
builder.Configuration.GetSection("Database").Bind(databaseSettings);

// Or using strongly-typed options
builder.Services.Configure<DatabaseSettings>(
    builder.Configuration.GetSection("Database"));

builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("Jwt"));

// Inject and use
public class UserRepository
{
    private readonly DatabaseSettings _settings;

    public UserRepository(IOptions<DatabaseSettings> options)
    {
        _settings = options.Value;
    }

    public async Task<User> GetUserAsync(int id)
    {
        var connectionString = $"Server={_settings.Host};Port={_settings.Port};Database={_settings.Name}";
        // Use connection
    }
}
```

### Configuration from Multiple Sources

```csharp
var builder = WebApplication.CreateBuilder(args);

// Default sources
// 1. appsettings.json
// 2. appsettings.{Environment}.json
// 3. User secrets (development)
// 4. Environment variables
// 5. Command line arguments

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables()
    .AddEnvironmentVariables("MYAPP_")  // Prefix
    .AddUserSecrets<Program>(optional: true)
    .AddInMemoryCollection(new Dictionary<string, string>
    {
        { "CustomKey", "CustomValue" }
    });
```

## User Secrets (Development)

Store sensitive data locally without committing to git:

```bash
# Initialize secrets
dotnet user-secrets init

# Set secret
dotnet user-secrets set "Jwt:SecretKey" "super-secret-key"

# List secrets
dotnet user-secrets list

# Remove secret
dotnet user-secrets remove "Jwt:SecretKey"

# Clear all
dotnet user-secrets clear
```

Secrets stored in:
- Windows: `%APPDATA%\Microsoft\UserSecrets\{project-id}\secrets.json`
- Linux/Mac: `~/.microsoft/usersecrets/{project-id}/secrets.json`

## Environment Management

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Check environment
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
    _logger.LogInformation("Running in Development mode");
}
else if (app.Environment.IsStaging())
{
    app.UseExceptionHandler("/error");
    _logger.LogInformation("Running in Staging mode");
}
else if (app.Environment.IsProduction())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
    _logger.LogInformation("Running in Production mode");
}

// Custom environment
if (app.Environment.IsEnvironment("Testing"))
{
    // Testing-specific config
}

// In code
public class DataService
{
    private readonly IHostEnvironment _environment;

    public DataService(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public void LogEnvironment()
    {
        Console.WriteLine(_environment.EnvironmentName);  // Development, Production, etc.
        Console.WriteLine(_environment.ContentRootPath);
    }
}
```

### Set Environment

```bash
# Windows
set ASPNETCORE_ENVIRONMENT=Production

# Linux/Mac
export ASPNETCORE_ENVIRONMENT=Production

# In launchSettings.json
{
  "profiles": {
    "MyApp": {
      "commandName": "Project",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    },
    "Production": {
      "commandName": "Project",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Production"
      }
    }
  }
}

# Docker
docker run -e ASPNETCORE_ENVIRONMENT=Production myapp:latest
```

## Options Pattern

```csharp
// Configuration class
public class EmailOptions
{
    public string SmtpServer { get; set; }
    public int SmtpPort { get; set; }
    public string SenderEmail { get; set; }
    public string SenderPassword { get; set; }
}

// Register
builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection("EmailSettings"));

// Use in service
public class EmailService
{
    private readonly EmailOptions _emailOptions;

    public EmailService(IOptions<EmailOptions> emailOptions)
    {
        _emailOptions = emailOptions.Value;
    }

    public async Task SendAsync(string to, string subject, string body)
    {
        using var client = new SmtpClient(_emailOptions.SmtpServer, _emailOptions.SmtpPort);
        // Send email
    }
}

// Optional: Monitor changes
public class ConfigChangeListener
{
    public ConfigChangeListener(IOptionsMonitor<EmailOptions> emailOptionsMonitor)
    {
        emailOptionsMonitor.OnChange(newOptions =>
        {
            Console.WriteLine("Email settings changed!");
        });
    }
}
```

## Configuration Validation

```csharp
public class DatabaseSettings
{
    [Required]
    public string Host { get; set; }

    [Range(1, 65535)]
    public int Port { get; set; } = 5432;

    [Required]
    [MinLength(3)]
    public string Name { get; set; }
}

// Validate on startup
builder.Services
    .Configure<DatabaseSettings>(builder.Configuration.GetSection("Database"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Custom validation
builder.Services
    .Configure<DatabaseSettings>(builder.Configuration.GetSection("Database"))
    .Validate(settings => settings.Port > 0, "Port must be positive")
    .ValidateOnStart();
```

## Feature Flags

Control features without redeployment:

```json
{
  "Features": {
    "EnableNewUI": true,
    "EnableBetaFeatures": false,
    "MaintenanceMode": false
  }
}
```

```csharp
public interface IFeatureToggle
{
    bool IsEnabled(string featureName);
}

public class FeatureToggle : IFeatureToggle
{
    private readonly IConfiguration _configuration;

    public FeatureToggle(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsEnabled(string featureName)
    {
        return _configuration.GetValue<bool>($"Features:{featureName}");
    }
}

// In controller
[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IFeatureToggle _featureToggle;

    public DashboardController(IFeatureToggle featureToggle)
    {
        _featureToggle = featureToggle;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var response = new { };

        if (_featureToggle.IsEnabled("EnableNewUI"))
        {
            // Use new UI
        }
        else
        {
            // Use old UI
        }

        return Ok(response);
    }
}
```

## Best Practices

```csharp
// ✓ Use IOptions<T> for configuration
public MyService(IOptions<MySettings> options)
{
    _settings = options.Value;
}

// ✗ Don't store IConfiguration directly
public MyService(IConfiguration config)
{
    _config = config;  // Hard to test, no validation
}

// ✓ Separate concerns by environment
// appsettings.json (defaults)
// appsettings.Development.json (dev overrides)
// appsettings.Production.json (prod overrides)

// ✓ Use user secrets for development sensitive data
dotnet user-secrets set "ConnectionString" "..."

// ✗ Don't commit secrets to git
// .gitignore should include secrets.json

// ✓ Validate configuration on startup
builder.Services.Configure<AppSettings>(config)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ✓ Use strongly-typed configuration
services.Configure<DatabaseSettings>(config.GetSection("Database"));

// ✗ Magic strings scattered through code
var server = config["Server"];
var timeout = config["Timeout"];
```

## Practice Exercises

1. **Logging**: Add structured logging throughout an application
2. **Environment Config**: Set up development, staging, and production configs
3. **Options Pattern**: Create strongly-typed configuration classes
4. **Secrets**: Use user secrets for local development
5. **Feature Flags**: Implement a simple feature toggle system

## Key Takeaways

- **Logging** provides visibility into application behavior; **Serilog** is professional-grade
- **ILogger<T>** is built in; structure logs with properties
- **appsettings.json** for configuration; **environment-specific** overrides
- **IOptions<T>** for strongly-typed configuration injection
- **User secrets** for sensitive local development data
- **Environment-specific** startup configuration
- **Validate configuration** on application startup
- **Feature flags** enable safe feature rollout
