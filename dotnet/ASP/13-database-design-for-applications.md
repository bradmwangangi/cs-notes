# 13. Database Design for Applications

## Overview

Good database design is fundamental to application performance, maintainability, and scalability. This topic covers relational design principles, normalization, indexing, constraints, and optimization techniques for enterprise applications.

## Relational Database Fundamentals

### Tables, Rows, and Columns

```
users table:
┌────┬────────┬─────────────────┬────────────┐
│ Id │ Name   │ Email           │ CreatedAt  │
├────┼────────┼─────────────────┼────────────┤
│ 1  │ John   │ john@example.com│ 2024-01-01 │
│ 2  │ Jane   │ jane@example.com│ 2024-01-02 │
│ 3  │ Bob    │ bob@example.com │ 2024-01-03 │
└────┴────────┴─────────────────┴────────────┘
 Col  Col     Col               Col

Each row is a record (3 users)
Each column is a field
Table is collection of records with same schema
```

## Normalization

Normalization reduces data redundancy and improves data integrity.

### First Normal Form (1NF)

**Rule**: Eliminate repeating groups. Each field contains only atomic (indivisible) values.

```csharp
// Bad: Violates 1NF (repeating groups)
public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string PhoneNumbers { get; set; }  // "555-1234, 555-5678, ..."
}

// Good: 1NF compliant
public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public ICollection<PhoneNumber> PhoneNumbers { get; set; }
}

public class PhoneNumber
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Number { get; set; }
    public User User { get; set; }
}

// Schema:
// users: Id, Name
// phone_numbers: Id, UserId, Number
//                        ↑ Foreign key to users
```

### Second Normal Form (2NF)

**Rule**: Satisfy 1NF AND all non-key attributes depend on the entire primary key (not just part of it).

```csharp
// Bad: Violates 2NF (StudentCourse has partial key dependency)
public class StudentCourse
{
    public int StudentId { get; set; }
    public int CourseId { get; set; }
    public string StudentName { get; set; }  // Depends only on StudentId, not CourseId!
    public string CourseName { get; set; }   // Depends only on CourseId, not StudentId!
    public DateTime EnrolledDate { get; set; }  // Depends on both
}

// Good: 2NF compliant
public class StudentCourse
{
    public int StudentId { get; set; }
    public int CourseId { get; set; }
    public DateTime EnrolledDate { get; set; }  // Depends on entire primary key
    
    public Student Student { get; set; }
    public Course Course { get; set; }
}

public class Student
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class Course
{
    public int Id { get; set; }
    public string Name { get; set; }
}

// Schema:
// students: Id, Name
// courses: Id, Name
// student_courses: StudentId, CourseId, EnrolledDate
```

### Third Normal Form (3NF)

**Rule**: Satisfy 2NF AND no non-key attribute depends on another non-key attribute.

```csharp
// Bad: Violates 3NF (City depends on State, not directly on Address)
public class Address
{
    public int Id { get; set; }
    public string Street { get; set; }
    public string State { get; set; }
    public string StateCapital { get; set; }  // Depends on State, not Address!
}

// Good: 3NF compliant
public class Address
{
    public int Id { get; set; }
    public string Street { get; set; }
    public int StateId { get; set; }
    
    public State State { get; set; }
}

public class State
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Capital { get; set; }
}

// Schema:
// states: Id, Name, Capital
// addresses: Id, Street, StateId (foreign key to states)
```

### Denormalization for Performance

```csharp
// Fully normalized but requires JOIN
public class UserStats
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int OrderCount { get; set; }
    
    public User User { get; set; }
}

// Schema:
// users: Id, Name, Email
// user_stats: Id, UserId, OrderCount
// Must JOIN to get user with stats

// Denormalized for performance
public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public int OrderCount { get; set; }  // Cached/denormalized
    public DateTime OrderCountUpdatedAt { get; set; }
}

// Schema:
// users: Id, Name, Email, OrderCount, OrderCountUpdatedAt
// No JOIN needed, faster queries
// Trade-off: OrderCount must be kept in sync with orders table

// Update trigger to keep denormalized data in sync:
// CREATE TRIGGER UpdateUserOrderCount
// AFTER INSERT ON orders
// BEGIN
//   UPDATE users
//   SET OrderCount = (SELECT COUNT(*) FROM orders WHERE UserId = NEW.UserId),
//       OrderCountUpdatedAt = NOW()
//   WHERE Id = NEW.UserId;
// END;
```

## Primary Keys & Constraints

### Primary Key Design

```csharp
// Option 1: Surrogate key (auto-increment integer)
public class User
{
    [Key]
    public int Id { get; set; }  // 1, 2, 3, ...
    public string Email { get; set; }
}

// Pros: Simple, efficient, stable
// Cons: No business meaning, extra storage

// Option 2: Natural key (from business data)
public class State
{
    [Key]
    public string Code { get; set; }  // "CA", "NY", "TX"
    public string Name { get; set; }
}

// Pros: Business meaning, no extra storage
// Cons: May change, slower queries, references brittle

// Option 3: Composite key (multiple columns)
public class StudentCourse
{
    public int StudentId { get; set; }
    public int CourseId { get; set; }
    
    [Key]
    public string CompositeKey => $"{StudentId}_{CourseId}";
}

// Or Fluent API:
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<StudentCourse>()
        .HasKey(sc => new { sc.StudentId, sc.CourseId });
}

// Option 4: GUID for distributed systems
public class Document
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; }
}

// Pros: Globally unique, good for distributed systems
// Cons: Larger storage (16 bytes vs 4 bytes for int), slower indexing
```

### Foreign Key Constraints

```csharp
public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public decimal Total { get; set; }
    
    public User User { get; set; }
}

// Configure foreign key behavior
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>()
        .HasOne(o => o.User)
        .WithMany(u => u.Orders)
        .HasForeignKey(o => o.UserId)
        .OnDelete(DeleteBehavior.Restrict);  // Don't allow deleting user with orders
}

// DeleteBehavior options:
// - Cascade: Delete user → automatically delete their orders
// - Restrict: Can't delete user if they have orders (MUST delete orders first)
// - SetNull: Delete user → set UserId to NULL for their orders
// - ClientSetNull: Delete user → delete orders in memory, then save
```

### Unique Constraints

```csharp
public class User
{
    public int Id { get; set; }
    
    [EmailAddress]
    public string Email { get; set; }
    
    public string Username { get; set; }
}

// Configure unique constraint
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // Single column unique
    modelBuilder.Entity<User>()
        .HasIndex(u => u.Email)
        .IsUnique();
    
    // Multiple columns unique
    modelBuilder.Entity<User>()
        .HasIndex(u => new { u.Email, u.Username })
        .IsUnique()
        .HasName("IX_Email_Username_Unique");
}

// Database enforces:
// - Only one user per email
// - Only one user per (email, username) combination
```

## Indexing & Performance

### B-Tree Indexes (Default)

```csharp
public class User
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Configure indexes
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // Single column index
    modelBuilder.Entity<User>()
        .HasIndex(u => u.Email)
        .IsUnique();
    
    // Multi-column composite index
    modelBuilder.Entity<User>()
        .HasIndex(u => new { u.CreatedAt, u.Name })
        .HasName("IX_CreatedAt_Name");
}

// Query: SELECT * FROM users WHERE Email = 'john@example.com'
// Index on Email: O(log N) lookup (very fast)
// Without index: O(N) full table scan (slow)
```

### Index Selection Strategy

```csharp
public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; }
}

// Common queries:
// 1. WHERE UserId = @id
// 2. WHERE UserId = @id AND OrderDate > @date
// 3. WHERE Status = @status
// 4. WHERE OrderDate > @date

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // Query 1 & 2: Composite index on UserId, OrderDate
    modelBuilder.Entity<Order>()
        .HasIndex(o => new { o.UserId, o.OrderDate })
        .HasName("IX_UserId_OrderDate");
    
    // Query 3: Index on Status
    modelBuilder.Entity<Order>()
        .HasIndex(o => o.Status)
        .HasName("IX_Status");
    
    // Query 4: Index on OrderDate (already covered by IX_UserId_OrderDate partially)
    // But create separate if this query is very frequent
    modelBuilder.Entity<Order>()
        .HasIndex(o => o.OrderDate)
        .HasName("IX_OrderDate");
}

// Index design rules:
// - Index columns frequently used in WHERE
// - Order matters: More selective columns first
// - Consider query SARGability (Search ARGument ability)
// - Avoid over-indexing (slows INSERT/UPDATE)
```

### Covering Indexes

```csharp
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }
}

// Query: SELECT Name, Price FROM products WHERE Id = @id
// Without covering index: Lookup index, then lookup table (2 operations)
// With covering index: All needed data in index (1 operation)

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Product>()
        .HasIndex(p => p.Id)
        .HasName("IX_Id");
}

// For SQL Server, use migration to create covering index:
// CREATE NONCLUSTERED INDEX IX_Id_Covering
// ON products (Id)
// INCLUDE (Name, Price);
```

### When NOT to Index

```csharp
// Bad candidates for indexing:

// 1. Low cardinality columns (few distinct values)
public class User
{
    public bool IsActive { get; set; }  // Only 2 values
}
// Index won't help much (50% of table either way)

// 2. Columns with NULL values in most rows
public class User
{
    public string OptionalField { get; set; }  // Mostly NULL
}
// Indexes may not be used effectively

// 3. Columns that are rarely used in WHERE
public class User
{
    public byte[] ProfilePicture { get; set; }
}
// Unused index wastes storage

// 4. Small tables
public class Country
{
    public string Code { get; set; }
    public string Name { get; set; }
}
// 250 countries - full scan is already fast
```

## Partitioning & Sharding

### Table Partitioning (Single Server)

```csharp
// Partition orders table by year
// 2022: orders_2022
// 2023: orders_2023
// 2024: orders_2024

// Query from appropriate partition automatically
public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal Total { get; set; }
}

// Create partitions in migration:
// CREATE TABLE orders_2024 (
//   ... columns ...
//   CHECK (YEAR(OrderDate) = 2024)
// ) PARTITION BY RANGE (YEAR(OrderDate));

// Benefits:
// - Faster queries on specific year range
// - Easier archiving of old data
// - Improved maintenance (rebuild/reindex per partition)
```

### Sharding (Multiple Databases)

```csharp
// Distribute users across multiple databases based on UserId
// Shard 1 (DB1): UserId 1-1000000
// Shard 2 (DB2): UserId 1000001-2000000
// Shard 3 (DB3): UserId 2000001-3000000

public interface IShardResolver
{
    string GetShardConnectionString(int userId);
}

public class ShardResolver : IShardResolver
{
    private readonly IConfiguration _config;
    
    public string GetShardConnectionString(int userId)
    {
        const int shardSize = 1000000;
        var shardNumber = (userId / shardSize) + 1;
        
        return _config.GetConnectionString($"Shard{shardNumber}");
    }
}

// Usage
[HttpGet("{userId}")]
public async Task<ActionResult<UserDto>> GetUser(int userId)
{
    var shardConnectionString = _shardResolver
        .GetShardConnectionString(userId);
    
    using var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer(shardConnectionString)
        .Options;
    
    using var context = new ApplicationDbContext(options);
    
    var user = await context.Users.FindAsync(userId);
    return user == null ? NotFound() : Ok(user);
}

// Benefits:
// - Horizontal scalability
// - Distribute load across multiple servers
// Drawbacks:
// - Complex queries across shards
// - Difficult to redistribute shards
// - Limited ACID transactions
```

## Query Optimization

### Execution Plans

```csharp
// Always check query execution plan
// SQL Server: Include Actual Execution Plan (Ctrl+L)
// Look for:
// - Table scans (bad, full table scan)
// - Index seeks (good, using index)
// - Key lookups (moderate, need covering index)

// Sample query
var users = await _context.Users
    .Where(u => u.Email == "john@example.com")
    .ToListAsync();

// Without index: Table scan (scan all rows)
// With index on Email: Index seek (jump to email value)
// Much faster!
```

### JOIN Performance

```csharp
// Prefer INNER JOIN over OUTER JOIN when possible
// More selective filters = faster queries

// Good: Filter joined table
var orders = await _context.Orders
    .Include(o => o.User)
    .Where(o => o.User.Email == "john@example.com")
    .ToListAsync();

// Better: Filter before include
var user = await _context.Users
    .FirstOrDefaultAsync(u => u.Email == "john@example.com");

var orders = await _context.Orders
    .Where(o => o.UserId == user.Id)
    .ToListAsync();
// Fewer rows in memory, faster query
```

### Statistics & Maintenance

```csharp
// Keep statistics updated for query optimizer
// SQL Server automatically, but can force:

// Update statistics
// UPDATE STATISTICS users;

// Rebuild fragmented indexes
// ALTER INDEX IX_Email ON users REBUILD;

// Reorganize mildly fragmented indexes
// ALTER INDEX IX_Email ON users REORGANIZE;

// Monitor in code
var fragmentedIndexes = await _context.Database
    .SqlQueryRaw<dynamic>(@"
        SELECT OBJECT_NAME(ips.object_id) AS TableName,
               i.name AS IndexName,
               ips.avg_fragmentation_in_percent
        FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ips
        INNER JOIN sys.indexes i ON ips.object_id = i.object_id 
            AND ips.index_id = i.index_id
        WHERE ips.avg_fragmentation_in_percent > 10
          AND ips.page_count > 1000
    ")
    .ToListAsync();
```

## Transactions & Isolation Levels

### Isolation Levels

```csharp
// Dirty Read: Read uncommitted data
// Non-Repeatable Read: Same query returns different results
// Phantom Read: Rows appear/disappear between queries

// SQL Server isolation levels:
public enum IsolationLevel
{
    ReadUncommitted,   // Dirty reads possible
    ReadCommitted,     // Default, prevents dirty reads
    RepeatableRead,    // Prevents non-repeatable reads
    Serializable,      // Prevents phantom reads (slowest)
    Snapshot           // MVCC, highest concurrency
}

// Configure in code
using var transaction = await _context.Database
    .BeginTransactionAsync(IsolationLevel.RepeatableRead);

try
{
    var user = await _context.Users.FindAsync(userId);
    user.Balance -= amount;
    
    await _context.SaveChangesAsync();
    
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}

// Or per query
var user = await _context.Users
    .FromSql($"SELECT * FROM users WITH (NOLOCK) WHERE Id = {id}")
    .FirstOrDefaultAsync();
// WITH (NOLOCK) = ReadUncommitted, faster but risky
```

## Connection Pooling & Performance

### Connection Pool Configuration

```csharp
// Configure connection pool
var connectionString = "Server=localhost;Database=MyApp;Min Pool Size=5;Max Pool Size=20;Connection Lifetime=300;";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

// Pool size:
// Min: Minimum connections to keep open
// Max: Maximum concurrent connections
// Connection Lifetime: Recycle connections after N seconds

// Recommendations:
// Min Pool Size = 5-10
// Max Pool Size = (number of app threads) * 2
// Connection Lifetime = 300 seconds (5 minutes)
```

## Key Takeaways

1. **Normalize to 3NF**: Reduces redundancy and improves integrity
2. **Denormalize for performance**: When justified by query patterns
3. **Choose primary key wisely**: Int vs GUID vs Natural key
4. **Index WHERE clauses**: Most significant performance impact
5. **Avoid over-indexing**: Slows writes, wastes storage
6. **Monitor execution plans**: Ensure queries use indexes
7. **Use covering indexes**: Include non-key columns
8. **Foreign key constraints**: Maintain referential integrity
9. **Unique constraints**: Prevent duplicates
10. **Partition large tables**: Improve maintenance and query performance
11. **Connection pooling**: Reuse connections for efficiency
12. **Keep statistics updated**: Optimizer needs accurate cardinality info

## Related Topics

- **EF Core Fundamentals** (Topic 11): How to query databases
- **Advanced EF Core Patterns** (Topic 12): Optimization techniques
- **Data Access Patterns** (Topic 14): Repository, Unit of Work

