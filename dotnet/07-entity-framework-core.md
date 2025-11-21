# Entity Framework Core

EF Core is an ORM (Object-Relational Mapper) that bridges objects and databases.

## Setup

### Install Packages

```bash
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
# or
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

### Create DbContext

```csharp
using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Post> Posts { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer(
            "Server=localhost;Database=MyDb;User Id=sa;Password=YourPassword;"
        );
    }
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}

public class Post
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public int UserId { get; set; }
    public User Author { get; set; }  // Navigation property
}
```

### Dependency Injection Setup

```csharp
// In Program.cs
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer("Server=localhost;Database=MyDb;...")
);

var serviceProvider = services.BuildServiceProvider();
```

## Entity Configuration

### Data Annotations

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; }

    [EmailAddress]
    public string Email { get; set; }

    [Range(0, 150)]
    public int Age { get; set; }

    [NotMapped]  // Not stored in database
    public string FullDisplayName { get; set; }

    [Column(TypeName = "decimal(10, 2)")]
    public decimal Salary { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; }
}
```

### Fluent API (Preferred)

```csharp
public class AppDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Email)
                .HasMaxLength(255);

            entity.Property(e => e.Salary)
                .HasPrecision(10, 2)  // decimal precision
                .IsRequired();

            entity.HasIndex(e => e.Email)
                .IsUnique();  // Create unique index

            entity.Ignore(e => e.FullDisplayName);
        });
    }
}
```

## Relationships

### One-to-Many

```csharp
public class Author
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<Book> Books { get; set; } = new();
}

public class Book
{
    public int Id { get; set; }
    public string Title { get; set; }
    public int AuthorId { get; set; }  // Foreign key
    public Author Author { get; set; }  // Navigation property
}

// Configuration
modelBuilder.Entity<Book>()
    .HasOne(b => b.Author)
    .WithMany(a => a.Books)
    .HasForeignKey(b => b.AuthorId);
```

### Many-to-Many

```csharp
public class Student
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<Course> Courses { get; set; } = new();
}

public class Course
{
    public int Id { get; set; }
    public string Title { get; set; }
    public List<Student> Students { get; set; } = new();
}

// Configuration
modelBuilder.Entity<Student>()
    .HasMany(s => s.Courses)
    .WithMany(c => c.Students)
    .UsingEntity(j => j.ToTable("StudentCourse"));  // Join table
```

### One-to-One

```csharp
public class Person
{
    public int Id { get; set; }
    public string Name { get; set; }
    public Passport Passport { get; set; }
}

public class Passport
{
    public int Id { get; set; }
    public string Number { get; set; }
    public int PersonId { get; set; }
    public Person Person { get; set; }
}

// Configuration
modelBuilder.Entity<Passport>()
    .HasOne(p => p.Person)
    .WithOne(p => p.Passport)
    .HasForeignKey<Passport>(p => p.PersonId);
```

## CRUD Operations

### Create (Insert)

```csharp
using (var context = new AppDbContext())
{
    var user = new User { Name = "Alice", Email = "alice@example.com" };
    context.Users.Add(user);
    context.SaveChanges();
}
```

### Read (Query)

```csharp
using (var context = new AppDbContext())
{
    // Get all
    var allUsers = context.Users.ToList();

    // Get by ID
    var user = context.Users.FirstOrDefault(u => u.Id == 1);

    // Filter
    var activeUsers = context.Users
        .Where(u => u.Email.Contains("@"))
        .ToList();

    // Projection
    var names = context.Users
        .Select(u => u.Name)
        .ToList();

    // Include related data
    var usersWithPosts = context.Users
        .Include(u => u.Posts)
        .ToList();

    // Multiple includes
    var data = context.Users
        .Include(u => u.Posts)
        .ThenInclude(p => p.Comments)
        .ToList();

    // Pagination
    var page = 1;
    var pageSize = 10;
    var users = context.Users
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToList();
}
```

### Update

```csharp
using (var context = new AppDbContext())
{
    var user = context.Users.FirstOrDefault(u => u.Id == 1);
    user.Email = "newemail@example.com";
    context.SaveChanges();

    // Bulk update
    context.Users
        .Where(u => u.Age > 65)
        .ExecuteUpdate(s => s.SetProperty(u => u.Status, "Senior"));
}
```

### Delete

```csharp
using (var context = new AppDbContext())
{
    var user = context.Users.FirstOrDefault(u => u.Id == 1);
    context.Users.Remove(user);
    context.SaveChanges();

    // Bulk delete
    context.Users
        .Where(u => u.Status == "Inactive")
        .ExecuteDelete();
}
```

## Querying

### LINQ to Entities

```csharp
using (var context = new AppDbContext())
{
    // Filtering
    var adults = context.Users
        .Where(u => u.Age >= 18)
        .ToList();

    // Ordering
    var sorted = context.Users
        .OrderBy(u => u.Name)
        .ThenByDescending(u => u.Age)
        .ToList();

    // Grouping
    var grouped = context.Users
        .GroupBy(u => u.Status)
        .Select(g => new
        {
            Status = g.Key,
            Count = g.Count(),
            AvgAge = g.Average(u => u.Age)
        })
        .ToList();

    // Join
    var joined = context.Posts
        .Join(context.Users,
            p => p.UserId,
            u => u.Id,
            (p, u) => new { Post = p.Title, Author = u.Name })
        .ToList();

    // Distinct
    var uniqueEmails = context.Users
        .Select(u => u.Email)
        .Distinct()
        .ToList();

    // Count/Exists
    int count = context.Users.Count();
    bool exists = context.Users.Any(u => u.Id == 1);
}
```

### Raw SQL

```csharp
using (var context = new AppDbContext())
{
    // Execute raw query
    var users = context.Users
        .FromSqlInterpolated($"SELECT * FROM Users WHERE Age > {18}")
        .ToList();

    // Raw command without mapping
    var result = context.Database.ExecuteSqlInterpolated(
        $"UPDATE Users SET Status = {'Active'} WHERE Age > {18}"
    );
}
```

## Migrations

Track schema changes:

```bash
# Create initial migration
dotnet ef migrations add InitialCreate

# Create migration for a change
dotnet ef migrations add AddUserStatus

# Apply migrations
dotnet ef database update

# Revert to previous migration
dotnet ef database update PreviousMigrationName

# Remove last migration
dotnet ef migrations remove

# List migrations
dotnet ef migrations list
```

### Migration Files

```csharp
public partial class AddUserStatus : Migration
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
        migrationBuilder.DropColumn(
            name: "Status",
            table: "Users");
    }
}
```

## Performance Tips

### AsNoTracking

Don't track entities you only read:

```csharp
var users = context.Users
    .AsNoTracking()  // Don't track changes
    .Where(u => u.Active)
    .ToList();
```

### Split Queries

Avoid cartesian explosion with large includes:

```csharp
var users = context.Users
    .Include(u => u.Posts)
    .Include(u => u.Comments)
    .AsSplitQuery()  // Execute as separate queries
    .ToList();
```

### Batch Operations

```csharp
// Bulk insert
context.Users.AddRange(users);
context.SaveChanges();

// Bulk update/delete use ExecuteUpdate/ExecuteDelete
```

## Transactions

```csharp
using (var transaction = context.Database.BeginTransaction())
{
    try
    {
        context.Users.Add(new User { Name = "Alice" });
        context.SaveChanges();

        context.Posts.Add(new Post { UserId = user.Id });
        context.SaveChanges();

        transaction.Commit();
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}
```

## Practice Exercises

1. **Database Design**: Model a blog system with Users, Posts, Comments, Tags
2. **CRUD App**: Build a console app with menu for managing users
3. **Relationships**: Create entities with one-to-many and many-to-many relationships
4. **Queries**: Write complex LINQ queries with includes and projections
5. **Migrations**: Add and remove columns, handle schema evolution

## Key Takeaways

- **EF Core** maps database tables to C# classes
- **DbContext** manages database connections and queries
- **Relationships** (one-to-many, many-to-many) enable rich data models
- **LINQ** provides type-safe queries that translate to SQL
- **Migrations** track schema changes with code
- Use **AsNoTracking** for read-only queries
- **Include** related data to avoid N+1 queries
- **Transactions** ensure multiple operations succeed together or fail together
