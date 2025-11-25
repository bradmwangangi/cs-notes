# Chapter 4: Data Access & Entity Framework Core

## 4.1 Entity Framework Core Fundamentals

Entity Framework Core (EF Core) is an Object-Relational Mapper (ORM) that maps database tables to C# classes and queries to SQL.

### DbContext

The `DbContext` is the main gateway to the database:

```csharp
public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<Product> Products { get; set; }
    
    public AppDbContext(DbContextOptions<AppDbContext> options) 
        : base(options)
    {
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure entities, relationships, constraints
        base.OnModelCreating(modelBuilder);
    }
}
```

**DbSet<T>** represents a table in database:
- `DbSet<User> Users` maps to `Users` table
- Provides LINQ queries against that table
- Used to add, update, delete records

### Entities

Entities are C# classes representing database records:

```csharp
public class User
{
    // Primary key (EF recognizes Id or UserId by convention)
    public int Id { get; set; }
    
    // Properties map to columns
    public string Email { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties for relationships
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

public class Order
{
    public int Id { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal Total { get; set; }
    
    // Foreign key
    public int UserId { get; set; }
    
    // Navigation property (relationship)
    public User User { get; set; }
    
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

public class OrderItem
{
    public int Id { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    
    public int OrderId { get; set; }
    public Order Order { get; set; }
    
    public int ProductId { get; set; }
    public Product Product { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Sku { get; set; }
    public decimal Price { get; set; }
    
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
```

**Key conventions:**
- Property named `Id` or `{EntityName}Id` is primary key
- Properties become columns (nullable if `string?` or nullable types)
- `ICollection<T>` properties are one-to-many relationships
- Navigation properties enable accessing related data

### Registration and Configuration

```csharp
// In Program.cs
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        // Optimization: retry strategy for transient failures
        sqlOptions => sqlOptions.EnableRetryOnFailure()
    )
);
```

---

## 4.2 Relationships and Navigation Properties

### One-to-Many Relationship

One user has many orders:

```csharp
public class User
{
    public int Id { get; set; }
    public string Email { get; set; }
    
    // One-to-many: one user, many orders
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

public class Order
{
    public int Id { get; set; }
    
    // Foreign key
    public int UserId { get; set; }
    
    // Navigation back to user
    public User User { get; set; }
}
```

**In database:**
```
Users table
- Id (PK)
- Email

Orders table
- Id (PK)
- UserId (FK → Users.Id)
```

### Many-to-Many Relationship

Students enroll in many courses, courses have many students:

```csharp
public class Student
{
    public int Id { get; set; }
    public string Name { get; set; }
    
    public ICollection<Course> Courses { get; set; } = new List<Course>();
}

public class Course
{
    public int Id { get; set; }
    public string Title { get; set; }
    
    public ICollection<Student> Students { get; set; } = new List<Student>();
}

// EF Core automatically creates join table
```

**In database:**
```
Students table
- Id (PK)
- Name

Courses table
- Id (PK)
- Title

StudentCourse (join table, auto-created)
- StudentId (FK → Students.Id)
- CourseId (FK → Courses.Id)
```

### Configuring Relationships

When conventions aren't enough, configure explicitly:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // One-to-many: explicit
    modelBuilder.Entity<Order>()
        .HasOne(o => o.User)
        .WithMany(u => u.Orders)
        .HasForeignKey(o => o.UserId)
        .OnDelete(DeleteBehavior.Cascade);  // Delete orders when user deleted
    
    // Many-to-many: explicit
    modelBuilder.Entity<Student>()
        .HasMany(s => s.Courses)
        .WithMany(c => c.Students)
        .UsingEntity<Enrollment>(  // Custom join table
            j => j.HasOne<Course>().WithMany(),
            j => j.HasOne<Student>().WithMany()
        );
}
```

**Cascade delete options:**
- `Cascade` - Delete related records when parent deleted
- `SetNull` - Set foreign key to null (parent deleted, child orphaned)
- `Restrict` - Prevent deletion if children exist
- `NoAction` - No automatic action (database may enforce constraint)

---

## 4.3 LINQ Queries

LINQ (Language Integrated Query) translates to SQL:

### Basic Queries

```csharp
var context = new AppDbContext();

// Get single user by ID
var user = await context.Users.FirstOrDefaultAsync(u => u.Id == 1);

// Get all users
var allUsers = await context.Users.ToListAsync();

// Filter
var activeUsers = await context.Users
    .Where(u => u.Status == "Active")
    .ToListAsync();

// Select specific columns
var userEmails = await context.Users
    .Select(u => u.Email)
    .ToListAsync();

// Count
int userCount = await context.Users.CountAsync();

// Check existence
bool exists = await context.Users.AnyAsync(u => u.Id == 1);
```

**Key LINQ methods:**
- `First()` / `FirstOrDefault()` - Get first match
- `Single()` / `SingleOrDefault()` - Get exactly one match
- `Last()` / `LastOrDefault()` - Get last match
- `Where()` - Filter
- `Select()` - Project to different shape
- `OrderBy()` / `OrderByDescending()` - Sort
- `Skip()` / `Take()` - Pagination
- `Count()` / `Any()` / `All()` - Aggregation

### Eager Loading

Load related data eagerly to avoid N+1 queries:

```csharp
// Bad: N+1 query problem
var users = await context.Users.ToListAsync();
foreach (var user in users)
{
    var orders = await context.Orders
        .Where(o => o.UserId == user.Id)
        .ToListAsync();  // One query per user!
    // Total: 1 + n queries
}

// Good: Eager loading with Include
var users = await context.Users
    .Include(u => u.Orders)
    .ToListAsync();
// Total: 1 query with JOIN

foreach (var user in users)
{
    var orders = user.Orders;  // Already loaded, no query
}
```

**Multiple levels:**
```csharp
var orders = await context.Orders
    .Include(o => o.User)           // User for this order
    .Include(o => o.Items)          // Items in this order
        .ThenInclude(oi => oi.Product)  // Product for each item
    .ToListAsync();
```

**Multiple includes:**
```csharp
var orders = await context.Orders
    .Include(o => o.User)
    .Include(o => o.Items)
    .Include(o => o.ShippingAddress)
    .ToListAsync();
```

### Filtering and Projection

```csharp
// Complex filtering
var recentOrders = await context.Orders
    .Where(o => o.OrderDate > DateTime.Now.AddMonths(-1))
    .Where(o => o.Total > 100)
    .Where(o => o.Status == "Completed")
    .ToListAsync();

// Project to different shape (DTO)
var orderDtos = await context.Orders
    .Include(o => o.Items)
    .Select(o => new OrderDto
    {
        Id = o.Id,
        OrderDate = o.OrderDate,
        Total = o.Total,
        ItemCount = o.Items.Count,
        UserName = o.User.Name  // Requires Include(o => o.User)
    })
    .ToListAsync();
```

**Benefits of projection:**
- Returns only needed columns (smaller data transfer)
- Shapes response for client
- Avoids sending sensitive data

### Pagination

```csharp
public async Task<List<UserDto>> GetUsersAsync(int pageNumber, int pageSize)
{
    var skip = (pageNumber - 1) * pageSize;
    
    return await context.Users
        .OrderBy(u => u.Id)  // Must order before Skip/Take
        .Skip(skip)
        .Take(pageSize)
        .Select(u => new UserDto { /* ... */ })
        .ToListAsync();
}

// Usage: page 2 with 10 items per page
var users = await GetUsersAsync(pageNumber: 2, pageSize: 10);
```

### Aggregation

```csharp
// Total sales
var totalSales = await context.Orders
    .Where(o => o.Status == "Completed")
    .SumAsync(o => o.Total);

// Average order value
var avgOrderValue = await context.Orders
    .AverageAsync(o => o.Total);

// Group by
var salesByUser = await context.Orders
    .GroupBy(o => o.UserId)
    .Select(g => new
    {
        UserId = g.Key,
        OrderCount = g.Count(),
        TotalSpent = g.Sum(o => o.Total)
    })
    .ToListAsync();
```

---

## 4.4 Creating and Updating Records

### Adding Records

```csharp
var newUser = new User
{
    Email = "john@example.com",
    Name = "John Doe",
    CreatedAt = DateTime.UtcNow
};

context.Users.Add(newUser);
await context.SaveChangesAsync();

// newUser.Id is now populated (by database)
```

**Adding related records:**
```csharp
var user = new User { /* ... */ };
var order = new Order { /* ... */ };

user.Orders.Add(order);  // Add to navigation collection
context.Users.Add(user);

// When saving user, order is also saved
await context.SaveChangesAsync();
```

**AddRange for bulk adds:**
```csharp
var users = new List<User> { /* ... */ };
context.Users.AddRange(users);
await context.SaveChangesAsync();
```

### Updating Records

**Attached entity approach:**
```csharp
// Get entity from database
var user = await context.Users.FirstOrDefaultAsync(u => u.Id == 1);

// Modify properties
user.Email = "newemail@example.com";
user.Name = "New Name";

// Save changes (EF tracks modifications)
await context.SaveChangesAsync();
```

**Detached entity approach:**
```csharp
// Entity not from database (deserialized from JSON)
var user = new User { Id = 1, Email = "new@example.com", Name = "New" };

context.Users.Update(user);
await context.SaveChangesAsync();

// This is a full replace of all properties
```

**Partial update (best practice):**
```csharp
public async Task UpdateUserEmailAsync(int userId, string newEmail)
{
    var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId);
    if (user == null)
        throw new NotFoundException("User not found");
    
    user.Email = newEmail;
    await context.SaveChangesAsync();
}
```

**Bulk update:**
```csharp
// Update multiple records directly in database (no loading)
await context.Users
    .Where(u => u.Status == "Inactive")
    .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "Archived"));
```

### Deleting Records

```csharp
// Get and delete
var user = await context.Users.FirstOrDefaultAsync(u => u.Id == 1);
if (user != null)
{
    context.Users.Remove(user);
    await context.SaveChangesAsync();
}

// Remove multiple
var inactiveUsers = await context.Users
    .Where(u => u.Status == "Inactive")
    .ToListAsync();
context.Users.RemoveRange(inactiveUsers);
await context.SaveChangesAsync();

// Bulk delete directly
await context.Users
    .Where(u => u.Status == "Inactive")
    .ExecuteDeleteAsync();
```

---

## 4.5 Query Optimization

### The N+1 Problem

**Problem scenario:**
```csharp
var users = await context.Users.ToListAsync();  // Query 1
foreach (var user in users)
{
    var orderCount = await context.Orders
        .Where(o => o.UserId == user.Id)
        .CountAsync();  // Query N (one per user)
}
// Total: 1 + N queries for N users
```

**Solution 1: Eager load**
```csharp
var users = await context.Users
    .Include(u => u.Orders)
    .ToListAsync();

foreach (var user in users)
{
    var orderCount = user.Orders.Count;  // No query
}
```

**Solution 2: Projection**
```csharp
var userOrderCounts = await context.Users
    .Select(u => new
    {
        u.Id,
        u.Name,
        OrderCount = u.Orders.Count
    })
    .ToListAsync();

// Single query with aggregation in SQL
```

### Query Performance

**Select only needed columns:**
```csharp
// Bad: loads entire entity including large BLOB
var users = await context.Users.ToListAsync();

// Good: select only needed columns
var userEmails = await context.Users
    .Select(u => new { u.Id, u.Email })
    .ToListAsync();
```

**Use AsNoTracking for read-only queries:**
```csharp
// Good: not tracking changes, faster for large result sets
var users = await context.Users
    .AsNoTracking()
    .ToListAsync();

// Bad: tracking entities we won't modify
var users = await context.Users.ToListAsync();
```

**Avoid client-side filtering:**
```csharp
// Bad: loads all users, filters in memory
var users = await context.Users.ToListAsync();
var activeUsers = users.Where(u => u.Status == "Active").ToList();

// Good: filters in database
var activeUsers = await context.Users
    .Where(u => u.Status == "Active")
    .ToListAsync();
```

### Database Indexes

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // Single column index
    modelBuilder.Entity<User>()
        .HasIndex(u => u.Email)
        .IsUnique();  // Email unique constraint
    
    // Composite index
    modelBuilder.Entity<Order>()
        .HasIndex(o => new { o.UserId, o.OrderDate })
        .IsDescending(false, true);  // OrderDate descending
    
    // Named index
    modelBuilder.Entity<User>()
        .HasIndex(u => u.Status)
        .HasDatabaseName("idx_user_status");
}
```

Indexes speed up WHERE, ORDER BY, JOIN clauses. Use for:
- Foreign keys (automatic)
- Columns frequently filtered
- Columns in ORDER BY
- Columns in JOIN conditions

---

## 4.6 Migrations

Migrations manage database schema changes:

### Creating Migrations

```bash
# Add migration after model changes
dotnet ef migrations add AddUserStatusColumn

# Creates migration file: 20231215120000_AddUserStatusColumn.cs
```

**Migration file structure:**
```csharp
public partial class AddUserStatusColumn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Status",
            table: "Users",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "Active");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Status", table: "Users");
    }
}
```

### Applying Migrations

```bash
# Apply all pending migrations
dotnet ef database update

# Apply to specific migration
dotnet ef database update AddUserStatusColumn

# Rollback to previous migration
dotnet ef database update PreviousMigrationName
```

**In production, use:**
```bash
# Generate SQL script instead of applying directly
dotnet ef migrations script > migration.sql

# Review and apply manually to production database
```

### Best Practices

**Name migrations descriptively:**
```
✓ AddUserStatusColumn
✓ RenameOrderDateToCreatedAt
✓ CreateOrderItemsTable
✗ Migration001
✗ AddStuff
```

**Keep migrations small:**
```
✓ One logical change per migration
✓ Easier to understand and rollback
✗ Multiple unrelated changes in one migration
```

**Don't edit migrations after running:**
```
✓ Create new migration to fix
✗ Edit migration file (breaks tracking)
```

---

## 4.7 Data Seeding

Seed initial data:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Role>().HasData(
        new Role { Id = 1, Name = "Admin" },
        new Role { Id = 2, Name = "User" }
    );
}
```

**Better approach using dedicated method:**
```csharp
public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        // Only seed if not already seeded
        if (context.Users.Any())
            return;
        
        var users = new[]
        {
            new User { Id = 1, Email = "admin@example.com", Name = "Admin" },
            new User { Id = 2, Email = "user@example.com", Name = "User" }
        };
        
        context.Users.AddRange(users);
        await context.SaveChangesAsync();
    }
}

// In Program.cs
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    context.Database.Migrate();
    await DataSeeder.SeedAsync(context);
}
```

---

## 4.8 Transactions

Transactions ensure atomic operations:

```csharp
public async Task TransferMoneyAsync(int fromUserId, int toUserId, decimal amount)
{
    using (var transaction = await context.Database.BeginTransactionAsync())
    {
        try
        {
            var fromUser = await context.Users.FindAsync(fromUserId);
            var toUser = await context.Users.FindAsync(toUserId);
            
            fromUser.Balance -= amount;
            toUser.Balance += amount;
            
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
```

**Isolation levels:**
```csharp
await context.Database.BeginTransactionAsync(
    System.Data.IsolationLevel.ReadCommitted
);
```

---

## Summary

Entity Framework Core provides a powerful abstraction over databases. Use DbContext for data access, configure relationships in OnModelCreating, leverage LINQ for type-safe queries, and optimize with eager loading and projections. Migrations track schema changes, and transactions ensure data consistency. The next chapter covers request/response handling, building on this data access foundation.
