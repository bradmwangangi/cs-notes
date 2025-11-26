# 12. Advanced EF Core Patterns

## Overview

While EF Core fundamentals cover basic CRUD and relationships, advanced patterns unlock performance optimization, complex querying, and sophisticated data operations required in enterprise systems.

## Query Optimization

### The N+1 Problem

```csharp
// Problem: N+1 queries
var users = await _context.Users.ToListAsync();  // Query 1

foreach (var user in users)
{
    var orders = await _context.Orders
        .Where(o => o.UserId == user.Id)
        .ToListAsync();  // Query 2, 3, 4, ... N+1
    
    user.Orders = orders;
}
// Total queries: 1 + N (where N = number of users)
// For 1000 users = 1001 queries!

// Solution: Use Include
var users = await _context.Users
    .Include(u => u.Orders)
    .ToListAsync();  // Single query with JOIN
```

### Query Splitting (EF Core 5+)

```csharp
// By default, complex includes create large joins
var orders = await _context.Orders
    .Include(o => o.User)
    .Include(o => o.Items)
        .ThenInclude(oi => oi.Product)
    .ToListAsync();
// Creates complex multi-join query

// Split into multiple simpler queries
var orders = await _context.Orders
    .Include(o => o.User)
    .Include(o => o.Items
        .ThenInclude(oi => oi.Product))
    .AsSplitQuery()
    .ToListAsync();
// Executes: 1 query for Orders, 1 for Users, 1 for Items, 1 for Products
// Simpler queries, easier for optimizer
```

### Filtering Before Including

```csharp
// Bad: Load all orders, then filter
var user = await _context.Users
    .Include(u => u.Orders)
    .Where(u => u.Id == userId)
    .FirstOrDefaultAsync();
// Loads all orders for user, then filters in memory (inefficient)

// Good: Filter first, then include
var user = await _context.Users
    .Where(u => u.Id == userId)
    .Include(u => u.Orders)
    .FirstOrDefaultAsync();
// Filters first, then only loads matching user's orders
```

### Selective Projection

```csharp
// Bad: Load entire entities when you only need certain fields
var users = await _context.Users
    .Include(u => u.Orders)
    .ToListAsync();
// Loads all user and order data

// Good: Project to DTO with only needed fields
var userDtos = await _context.Users
    .Select(u => new UserSummaryDto
    {
        Id = u.Id,
        Name = u.Name,
        OrderCount = u.Orders.Count
    })
    .ToListAsync();
// Only loads necessary data
```

### Compiled Queries (EF Core 5+)

```csharp
// Define compiled query once
private static readonly Func<ApplicationDbContext, int, Task<User>> 
    GetUserCompiledQuery = EF.CompileAsyncQuery((
        ApplicationDbContext context, int id) =>
        context.Users
            .Include(u => u.Orders)
            .FirstOrDefault(u => u.Id == id));

// Use compiled query (faster for frequently called queries)
var user = await GetUserCompiledQuery(_context, userId);

// Benefits:
// - Query compiled once, executed multiple times
// - Reduces query tree construction overhead
// - Especially helpful for complex queries

// Multiple parameters
private static readonly Func<ApplicationDbContext, int, int, Task<List<Order>>> 
    GetOrdersByUserAndStatusCompiledQuery = EF.CompileAsyncQuery((
        ApplicationDbContext context, int userId, int status) =>
        context.Orders
            .Where(o => o.UserId == userId && o.Status == status)
            .ToList());

var orders = await GetOrdersByUserAndStatusCompiledQuery(
    _context, userId, (int)OrderStatus.Pending);
```

### AsNoTracking for Reporting Queries

```csharp
// Reporting query - don't need change tracking
var monthlyRevenue = await _context.Orders
    .AsNoTracking()
    .Where(o => o.OrderDate.Month == DateTime.UtcNow.Month)
    .GroupBy(o => o.OrderDate.Date)
    .Select(g => new
    {
        Date = g.Key,
        Revenue = g.Sum(o => o.Total),
        OrderCount = g.Count()
    })
    .ToListAsync();

// Performance benefit: No change tracking overhead
```

## Raw SQL Queries

### SQL Queries with FromSql

```csharp
// Execute raw SQL, map to entities
var users = await _context.Users
    .FromSql($"SELECT * FROM users WHERE status = 'active'")
    .ToListAsync();

// With parameters (prevents SQL injection)
var status = "active";
var users = await _context.Users
    .FromSql($"SELECT * FROM users WHERE status = {status}")
    .ToListAsync();

// Complex query
var result = await _context.Users
    .FromSql($@"
        SELECT u.*, COUNT(o.Id) as OrderCount
        FROM users u
        LEFT JOIN orders o ON u.Id = o.UserId
        WHERE u.CreatedAt > {DateTime.UtcNow.AddMonths(-1)}
        GROUP BY u.Id")
    .ToListAsync();

// Warning: FromSql returns IQueryable, can't be combined with LINQ
// This will fail:
var users = await _context.Users
    .FromSql($"SELECT * FROM users")
    .Where(u => u.Id > 100)  // Error: Can't compose on raw SQL
    .ToListAsync();

// Solution: Use AsEnumerable() and filter in memory (if needed)
var users = await _context.Users
    .FromSql($"SELECT * FROM users")
    .AsEnumerable()
    .Where(u => u.Id > 100)
    .ToList();
```

### ExecuteUpdate and ExecuteDelete (EF Core 7+)

```csharp
// Update without loading entities (more efficient)
var updated = await _context.Users
    .Where(u => u.Status == "inactive")
    .ExecuteUpdateAsync(s => s
        .SetProperty(u => u.Status, "deleted")
        .SetProperty(u => u.DeletedAt, DateTime.UtcNow));

return Ok(new { updated });

// Delete without loading entities
var deleted = await _context.Users
    .Where(u => u.CreatedAt < DateTime.UtcNow.AddYears(-1))
    .ExecuteDeleteAsync();

return Ok(new { deleted });

// Bulk update with computation
var updated = await _context.Products
    .Where(p => p.Stock < 10)
    .ExecuteUpdateAsync(s => s
        .SetProperty(p => p.Status, "low-stock")
        .SetProperty(p => p.UpdatedAt, DateTime.UtcNow)
        .SetProperty(p => p.Price, p => p.Price * 1.1M));  // Increase by 10%

// Benefits:
// - No entities loaded into memory
// - Single database command
// - Much faster for bulk operations
```

### Stored Procedures

```csharp
// Configure stored procedure mapping
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // Map function to stored procedure
    modelBuilder.Entity<User>()
        .HasKey(u => u.Id);
    
    // For basic stored procedures returning entities
    modelBuilder.HasDbFunction(typeof(StoredProcedures)
        .GetMethod(nameof(StoredProcedures.GetUsersByStatus)))
        .HasName("usp_GetUsersByStatus");
}

public class StoredProcedures
{
    [DbFunction("usp_GetUsersByStatus")]
    public IQueryable<User> GetUsersByStatus(string status)
    {
        throw new NotImplementedException();
    }
}

// Call stored procedure
var users = _context.GetUsersByStatus("active").ToList();

// For stored procedures that don't return entities
using var command = _context.Database.GetDbConnection().CreateCommand();
command.CommandText = "usp_ArchiveOldOrders";
command.CommandType = CommandType.StoredProcedure;

// Add parameters
var parameter = command.CreateParameter();
parameter.ParameterName = "@daysBefore";
parameter.Value = 365;
command.Parameters.Add(parameter);

await _context.Database.OpenConnectionAsync();
await command.ExecuteNonQueryAsync();
```

## Computed Properties & Generated Columns

### Shadow Properties

```csharp
// Properties not in the C# class, only in database
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<User>()
        .Property<DateTime>("CreatedAt")
        .HasDefaultValueSql("GETUTCDATE()");
    
    // Set shadow property
    var user = new User { Name = "John" };
    _context.Entry(user).Property("CreatedAt")
        .CurrentValue = DateTime.UtcNow;
    
    _context.Users.Add(user);
    await _context.SaveChangesAsync();
}

// Query shadow properties
var usersAfterDate = await _context.Users
    .Where(u => EF.Property<DateTime>(u, "CreatedAt") > cutoffDate)
    .ToListAsync();
```

### Computed Columns (Database-Generated)

```csharp
public class Product
{
    public int Id { get; set; }
    public decimal ListPrice { get; set; }
    public decimal DiscountPercentage { get; set; }
    
    // Computed column: Database calculates value
    public decimal SalePrice { get; set; }
}

// Configure computed column
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Product>()
        .Property(p => p.SalePrice)
        .HasComputedColumnSql("ListPrice * (1 - DiscountPercentage / 100)");
}

// Or in migration:
// migrationBuilder.Sql(
//     @"ALTER TABLE products
//     ADD SalePrice AS ListPrice * (1 - DiscountPercentage / 100)");

// Value is calculated by database automatically
var product = new Product 
{ 
    ListPrice = 100, 
    DiscountPercentage = 10 
};
_context.Products.Add(product);
await _context.SaveChangesAsync();

// SalePrice is now 90 (100 * 0.9)
```

### Generated Values

```csharp
public class Order
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    
    // Generate GUID on insert
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid OrderNumber { get; set; }
    
    // Generate on insert and update
    [Timestamp]  // SQL Server rowversion
    public byte[] Version { get; set; }
    
    public DateTime OrderDate { get; set; }
}

// Configure
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>()
        .Property(o => o.OrderNumber)
        .HasDefaultValueSql("NEWID()");
    
    // Concurrency token (checked on update)
    modelBuilder.Entity<Order>()
        .Property(o => o.Version)
        .IsRowVersion();
}
```

## Value Conversions

### Custom Type Conversions

```csharp
// Convert enum to string in database
public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered
}

public class Order
{
    public int Id { get; set; }
    public OrderStatus Status { get; set; }  // Stored as string
}

// Configure conversion
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>()
        .Property(o => o.Status)
        .HasConversion(
            v => v.ToString(),  // C# enum to string
            v => (OrderStatus)Enum.Parse(typeof(OrderStatus), v));  // string to enum
}

// Or using ValueConverter
modelBuilder.Entity<Order>()
    .Property(o => o.Status)
    .HasConversion<string>();  // Auto enum-to-string
```

### JSON Conversions (EF Core 7+)

```csharp
public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    
    // Complex type stored as JSON in database
    public UserPreferences Preferences { get; set; }
}

public class UserPreferences
{
    public string Theme { get; set; }
    public string Language { get; set; }
    public bool EmailNotifications { get; set; }
}

// Configure JSON conversion
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<User>()
        .Property(u => u.Preferences)
        .HasConversion(
            v => JsonSerializer.Serialize(v, null),
            v => JsonSerializer.Deserialize<UserPreferences>(v, null));
}

// Query JSON properties
var users = await _context.Users
    .Where(u => u.Preferences.Theme == "dark")
    .ToListAsync();

// SQL uses JSON functions (SQL Server, PostgreSQL support)
// SELECT * FROM users WHERE JSON_VALUE(preferences, '$.theme') = 'dark'
```

## Temporal Tables (SQL Server 2016+)

```csharp
public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}

// Configure as temporal table
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<User>()
        .ToTable("users", t => t.IsTemporal());
}

// EF Core automatically adds:
// - SysStartTime (when row started being valid)
// - SysEndTime (when row stopped being valid)

// Query history
var userHistory = await _context.Users
    .TemporalAll()  // Include deleted versions
    .Where(u => u.Id == userId)
    .OrderBy(u => EF.Property<DateTime>(u, "SysStartTime"))
    .ToListAsync();

// Get state at specific point in time
var userAsOf = await _context.Users
    .TemporalAsOf(DateTime.UtcNow.AddDays(-7))  // As it was 7 days ago
    .FirstOrDefaultAsync(u => u.Id == userId);
```

## Concurrency & Concurrency Tokens

### Optimistic Concurrency

```csharp
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    
    // Concurrency token (checked on update/delete)
    [Timestamp]
    public byte[] RowVersion { get; set; }
}

// Configure
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Product>()
        .Property(p => p.RowVersion)
        .IsRowVersion();
}

// Update with concurrency check
[HttpPut("{id}")]
public async Task<IActionResult> UpdateProduct(int id, UpdateProductDto dto)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
        return NotFound();
    
    product.Price = dto.Price;
    
    try
    {
        await _context.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException ex)
    {
        // Another process modified the product
        var entry = ex.Entries.Single();
        var databaseValues = entry.GetDatabaseValues();
        var databaseVersion = (byte[])databaseValues["RowVersion"];
        
        return Conflict(new
        {
            message = "Product was modified by another user",
            databaseVersion
        });
    }
    
    return NoContent();
}
```

### Custom Concurrency Properties

```csharp
public class Order
{
    public int Id { get; set; }
    public decimal Total { get; set; }
    
    // Use simple version number instead of timestamp
    [ConcurrencyCheck]
    public int Version { get; set; }
}

// Configure
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>()
        .Property(o => o.Version)
        .IsConcurrencyToken();
}

// Update increments version
product.Price = 100;
product.Version += 1;

try
{
    await _context.SaveChangesAsync();
}
catch (DbUpdateConcurrencyException)
{
    // Handle conflict - another process incremented version
}
```

## Filtering & Query Filters

### Global Query Filters

```csharp
public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime DeletedAt { get; set; }
}

// Automatically filter out soft-deleted records
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>()
        .HasQueryFilter(o => !o.IsDeleted);
}

// All queries automatically exclude deleted orders
var orders = await _context.Orders.ToListAsync();  
// WHERE IsDeleted = 0

// Bypass filter if needed
var allOrders = await _context.Orders
    .IgnoreQueryFilters()
    .ToListAsync();  // Includes deleted
```

## Bulk Operations

### EF Core Plus (Third-Party)

```csharp
// Install: dotnet add package EFCore.BulkExtensions

// Bulk insert (very fast)
var users = new List<User> { /* ... */ };
await _context.BulkInsertAsync(users);

// Bulk update
await _context.BulkUpdateAsync(users);

// Bulk delete
await _context.BulkDeleteAsync(users);

// Bulk upsert
await _context.BulkInsertOrUpdateAsync(users);

// Performance: 1000 records in ~50ms vs 2+ seconds with SaveChangesAsync
```

## Key Takeaways

1. **Avoid N+1 queries**: Use Include() or AsSplitQuery()
2. **Compiled queries for frequent operations**: Reduce compilation overhead
3. **AsNoTracking for read-only**: Performance benefit
4. **ExecuteUpdate/ExecuteDelete for bulk**: Much faster than loading entities
5. **Raw SQL for complex queries**: When LINQ isn't sufficient
6. **Computed columns for calculations**: Database-level computation
7. **Value converters for custom types**: JSON, enums, complex objects
8. **Temporal tables for auditing**: Built-in history tracking
9. **Optimistic concurrency for safety**: Detect update conflicts
10. **Bulk operations for mass changes**: Use EF Core Plus or SQL directly

## Related Topics

- **EF Core Fundamentals** (Topic 11): Basic CRUD and relationships
- **Database Design** (Topic 13): Indexes, performance optimization
- **Data Access Patterns** (Topic 14): Repository, Unit of Work

