# 14. Data Access Patterns

## Overview

Data access patterns provide abstraction over how data is retrieved and persisted. They enable testability, maintainability, and flexibility in choosing data storage implementations. Understanding these patterns is essential for building enterprise applications.

## Repository Pattern

### Purpose

The Repository pattern provides an abstraction over data access logic, making it easy to:
- Switch data sources (SQL Server → PostgreSQL, database → cache)
- Unit test by mocking repositories
- Centralize query logic
- Hide database complexity from business logic

### Generic Repository

```csharp
// IRepository.cs - Generic interface
public interface IRepository<T> where T : class
{
    // Query
    Task<T> GetByIdAsync(int id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate);
    Task<T> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate);
    
    // Modification
    Task<T> AddAsync(T entity);
    Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities);
    Task UpdateAsync(T entity);
    Task RemoveAsync(T entity);
    Task RemoveRangeAsync(IEnumerable<T> entities);
    
    // Counting
    Task<int> CountAsync();
    Task<int> CountAsync(Expression<Func<T, bool>> predicate);
    
    // Paging
    Task<PaginatedResult<T>> GetPagedAsync(int page, int pageSize);
}

// Repository.cs - Generic implementation
public class Repository<T> : IRepository<T> where T : class
{
    private readonly ApplicationDbContext _context;
    private readonly DbSet<T> _dbSet;
    
    public Repository(ApplicationDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }
    
    public async Task<T> GetByIdAsync(int id)
    {
        return await _dbSet.FindAsync(id);
    }
    
    public async Task<IEnumerable<T>> GetAllAsync()
    {
        return await _dbSet.ToListAsync();
    }
    
    public async Task<IEnumerable<T>> FindAsync(
        Expression<Func<T, bool>> predicate)
    {
        return await _dbSet.Where(predicate).ToListAsync();
    }
    
    public async Task<T> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate)
    {
        return await _dbSet.FirstOrDefaultAsync(predicate);
    }
    
    public async Task<T> AddAsync(T entity)
    {
        await _dbSet.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }
    
    public async Task<IEnumerable<T>> AddRangeAsync(
        IEnumerable<T> entities)
    {
        await _dbSet.AddRangeAsync(entities);
        await _context.SaveChangesAsync();
        return entities;
    }
    
    public async Task UpdateAsync(T entity)
    {
        _dbSet.Update(entity);
        await _context.SaveChangesAsync();
    }
    
    public async Task RemoveAsync(T entity)
    {
        _dbSet.Remove(entity);
        await _context.SaveChangesAsync();
    }
    
    public async Task RemoveRangeAsync(IEnumerable<T> entities)
    {
        _dbSet.RemoveRange(entities);
        await _context.SaveChangesAsync();
    }
    
    public async Task<int> CountAsync()
    {
        return await _dbSet.CountAsync();
    }
    
    public async Task<int> CountAsync(Expression<Func<T, bool>> predicate)
    {
        return await _dbSet.CountAsync(predicate);
    }
    
    public async Task<PaginatedResult<T>> GetPagedAsync(int page, int pageSize)
    {
        var total = await _dbSet.CountAsync();
        var items = await _dbSet
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        
        return new PaginatedResult<T>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }
}

// Register in DI
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

// Usage in service
public class UserService
{
    private readonly IRepository<User> _userRepository;
    
    public UserService(IRepository<User> userRepository)
    {
        _userRepository = userRepository;
    }
    
    public async Task<User> GetUserAsync(int id)
    {
        return await _userRepository.GetByIdAsync(id);
    }
    
    public async Task<IEnumerable<User>> GetActiveUsersAsync()
    {
        return await _userRepository.FindAsync(u => u.IsActive);
    }
}
```

### Specialized Repository

For complex entities with specific queries, create specialized repositories:

```csharp
// IUserRepository.cs - Specific to User entity
public interface IUserRepository : IRepository<User>
{
    Task<User> GetByEmailAsync(string email);
    Task<User> GetWithOrdersAsync(int userId);
    Task<IEnumerable<User>> GetInactiveUsersAsync(int daysSinceLogin);
    Task<IEnumerable<UserStatisticsDto>> GetUserStatisticsAsync();
    Task<bool> EmailExistsAsync(string email);
}

// UserRepository.cs - Implementation
public class UserRepository : Repository<User>, IUserRepository
{
    private readonly ApplicationDbContext _context;
    
    public UserRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }
    
    public async Task<User> GetByEmailAsync(string email)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);
    }
    
    public async Task<User> GetWithOrdersAsync(int userId)
    {
        return await _context.Users
            .Include(u => u.Orders)
                .ThenInclude(o => o.Items)
            .FirstOrDefaultAsync(u => u.Id == userId);
    }
    
    public async Task<IEnumerable<User>> GetInactiveUsersAsync(
        int daysSinceLogin)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-daysSinceLogin);
        
        return await _context.Users
            .Where(u => u.LastLoginDate < cutoffDate)
            .ToListAsync();
    }
    
    public async Task<IEnumerable<UserStatisticsDto>> GetUserStatisticsAsync()
    {
        return await _context.Users
            .Select(u => new UserStatisticsDto
            {
                UserId = u.Id,
                Name = u.Name,
                OrderCount = u.Orders.Count,
                TotalSpent = u.Orders.Sum(o => o.Total)
            })
            .ToListAsync();
    }
    
    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _context.Users
            .AnyAsync(u => u.Email == email);
    }
}

// Register
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// Usage in controller
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    
    public UsersController(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }
    
    [HttpGet("by-email/{email}")]
    public async Task<ActionResult<UserDto>> GetByEmail(string email)
    {
        var user = await _userRepository.GetByEmailAsync(email);
        return user == null ? NotFound() : Ok(user);
    }
    
    [HttpGet("{id}/with-orders")]
    public async Task<ActionResult<UserWithOrdersDto>> GetWithOrders(int id)
    {
        var user = await _userRepository.GetWithOrdersAsync(id);
        return user == null ? NotFound() : Ok(user);
    }
    
    [HttpGet("inactive")]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetInactiveUsers(
        int daysSinceLogin = 30)
    {
        var users = await _userRepository
            .GetInactiveUsersAsync(daysSinceLogin);
        return Ok(users);
    }
    
    [HttpGet("statistics")]
    public async Task<ActionResult<IEnumerable<UserStatisticsDto>>> GetStatistics()
    {
        var stats = await _userRepository.GetUserStatisticsAsync();
        return Ok(stats);
    }
}
```

## Unit of Work Pattern

The Unit of Work pattern manages multiple repositories and coordinates changes across multiple entities in a transaction.

### Implementation

```csharp
// IUnitOfWork.cs
public interface IUnitOfWork : IDisposable
{
    IUserRepository Users { get; }
    IOrderRepository Orders { get; }
    IProductRepository Products { get; }
    
    Task<int> SaveChangesAsync();
    Task<bool> BeginTransactionAsync();
    Task<bool> CommitAsync();
    Task<bool> RollbackAsync();
}

// UnitOfWork.cs
public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IUserRepository _userRepository;
    private IOrderRepository _orderRepository;
    private IProductRepository _productRepository;
    
    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public IUserRepository Users =>
        _userRepository ??= new UserRepository(_context);
    
    public IOrderRepository Orders =>
        _orderRepository ??= new OrderRepository(_context);
    
    public IProductRepository Products =>
        _productRepository ??= new ProductRepository(_context);
    
    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }
    
    public async Task<bool> BeginTransactionAsync()
    {
        _transaction = await _context.Database
            .BeginTransactionAsync();
        return true;
    }
    
    public async Task<bool> CommitAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
            await _transaction?.CommitAsync();
            return true;
        }
        catch
        {
            await RollbackAsync();
            throw;
        }
    }
    
    public async Task<bool> RollbackAsync()
    {
        try
        {
            await _transaction?.RollbackAsync();
            return true;
        }
        finally
        {
            await _transaction?.DisposeAsync();
            _transaction = null;
        }
    }
    
    public void Dispose()
    {
        _transaction?.Dispose();
        _context?.Dispose();
    }
    
    private IDbContextTransaction _transaction;
}

// Register
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Usage: Atomic operation across multiple repositories
[HttpPost("transfer")]
public async Task<IActionResult> TransferFunds(
    int fromUserId, int toUserId, decimal amount)
{
    await _unitOfWork.BeginTransactionAsync();
    
    try
    {
        var fromUser = await _unitOfWork.Users.GetByIdAsync(fromUserId);
        var toUser = await _unitOfWork.Users.GetByIdAsync(toUserId);
        
        if (fromUser.Balance < amount)
            return BadRequest("Insufficient funds");
        
        // Modify multiple entities
        fromUser.Balance -= amount;
        toUser.Balance += amount;
        
        // Both repositories use same DbContext
        // So changes are coordinated
        await _unitOfWork.Users.UpdateAsync(fromUser);
        await _unitOfWork.Users.UpdateAsync(toUser);
        
        // Commit all changes atomically
        await _unitOfWork.CommitAsync();
        
        return Ok("Transfer successful");
    }
    catch (Exception ex)
    {
        await _unitOfWork.RollbackAsync();
        _logger.LogError(ex, "Transfer failed");
        return StatusCode(500, "Transfer failed");
    }
}
```

## Specification Pattern

The Specification pattern encapsulates query logic into reusable objects, making complex queries maintainable and testable.

### Implementation

```csharp
// Specification base class
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
}

// Concrete specification
public class ActiveUsersWithOrdersSpecification : Specification<User>
{
    public ActiveUsersWithOrdersSpecification()
    {
        // Define criteria
        Criteria = u => u.IsActive;
        
        // Include related data
        Includes.Add(u => u.Orders);
        IncludeStrings.Add("Orders.Items");
        IncludeStrings.Add("Orders.Items.Product");
        
        // Sort
        OrderBy = u => u.Name;
        
        // Paging
        Skip = 0;
        Take = 10;
        IsPagingEnabled = true;
    }
}

public class InactiveUsersSpecification : Specification<User>
{
    public InactiveUsersSpecification(int daysSinceLogin)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-daysSinceLogin);
        
        Criteria = u => u.LastLoginDate < cutoffDate;
        OrderByDescending = u => u.LastLoginDate;
    }
}

// Repository using specifications
public interface ISpecificationRepository<T> where T : class
{
    Task<T> GetEntityWithSpec(Specification<T> spec);
    Task<IReadOnlyList<T>> ListAsync(Specification<T> spec);
    Task<int> CountAsync(Specification<T> spec);
}

public class SpecificationRepository<T> : ISpecificationRepository<T> where T : class
{
    private readonly ApplicationDbContext _context;
    
    public SpecificationRepository(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<T> GetEntityWithSpec(Specification<T> spec)
    {
        return await ApplySpecification(spec).FirstOrDefaultAsync();
    }
    
    public async Task<IReadOnlyList<T>> ListAsync(Specification<T> spec)
    {
        return await ApplySpecification(spec).ToListAsync();
    }
    
    public async Task<int> CountAsync(Specification<T> spec)
    {
        return await ApplySpecification(spec).CountAsync();
    }
    
    private IQueryable<T> ApplySpecification(Specification<T> spec)
    {
        var query = _context.Set<T>().AsQueryable();
        
        // Apply criteria
        if (spec.Criteria != null)
        {
            query = query.Where(spec.Criteria);
        }
        
        // Apply includes
        query = spec.Includes.Aggregate(query,
            (current, include) => current.Include(include));
        
        // Apply include strings
        query = spec.IncludeStrings.Aggregate(query,
            (current, include) => current.Include(include));
        
        // Apply ordering
        if (spec.OrderBy != null)
        {
            query = query.OrderBy(spec.OrderBy);
        }
        
        if (spec.OrderByDescending != null)
        {
            query = query.OrderByDescending(spec.OrderByDescending);
        }
        
        // Apply paging
        if (spec.IsPagingEnabled)
        {
            query = query.Skip(spec.Skip).Take(spec.Take);
        }
        
        return query;
    }
}

// Usage
var spec = new ActiveUsersWithOrdersSpecification();
var users = await _repository.ListAsync(spec);

var inactiveSpec = new InactiveUsersSpecification(daysSinceLogin: 30);
var inactiveUsers = await _repository.ListAsync(inactiveSpec);
var inactiveCount = await _repository.CountAsync(inactiveSpec);
```

## CQRS (Command Query Responsibility Segregation)

CQRS separates read and write operations, allowing different models and optimization strategies.

### Implementation

```csharp
// Commands - Write operations
public class CreateUserCommand
{
    public string Name { get; set; }
    public string Email { get; set; }
}

public interface ICommandHandler<TCommand, TResult>
{
    Task<TResult> Handle(TCommand command);
}

public class CreateUserCommandHandler : 
    ICommandHandler<CreateUserCommand, int>
{
    private readonly ApplicationDbContext _context;
    
    public CreateUserCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<int> Handle(CreateUserCommand command)
    {
        var user = new User
        {
            Name = command.Name,
            Email = command.Email,
            CreatedAt = DateTime.UtcNow
        };
        
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        
        return user.Id;
    }
}

// Queries - Read operations
public class GetUserQuery
{
    public int UserId { get; set; }
}

public class UserQueryResult
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}

public interface IQueryHandler<TQuery, TResult>
{
    Task<TResult> Handle(TQuery query);
}

public class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserQueryResult>
{
    private readonly ApplicationDbContext _context;
    
    public GetUserQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<UserQueryResult> Handle(GetUserQuery query)
    {
        return await _context.Users
            .Where(u => u.Id == query.UserId)
            .Select(u => new UserQueryResult
            {
                Id = u.Id,
                Name = u.Name,
                Email = u.Email
            })
            .FirstOrDefaultAsync();
    }
}

// Or use MediatR library (popular CQRS implementation)
// Install: dotnet add package MediatR

// With MediatR
public class CreateUserCommand : IRequest<int>
{
    public string Name { get; set; }
    public string Email { get; set; }
}

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, int>
{
    private readonly ApplicationDbContext _context;
    
    public CreateUserCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<int> Handle(CreateUserCommand request,
        CancellationToken cancellationToken)
    {
        var user = new User { Name = request.Name, Email = request.Email };
        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
        return user.Id;
    }
}

public class GetUserQuery : IRequest<UserQueryResult>
{
    public int UserId { get; set; }
}

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserQueryResult>
{
    private readonly ApplicationDbContext _context;
    
    public GetUserQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<UserQueryResult> Handle(GetUserQuery request,
        CancellationToken cancellationToken)
    {
        return await _context.Users
            .Where(u => u.Id == request.UserId)
            .Select(u => new UserQueryResult
            {
                Id = u.Id,
                Name = u.Name,
                Email = u.Email
            })
            .FirstOrDefaultAsync(cancellationToken);
    }
}

// Register MediatR
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
    public async Task<IActionResult> Create(CreateUserCommand command)
    {
        var userId = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetUser), new { id = userId });
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<UserQueryResult>> GetUser(int id)
    {
        var user = await _mediator.Send(new GetUserQuery { UserId = id });
        return user == null ? NotFound() : Ok(user);
    }
}

// Benefits of CQRS:
// - Separate read and write optimization
// - Read model can be denormalized for performance
// - Write model can enforce strict business rules
// - Easier to scale reads (separate database, caching)
// - Clear separation of concerns
```

## Pattern Selection Guide

### Use Repository When:
- Simple CRUD operations
- Need to switch data sources
- Want to abstract EF Core details
- Small to medium application complexity

### Use Unit of Work When:
- Multiple repositories working together
- Need to ensure atomic operations
- Complex transaction management
- Coordinating changes across entities

### Use Specification When:
- Complex query logic
- Reusable query patterns
- Want to keep repositories thin
- Need testable query definitions

### Use CQRS When:
- Read and write patterns differ significantly
- Need separate scaling for reads vs writes
- Complex command validation
- Separate read models beneficial
- Event sourcing integration

## Key Takeaways

1. **Repository abstracts data access**: Easier testing and switching data sources
2. **Unit of Work coordinates repositories**: Atomic transactions across entities
3. **Specification encapsulates query logic**: Reusable, testable queries
4. **CQRS separates reads and writes**: Different optimization strategies
5. **Generic repositories reduce boilerplate**: Shared base functionality
6. **Specialized repositories add domain logic**: Custom queries per entity
7. **Lazy loading in repositories**: Initialize collections explicitly
8. **Specification pattern increases maintainability**: Complex queries readable
9. **CQRS improves scalability**: Scale reads separately from writes
10. **Choose pattern based on complexity**: Don't over-engineer simple applications

## Related Topics

- **EF Core Fundamentals** (Topic 11): Building queries
- **Advanced EF Core Patterns** (Topic 12): Query optimization
- **Domain-Driven Design** (Phase 6): How patterns fit into architecture

