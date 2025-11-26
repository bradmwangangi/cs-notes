# 11. Entity Framework Core Fundamentals

## Overview

Entity Framework Core (EF Core) is the modern ORM (Object-Relational Mapping) for .NET. It bridges the gap between object-oriented code and relational databases. Mastering EF Core is essential for building scalable, maintainable data access layers in enterprise ASP.NET applications.

## What is EF Core?

EF Core maps:
- **C# classes** → **Database tables**
- **Properties** → **Columns**
- **Objects** → **Rows**
- **Relationships** → **Foreign keys**

## Installation & Setup

### Install EF Core Packages

```bash
# Core EF library
dotnet add package Microsoft.EntityFrameworkCore

# Database provider (SQL Server example)
dotnet add package Microsoft.EntityFrameworkCore.SqlServer

# Tools for migrations
dotnet add package Microsoft.EntityFrameworkCore.Tools

# Pomelo for MySQL
dotnet add package Pomelo.EntityFrameworkCore.MySql

# Npgsql for PostgreSQL
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
```

### Create DbContext

```csharp
// ApplicationDbContext.cs
using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : DbContext
{
    // DbSet properties represent tables
    public DbSet<User> Users { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    
    // Constructor
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
    
    // Configure model relationships and constraints
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configuration here
    }
}

// Domain models
public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Relationships
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal Total { get; set; }
    
    // Foreign key navigation
    public User User { get; set; }
    
    // Collection navigation
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    
    // Navigation properties
    public Order Order { get; set; }
    public Product Product { get; set; }
}
```

### Register DbContext in Dependency Injection

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration
        .GetConnectionString("DefaultConnection");
    
    options.UseSqlServer(connectionString);
    
    // Optional configuration
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();  // Log parameter values
        options.LogTo(Console.WriteLine);      // Log to console
    }
});

var app = builder.Build();
app.MapControllers();
app.Run();
```

### Connection String Configuration

```json
// appsettings.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyApp;Trusted_Connection=true;TrustServerCertificate=true;"
  }
}

// appsettings.Development.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=MyApp_Dev;Integrated Security=true;"
  }
}

// appsettings.Production.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=prod-server.database.windows.net;Database=MyApp_Prod;User Id=admin;Password=secret;"
  }
}
```

## DbContext Lifecycle

Understanding DbContext lifetime is critical for performance and correctness.

### Scoped Lifetime (Recommended)

```csharp
// Program.cs
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

// DbContext is scoped to HTTP request
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    
    public UsersController(ApplicationDbContext context)
    {
        _context = context;  // Fresh DbContext per request
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id);
        
        return user == null ? NotFound() : Ok(user);
    }
}

// Per-request lifecycle:
// 1. Request arrives
// 2. New DbContext created
// 3. Handler executes with DbContext
// 4. Response sent
// 5. DbContext.Dispose() called
// 6. Database connection released
```

### Manual Scope Management

```csharp
// For non-HTTP contexts (background jobs, etc.)
using var scope = serviceScopeFactory.CreateScope();
var context = scope.ServiceProvider
    .GetRequiredService<ApplicationDbContext>();

var users = await context.Users.ToListAsync();

// Scope is disposed when exiting using block
// DbContext is disposed automatically
```

### DbContext States

```csharp
var context = new ApplicationDbContext(options);

// DETACHED: Object not tracked by context
var user = new User { Id = 1, Name = "John" };
Console.WriteLine(context.Entry(user).State);  // Detached

// ADDED: Object marked for insertion
context.Users.Add(user);
Console.WriteLine(context.Entry(user).State);  // Added

// UNCHANGED: Object tracked, no changes
var trackedUser = await context.Users.FirstAsync();
Console.WriteLine(context.Entry(trackedUser).State);  // Unchanged

// MODIFIED: Object tracked, changes detected
trackedUser.Name = "Jane";
Console.WriteLine(context.Entry(trackedUser).State);  // Modified

// DELETED: Object marked for deletion
context.Users.Remove(trackedUser);
Console.WriteLine(context.Entry(trackedUser).State);  // Deleted
```

## Basic CRUD Operations

### Create (Insert)

```csharp
[HttpPost]
public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto dto)
{
    // Create new entity
    var user = new User
    {
        Name = dto.Name,
        Email = dto.Email,
        CreatedAt = DateTime.UtcNow
    };
    
    // Add to DbSet (marks as Added)
    _context.Users.Add(user);
    
    // Save to database
    await _context.SaveChangesAsync();
    
    return CreatedAtAction(nameof(GetUser), 
        new { id = user.Id }, user);
}

// Bulk insert
[HttpPost("bulk")]
public async Task<IActionResult> BulkCreateUsers(
    IEnumerable<CreateUserDto> dtos)
{
    var users = dtos.Select(dto => new User
    {
        Name = dto.Name,
        Email = dto.Email,
        CreatedAt = DateTime.UtcNow
    }).ToList();
    
    _context.Users.AddRange(users);
    await _context.SaveChangesAsync();
    
    return Ok(new { created = users.Count });
}
```

### Read (Query)

```csharp
// Get single entity by primary key
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _context.Users.FindAsync(id);
    return user == null ? NotFound() : Ok(user);
}

// Get all entities
[HttpGet]
public async Task<ActionResult<List<UserDto>>> GetUsers()
{
    var users = await _context.Users.ToListAsync();
    return Ok(users);
}

// Filter with Where
[HttpGet("by-email/{email}")]
public async Task<ActionResult<UserDto>> GetUserByEmail(string email)
{
    var user = await _context.Users
        .FirstOrDefaultAsync(u => u.Email == email);
    
    return user == null ? NotFound() : Ok(user);
}

// Projection to DTO
[HttpGet("all")]
public async Task<ActionResult<List<UserSummaryDto>>> GetAllUsersSummary()
{
    var users = await _context.Users
        .Select(u => new UserSummaryDto
        {
            Id = u.Id,
            Name = u.Name
        })
        .ToListAsync();
    
    return Ok(users);
}

// Pagination
[HttpGet("paged")]
public async Task<ActionResult<PaginatedResult<UserDto>>> GetUsersPaged(
    int page = 1, int pageSize = 10)
{
    var total = await _context.Users.CountAsync();
    
    var users = await _context.Users
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();
    
    return Ok(new PaginatedResult<UserDto>
    {
        Items = users,
        TotalCount = total,
        Page = page,
        PageSize = pageSize
    });
}

public class PaginatedResult<T>
{
    public List<T> Items { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
```

### Update (Modify)

```csharp
// Update attached entity
[HttpPut("{id}")]
public async Task<IActionResult> UpdateUser(int id, UpdateUserDto dto)
{
    var user = await _context.Users.FindAsync(id);
    if (user == null)
        return NotFound();
    
    // Modify properties (tracked by DbContext)
    user.Name = dto.Name;
    user.Email = dto.Email;
    
    // SaveChangesAsync detects changes and updates
    await _context.SaveChangesAsync();
    
    return NoContent();
}

// Update detached entity
[HttpPut("detached/{id}")]
public async Task<IActionResult> UpdateUserDetached(
    int id, UpdateUserDto dto)
{
    var user = new User
    {
        Id = id,
        Name = dto.Name,
        Email = dto.Email
    };
    
    // Attach detached entity
    _context.Users.Update(user);
    
    // Mark as modified
    _context.Entry(user).State = EntityState.Modified;
    
    await _context.SaveChangesAsync();
    
    return NoContent();
}

// Partial update
[HttpPatch("{id}")]
public async Task<IActionResult> PatchUser(int id, UpdateUserDto dto)
{
    var user = await _context.Users.FindAsync(id);
    if (user == null)
        return NotFound();
    
    // Only update non-null properties
    if (!string.IsNullOrWhiteSpace(dto.Name))
        user.Name = dto.Name;
    
    if (!string.IsNullOrWhiteSpace(dto.Email))
        user.Email = dto.Email;
    
    await _context.SaveChangesAsync();
    
    return NoContent();
}
```

### Delete (Remove)

```csharp
// Delete single entity
[HttpDelete("{id}")]
public async Task<IActionResult> DeleteUser(int id)
{
    var user = await _context.Users.FindAsync(id);
    if (user == null)
        return NotFound();
    
    _context.Users.Remove(user);
    await _context.SaveChangesAsync();
    
    return NoContent();
}

// Delete by condition
[HttpDelete("inactive")]
public async Task<IActionResult> DeleteInactiveUsers()
{
    var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
    
    var inactiveUsers = await _context.Users
        .Where(u => u.LastLoginDate < sixMonthsAgo)
        .ToListAsync();
    
    _context.Users.RemoveRange(inactiveUsers);
    await _context.SaveChangesAsync();
    
    return Ok(new { deleted = inactiveUsers.Count });
}

// Bulk delete with ExecuteDeleteAsync (EF Core 7+, more efficient)
[HttpDelete("bulk-inactive")]
public async Task<IActionResult> BulkDeleteInactiveUsers()
{
    var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
    
    var deletedCount = await _context.Users
        .Where(u => u.LastLoginDate < sixMonthsAgo)
        .ExecuteDeleteAsync();
    
    return Ok(new { deleted = deletedCount });
}
```

## Entity Relationships

### One-to-Many Relationship

```csharp
public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    
    // Collection navigation
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }  // Foreign key
    public decimal Total { get; set; }
    
    // Reference navigation
    public User User { get; set; }
}

// Configure relationship
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>()
        .HasOne(o => o.User)
        .WithMany(u => u.Orders)
        .HasForeignKey(o => o.UserId)
        .OnDelete(DeleteBehavior.Cascade);
}

// Query with relationship
[HttpGet("{id}/orders")]
public async Task<ActionResult<List<OrderDto>>> GetUserOrders(int id)
{
    var orders = await _context.Orders
        .Where(o => o.UserId == id)
        .ToListAsync();
    
    return Ok(orders);
}
```

### Many-to-Many Relationship

```csharp
public class Course
{
    public int Id { get; set; }
    public string Name { get; set; }
    
    // Junction table represented as collection
    public ICollection<StudentCourse> StudentCourses { get; set; } 
        = new List<StudentCourse>();
}

public class Student
{
    public int Id { get; set; }
    public string Name { get; set; }
    
    public ICollection<StudentCourse> StudentCourses { get; set; } 
        = new List<StudentCourse>();
}

public class StudentCourse
{
    public int StudentId { get; set; }
    public int CourseId { get; set; }
    public DateTime EnrolledDate { get; set; }
    public decimal Grade { get; set; }
    
    public Student Student { get; set; }
    public Course Course { get; set; }
}

// Configure many-to-many
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<StudentCourse>()
        .HasKey(sc => new { sc.StudentId, sc.CourseId });
    
    modelBuilder.Entity<StudentCourse>()
        .HasOne(sc => sc.Student)
        .WithMany(s => s.StudentCourses)
        .HasForeignKey(sc => sc.StudentId);
    
    modelBuilder.Entity<StudentCourse>()
        .HasOne(sc => sc.Course)
        .WithMany(c => c.StudentCourses)
        .HasForeignKey(sc => sc.CourseId);
}

// Query many-to-many
var studentCourses = await _context.StudentCourses
    .Include(sc => sc.Course)
    .Where(sc => sc.StudentId == studentId)
    .ToListAsync();
```

### Self-Referencing Relationship

```csharp
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int? ParentCategoryId { get; set; }
    
    // Parent reference
    public Category ParentCategory { get; set; }
    
    // Children collection
    public ICollection<Category> SubCategories { get; set; } 
        = new List<Category>();
}

// Configure
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Category>()
        .HasOne(c => c.ParentCategory)
        .WithMany(c => c.SubCategories)
        .HasForeignKey(c => c.ParentCategoryId)
        .OnDelete(DeleteBehavior.Restrict);
}

// Query hierarchical data
var category = await _context.Categories
    .Include(c => c.SubCategories)
    .FirstOrDefaultAsync(c => c.Id == id);
```

## Loading Related Data

### Eager Loading (Include)

```csharp
// Load all related data upfront
var order = await _context.Orders
    .Include(o => o.User)  // Load User
    .Include(o => o.Items) // Load OrderItems
        .ThenInclude(oi => oi.Product)  // Load Product for each item
    .FirstOrDefaultAsync(o => o.Id == orderId);

// Now access related data (no additional queries)
var userName = order.User.Name;  // Already loaded
foreach (var item in order.Items)
{
    var productName = item.Product.Name;  // Already loaded
}
```

### Explicit Loading

```csharp
var order = await _context.Orders.FindAsync(orderId);

// Load related data explicitly
await _context.Entry(order)
    .Reference(o => o.User)
    .LoadAsync();

await _context.Entry(order)
    .Collection(o => o.Items)
    .LoadAsync();

// Now data is available
var userName = order.User.Name;
```

### Lazy Loading (Not Recommended)

```csharp
// Enable lazy loading
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(connectionString)
        .UseLazyLoadingProxies();  // Requires virtual properties
});

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    
    // Virtual enables lazy loading
    public virtual User User { get; set; }
    public virtual ICollection<OrderItem> Items { get; set; }
}

// Lazy loading triggers database query on access
var order = await _context.Orders.FindAsync(orderId);
var userName = order.User.Name;  // Extra query triggered!

// Issues:
// - N+1 query problem
// - Unpredictable performance
// - Harder to optimize
// Better to use eager loading or explicit loading
```

## Tracking vs No-Tracking Queries

### Tracking Queries (Default)

```csharp
// DbContext tracks changes
var user = await _context.Users.FindAsync(id);

user.Name = "Updated Name";

// Changes automatically saved
await _context.SaveChangesAsync();  // Updates database
```

### No-Tracking Queries (for Read-Only)

```csharp
// Useful for read-only queries or reporting
var users = await _context.Users
    .AsNoTracking()
    .ToListAsync();

// DbContext doesn't track these entities
// Better performance for large result sets

// Changes won't be saved
users[0].Name = "New Name";
await _context.SaveChangesAsync();  // No changes saved
```

### Untracking Tracked Entities

```csharp
var user = await _context.Users.FindAsync(id);

// Detach entity
_context.Entry(user).State = EntityState.Detached;

user.Name = "New Name";

// Changes not tracked or saved
await _context.SaveChangesAsync();
```

## Save Changes & Transactions

### Basic SaveChangesAsync

```csharp
[HttpPost]
public async Task<IActionResult> CreateUser(CreateUserDto dto)
{
    var user = new User { Name = dto.Name, Email = dto.Email };
    
    _context.Users.Add(user);
    
    // Executes INSERT statement
    var changes = await _context.SaveChangesAsync();
    
    // Returns number of entities affected
    return Ok(new { created = changes });
}
```

### Transactions

```csharp
[HttpPost("transfer")]
public async Task<IActionResult> TransferFunds(
    int fromAccountId, int toAccountId, decimal amount)
{
    using var transaction = await _context.Database
        .BeginTransactionAsync();
    
    try
    {
        var fromAccount = await _context.Accounts
            .FindAsync(fromAccountId);
        var toAccount = await _context.Accounts
            .FindAsync(toAccountId);
        
        if (fromAccount.Balance < amount)
            return BadRequest("Insufficient funds");
        
        fromAccount.Balance -= amount;
        toAccount.Balance += amount;
        
        await _context.SaveChangesAsync();
        
        // Commit transaction
        await transaction.CommitAsync();
        
        return Ok("Transfer successful");
    }
    catch (Exception ex)
    {
        // Rollback on error
        await transaction.RollbackAsync();
        
        _logger.LogError(ex, "Transfer failed");
        return StatusCode(500, "Transfer failed");
    }
}

// Explicit transaction scope
var strategy = _context.Database.CreateExecutionStrategy();

await strategy.ExecuteAsync(async () =>
{
    using var transaction = await _context.Database
        .BeginTransactionAsync();
    
    try
    {
        // Database operations
        
        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
});
```

## Configuration & Conventions

### Data Annotations

```csharp
public class User
{
    [Key]  // Primary key
    public int Id { get; set; }
    
    [Required]  // Not nullable
    [StringLength(100)]  // Max length
    public string Name { get; set; }
    
    [EmailAddress]
    [Column("email_address")]  // Custom column name
    public string Email { get; set; }
    
    [Range(0, 150)]
    public int Age { get; set; }
    
    [DataType(DataType.DateTime)]
    public DateTime CreatedAt { get; set; }
    
    [NotMapped]  // Exclude from database
    public string FullName => Name;
}
```

### Fluent API Configuration

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<User>(entity =>
    {
        // Primary key
        entity.HasKey(e => e.Id);
        
        // Properties
        entity.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);
        
        entity.Property(e => e.Email)
            .IsRequired()
            .HasMaxLength(255);
        
        // Indexes
        entity.HasIndex(e => e.Email)
            .IsUnique();
        
        entity.HasIndex(e => e.CreatedAt);
        
        // Table name
        entity.ToTable("users");
        
        // Column configuration
        entity.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("GETUTCDATE()");
    });
}
```

## Key Takeaways

1. **DbContext is the bridge**: Object ↔ Database
2. **Scoped lifetime**: One DbContext per HTTP request
3. **Change tracking is automatic**: Modified entities tracked
4. **SaveChangesAsync commits changes**: Single roundtrip to database
5. **Relationships define structure**: One-to-many, many-to-many
6. **Eager loading prevents N+1 queries**: Include related data upfront
7. **No-tracking for read-only**: Better performance
8. **Transactions for consistency**: Multiple operations atomic
9. **Configuration via Fluent API**: Most powerful approach
10. **DbSet<T> is queryable**: Uses IQueryable for deferred execution

## Related Topics

- **Advanced EF Core Patterns** (Topic 12): Query optimization, computed columns
- **Database Design** (Topic 13): Indexes, constraints, normalization
- **Data Access Patterns** (Topic 14): Repository pattern, Unit of Work

