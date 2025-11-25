# Chapter 10: Domain-Driven Design & Clean Architecture

## 10.1 Introduction to Domain-Driven Design (DDD)

Domain-Driven Design is an approach to building complex systems by organizing code around the business domain.

### Core Principle

**Structure code to reflect the business, not the technology.**

### Problem with Layered Architecture

Traditional layered architecture mixes business logic with infrastructure concerns:

```
Controllers
    ↓
Services (business logic mixed with infrastructure)
    ↓
Repositories (data access)
    ↓
Database
```

**Issues:**
- Business logic depends on database implementation
- Hard to test business rules without database
- Domain concepts buried in service classes
- Easy to bypass business rules

### DDD Layers

```
User Interface
    ↓
Application Layer (use cases, orchestration)
    ↓
Domain Layer (business logic, entities, value objects)
    ↓
Infrastructure Layer (database, external services)
```

**Key difference:**
- Domain Layer is independent (no database, no frameworks)
- Application Layer orchestrates domain logic
- Infrastructure implements interfaces defined by domain

---

## 10.2 Domain Models and Entities

### Entities

Entities are domain objects with identity. They persist and change over time.

```csharp
// Domain Entity - independent of infrastructure
public class User
{
    public int Id { get; private set; }
    public Email Email { get; private set; }
    public Name Name { get; private set; }
    public Password Password { get; private set; }
    public UserStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    
    // Private constructor for Entity Framework
    private User() { }
    
    // Factory method for creating new users
    public static User Create(Email email, Name name, Password password)
    {
        if (email == null)
            throw new ArgumentNullException(nameof(email), "Email is required");
        if (name == null)
            throw new ArgumentNullException(nameof(name), "Name is required");
        if (password == null)
            throw new ArgumentNullException(nameof(password), "Password is required");
        
        return new User
        {
            Email = email,
            Name = name,
            Password = password,
            Status = UserStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
    }
    
    // Business methods
    public void UpdateEmail(Email newEmail)
    {
        if (newEmail == null)
            throw new ArgumentNullException(nameof(newEmail));
        
        Email = newEmail;
    }
    
    public void Suspend(string reason)
    {
        if (Status == UserStatus.Suspended)
            throw new InvalidOperationException("User already suspended");
        
        Status = UserStatus.Suspended;
        // Could add domain event here for audit
    }
    
    public bool VerifyPassword(string plainPassword)
    {
        return Password.Verify(plainPassword);
    }
}

// Enum for status (value object)
public enum UserStatus
{
    Active,
    Suspended,
    Deleted
}
```

**Key principles:**

- **Encapsulation**: Properties are private set (only business methods modify state)
- **Validation**: Constructor and methods validate business rules
- **Identity**: Id property defines equality (not value)
- **Statelessness**: Business logic doesn't depend on database/external services

### Value Objects

Value objects represent domain concepts without identity. Equal if their values are equal.

```csharp
// Email - value object
public class Email : IEquatable<Email>
{
    public string Value { get; }
    
    public Email(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Email cannot be empty", nameof(value));
        
        if (!IsValidEmail(value))
            throw new ArgumentException("Email format is invalid", nameof(value));
        
        Value = value.ToLowerInvariant();
    }
    
    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
    
    public override bool Equals(object obj) => Equals(obj as Email);
    
    public bool Equals(Email other) => other != null && Value == other.Value;
    
    public override int GetHashCode() => Value.GetHashCode();
    
    public override string ToString() => Value;
    
    public static implicit operator string(Email email) => email?.Value;
    public static explicit operator Email(string value) => new Email(value);
}

// Name - value object
public class Name : IEquatable<Name>
{
    public string FirstName { get; }
    public string LastName { get; }
    
    public Name(string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name is required", nameof(lastName));
        
        FirstName = firstName;
        LastName = lastName;
    }
    
    public string FullName => $"{FirstName} {LastName}";
    
    public override bool Equals(object obj) => Equals(obj as Name);
    
    public bool Equals(Name other) => 
        other != null && FirstName == other.FirstName && LastName == other.LastName;
    
    public override int GetHashCode() => HashCode.Combine(FirstName, LastName);
}

// Password - value object with security logic
public class Password : IEquatable<Password>
{
    public string Hash { get; }
    
    public Password(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("Password hash cannot be empty", nameof(hash));
        Hash = hash;
    }
    
    // Create from plaintext password (used during registration)
    public static Password CreateFromPlaintext(string plainPassword)
    {
        if (string.IsNullOrWhiteSpace(plainPassword))
            throw new ArgumentException("Password cannot be empty", nameof(plainPassword));
        
        if (plainPassword.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters", nameof(plainPassword));
        
        var hash = BCrypt.Net.BCrypt.HashPassword(plainPassword);
        return new Password(hash);
    }
    
    public bool Verify(string plainPassword)
    {
        return BCrypt.Net.BCrypt.Verify(plainPassword, Hash);
    }
    
    public override bool Equals(object obj) => Equals(obj as Password);
    
    public bool Equals(Password other) => other != null && Hash == other.Hash;
    
    public override int GetHashCode() => Hash.GetHashCode();
}
```

**Value Object Benefits:**
- Encapsulate complex logic (Email validation, Password hashing)
- Immutable (thread-safe)
- Testable independently
- Domain language clarity

---

## 10.3 Aggregates and Aggregate Roots

Aggregates are clusters of entities treated as a single unit.

```csharp
// Aggregate Root - entry point to aggregate
public class Order
{
    public int Id { get; private set; }
    public int UserId { get; private set; }
    public DateTime OrderDate { get; private set; }
    public OrderStatus Status { get; private set; }
    private List<OrderItem> _items = new();  // Child aggregate
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();
    
    private Order() { }
    
    public static Order Create(int userId)
    {
        return new Order
        {
            UserId = userId,
            OrderDate = DateTime.UtcNow,
            Status = OrderStatus.Pending
        };
    }
    
    // Business methods modify aggregate
    public void AddItem(Product product, int quantity)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Cannot modify non-pending order");
        
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));
        
        var existingItem = _items.FirstOrDefault(i => i.ProductId == product.Id);
        
        if (existingItem != null)
        {
            existingItem.IncreaseQuantity(quantity);
        }
        else
        {
            _items.Add(OrderItem.Create(product, quantity));
        }
    }
    
    public void Complete()
    {
        if (Status == OrderStatus.Completed)
            throw new InvalidOperationException("Order already completed");
        if (_items.Count == 0)
            throw new InvalidOperationException("Cannot complete order with no items");
        
        Status = OrderStatus.Completed;
    }
    
    public decimal GetTotal() => _items.Sum(i => i.LineTotal);
}

// Child entity - only accessed through aggregate root
public class OrderItem
{
    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    
    public decimal LineTotal => UnitPrice * Quantity;
    
    private OrderItem() { }
    
    public static OrderItem Create(Product product, int quantity)
    {
        return new OrderItem
        {
            ProductId = product.Id,
            Quantity = quantity,
            UnitPrice = product.Price
        };
    }
    
    public void IncreaseQuantity(int amount)
    {
        Quantity += amount;
    }
}

public enum OrderStatus
{
    Pending,
    Completed,
    Cancelled
}
```

**Aggregate Principles:**
- Single entry point (aggregate root)
- Consistent within aggregate (all rules enforced)
- No direct references to child entities from outside
- Children only accessible through root

---

## 10.4 Repositories

Repositories abstract data access. Domain layer only sees repository interfaces.

```csharp
// Domain repository interface (in Domain layer)
public interface IUserRepository
{
    Task<User> GetByIdAsync(int id);
    Task<User> GetByEmailAsync(Email email);
    Task AddAsync(User user);
    Task UpdateAsync(User user);
    Task RemoveAsync(User user);
    Task<bool> ExistsAsync(Email email);
}

// Infrastructure implementation (in Infrastructure layer)
public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;
    
    public UserRepository(AppDbContext context)
    {
        _context = context;
    }
    
    public async Task<User> GetByIdAsync(int id)
    {
        return await _context.Users.FindAsync(id);
    }
    
    public async Task<User> GetByEmailAsync(Email email)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email.Value);
    }
    
    public async Task AddAsync(User user)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
    }
    
    public async Task UpdateAsync(User user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }
    
    public async Task RemoveAsync(User user)
    {
        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
    }
    
    public async Task<bool> ExistsAsync(Email email)
    {
        return await _context.Users
            .AnyAsync(u => u.Email == email.Value);
    }
}
```

**Repository Pattern Benefits:**
- Domain doesn't depend on EF Core
- Easy to mock for testing
- Swap implementations (switch databases, use in-memory for tests)
- Centralized data access logic

---

## 10.5 Application Services

Application services orchestrate domain logic to fulfill use cases.

```csharp
// Application service - orchestrates domain objects
public class CreateUserUseCase
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublisher _eventPublisher;
    
    public CreateUserUseCase(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IPublisher eventPublisher)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _eventPublisher = eventPublisher;
    }
    
    public async Task<CreateUserResponse> ExecuteAsync(CreateUserRequest request)
    {
        // 1. Validate input (use case specific validation)
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new ValidationException("Email is required");
        
        // 2. Check preconditions (business rules)
        var email = new Email(request.Email);
        
        if (await _userRepository.ExistsAsync(email))
            throw new DomainException("User with this email already exists");
        
        // 3. Create domain object
        var name = new Name(request.FirstName, request.LastName);
        var password = Password.CreateFromPlaintext(request.Password);
        var user = User.Create(email, name, password);
        
        // 4. Persist using repository
        await _userRepository.AddAsync(user);
        
        // 5. Publish domain events (async notifications)
        await _eventPublisher.PublishAsync(new UserCreatedEvent
        {
            UserId = user.Id,
            Email = user.Email,
            Name = user.Name
        });
        
        // 6. Return response (DTO)
        return new CreateUserResponse
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name
        };
    }
}

public class CreateUserRequest
{
    public string Email { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Password { get; set; }
}

public class CreateUserResponse
{
    public int Id { get; set; }
    public Email Email { get; set; }
    public Name Name { get; set; }
}
```

**Application Service Responsibilities:**
- Orchestrate domain logic
- Manage transactions
- Publish domain events
- Return DTOs to controllers

---

## 10.6 Clean Architecture Folder Structure

Organize code by domain/feature, not by layer:

```
MyApi/
├── Core/
│   ├── Domain/
│   │   ├── Users/
│   │   │   ├── User.cs              (Entity)
│   │   │   ├── Email.cs             (Value Object)
│   │   │   ├── IUserRepository.cs   (Interface)
│   │   │   └── UserEvents.cs        (Domain Events)
│   │   ├── Orders/
│   │   │   ├── Order.cs
│   │   │   ├── OrderItem.cs
│   │   │   └── IOrderRepository.cs
│   │   └── Common/
│   │       └── IRepository.cs
│   │
│   └── Application/
│       ├── Users/
│       │   ├── CreateUserUseCase.cs
│       │   ├── GetUserQuery.cs
│       │   └── Dtos/UserDto.cs
│       └── Orders/
│           ├── CreateOrderUseCase.cs
│           └── Dtos/OrderDto.cs
│
├── Infrastructure/
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── Repositories/
│   │   │   ├── UserRepository.cs
│   │   │   └── OrderRepository.cs
│   │   └── Migrations/
│   ├── External/
│   │   ├── PaymentGateway.cs
│   │   └── EmailService.cs
│   └── Identity/
│       ├── JwtService.cs
│       └── PasswordHasher.cs
│
├── Presentation/
│   ├── Controllers/
│   │   ├── UsersController.cs
│   │   └── OrdersController.cs
│   └── Middleware/
│       └── ErrorHandlingMiddleware.cs
│
└── Program.cs
```

**Organization principles:**
- Organize by business domain (Users, Orders), not technical layer
- Dependencies flow inward (Controllers → Application → Domain → Infrastructure)
- Each layer can only depend on layers toward the core
- Infrastructure (implementation) at the edges, Domain (business logic) at the core

---

## 10.7 Dependency Injection Setup

```csharp
// Program.cs - Register according to layers

// Domain services (pure business logic)
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// Application services (use cases)
builder.Services.AddScoped<CreateUserUseCase>();
builder.Services.AddScoped<CreateOrderUseCase>();
builder.Services.AddScoped<GetUserQuery>();

// Infrastructure services
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IJwtService, JwtService>();

// Data access
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
);
```

---

## 10.8 Testing Domain Logic

Domain entities are easy to test because they're independent:

```csharp
public class UserTests
{
    [Fact]
    public void Create_WithValidData_CreatesUser()
    {
        // Arrange & Act
        var email = new Email("john@example.com");
        var name = new Name("John", "Doe");
        var password = Password.CreateFromPlaintext("SecurePassword123!");
        
        var user = User.Create(email, name, password);
        
        // Assert
        Assert.Equal(email, user.Email);
        Assert.Equal(name, user.Name);
        Assert.Equal(UserStatus.Active, user.Status);
    }
    
    [Fact]
    public void UpdateEmail_WithValidEmail_UpdatesEmail()
    {
        // Arrange
        var user = User.Create(
            new Email("old@example.com"),
            new Name("John", "Doe"),
            Password.CreateFromPlaintext("SecurePassword123!")
        );
        
        // Act
        var newEmail = new Email("new@example.com");
        user.UpdateEmail(newEmail);
        
        // Assert
        Assert.Equal(newEmail, user.Email);
    }
    
    [Fact]
    public void Suspend_WhenAlreadySuspended_ThrowsException()
    {
        // Arrange
        var user = User.Create(
            new Email("john@example.com"),
            new Name("John", "Doe"),
            Password.CreateFromPlaintext("SecurePassword123!")
        );
        user.Suspend("Spam");
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => user.Suspend("Another reason"));
    }
}

public class EmailTests
{
    [Theory]
    [InlineData("valid@example.com")]
    [InlineData("user+tag@example.co.uk")]
    public void Create_WithValidEmail_Succeeds(string emailString)
    {
        // Act
        var email = new Email(emailString);
        
        // Assert
        Assert.NotNull(email);
    }
    
    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("@example.com")]
    public void Create_WithInvalidEmail_ThrowsException(string emailString)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Email(emailString));
    }
}
```

---

## Summary

Domain-Driven Design separates business logic (Domain layer) from infrastructure concerns (Infrastructure layer). Entities encapsulate state and enforce business rules. Value objects represent domain concepts. Aggregates group entities with consistent boundaries. Repositories abstract data access. Application services orchestrate use cases. Clean architecture organizes code by domain features, with dependencies flowing toward the core business logic. This makes systems testable, maintainable, and aligned with business goals. The next chapter covers CQRS and Event-Driven Architecture—advanced patterns for complex systems.
