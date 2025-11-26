# Common Libraries & Best Practices

Essential libraries and patterns for professional .NET development.

## Popular NuGet Packages

### JSON & Serialization

```bash
# Newtonsoft.Json (older, still widely used)
dotnet add package Newtonsoft.Json

# AutoMapper (object mapping)
dotnet add package AutoMapper.Extensions.Microsoft.DependencyInjection
```

```csharp
using AutoMapper;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}

// Mapping configuration
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<User, UserDto>().ReverseMap();
        CreateMap<CreateUserRequest, User>();
    }
}

// Register in DI
builder.Services.AddAutoMapper(typeof(Program));

// Usage
public class UserService
{
    private readonly IMapper _mapper;

    public UserService(IMapper mapper)
    {
        _mapper = mapper;
    }

    public UserDto GetUserDto(User user)
    {
        return _mapper.Map<UserDto>(user);
    }
}
```

### Validation

```bash
# FluentValidation
dotnet add package FluentValidation.DependencyInjectionExtensions
```

```csharp
using FluentValidation;

public class CreateUserRequest
{
    public string Name { get; set; }
    public string Email { get; set; }
    public int Age { get; set; }
}

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MinimumLength(3).WithMessage("Name must be at least 3 characters");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format");

        RuleFor(x => x.Age)
            .GreaterThan(0).WithMessage("Age must be positive")
            .LessThan(150).WithMessage("Age seems unrealistic");
    }
}

// Register and use
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// In controller (automatic validation)
[HttpPost]
public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
{
    var validator = new CreateUserRequestValidator();
    var result = await validator.ValidateAsync(request);

    if (!result.IsValid)
    {
        var errors = result.Errors.Select(e => e.ErrorMessage);
        return BadRequest(new { errors });
    }

    // Process request...
    return Ok();
}
```

### Monitoring & APM

```bash
# Application Insights
dotnet add package Microsoft.ApplicationInsights.AspNetCore

# ELK Stack (logs)
dotnet add package Serilog.Sinks.Elasticsearch

# Prometheus metrics
dotnet add package prometheus-net.AspNetCore
```

```csharp
// Application Insights
builder.Services.AddApplicationInsightsTelemetry();

// Prometheus
app.UseHttpMetrics();  // Must be before routing

// Custom metrics
app.MapGet("/api/data", () =>
{
    var counter = Metrics.CreateCounter("data_requests_total", "Total data requests");
    counter.Inc();
    return Results.Ok();
});
```

### HTTP Client Extensions

```bash
# Polly (resilience)
dotnet add package Polly
dotnet add package Polly.Extensions.Http
```

```csharp
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

// Setup HTTP client with resilience
builder.Services
    .AddHttpClient<ApiClient>()
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt =>
                TimeSpan.FromSeconds(Math.Pow(2, attempt)),
            onRetry: (outcome, timespan, retryCount, context) =>
            {
                Console.WriteLine($"Retry {retryCount} after {timespan.TotalSeconds}s");
            });
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (outcome, timespan) =>
            {
                Console.WriteLine($"Circuit broken for {timespan.TotalSeconds}s");
            });
}
```

### Data Validation & Sanitization

```bash
# XSS protection
dotnet add package AntiXss

# Rate limiting
dotnet add package AspNetCoreRateLimit
```

```csharp
using AntiXssLibrary;

// Sanitize user input
var userInput = "<script>alert('xss')</script>";
var sanitized = HtmlEncoder.Default.Encode(userInput);  // Built-in

// Custom sanitizer for complex HTML
var cleaner = new HtmlSanitizer();
var html = "<p>Hello <script>alert('xss')</script></p>";
var safe = cleaner.Sanitize(html);  // Returns "<p>Hello </p>"
```

## Code Quality Tools

### Code Analysis

```bash
# StyleCop for code style
dotnet add package StyleCop.Analyzers

# FxCop for API design
dotnet add package Microsoft.CodeAnalysis.FxCopAnalyzers

# SonarAnalyzer
dotnet add package SonarAnalyzer.CSharp
```

### Code Coverage

```bash
# Coverlet (code coverage)
dotnet add package coverlet.collector

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverageFormat=opencover
```

## Dependency Injection Best Practices

```csharp
// ✓ Register dependencies in Program.cs
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddSingleton<ICacheService, CacheService>();

// ✓ Use IOptions<T> for configuration
builder.Services.Configure<DatabaseSettings>(
    builder.Configuration.GetSection("Database"));

// ✓ Register multiple implementations with keying (C# 8.1+)
builder.Services.AddKeyedScoped<INotificationService, EmailService>("email");
builder.Services.AddKeyedScoped<INotificationService, SmsService>("sms");

[FromKeyedServices("email")]
public EmailController(INotificationService emailService) { }

// ✗ Don't use service locator pattern
var service = _serviceProvider.GetRequiredService<IMyService>();

// ✓ Inject dependencies into constructors
public class MyService
{
    private readonly IMyDependency _dependency;

    public MyService(IMyDependency dependency)
    {
        _dependency = dependency;
    }
}
```

## Error Handling Best Practices

```csharp
// ✓ Create custom exceptions
public class UserNotFoundException : Exception
{
    public int UserId { get; set; }
    
    public UserNotFoundException(int userId)
        : base($"User {userId} not found")
    {
        UserId = userId;
    }
}

// ✓ Use structured exceptions
public class ApiException : Exception
{
    public string ErrorCode { get; set; }
    public object Details { get; set; }

    public ApiException(string code, string message, object details = null)
        : base(message)
    {
        ErrorCode = code;
        Details = details;
    }
}

// ✓ Handle at appropriate level
[HttpGet("{id}")]
public async Task<IActionResult> GetUser(int id)
{
    try
    {
        var user = await _userService.GetUserAsync(id);
        return Ok(user);
    }
    catch (UserNotFoundException ex)
    {
        _logger.LogWarning(ex, "User not found: {UserId}", ex.UserId);
        return NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error getting user {UserId}", id);
        return StatusCode(500, new { error = "Internal server error" });
    }
}

// ✗ Catch and swallow exceptions
try
{
    // Do something
}
catch (Exception)
{
    // Silently fail
}

// ✗ Use generic Exception
throw new Exception("Something went wrong");
```

## Naming Conventions

```csharp
// ✓ PascalCase for public members
public class UserService { }
public void GetUserAsync() { }
public int UserId { get; set; }

// ✓ camelCase for private members
private string _userName;
private async Task ProcessUserAsync() { }

// ✓ UPPER_CASE for constants
private const string DEFAULT_ROLE = "User";
private static readonly string ApiKey = "...";

// ✓ Prefix interfaces with 'I'
public interface IUserService { }
public interface IRepository<T> { }

// ✓ Suffix implementations with specific name or pattern
public class UserService : IUserService { }
public class EFUserRepository : IUserRepository { }

// ✓ Avoid abbreviations (unless common domain knowledge)
// ✓ GetUser instead of GetUsr
// ✓ UserRepository instead of UserRep
```

## Async Best Practices

```csharp
// ✓ Use async Task, not async void
public async Task ProcessAsync() { }

// ✗ Don't use async void (exception handling issues)
public async void Process() { }

// ✓ Use ConfigureAwait(false) in libraries
public async Task<string> GetDataAsync()
{
    var response = await _http.GetAsync(url)
        .ConfigureAwait(false);
    var content = await response.Content.ReadAsStringAsync()
        .ConfigureAwait(false);
    return content;
}

// ✓ Proper cancellation token usage
public async Task<Data> FetchAsync(CancellationToken ct)
{
    return await _http.GetAsync(url, ct);
}

// ✗ Don't block on async
var result = GetAsync().Result;  // Can deadlock
```

## Logging Best Practices

```csharp
// ✓ Use structured logging with properties
_logger.LogInformation("User {UserId} logged in from {IpAddress}",
    userId, ipAddress);

// ✓ Use appropriate log levels
_logger.LogDebug("Debug: entering method with parameter {Value}", value);
_logger.LogInformation("Info: operation completed successfully");
_logger.LogWarning("Warning: unexpected but handled condition");
_logger.LogError(exception, "Error: operation failed");
_logger.LogCritical(exception, "Critical: system is in bad state");

// ✓ Include context
using (_logger.BeginScope("{TraceId}", traceId))
{
    _logger.LogInformation("Processing request");
}

// ✗ Don't log sensitive data
// ✗ _logger.LogInformation("User password: {Password}", password);
// ✓ _logger.LogInformation("User authentication attempt for {Email}", email);
```

## Testing Best Practices

```csharp
// ✓ One assertion per test (or related assertions)
[Fact]
public void GetUser_WithValidId_ReturnsUser()
{
    var user = _service.GetUser(1);
    Assert.NotNull(user);
    Assert.Equal("Alice", user.Name);
}

// ✓ Meaningful test names
[Fact]
public void Add_WithPositiveNumbers_ReturnsSum() { }

// ✗ Vague test names
[Fact]
public void TestAdd() { }

// ✓ Test behavior, not implementation
[Fact]
public void CreateUser_CallsSave() { }  // ✓ Behavior

// ✗ Test internal details
[Fact]
public void _CreateBackingField_InitializedCorrectly() { }  // ✗ Implementation

// ✓ Arrange-Act-Assert
[Fact]
public void TransferFunds_DeductsFromSource_AddsToTarget()
{
    // Arrange
    var source = new Account { Balance = 1000 };
    var target = new Account { Balance = 0 };

    // Act
    source.TransferTo(target, 100);

    // Assert
    Assert.Equal(900, source.Balance);
    Assert.Equal(100, target.Balance);
}
```

## SOLID Principles Summary

```csharp
// Single Responsibility Principle
// ✓ Each class has one reason to change
public class UserService { /* manages users */ }
public class EmailService { /* sends emails */ }

// Open/Closed Principle
// ✓ Open for extension, closed for modification
public abstract class PaymentProcessor { /* template */ }
public class StripeProcessor : PaymentProcessor { /* specific impl */ }

// Liskov Substitution Principle
// ✓ Derived classes can be used interchangeably
public interface IRepository<T> { }
public class EFRepository<T> : IRepository<T> { }
public class CachedRepository<T> : IRepository<T> { }

// Interface Segregation Principle
// ✓ Clients don't depend on methods they don't use
public interface IReadable { Task<T> GetAsync(int id); }
public interface IWritable { Task SaveAsync(T item); }

// Dependency Inversion Principle
// ✓ Depend on abstractions, not concrete types
public class UserService
{
    public UserService(IRepository<User> repository) { }  // ✓ Abstraction
}
```

## Documentation Best Practices

```csharp
// ✓ XML documentation for public members
/// <summary>
/// Gets a user by their ID.
/// </summary>
/// <param name="id">The user ID</param>
/// <returns>The user, or null if not found</returns>
/// <exception cref="ArgumentException">Thrown if ID is invalid</exception>
public async Task<User> GetUserAsync(int id)
{
    if (id <= 0)
        throw new ArgumentException("ID must be positive", nameof(id));

    return await _repository.GetUserAsync(id);
}

// ✓ README files for projects
// ✓ Architecture decision records (ADRs)
// ✓ Inline comments for complex logic (sparingly)

// ✗ Comments that repeat code
// ✗ int x = 5;  // Set x to 5
```

## Practice Exercises

1. **Library Integration**: Add AutoMapper and FluentValidation to existing project
2. **Resilience**: Configure HttpClient with Polly retry and circuit breaker policies
3. **Code Analysis**: Enable StyleCop and fix violations
4. **Custom Exception**: Create domain-specific exceptions with proper handling
5. **Documentation**: Add XML documentation to public API

## Key Takeaways

- **AutoMapper** for object mapping; **FluentValidation** for validation
- **Polly** for resilience (retries, circuit breaker)
- **Serilog** for structured logging
- **IOptions<T>** for configuration, never raw IConfiguration
- **Custom exceptions** for domain-specific errors
- **DI container** manages dependencies, no service locator
- **SOLID principles** guide design decisions
- **Async all the way**: use Task, ConfigureAwait in libraries
- **Proper naming** improves readability dramatically
- **Well-documented** code is maintainable code
