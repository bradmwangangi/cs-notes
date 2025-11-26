# Advanced Patterns

Architectural patterns for building maintainable, scalable applications.

## Repository Pattern

Abstraction over data access:

```csharp
// Generic repository interface
public interface IRepository<T> where T : class
{
    Task<T> GetByIdAsync(int id);
    Task<List<T>> GetAllAsync();
    Task<T> AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
    Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate);
}

// Implementation
public class Repository<T> : IRepository<T> where T : class
{
    private readonly AppDbContext _context;
    private readonly DbSet<T> _dbSet;

    public Repository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public async Task<T> GetByIdAsync(int id)
    {
        return await _dbSet.FindAsync(id);
    }

    public async Task<List<T>> GetAllAsync()
    {
        return await _dbSet.ToListAsync();
    }

    public async Task<T> AddAsync(T entity)
    {
        await _dbSet.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(T entity)
    {
        _dbSet.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(T entity)
    {
        _dbSet.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        return await _dbSet.Where(predicate).ToListAsync();
    }
}

// Unit of Work pattern
public interface IUnitOfWork : IDisposable
{
    IRepository<User> Users { get; }
    IRepository<Post> Posts { get; }
    Task SaveChangesAsync();
}

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private IRepository<User> _users;
    private IRepository<Post> _posts;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public IRepository<User> Users => _users ??= new Repository<User>(_context);
    public IRepository<Post> Posts => _posts ??= new Repository<Post>(_context);

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context?.Dispose();
    }
}

// Usage
public class UserService
{
    private readonly IUnitOfWork _unitOfWork;

    public UserService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<User> CreateUserAsync(User user)
    {
        await _unitOfWork.Users.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();
        return user;
    }

    public async Task<List<User>> GetActiveUsersAsync()
    {
        return await _unitOfWork.Users.FindAsync(u => u.IsActive);
    }
}
```

## Specification Pattern

Complex queries as reusable objects:

```csharp
// Base specification
public abstract class Specification<T> where T : class
{
    public Expression<Func<T, bool>> Criteria { get; protected set; }
    public List<Expression<Func<T, object>>> Includes { get; } = new();
    public List<string> IncludeStrings { get; } = new();
    public Expression<Func<T, object>> OrderBy { get; protected set; }
    public Expression<Func<T, object>> OrderByDescending { get; protected set; }
    public int Take { get; protected set; }
    public int Skip { get; protected set; }
    public bool IsPagingEnabled { get; protected set; }

    protected virtual void AddInclude(Expression<Func<T, object>> includeExpression)
    {
        Includes.Add(includeExpression);
    }

    protected virtual void AddInclude(string includeString)
    {
        IncludeStrings.Add(includeString);
    }

    protected virtual void ApplyPaging(int skip, int take)
    {
        Skip = skip;
        Take = take;
        IsPagingEnabled = true;
    }
}

// Specific specifications
public class GetActiveUsersSpecification : Specification<User>
{
    public GetActiveUsersSpecification()
    {
        Criteria = u => u.IsActive;
        AddInclude(u => u.Posts);
        OrderBy = u => u.Name;
    }
}

public class GetUserWithPostsSpecification : Specification<User>
{
    public GetUserWithPostsSpecification(int userId)
    {
        Criteria = u => u.Id == userId;
        AddInclude(u => u.Posts);
        AddInclude(u => u.Comments);
    }
}

public class GetPostsPagedSpecification : Specification<Post>
{
    public GetPostsPagedSpecification(int skip, int take, string category = null)
    {
        Criteria = p => category == null || p.Category == category;
        AddInclude(u => u.Author);
        OrderByDescending = p => p.CreatedAt;
        ApplyPaging(skip, take);
    }
}

// Repository that uses specifications
public class SpecificationRepository<T> : IRepository<T> where T : class
{
    private readonly AppDbContext _context;

    public SpecificationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<T>> GetAsync(Specification<T> spec)
    {
        var query = _context.Set<T>().AsQueryable();

        // Apply criteria
        if (spec.Criteria != null)
            query = query.Where(spec.Criteria);

        // Apply includes
        foreach (var include in spec.Includes)
            query = query.Include(include);

        // Apply ordering
        if (spec.OrderBy != null)
            query = query.OrderBy(spec.OrderBy);
        else if (spec.OrderByDescending != null)
            query = query.OrderByDescending(spec.OrderByDescending);

        // Apply paging
        if (spec.IsPagingEnabled)
            query = query.Skip(spec.Skip).Take(spec.Take);

        return await query.ToListAsync();
    }
}

// Usage
public class PostService
{
    private readonly SpecificationRepository<Post> _repository;

    public async Task<List<Post>> GetPostsAsync(int page, int pageSize)
    {
        var spec = new GetPostsPagedSpecification(
            (page - 1) * pageSize,
            pageSize,
            category: "Technology"
        );

        return await _repository.GetAsync(spec);
    }
}
```

## CQRS (Command Query Responsibility Segregation)

Separate read and write operations:

```csharp
// Command (writes)
public interface ICommand<out T>
{
    // Returns result
}

public class CreateUserCommand : ICommand<UserDto>
{
    public string Name { get; set; }
    public string Email { get; set; }
}

public interface ICommandHandler<in T, T2> where T : ICommand<T2>
{
    Task<T2> HandleAsync(T command);
}

public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, UserDto>
{
    private readonly AppDbContext _context;

    public async Task<UserDto> HandleAsync(CreateUserCommand command)
    {
        var user = new User { Name = command.Name, Email = command.Email };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return new UserDto { Id = user.Id, Name = user.Name };
    }
}

// Query (reads)
public interface IQuery<out T>
{
    // Returns result
}

public class GetUserQuery : IQuery<UserDto>
{
    public int Id { get; set; }
}

public interface IQueryHandler<in T, T2> where T : IQuery<T2>
{
    Task<T2> HandleAsync(T query);
}

public class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserDto>
{
    private readonly AppDbContext _context;

    public async Task<UserDto> HandleAsync(GetUserQuery query)
    {
        var user = await _context.Users.FindAsync(query.Id);
        return new UserDto { Id = user.Id, Name = user.Name };
    }
}

// Mediator to dispatch commands/queries
public interface IMediator
{
    Task<T> SendAsync<T>(ICommand<T> command);
    Task<T> SendAsync<T>(IQuery<T> query);
}

public class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;

    public Mediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<T> SendAsync<T>(ICommand<T> command)
    {
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(T));
        var handler = _serviceProvider.GetService(handlerType);

        if (handler == null)
            throw new InvalidOperationException($"No handler for {command.GetType()}");

        var method = handlerType.GetMethod("HandleAsync");
        var result = await (Task<T>)method.Invoke(handler, new object[] { command });
        return result;
    }

    public async Task<T> SendAsync<T>(IQuery<T> query)
    {
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(T));
        var handler = _serviceProvider.GetService(handlerType);

        if (handler == null)
            throw new InvalidOperationException($"No handler for {query.GetType()}");

        var method = handlerType.GetMethod("HandleAsync");
        var result = await (Task<T>)method.Invoke(handler, new object[] { query });
        return result;
    }
}

// Or use MediatR library
builder.Services.AddMediatR(typeof(Program));

// Usage in controller
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserCommand command)
    {
        var result = await _mediator.SendAsync(command);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var result = await _mediator.SendAsync(new GetUserQuery { Id = id });
        if (result == null) return NotFound();
        return Ok(result);
    }
}
```

## Event Sourcing (Basics)

Store state changes as events:

```csharp
// Event base class
public abstract class Event
{
    public int AggregateId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// Domain events
public class UserCreatedEvent : Event
{
    public string Name { get; set; }
    public string Email { get; set; }
}

public class UserEmailChangedEvent : Event
{
    public string NewEmail { get; set; }
}

public class UserDeletedEvent : Event
{
}

// Event store
public interface IEventStore
{
    Task AppendAsync(int aggregateId, Event @event);
    Task<List<Event>> GetEventsAsync(int aggregateId);
}

public class EventStore : IEventStore
{
    private readonly AppDbContext _context;

    public async Task AppendAsync(int aggregateId, Event @event)
    {
        @event.AggregateId = aggregateId;
        // Store event in database
        // _context.Events.Add(new EventRecord { ... });
        await _context.SaveChangesAsync();
    }

    public async Task<List<Event>> GetEventsAsync(int aggregateId)
    {
        // Retrieve all events for aggregate
        // return await _context.Events.Where(e => e.AggregateId == aggregateId).ToListAsync();
        return await Task.FromResult(new List<Event>());
    }
}

// Aggregate (rebuilds state from events)
public class UserAggregate
{
    public int Id { get; private set; }
    public string Name { get; private set; }
    public string Email { get; private set; }
    private List<Event> _uncommittedEvents = new();

    public UserAggregate() { }

    public static UserAggregate Create(int id, string name, string email)
    {
        var aggregate = new UserAggregate();
        aggregate.ApplyEvent(new UserCreatedEvent
        {
            AggregateId = id,
            Name = name,
            Email = email
        });
        return aggregate;
    }

    public void ChangeEmail(string newEmail)
    {
        ApplyEvent(new UserEmailChangedEvent
        {
            AggregateId = Id,
            NewEmail = newEmail
        });
    }

    public void Delete()
    {
        ApplyEvent(new UserDeletedEvent { AggregateId = Id });
    }

    public void LoadFromHistory(List<Event> events)
    {
        foreach (var @event in events)
        {
            ApplyEvent(@event, false);
        }
    }

    private void ApplyEvent(Event @event, bool isNew = true)
    {
        switch (@event)
        {
            case UserCreatedEvent e:
                Id = e.AggregateId;
                Name = e.Name;
                Email = e.Email;
                break;

            case UserEmailChangedEvent e:
                Email = e.NewEmail;
                break;

            case UserDeletedEvent:
                // Mark as deleted
                break;
        }

        if (isNew)
            _uncommittedEvents.Add(@event);
    }

    public List<Event> GetUncommittedEvents() => _uncommittedEvents;

    public void MarkEventsAsCommitted()
    {
        _uncommittedEvents.Clear();
    }
}

// Usage
public class UserCommandHandler
{
    private readonly IEventStore _eventStore;

    public async Task CreateUserAsync(int id, string name, string email)
    {
        var user = UserAggregate.Create(id, name, email);

        // Save events
        foreach (var @event in user.GetUncommittedEvents())
        {
            await _eventStore.AppendAsync(id, @event);
        }

        user.MarkEventsAsCommitted();
    }

    public async Task ChangeUserEmailAsync(int id, string newEmail)
    {
        // Rebuild state from events
        var events = await _eventStore.GetEventsAsync(id);
        var user = new UserAggregate();
        user.LoadFromHistory(events);

        // Apply change
        user.ChangeEmail(newEmail);

        // Save new events
        foreach (var @event in user.GetUncommittedEvents())
        {
            await _eventStore.AppendAsync(id, @event);
        }
    }
}
```

## Decorator Pattern

Add behavior dynamically:

```csharp
public interface IDataService
{
    Task<Data> GetDataAsync(int id);
}

public class DataService : IDataService
{
    private readonly AppDbContext _context;

    public async Task<Data> GetDataAsync(int id)
    {
        return await _context.Data.FindAsync(id);
    }
}

// Decorator: add caching
public class CachedDataService : IDataService
{
    private readonly IDataService _inner;
    private readonly IMemoryCache _cache;

    public CachedDataService(IDataService inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<Data> GetDataAsync(int id)
    {
        var key = $"data_{id}";
        if (_cache.TryGetValue(key, out Data cached))
            return cached;

        var data = await _inner.GetDataAsync(id);
        _cache.Set(key, data, TimeSpan.FromMinutes(10));
        return data;
    }
}

// Decorator: add logging
public class LoggingDataService : IDataService
{
    private readonly IDataService _inner;
    private readonly ILogger<LoggingDataService> _logger;

    public LoggingDataService(IDataService inner, ILogger<LoggingDataService> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<Data> GetDataAsync(int id)
    {
        _logger.LogInformation("Getting data {Id}", id);
        var data = await _inner.GetDataAsync(id);
        _logger.LogInformation("Retrieved data {Id}", id);
        return data;
    }
}

// Stack decorators
// new LoggingDataService(
//   new CachedDataService(
//     new DataService(...), cache), logger)
```

## Chain of Responsibility

Process requests through handlers:

```csharp
public abstract class RequestHandler<TRequest>
{
    protected RequestHandler<TRequest> _next;

    public void SetNext(RequestHandler<TRequest> next)
    {
        _next = next;
    }

    public virtual async Task HandleAsync(TRequest request)
    {
        await ProcessAsync(request);
        if (_next != null)
            await _next.HandleAsync(request);
    }

    protected abstract Task ProcessAsync(TRequest request);
}

// Concrete handlers
public class ValidationHandler : RequestHandler<CreateUserRequest>
{
    protected override async Task ProcessAsync(CreateUserRequest request)
    {
        if (string.IsNullOrEmpty(request.Email))
            throw new ValidationException("Email required");

        await Task.CompletedTask;
    }
}

public class AuthenticationHandler : RequestHandler<CreateUserRequest>
{
    protected override async Task ProcessAsync(CreateUserRequest request)
    {
        Console.WriteLine("Validating authentication");
        await Task.CompletedTask;
    }
}

public class LoggingHandler : RequestHandler<CreateUserRequest>
{
    protected override async Task ProcessAsync(CreateUserRequest request)
    {
        Console.WriteLine($"Processing request for {request.Email}");
        await Task.CompletedTask;
    }
}

// Setup chain
var validation = new ValidationHandler();
var auth = new AuthenticationHandler();
var logging = new LoggingHandler();

validation.SetNext(auth);
auth.SetNext(logging);

// Process
await validation.HandleAsync(new CreateUserRequest { Email = "user@example.com" });
```

## Factory Pattern

Create objects without specifying exact classes:

```csharp
public interface INotificationService
{
    Task SendAsync(string message, string recipient);
}

public class EmailNotificationService : INotificationService
{
    public async Task SendAsync(string message, string recipient)
    {
        Console.WriteLine($"Sending email to {recipient}: {message}");
        await Task.CompletedTask;
    }
}

public class SmsNotificationService : INotificationService
{
    public async Task SendAsync(string message, string recipient)
    {
        Console.WriteLine($"Sending SMS to {recipient}: {message}");
        await Task.CompletedTask;
    }
}

public interface INotificationServiceFactory
{
    INotificationService CreateService(NotificationType type);
}

public class NotificationServiceFactory : INotificationServiceFactory
{
    private readonly IServiceProvider _serviceProvider;

    public NotificationServiceFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public INotificationService CreateService(NotificationType type)
    {
        return type switch
        {
            NotificationType.Email => _serviceProvider.GetRequiredService<EmailNotificationService>(),
            NotificationType.Sms => _serviceProvider.GetRequiredService<SmsNotificationService>(),
            _ => throw new ArgumentException("Unknown notification type")
        };
    }
}

public enum NotificationType
{
    Email,
    Sms
}

// Usage
var factory = serviceProvider.GetRequiredService<INotificationServiceFactory>();
var emailService = factory.CreateService(NotificationType.Email);
await emailService.SendAsync("Hello", "user@example.com");
```

## Practice Exercises

1. **Repository Pattern**: Implement repository with Unit of Work
2. **Specification Pattern**: Create specifications for complex queries
3. **CQRS**: Separate commands and queries in an application
4. **Event Sourcing**: Store user actions as events
5. **Decorators**: Stack multiple decorators for cross-cutting concerns

## Key Takeaways

- **Repository** abstracts data access; **Unit of Work** coordinates multiple repos
- **Specification** encapsulates query logic reusably
- **CQRS** separates read and write models for scalability
- **Event Sourcing** stores state as immutable events
- **Decorators** add behavior without modifying original class
- **Chain of Responsibility** processes requests through handlers
- **Factory** creates objects without tight coupling
- These patterns enable **testability**, **maintainability**, and **scalability**
