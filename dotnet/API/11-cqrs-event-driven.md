# Chapter 11: CQRS & Event-Driven Architecture

## 11.1 CQRS Fundamentals

CQRS (Command Query Responsibility Segregation) separates read and write operations into different models.

### Traditional CRUD Model

```csharp
// Single model for both read and write
public class User
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    public string PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastLoginAt { get; set; }
    // Many more properties...
}

// Both read and write operations use same model
public async Task<User> GetUserAsync(int id)
{
    return await context.Users.FindAsync(id);
}

public async Task UpdateUserAsync(User user)
{
    context.Users.Update(user);
    await context.SaveChangesAsync();
}
```

**Problems:**
- Read queries include sensitive data (PasswordHash)
- Write model has unnecessary read-only data (LastLoginAt)
- Single model for different purposes
- Performance: can't optimize for both

### CQRS Separation

**Commands** - Operations that modify state (Create, Update, Delete)
**Queries** - Operations that read state (no modifications)

Different models optimized for each:

```csharp
// Write Model - optimized for persistence
public class UserWriteModel
{
    public int Id { get; set; }
    public Email Email { get; set; }
    public Password Password { get; set; }
    public Name Name { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Business logic methods for writes
    public void UpdateEmail(Email newEmail) { /* ... */ }
    public void ChangePassword(Password newPassword) { /* ... */ }
}

// Read Model - optimized for queries
public class UserReadModel
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastLoginAt { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
}

// Commands - write operations
public class UpdateUserEmailCommand
{
    public int UserId { get; set; }
    public string NewEmail { get; set; }
}

public class UpdateUserEmailCommandHandler
{
    private readonly IUserRepository _repository;
    
    public async Task HandleAsync(UpdateUserEmailCommand command)
    {
        var user = await _repository.GetByIdAsync(command.UserId);
        if (user == null)
            throw new UserNotFoundException(command.UserId);
        
        user.UpdateEmail(new Email(command.NewEmail));
        await _repository.UpdateAsync(user);
    }
}

// Queries - read operations
public class GetUserQuery
{
    public int UserId { get; set; }
}

public class GetUserQueryHandler
{
    private readonly IQueryRepository<UserReadModel> _queryRepository;
    
    public async Task<UserReadModel> HandleAsync(GetUserQuery query)
    {
        return await _queryRepository.GetByIdAsync(query.UserId);
    }
}
```

---

## 11.2 Implementing CQRS

### Command Pattern

```csharp
// Base command interface
public interface ICommand { }

public interface ICommandHandler<TCommand> where TCommand : ICommand
{
    Task ExecuteAsync(TCommand command);
}

// Specific command
public class CreateUserCommand : ICommand
{
    public string Email { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Password { get; set; }
}

public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    private readonly IUserRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    
    public CreateUserCommandHandler(
        IUserRepository repository,
        IEventPublisher eventPublisher)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
    }
    
    public async Task ExecuteAsync(CreateUserCommand command)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(command.Email))
            throw new ValidationException("Email required");
        
        // Check preconditions
        var email = new Email(command.Email);
        if (await _repository.ExistsAsync(email))
            throw new DomainException("Email already in use");
        
        // Create aggregate
        var name = new Name(command.FirstName, command.LastName);
        var password = Password.CreateFromPlaintext(command.Password);
        var user = User.Create(email, name, password);
        
        // Persist
        await _repository.AddAsync(user);
        
        // Publish events
        var @event = new UserCreatedEvent { UserId = user.Id, Email = email };
        await _eventPublisher.PublishAsync(@event);
    }
}

// In controller
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly ICommandHandler<CreateUserCommand> _createUserHandler;
    
    public UsersController(ICommandHandler<CreateUserCommand> createUserHandler)
    {
        _createUserHandler = createUserHandler;
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateUser(CreateUserRequest request)
    {
        var command = new CreateUserCommand
        {
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Password = request.Password
        };
        
        await _createUserHandler.ExecuteAsync(command);
        return Ok("User created");
    }
}
```

### Query Pattern

```csharp
// Base query interface
public interface IQuery<TResult> { }

public interface IQueryHandler<TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> ExecuteAsync(TQuery query);
}

// Specific query
public class GetUserDetailsQuery : IQuery<UserDetailsDto>
{
    public int UserId { get; set; }
}

public class GetUserDetailsQueryHandler : IQueryHandler<GetUserDetailsQuery, UserDetailsDto>
{
    private readonly AppDbContext _context;
    
    public GetUserDetailsQueryHandler(AppDbContext context)
    {
        _context = context;
    }
    
    public async Task<UserDetailsDto> ExecuteAsync(GetUserDetailsQuery query)
    {
        return await _context.Users
            .Where(u => u.Id == query.UserId)
            .Select(u => new UserDetailsDto
            {
                Id = u.Id,
                Email = u.Email,
                Name = u.Name,
                CreatedAt = u.CreatedAt,
                OrderCount = u.Orders.Count,
                TotalSpent = u.Orders.Sum(o => o.Total)
            })
            .FirstOrDefaultAsync()
            ?? throw new UserNotFoundException(query.UserId);
    }
}

// In controller
[HttpGet("{id}")]
public async Task<ActionResult<UserDetailsDto>> GetUser(
    int id,
    IQueryHandler<GetUserDetailsQuery, UserDetailsDto> handler)
{
    var query = new GetUserDetailsQuery { UserId = id };
    var result = await handler.ExecuteAsync(query);
    return Ok(result);
}
```

### Dispatcher Pattern

Simplify by using a dispatcher:

```csharp
public interface IDispatcher
{
    Task<TResult> QueryAsync<TQuery, TResult>(TQuery query)
        where TQuery : IQuery<TResult>;
    
    Task CommandAsync<TCommand>(TCommand command)
        where TCommand : ICommand;
}

public class Dispatcher : IDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    
    public Dispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public async Task<TResult> QueryAsync<TQuery, TResult>(TQuery query)
        where TQuery : IQuery<TResult>
    {
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(
            typeof(TQuery), typeof(TResult));
        
        dynamic handler = _serviceProvider.GetRequiredService(handlerType);
        return await handler.ExecuteAsync(query);
    }
    
    public async Task CommandAsync<TCommand>(TCommand command)
        where TCommand : ICommand
    {
        var handlerType = typeof(ICommandHandler<>).MakeGenericType(typeof(TCommand));
        dynamic handler = _serviceProvider.GetRequiredService(handlerType);
        await handler.ExecuteAsync(command);
    }
}

// Registration
builder.Services.AddScoped<IDispatcher, Dispatcher>();
builder.Services.AddScoped<ICommandHandler<CreateUserCommand>, CreateUserCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetUserDetailsQuery, UserDetailsDto>, 
    GetUserDetailsQueryHandler>();

// Usage in controller
[HttpPost]
public async Task<IActionResult> CreateUser(CreateUserRequest request)
{
    var command = new CreateUserCommand { /* ... */ };
    await _dispatcher.CommandAsync(command);
    return Ok();
}

[HttpGet("{id}")]
public async Task<ActionResult<UserDetailsDto>> GetUser(int id)
{
    var query = new GetUserDetailsQuery { UserId = id };
    var result = await _dispatcher.QueryAsync<GetUserDetailsQuery, UserDetailsDto>(query);
    return Ok(result);
}
```

---

## 11.3 Event-Driven Architecture

Events represent significant domain occurrences. They enable loose coupling and asynchronous processing.

### Domain Events

```csharp
// Domain Event - something happened in the domain
public abstract class DomainEvent
{
    public int AggregateId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class UserCreatedEvent : DomainEvent
{
    public string Email { get; set; }
    public string Name { get; set; }
}

public class UserEmailChangedEvent : DomainEvent
{
    public string OldEmail { get; set; }
    public string NewEmail { get; set; }
}

public class UserSuspendedEvent : DomainEvent
{
    public string Reason { get; set; }
}

// Entity raises events (not stored in database, published)
public class User
{
    private readonly List<DomainEvent> _events = new();
    public IReadOnlyList<DomainEvent> Events => _events.AsReadOnly();
    
    public static User Create(Email email, Name name, Password password)
    {
        var user = new User
        {
            Email = email,
            Name = name,
            Password = password,
            Status = UserStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        
        // Raise event
        user._events.Add(new UserCreatedEvent
        {
            AggregateId = user.Id,
            Email = email,
            Name = name.FullName
        });
        
        return user;
    }
    
    public void UpdateEmail(Email newEmail)
    {
        var oldEmail = Email;
        Email = newEmail;
        
        _events.Add(new UserEmailChangedEvent
        {
            AggregateId = Id,
            OldEmail = oldEmail,
            NewEmail = newEmail
        });
    }
    
    public void Suspend(string reason)
    {
        Status = UserStatus.Suspended;
        
        _events.Add(new UserSuspendedEvent
        {
            AggregateId = Id,
            Reason = reason
        });
    }
}
```

### Event Handlers

```csharp
// Event handler interface
public interface IDomainEventHandler<TEvent> where TEvent : DomainEvent
{
    Task HandleAsync(TEvent @event);
}

// Send welcome email when user created
public class SendWelcomeEmailEventHandler : IDomainEventHandler<UserCreatedEvent>
{
    private readonly IEmailService _emailService;
    
    public SendWelcomeEmailEventHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }
    
    public async Task HandleAsync(UserCreatedEvent @event)
    {
        await _emailService.SendWelcomeEmailAsync(
            @event.Email,
            @event.Name
        );
    }
}

// Log to audit when email changes
public class AuditUserEmailChangeEventHandler : IDomainEventHandler<UserEmailChangedEvent>
{
    private readonly IAuditLogger _auditLogger;
    
    public AuditUserEmailChangeEventHandler(IAuditLogger auditLogger)
    {
        _auditLogger = auditLogger;
    }
    
    public async Task HandleAsync(UserEmailChangedEvent @event)
    {
        await _auditLogger.LogAsync(new AuditEntry
        {
            AggregateId = @event.AggregateId,
            Action = "EmailChanged",
            Details = new { @event.OldEmail, @event.NewEmail },
            Timestamp = @event.OccurredAt
        });
    }
}

// Send notification when user suspended
public class NotifyAdminsOnSuspensionHandler : IDomainEventHandler<UserSuspendedEvent>
{
    private readonly INotificationService _notificationService;
    
    public NotifyAdminsOnSuspensionHandler(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }
    
    public async Task HandleAsync(UserSuspendedEvent @event)
    {
        await _notificationService.NotifyAdminsAsync(
            $"User {event.AggregateId} suspended: {@event.Reason}"
        );
    }
}
```

### Publishing Events

```csharp
// Event publisher
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event) where TEvent : DomainEvent;
}

public class EventPublisher : IEventPublisher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventPublisher> _logger;
    
    public EventPublisher(IServiceProvider serviceProvider, ILogger<EventPublisher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : DomainEvent
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(typeof(TEvent));
        var handlers = _serviceProvider.GetServices(handlerType);
        
        foreach (dynamic handler in handlers)
        {
            try
            {
                await handler.HandleAsync(@event);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling event {@EventType}", typeof(TEvent).Name);
                // Optionally re-throw or track failure
            }
        }
    }
}

// In command handler
public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    private readonly IUserRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    
    public async Task ExecuteAsync(CreateUserCommand command)
    {
        // ... create user ...
        var user = User.Create(email, name, password);
        
        // Persist
        await _repository.AddAsync(user);
        
        // Publish all raised events
        foreach (var @event in user.Events)
        {
            await _eventPublisher.PublishAsync(@event);
        }
    }
}
```

---

## 11.4 Outbox Pattern for Reliability

Domain events must be persisted with the aggregate. Use the Outbox pattern:

```csharp
// Outbox entry - stores events for reliable publishing
public class OutboxEvent
{
    public long Id { get; set; }
    public string AggregateId { get; set; }
    public string EventType { get; set; }
    public string EventData { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

// DbContext includes outbox
public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<OutboxEvent> OutboxEvents { get; set; }
}

// Save event to outbox within transaction
public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    private readonly IUserRepository _repository;
    private readonly AppDbContext _context;
    
    public async Task ExecuteAsync(CreateUserCommand command)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        
        try
        {
            // Create and save user
            var user = User.Create(email, name, password);
            await _repository.AddAsync(user);
            
            // Save events to outbox (same transaction!)
            foreach (var @event in user.Events)
            {
                _context.OutboxEvents.Add(new OutboxEvent
                {
                    AggregateId = user.Id.ToString(),
                    EventType = @event.GetType().Name,
                    EventData = JsonSerializer.Serialize(@event),
                    CreatedAt = DateTime.UtcNow
                });
            }
            
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

// Background worker processes outbox
public class OutboxWorkerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxWorkerService> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            
            // Get unprocessed events
            var outboxEvents = await context.OutboxEvents
                .Where(e => e.ProcessedAt == null)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync(stoppingToken);
            
            foreach (var outboxEvent in outboxEvents)
            {
                try
                {
                    // Deserialize and publish
                    var eventType = Type.GetType(outboxEvent.EventType);
                    var @event = JsonSerializer.Deserialize(outboxEvent.EventData, eventType) as DomainEvent;
                    
                    await eventPublisher.PublishAsync(@event);
                    
                    // Mark as processed
                    outboxEvent.ProcessedAt = DateTime.UtcNow;
                    await context.SaveChangesAsync(stoppingToken);
                    
                    _logger.LogInformation("Processed outbox event {EventId}", outboxEvent.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing outbox event {EventId}", outboxEvent.Id);
                    // Retry logic here
                }
            }
            
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

// Register in Program.cs
builder.Services.AddHostedService<OutboxWorkerService>();
```

---

## 11.5 Integration with Message Brokers

For distributed systems, publish events to message brokers:

```csharp
// Publish to RabbitMQ or Azure Service Bus
public class DistributedEventPublisher : IEventPublisher
{
    private readonly IMessageProducer _messageProducer;
    private readonly IEventPublisher _localPublisher;
    
    public DistributedEventPublisher(
        IMessageProducer messageProducer,
        IEventPublisher localPublisher)
    {
        _messageProducer = messageProducer;
        _localPublisher = localPublisher;
    }
    
    public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : DomainEvent
    {
        // Publish locally first (in-process handlers)
        await _localPublisher.PublishAsync(@event);
        
        // Publish to message broker (remote services)
        await _messageProducer.PublishAsync(@event.GetType().Name, @event);
    }
}

// In another service, subscribe to events
public class EmailServiceConsumer
{
    private readonly IMessageConsumer _messageConsumer;
    private readonly IEmailService _emailService;
    
    public async Task StartAsync()
    {
        await _messageConsumer.SubscribeAsync<UserCreatedEvent>(
            @event => _emailService.SendWelcomeEmailAsync(@event.Email, @event.Name)
        );
    }
}
```

---

## 11.6 Benefits and Trade-offs

**CQRS Benefits:**
- ✓ Separate optimization for reads and writes
- ✓ Clearer intent (commands vs. queries)
- ✓ Scalable: read model in cache, write model in database
- ✓ Easier to track business events

**CQRS Trade-offs:**
- ✗ More code (separate models)
- ✗ Eventual consistency (read model lags write model)
- ✗ Complex debugging (event flow)
- ✗ Overkill for simple CRUD

**Use CQRS when:**
- Complex domain with intricate business rules
- Different read/write patterns
- Need detailed audit trail
- Multiple read models needed

**Don't use CQRS for:**
- Simple CRUD operations
- Consistent read/write requirements
- Small teams (code complexity)

---

## Summary

CQRS separates commands (writes) from queries (reads), each optimized for its purpose. Domain events represent significant business occurrences and enable loose coupling. The Outbox pattern ensures reliable event persistence. Event handlers can be distributed across services via message brokers. CQRS adds complexity but provides significant benefits for complex domains. This completes the advanced patterns section. The next chapters cover enterprise concerns: logging/monitoring, resilience, and security.
