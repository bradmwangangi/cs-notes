# 18. Domain-Driven Design Fundamentals

## Overview
Domain-Driven Design (DDD) is an approach to software development that emphasizes aligning your codebase with business reality. It provides patterns and practices for building complex enterprise systems where business rules are clear and properly represented in code.

---

## 1. Core DDD Concepts

### 1.1 Ubiquitous Language
The foundation of DDD is establishing a **shared vocabulary** between developers and domain experts. This language:

- **Reflects business terms**, not technical jargon
- **Appears consistently** in code, documentation, and conversations
- **Evolves** as understanding of the domain improves
- **Eliminates translation** between business and technical teams

**Example: E-commerce domain**
```
❌ Don't use: "PurchaseOrderEntity", "TransactionRecord", "BuyerObject"
✅ Use: "Order", "Payment", "Customer"
```

**Benefits:**
- Reduces misunderstandings between stakeholders
- Makes code more readable and self-documenting
- Facilitates better architectural decisions
- Creates a common reference for discussions

### 1.2 Domain vs. Subdomains

Your business is divided into logical areas:

**Domain**: The entire business area you're modeling
```
Example: An insurance company's domain encompasses:
- Policy Management
- Claims Processing
- Underwriting
- Customer Service
```

**Subdomains**: Specific areas within the domain

| Type | Purpose | Example |
|------|---------|---------|
| **Core Subdomain** | Competitive advantage, highly complex | Claims Processing (where company excels) |
| **Supporting Subdomain** | Necessary but not differentiating | Invoice Generation |
| **Generic Subdomain** | Standard, off-the-shelf solutions work | Email Service, Logging |

```csharp
// Example structure reflecting subdomains
namespace InsuranceCompany.ClaimsProcessing { }  // Core
namespace InsuranceCompany.Underwriting { }       // Core
namespace InsuranceCompany.Billing { }            // Supporting
namespace InsuranceCompany.Common.Logging { }     // Generic
```

### 1.3 Bounded Contexts

A **Bounded Context** is an explicit boundary within which a model applies. Each context:

- Has its own ubiquitous language
- Owns its own data models
- Can implement rules differently
- Communicates across boundaries via well-defined contracts

**Real-world example:**
In an e-commerce system:

```
┌─────────────────────────────────┐
│    Sales Bounded Context        │
│  • Customer (buyer)             │
│  • Order (what they're buying)  │
│  • Product (what's for sale)    │
└─────────────────────────────────┘
            ↕ (shared contract)
┌─────────────────────────────────┐
│   Inventory Bounded Context     │
│  • Product (stock item)         │
│  • Warehouse (location)         │
│  • Stock Level                  │
└─────────────────────────────────┘
```

Notice: "Product" exists in both contexts but means different things:
- **Sales**: Something with a price and description
- **Inventory**: Something with quantity and location

```csharp
// Sales Context
namespace ECommerce.Sales
{
    public class Product
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; }
    }
}

// Inventory Context
namespace ECommerce.Inventory
{
    public class Product
    {
        public string Sku { get; set; }
        public int QuantityOnHand { get; set; }
        public string WarehouseLocation { get; set; }
    }
}
```

---

## 2. DDD Tactical Patterns

### 2.1 Value Objects

A **Value Object** represents a concept from the domain with no inherent identity. Two value objects with the same attributes are considered equal.

**Characteristics:**
- No identity (two instances with same values = equivalent)
- Immutable (never changes after creation)
- Represents a concept from the domain
- Can be compared, copied, and passed around freely

**Examples:**
- Money: Two $50 bills are interchangeable
- Email Address: jane@example.com is just a value
- Color: Red is red, doesn't matter which shade instance
- Address: Two people at the same address have equivalent addresses

**Implementation:**

```csharp
public class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }
    
    public Money(decimal amount, string currency)
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative");
        
        Amount = amount;
        Currency = currency;
    }
    
    // Override equality - two Money objects with same amount/currency are equal
    public override bool Equals(object obj)
    {
        if (!(obj is Money other))
            return false;
        
        return Amount == other.Amount && Currency == other.Currency;
    }
    
    public override int GetHashCode()
    {
        return HashCode.Combine(Amount, Currency);
    }
    
    public static Money operator +(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException("Cannot add different currencies");
        
        return new Money(left.Amount + right.Amount, left.Currency);
    }
}

public class EmailAddress : ValueObject
{
    public string Value { get; }
    
    public EmailAddress(string value)
    {
        if (!IsValidEmail(value))
            throw new ArgumentException("Invalid email format");
        
        Value = value;
    }
    
    private static bool IsValidEmail(string email)
    {
        try
        {
            var address = new System.Net.Mail.MailAddress(email);
            return address.Address == email;
        }
        catch
        {
            return false;
        }
    }
    
    public override string ToString() => Value;
    
    public override bool Equals(object obj) =>
        obj is EmailAddress other && Value == other.Value;
    
    public override int GetHashCode() => Value.GetHashCode();
}

public class Address : ValueObject
{
    public string Street { get; }
    public string City { get; }
    public string PostalCode { get; }
    public string Country { get; }
    
    public Address(string street, string city, string postalCode, string country)
    {
        Street = street ?? throw new ArgumentNullException(nameof(street));
        City = city ?? throw new ArgumentNullException(nameof(city));
        PostalCode = postalCode ?? throw new ArgumentNullException(nameof(postalCode));
        Country = country ?? throw new ArgumentNullException(nameof(country));
    }
    
    public override bool Equals(object obj) =>
        obj is Address other &&
        Street == other.Street &&
        City == other.City &&
        PostalCode == other.PostalCode &&
        Country == other.Country;
    
    public override int GetHashCode() =>
        HashCode.Combine(Street, City, PostalCode, Country);
}

// Base class for value objects
public abstract class ValueObject
{
    // Optional: implement operator overloads for convenience
    public static bool operator ==(ValueObject left, ValueObject right)
    {
        if (ReferenceEquals(left, null) ^ ReferenceEquals(right, null))
            return false;
        return ReferenceEquals(left, null) || left.Equals(right);
    }
    
    public static bool operator !=(ValueObject left, ValueObject right) =>
        !(left == right);
}
```

**Usage:**

```csharp
// Value objects ensure business rules are enforced
var price = new Money(99.99m, "USD");
var discountedPrice = new Money(89.99m, "USD");
var totalPrice = price + discountedPrice;  // Works because same currency

var customerEmail = new EmailAddress("john@example.com");
var order1Email = new EmailAddress("john@example.com");

// Two instances with same values are equal
Console.WriteLine(customerEmail == order1Email);  // true

// Value objects are simple to use and understand
var shippingAddress = new Address("123 Main St", "Anytown", "12345", "USA");
var billingAddress = new Address("123 Main St", "Anytown", "12345", "USA");
Console.WriteLine(shippingAddress == billingAddress);  // true
```

### 2.2 Entities

An **Entity** represents a domain concept that has a unique identity and changes over time.

**Characteristics:**
- Has unique identity (typically an ID)
- Identity persists even as attributes change
- Two entities with same attributes but different IDs are different
- Mutable (can change over its lifetime)
- Thread of continuity matters

**Examples:**
- Customer: Same customer even if they move addresses
- Order: Same order even if items are modified
- Account: Same account regardless of balance changes

**Implementation:**

```csharp
public abstract class Entity
{
    public int Id { get; protected set; }
    
    public override bool Equals(object obj)
    {
        var other = obj as Entity;
        
        if (other == null)
            return false;
        
        // Transient objects without an ID are never equal
        if (IsTransient() && other.IsTransient())
            return false;
        
        // Entities are equal if they have the same ID
        return Id == other.Id;
    }
    
    public static bool operator ==(Entity a, Entity b)
    {
        if (ReferenceEquals(a, null) && ReferenceEquals(b, null))
            return true;
        
        if (ReferenceEquals(a, null) || ReferenceEquals(b, null))
            return false;
        
        return a.Equals(b);
    }
    
    public static bool operator !=(Entity a, Entity b) => !(a == b);
    
    public override int GetHashCode() => Id.GetHashCode();
    
    private bool IsTransient() => Id == 0;
}

public class Customer : Entity
{
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public EmailAddress Email { get; private set; }
    public Address ShippingAddress { get; private set; }
    public List<Order> Orders { get; private set; } = new();
    
    // Factory method for creating new customers
    public static Customer Create(string firstName, string lastName, 
        EmailAddress email, Address address)
    {
        return new Customer
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            ShippingAddress = address
        };
    }
    
    // Behavior methods that enforce business rules
    public void UpdateShippingAddress(Address newAddress)
    {
        if (newAddress == null)
            throw new ArgumentNullException(nameof(newAddress));
        
        ShippingAddress = newAddress;
    }
    
    public void UpdateEmail(EmailAddress newEmail)
    {
        if (newEmail == null)
            throw new ArgumentNullException(nameof(newEmail));
        
        Email = newEmail;
    }
    
    public Order PlaceOrder(List<OrderItem> items)
    {
        if (!items.Any())
            throw new InvalidOperationException("Order must contain at least one item");
        
        var order = Order.Create(this, items);
        Orders.Add(order);
        
        return order;
    }
}

public class Order : Entity
{
    public Customer Customer { get; private set; }
    public List<OrderItem> Items { get; private set; } = new();
    public Money TotalPrice { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    
    public static Order Create(Customer customer, List<OrderItem> items)
    {
        var order = new Order
        {
            Customer = customer,
            Items = items,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            TotalPrice = CalculateTotal(items)
        };
        
        return order;
    }
    
    public void Ship()
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Can only ship pending orders");
        
        Status = OrderStatus.Shipped;
        ShippedAt = DateTime.UtcNow;
    }
    
    public void Cancel()
    {
        if (Status == OrderStatus.Shipped)
            throw new InvalidOperationException("Cannot cancel shipped orders");
        
        Status = OrderStatus.Cancelled;
    }
    
    private static Money CalculateTotal(List<OrderItem> items)
    {
        if (!items.Any())
            return new Money(0, "USD");
        
        var total = items.First().Subtotal;
        foreach (var item in items.Skip(1))
        {
            total = total + item.Subtotal;
        }
        
        return total;
    }
}

public enum OrderStatus
{
    Pending,
    Shipped,
    Delivered,
    Cancelled
}
```

### 2.3 Aggregates and Aggregate Roots

An **Aggregate** is a cluster of domain objects bound together by a root entity (Aggregate Root). It defines transaction boundaries and consistency rules.

**Key Principles:**
1. **Single root**: Only the Aggregate Root can be accessed from outside
2. **Encapsulation**: Internal entities only accessed through the root
3. **Consistency**: All changes maintain invariants
4. **Transaction boundaries**: The entire aggregate is updated atomically

```csharp
// Order is the Aggregate Root
public class Order : Entity
{
    public int Id { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public List<OrderItem> Items { get; private set; } // Internal collection
    public Money TotalPrice { get; private set; }
    public OrderStatus Status { get; private set; }
    
    // Only expose items through controlled methods
    public IReadOnlyList<OrderItem> GetItems() => Items.AsReadOnly();
    
    public void AddItem(Product product, int quantity)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Cannot modify non-pending orders");
        
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive");
        
        var existingItem = Items.FirstOrDefault(i => i.Product.Id == product.Id);
        
        if (existingItem != null)
        {
            existingItem.IncreaseQuantity(quantity);
        }
        else
        {
            var item = new OrderItem(product, quantity);
            Items.Add(item);
        }
        
        RecalculateTotal();
    }
    
    public void RemoveItem(int productId)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Cannot modify non-pending orders");
        
        var item = Items.FirstOrDefault(i => i.Product.Id == productId);
        if (item != null)
        {
            Items.Remove(item);
            RecalculateTotal();
        }
    }
    
    private void RecalculateTotal()
    {
        TotalPrice = Items.Aggregate(
            new Money(0, "USD"),
            (acc, item) => acc + item.Subtotal
        );
    }
}

// OrderItem is NOT an aggregate root - only accessible through Order
public class OrderItem : Entity
{
    public Product Product { get; private set; }
    public int Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    
    public Money Subtotal => 
        new Money(UnitPrice.Amount * Quantity, UnitPrice.Currency);
    
    public OrderItem(Product product, int quantity)
    {
        Product = product;
        Quantity = quantity;
        UnitPrice = product.Price;
    }
    
    public void IncreaseQuantity(int amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive");
        
        Quantity += amount;
    }
}

// The Product referenced is an external aggregate
// Order doesn't own or manage Product's lifecycle
public class Product : Entity
{
    public string Name { get; set; }
    public Money Price { get; set; }
}
```

**Aggregate Design Guidelines:**

```csharp
// ✅ GOOD: Clear boundary and consistency
public class ShoppingCart : Entity
{
    public int Id { get; private set; }
    public List<CartItem> Items { get; private set; }
    public Money Total { get; private set; }
    
    public void AddItem(CartItem item)
    {
        Items.Add(item);
        RecalculateTotal();
    }
    
    private void RecalculateTotal()
    {
        Total = Items.Aggregate(
            new Money(0, "USD"),
            (acc, item) => acc + item.Subtotal
        );
    }
}

// ❌ BAD: Aggregate is too large and tightly coupled
public class User : Entity
{
    public Profile Profile { get; set; }
    public List<Post> Posts { get; set; }
    public List<Friend> Friends { get; set; }
    public List<Notification> Notifications { get; set; }
    public List<Message> Messages { get; set; }
    public List<Comment> Comments { get; set; }
    // ... many more collections
}

// ✅ GOOD: Separate aggregates with IDs for cross-aggregate references
public class User : Entity
{
    public int Id { get; private set; }
    public Profile Profile { get; set; }
}

public class UserTimeline
{
    public int UserId { get; set; } // Reference to User aggregate
    public List<Post> Posts { get; set; }
    public List<Comment> Comments { get; set; }
}

public class UserNetwork
{
    public int UserId { get; set; } // Reference to User aggregate
    public List<int> FriendIds { get; set; } // IDs, not full entities
}
```

### 2.4 Repositories

A **Repository** mediates between the domain and data mapping layers. It acts like an in-memory collection of aggregates.

```csharp
// Repository abstraction - defined in domain layer
public interface IOrderRepository
{
    Task AddAsync(Order order);
    Task<Order> GetByIdAsync(int orderId);
    Task<List<Order>> GetByCustomerAsync(int customerId);
    Task UpdateAsync(Order order);
    Task DeleteAsync(int orderId);
}

// Repository implementation - in infrastructure layer
public class OrderRepository : IOrderRepository
{
    private readonly ApplicationDbContext _context;
    
    public OrderRepository(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task AddAsync(Order order)
    {
        await _context.Orders.AddAsync(order);
        await _context.SaveChangesAsync();
    }
    
    public async Task<Order> GetByIdAsync(int orderId)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);
    }
    
    public async Task<List<Order>> GetByCustomerAsync(int customerId)
    {
        return await _context.Orders
            .Where(o => o.CustomerId.Value == customerId)
            .Include(o => o.Items)
            .ToListAsync();
    }
    
    public async Task UpdateAsync(Order order)
    {
        _context.Orders.Update(order);
        await _context.SaveChangesAsync();
    }
    
    public async Task DeleteAsync(int orderId)
    {
        var order = await GetByIdAsync(orderId);
        if (order != null)
        {
            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();
        }
    }
}

// Usage in application service
public class PlaceOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICustomerRepository _customerRepository;
    
    public PlaceOrderService(IOrderRepository orderRepository, 
        ICustomerRepository customerRepository)
    {
        _orderRepository = orderRepository;
        _customerRepository = customerRepository;
    }
    
    public async Task<int> ExecuteAsync(int customerId, List<OrderItem> items)
    {
        var customer = await _customerRepository.GetByIdAsync(customerId);
        var order = customer.PlaceOrder(items);
        
        await _orderRepository.AddAsync(order);
        
        return order.Id;
    }
}
```

---

## 3. Strategic DDD: Mapping Subdomains

### 3.1 Subdomain Analysis

Classify your business into subdomains for clearer architecture:

```
INSURANCE COMPANY

Core Subdomains (Competitive Advantage):
├── Claims Assessment
│   └── Domain logic: Policy coverage rules, claim validity
├── Risk Underwriting
│   └── Domain logic: Risk evaluation, pricing algorithms

Supporting Subdomains (Necessary):
├── Customer Management
├── Policy Administration

Generic Subdomains (Standard Solutions):
├── Authentication
├── Email Notifications
├── Payment Processing
```

### 3.2 Anti-Corruption Layers

When integrating with external systems, protect your domain:

```csharp
// External third-party API (generic subdomain)
public class PaymentGatewayClient
{
    public PaymentResponse ProcessPayment(PaymentRequest request)
    {
        // Returns generic, external API response
    }
}

// Anti-corruption layer - translates to domain model
public class PaymentProcessor
{
    private readonly PaymentGatewayClient _gateway;
    
    public PaymentProcessor(PaymentGatewayClient gateway)
    {
        _gateway = gateway;
    }
    
    public Payment ProcessOrderPayment(Order order, CardDetails card)
    {
        // Translate domain model to gateway format
        var externalRequest = new PaymentRequest
        {
            Amount = order.TotalPrice.Amount.ToString(),
            Currency = order.TotalPrice.Currency,
            CardNumber = card.Number,
            Expiry = card.Expiry
        };
        
        try
        {
            var externalResponse = _gateway.ProcessPayment(externalRequest);
            
            // Translate gateway response back to domain model
            return new Payment(
                transactionId: externalResponse.TransactionId,
                amount: new Money(
                    decimal.Parse(externalResponse.Amount),
                    externalResponse.Currency
                ),
                status: MapStatus(externalResponse.Status),
                processedAt: DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            throw new PaymentFailedException("Payment processing failed", ex);
        }
    }
    
    private PaymentStatus MapStatus(string externalStatus) =>
        externalStatus switch
        {
            "approved" => PaymentStatus.Approved,
            "denied" => PaymentStatus.Denied,
            _ => PaymentStatus.Unknown
        };
}
```

---

## 4. Practical Implementation Example

Complete example: Online Bookstore

```csharp
// ============= VALUE OBJECTS =============

public class ISBN : ValueObject
{
    public string Value { get; }
    
    public ISBN(string value)
    {
        if (!IsValid(value))
            throw new ArgumentException("Invalid ISBN format");
        Value = value;
    }
    
    private static bool IsValid(string isbn)
    {
        // Simplified validation
        return !string.IsNullOrWhiteSpace(isbn) && 
               (isbn.Length == 10 || isbn.Length == 13);
    }
    
    public override bool Equals(object obj) =>
        obj is ISBN other && Value == other.Value;
    
    public override int GetHashCode() => Value.GetHashCode();
}

public class BookTitle : ValueObject
{
    public string Value { get; }
    
    public BookTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255)
            throw new ArgumentException("Invalid title");
        Value = value.Trim();
    }
    
    public override bool Equals(object obj) =>
        obj is BookTitle other && Value == other.Value;
    
    public override int GetHashCode() => Value.GetHashCode();
}

// ============= ENTITIES & AGGREGATES =============

public class Book : Entity
{
    public ISBN Isbn { get; private set; }
    public BookTitle Title { get; private set; }
    public string Author { get; private set; }
    public Money Price { get; private set; }
    public int AvailableQuantity { get; private set; }
    
    public static Book Create(ISBN isbn, BookTitle title, 
        string author, Money price, int quantity)
    {
        return new Book
        {
            Isbn = isbn,
            Title = title,
            Author = author,
            Price = price,
            AvailableQuantity = quantity
        };
    }
    
    public void ReduceQuantity(int quantity)
    {
        if (quantity > AvailableQuantity)
            throw new InvalidOperationException("Insufficient stock");
        AvailableQuantity -= quantity;
    }
}

public class BookOrder : Entity
{
    public int CustomerId { get; private set; }
    public List<BookOrderLine> OrderLines { get; private set; } = new();
    public Money TotalAmount { get; private set; }
    public OrderStatus Status { get; private set; }
    
    public static BookOrder Create(int customerId)
    {
        return new BookOrder
        {
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            TotalAmount = new Money(0, "USD")
        };
    }
    
    public void AddBook(Book book, int quantity)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Cannot modify non-pending order");
        
        var line = new BookOrderLine(book, quantity);
        OrderLines.Add(line);
        RecalculateTotal();
    }
    
    private void RecalculateTotal()
    {
        TotalAmount = OrderLines.Aggregate(
            new Money(0, "USD"),
            (acc, line) => acc + line.Subtotal
        );
    }
    
    public void Confirm()
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Order already confirmed");
        Status = OrderStatus.Confirmed;
    }
}

public class BookOrderLine : Entity
{
    public Book Book { get; private set; }
    public int Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    
    public Money Subtotal => 
        new Money(UnitPrice.Amount * Quantity, UnitPrice.Currency);
    
    public BookOrderLine(Book book, int quantity)
    {
        Book = book ?? throw new ArgumentNullException(nameof(book));
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive");
        
        Book = book;
        Quantity = quantity;
        UnitPrice = book.Price;
    }
}

// ============= REPOSITORIES =============

public interface IBookRepository
{
    Task<Book> GetByIsbnAsync(ISBN isbn);
    Task<List<Book>> SearchByTitleAsync(string titlePart);
    Task SaveAsync(Book book);
}

public interface IBookOrderRepository
{
    Task<BookOrder> GetByIdAsync(int orderId);
    Task SaveAsync(BookOrder order);
    Task<List<BookOrder>> GetByCustomerAsync(int customerId);
}
```

---

## 5. Common Pitfalls and How to Avoid Them

| Pitfall | Problem | Solution |
|---------|---------|----------|
| **Anemic Models** | Logic in services, not entities | Put business logic in entities and value objects |
| **Wrong Aggregate Boundaries** | Modifying multiple aggregates in one transaction | Keep aggregates small, reference via IDs |
| **Over-Engineering** | DDD everywhere, even for simple CRUD | Use DDD where business complexity exists |
| **Ignoring Ubiquitous Language** | Code doesn't match business terms | Collaborate with domain experts, rename regularly |
| **Mixing Concerns** | Domain logic scattered across layers | Keep domain objects pure, move infrastructure out |
| **Mutability** | Value objects can be modified | Make value objects immutable with private setters |

---

## Summary

Domain-Driven Design provides structure for complex enterprise systems:

1. **Ubiquitous Language**: Shared vocabulary between team and code
2. **Bounded Contexts**: Clear boundaries with distinct models
3. **Value Objects**: Immutable domain concepts without identity
4. **Entities**: Domain objects with persistent identity
5. **Aggregates**: Clustered entities with clear transaction boundaries
6. **Repositories**: Interface to aggregate persistence

In the next topic, we'll explore how to handle change and complexity with Domain Events and CQRS.
