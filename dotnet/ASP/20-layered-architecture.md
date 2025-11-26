# 20. Layered Architecture

## Overview
Layered Architecture organizes an enterprise application into horizontal layers, each with specific responsibilities. This separation creates maintainability, testability, and clear boundaries between concerns. Combined with DDD principles from the previous topics, it provides the structural foundation for building scalable enterprise systems.

---

## 1. Core Layers

A typical enterprise application has four main layers:

```
┌─────────────────────────────────────┐
│    Presentation / API Layer         │  (Controllers, WebAPI endpoints)
├─────────────────────────────────────┤
│    Application Layer                │  (Use cases, orchestration)
├─────────────────────────────────────┤
│    Domain Layer                     │  (Business rules, entities)
├─────────────────────────────────────┤
│    Infrastructure Layer             │  (Persistence, external services)
└─────────────────────────────────────┘
```

### 1.1 Domain Layer (Core Business Logic)

The innermost layer contains **pure business logic** with no dependencies on infrastructure or frameworks.

**Responsibilities:**
- Domain entities and aggregates
- Value objects
- Domain events
- Repository interfaces (not implementations)
- Business rule validation

**Key Principle:** Domain layer should not depend on any other layer.

```csharp
// Domain Layer Structure
namespace Bookstore.Domain
{
    // Entities
    public abstract class AggregateRoot
    {
        protected List<DomainEvent> _events = new();
        public IReadOnlyList<DomainEvent> Events => _events.AsReadOnly();
    }
    
    public class Order : AggregateRoot
    {
        public int Id { get; private set; }
        public int CustomerId { get; private set; }
        public List<OrderLine> Lines { get; private set; }
        public Money Total { get; private set; }
        public OrderStatus Status { get; private set; }
        
        // Pure business logic - no external dependencies
        public void ConfirmOrder(PaymentProcessor paymentProcessor)
        {
            if (Status != OrderStatus.Pending)
                throw new DomainException("Order is already confirmed");
            
            // Business rule: validate minimum order value
            if (Total.Amount < 10)
                throw new DomainException("Order total must be at least $10");
            
            Status = OrderStatus.Confirmed;
            
            _events.Add(new OrderConfirmedEvent(
                this.Id,
                this.CustomerId,
                this.Total.Amount
            ));
        }
        
        public void AddItem(BookId bookId, int quantity, Money price)
        {
            if (quantity <= 0)
                throw new DomainException("Quantity must be positive");
            
            var line = new OrderLine(bookId, quantity, price);
            Lines.Add(line);
            
            // Recalculate total
            RecalculateTotal();
        }
        
        private void RecalculateTotal()
        {
            Total = Lines.Aggregate(
                new Money(0, "USD"),
                (acc, line) => acc + line.Subtotal
            );
        }
    }
    
    // Value Objects
    public class BookId : ValueObject
    {
        public int Value { get; }
        
        public BookId(int value)
        {
            if (value <= 0)
                throw new DomainException("BookId must be positive");
            Value = value;
        }
        
        public override bool Equals(object obj) =>
            obj is BookId other && Value == other.Value;
        
        public override int GetHashCode() => Value.GetHashCode();
    }
    
    // Domain Exceptions (not technical exceptions)
    public class DomainException : Exception
    {
        public DomainException(string message) : base(message) { }
    }
    
    // Repository Interfaces (abstractions, not implementations)
    public interface IOrderRepository
    {
        Task<Order> GetByIdAsync(int orderId);
        Task SaveAsync(Order order);
        Task DeleteAsync(int orderId);
    }
    
    // Domain Services (when logic spans multiple aggregates)
    public interface IOrderPricingService
    {
        Money CalculateDiscount(Order order, Customer customer);
        Money CalculateTax(Order order, Address shippingAddress);
    }
}
```

**What Should NOT Be in Domain Layer:**
- ❌ Database/ORM code
- ❌ HTTP/API concerns
- ❌ UI framework code
- ❌ Configuration classes
- ❌ Dependency on external libraries (except core utilities)

### 1.2 Application Layer (Use Cases & Orchestration)

The application layer orchestrates domain objects to implement use cases. It's thin but crucial.

**Responsibilities:**
- Use case / application service implementation
- Command and query handling
- Transaction management
- Cross-cutting concerns (logging, validation)
- Assembling responses for presentation layer

**Key Principle:** Application layer depends on domain layer but is ignorant of presentation and infrastructure details.

```csharp
// Application Layer Structure
namespace Bookstore.Application
{
    // Application Services (use cases)
    public class PlaceOrderService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IBookRepository _bookRepository;
        private readonly IDomainEventPublisher _eventPublisher;
        private readonly IOrderPricingService _pricingService;
        private readonly ILogger<PlaceOrderService> _logger;
        
        public PlaceOrderService(
            IOrderRepository orderRepository,
            ICustomerRepository customerRepository,
            IBookRepository bookRepository,
            IDomainEventPublisher eventPublisher,
            IOrderPricingService pricingService,
            ILogger<PlaceOrderService> logger)
        {
            _orderRepository = orderRepository;
            _customerRepository = customerRepository;
            _bookRepository = bookRepository;
            _eventPublisher = eventPublisher;
            _pricingService = pricingService;
            _logger = logger;
        }
        
        // Orchestrates the use case
        public async Task<PlaceOrderResponse> ExecuteAsync(PlaceOrderRequest request)
        {
            try
            {
                // 1. Load and validate data
                var customer = await _customerRepository.GetByIdAsync(request.CustomerId);
                if (customer == null)
                    throw new ApplicationException("Customer not found");
                
                // 2. Create aggregate with domain logic
                var order = Order.Create(request.CustomerId);
                
                foreach (var item in request.Items)
                {
                    var book = await _bookRepository.GetByIdAsync(item.BookId);
                    if (book == null)
                        throw new ApplicationException($"Book {item.BookId} not found");
                    
                    order.AddItem(
                        new BookId(item.BookId),
                        item.Quantity,
                        book.Price
                    );
                }
                
                // 3. Apply business rules
                var discount = _pricingService.CalculateDiscount(order, customer);
                order.ApplyDiscount(discount);
                
                // 4. Persist
                await _orderRepository.SaveAsync(order);
                
                // 5. Publish domain events
                var events = order.GetDomainEvents();
                await _eventPublisher.PublishAsync(events);
                
                _logger.LogInformation("Order {OrderId} placed successfully", order.Id);
                
                // 6. Return response
                return new PlaceOrderResponse
                {
                    OrderId = order.Id,
                    Total = order.Total.Amount,
                    Status = "Success"
                };
            }
            catch (DomainException ex)
            {
                _logger.LogWarning("Domain error placing order: {Error}", ex.Message);
                throw new ApplicationException("Failed to place order: " + ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error placing order");
                throw;
            }
        }
    }
    
    // DTOs for requests/responses
    public class PlaceOrderRequest
    {
        public int CustomerId { get; set; }
        public List<OrderItemRequest> Items { get; set; }
    }
    
    public class OrderItemRequest
    {
        public int BookId { get; set; }
        public int Quantity { get; set; }
    }
    
    public class PlaceOrderResponse
    {
        public int OrderId { get; set; }
        public decimal Total { get; set; }
        public string Status { get; set; }
    }
    
    // Query handlers also live here
    public class GetOrderDetailsQueryHandler
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IReadModelContext _readContext;
        
        public GetOrderDetailsQueryHandler(
            IOrderRepository orderRepository,
            IReadModelContext readContext)
        {
            _orderRepository = orderRepository;
            _readContext = readContext;
        }
        
        public async Task<OrderDetailsResponse> ExecuteAsync(int orderId)
        {
            // For simple queries, read from read model
            var readModel = await _readContext.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId);
            
            return new OrderDetailsResponse
            {
                OrderId = readModel.Id,
                Total = readModel.Total,
                Status = readModel.Status,
                Items = readModel.Items
            };
        }
    }
    
    public class OrderDetailsResponse
    {
        public int OrderId { get; set; }
        public decimal Total { get; set; }
        public string Status { get; set; }
        public List<OrderItemResponse> Items { get; set; }
    }
    
    public class OrderItemResponse
    {
        public string BookTitle { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }
    
    // Domain event handlers (also orchestration)
    public class SendOrderConfirmationEmailWhenOrderPlacedHandler
    {
        private readonly IEmailService _emailService;
        private readonly ICustomerRepository _customerRepository;
        
        public SendOrderConfirmationEmailWhenOrderPlacedHandler(
            IEmailService emailService,
            ICustomerRepository customerRepository)
        {
            _emailService = emailService;
            _customerRepository = customerRepository;
        }
        
        public async Task HandleAsync(OrderPlacedEvent @event)
        {
            var customer = await _customerRepository.GetByIdAsync(@event.CustomerId);
            
            await _emailService.SendAsync(
                customer.Email,
                "Order Confirmation",
                $"Your order #{@event.OrderId} has been placed"
            );
        }
    }
}
```

### 1.3 Presentation Layer (API / Controller Layer)

Handles HTTP requests/responses and user interaction.

**Responsibilities:**
- ASP.NET Core controllers
- HTTP request validation
- Response formatting
- Error handling and status codes
- Authentication/Authorization checks
- Content negotiation

**Key Principle:** Controllers are thin, delegating to application layer.

```csharp
// Presentation Layer Structure
namespace Bookstore.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class OrdersController : ControllerBase
    {
        private readonly PlaceOrderService _placeOrderService;
        private readonly GetOrderDetailsQueryHandler _getOrderDetailsHandler;
        private readonly ILogger<OrdersController> _logger;
        
        public OrdersController(
            PlaceOrderService placeOrderService,
            GetOrderDetailsQueryHandler getOrderDetailsHandler,
            ILogger<OrdersController> logger)
        {
            _placeOrderService = placeOrderService;
            _getOrderDetailsHandler = getOrderDetailsHandler;
            _logger = logger;
        }
        
        [HttpPost]
        [ProducesResponseType(typeof(PlaceOrderResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
        {
            try
            {
                // Input validation
                if (request == null || !request.Items.Any())
                    return BadRequest(new ErrorResponse { Message = "No items in order" });
                
                // Call application service
                var response = await _placeOrderService.ExecuteAsync(request);
                
                // Return appropriate HTTP response
                return CreatedAtAction(nameof(GetOrder), 
                    new { id = response.OrderId }, response);
            }
            catch (ApplicationException ex)
            {
                _logger.LogWarning(ex, "Validation error placing order");
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error placing order");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse { Message = "An error occurred" });
            }
        }
        
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(OrderDetailsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetOrder(int id)
        {
            try
            {
                var result = await _getOrderDetailsHandler.ExecuteAsync(id);
                
                if (result == null)
                    return NotFound();
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving order {OrderId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new ErrorResponse { Message = "An error occurred" });
            }
        }
        
        [HttpGet("customer/{customerId}")]
        [ProducesResponseType(typeof(List<OrderSummaryResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCustomerOrders(int customerId)
        {
            var orders = await _getOrderDetailsHandler.GetCustomerOrdersAsync(customerId);
            return Ok(orders);
        }
    }
    
    // Response models
    public class ErrorResponse
    {
        public string Message { get; set; }
        public string ErrorCode { get; set; }
    }
}
```

### 1.4 Infrastructure Layer (Persistence & External Services)

Handles technical implementation details.

**Responsibilities:**
- Database access (EF Core implementations)
- Repository implementations
- External API clients
- Email, logging, caching services
- Configuration and dependency injection setup
- Data migration management

**Key Principle:** Infrastructure depends on all layers, but other layers only depend on abstractions (interfaces) defined in domain/application layers.

```csharp
// Infrastructure Layer Structure
namespace Bookstore.Infrastructure
{
    // Database Context
    public class BookstoreDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Book> Books { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure entities
            modelBuilder.Entity<Order>()
                .HasKey(o => o.Id);
            
            modelBuilder.Entity<Order>()
                .HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder.Entity<OrderLine>()
                .HasNoKey()
                .ToTable("OrderLines");
        }
    }
    
    // Repository Implementation
    public class EFOrderRepository : IOrderRepository
    {
        private readonly BookstoreDbContext _context;
        private readonly ILogger<EFOrderRepository> _logger;
        
        public EFOrderRepository(BookstoreDbContext context,
            ILogger<EFOrderRepository> logger)
        {
            _context = context;
            _logger = logger;
        }
        
        public async Task<Order> GetByIdAsync(int orderId)
        {
            try
            {
                return await _context.Orders
                    .Include(o => o.Lines)
                    .FirstOrDefaultAsync(o => o.Id == orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving order {OrderId}", orderId);
                throw new RepositoryException("Failed to retrieve order", ex);
            }
        }
        
        public async Task SaveAsync(Order order)
        {
            try
            {
                _context.Orders.Add(order);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Order {OrderId} saved", order.Id);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error saving order");
                throw new RepositoryException("Failed to save order", ex);
            }
        }
        
        public async Task DeleteAsync(int orderId)
        {
            try
            {
                var order = await GetByIdAsync(orderId);
                if (order != null)
                {
                    _context.Orders.Remove(order);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Order {OrderId} deleted", orderId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting order {OrderId}", orderId);
                throw new RepositoryException("Failed to delete order", ex);
            }
        }
    }
    
    // External Service Implementation
    public class SmtpEmailService : IEmailService
    {
        private readonly EmailConfiguration _config;
        private readonly ILogger<SmtpEmailService> _logger;
        
        public SmtpEmailService(IOptions<EmailConfiguration> config,
            ILogger<SmtpEmailService> logger)
        {
            _config = config.Value;
            _logger = logger;
        }
        
        public async Task SendAsync(string to, string subject, string body)
        {
            using (var client = new SmtpClient(_config.SmtpServer, _config.SmtpPort))
            {
                client.Credentials = new NetworkCredential(
                    _config.SenderEmail,
                    _config.SenderPassword
                );
                client.EnableSsl = true;
                
                var mailMessage = new MailMessage(
                    _config.SenderEmail,
                    to,
                    subject,
                    body
                )
                {
                    IsBodyHtml = true
                };
                
                try
                {
                    await client.SendMailAsync(mailMessage);
                    _logger.LogInformation("Email sent to {To}", to);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send email to {To}", to);
                    throw new ServiceException("Failed to send email", ex);
                }
            }
        }
    }
    
    // Configuration
    public class EmailConfiguration
    {
        public string SmtpServer { get; set; }
        public int SmtpPort { get; set; }
        public string SenderEmail { get; set; }
        public string SenderPassword { get; set; }
    }
}
```

---

## 2. Cross-Cutting Concerns

### 2.1 Middleware and Filters

Concerns that span layers are handled through middleware and filters:

```csharp
// Global exception handling middleware
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    
    public ExceptionHandlingMiddleware(RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Domain exception occurred");
            await HandleExceptionAsync(context, ex, StatusCodes.Status400BadRequest);
        }
        catch (ApplicationException ex)
        {
            _logger.LogWarning(ex, "Application exception occurred");
            await HandleExceptionAsync(context, ex, StatusCodes.Status400BadRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred");
            await HandleExceptionAsync(context, ex, StatusCodes.Status500InternalServerError);
        }
    }
    
    private static Task HandleExceptionAsync(HttpContext context, 
        Exception exception, int statusCode)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;
        
        var response = new { message = exception.Message };
        return context.Response.WriteAsJsonAsync(response);
    }
}

// Logging filter for tracking requests
public class LoggingFilter : IAsyncActionFilter
{
    private readonly ILogger<LoggingFilter> _logger;
    
    public LoggingFilter(ILogger<LoggingFilter> logger)
    {
        _logger = logger;
    }
    
    public async Task OnActionExecutionAsync(ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var correlationId = Guid.NewGuid().ToString();
        _logger.LogInformation(
            "Request {CorrelationId} to {Action} started",
            correlationId, context.ActionDescriptor.DisplayName
        );
        
        var result = await next();
        
        _logger.LogInformation(
            "Request {CorrelationId} completed with status {StatusCode}",
            correlationId, context.HttpContext.Response.StatusCode
        );
    }
}
```

### 2.2 Dependency Injection Setup

Organize DI registration by layer:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add layers to DI container
builder.Services
    .AddDomainServices()
    .AddApplicationServices()
    .AddInfrastructureServices(builder.Configuration)
    .AddPresentationServices();

var app = builder.Build();

// Configure middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();

// Extension methods for organizing registration
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        services.AddScoped<IOrderPricingService, OrderPricingService>();
        return services;
    }
    
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<PlaceOrderService>();
        services.AddScoped<GetOrderDetailsQueryHandler>();
        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
        return services;
    }
    
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BookstoreDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default")));
        
        services.AddScoped<IOrderRepository, EFOrderRepository>();
        services.AddScoped<ICustomerRepository, EFCustomerRepository>();
        services.AddScoped<IBookRepository, EFBookRepository>();
        
        services.Configure<EmailConfiguration>(configuration.GetSection("Email"));
        services.AddScoped<IEmailService, SmtpEmailService>();
        
        return services;
    }
    
    public static IServiceCollection AddPresentationServices(this IServiceCollection services)
    {
        services.AddControllers();
        services.AddSwaggerGen();
        services.AddScoped<LoggingFilter>();
        return services;
    }
}
```

---

## 3. Dependency Flow

A critical principle: **Dependencies flow inward.**

```
External Layer
    ↓
Presentation → Application → Domain
    ↓            ↓
Infrastructure
```

**Rules:**
- Domain layer has NO external dependencies
- Application layer depends on Domain
- Infrastructure implements interfaces from Domain/Application
- Presentation depends on Application services

**Anti-pattern: Circular Dependencies**
```csharp
// ❌ DON'T DO THIS
namespace Bookstore.Domain
{
    public class Order
    {
        private readonly EmailService _emailService;  // Infrastructure dependency!
        
        public void Confirm()
        {
            _emailService.Send("...");  // Wrong!
        }
    }
}

// ✅ DO THIS INSTEAD
namespace Bookstore.Domain
{
    public class Order
    {
        public void Confirm()
        {
            _events.Add(new OrderConfirmedEvent(...));
        }
    }
}

namespace Bookstore.Application
{
    public class ConfirmOrderService
    {
        private readonly IEmailService _emailService;
        
        public async Task ExecuteAsync(int orderId)
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            order.Confirm();
            
            // Application layer handles side effects
            var @event = order.GetDomainEvents().OfType<OrderConfirmedEvent>().First();
            await _emailService.SendAsync("...");
        }
    }
}
```

---

## 4. Folder Structure

Organize projects by layer:

```
solution/
├── src/
│   ├── Bookstore.Domain/              # Pure business logic
│   │   ├── Entities/
│   │   │   ├── Order.cs
│   │   │   ├── Customer.cs
│   │   ├── ValueObjects/
│   │   │   ├── BookId.cs
│   │   │   ├── Money.cs
│   │   ├── Events/
│   │   │   ├── OrderPlacedEvent.cs
│   │   │   ├── OrderConfirmedEvent.cs
│   │   ├── Interfaces/
│   │   │   ├── IOrderRepository.cs
│   │   │   ├── IOrderPricingService.cs
│   │   └── Exceptions/
│   │       └── DomainException.cs
│   │
│   ├── Bookstore.Application/         # Use cases and orchestration
│   │   ├── Services/
│   │   │   ├── PlaceOrderService.cs
│   │   │   ├── ConfirmOrderService.cs
│   │   ├── QueryHandlers/
│   │   │   ├── GetOrderDetailsQueryHandler.cs
│   │   ├── EventHandlers/
│   │   │   ├── SendOrderConfirmationEmailHandler.cs
│   │   ├── DTOs/
│   │   │   ├── PlaceOrderRequest.cs
│   │   │   ├── OrderDetailsResponse.cs
│   │   └── Exceptions/
│   │       └── ApplicationException.cs
│   │
│   ├── Bookstore.Infrastructure/      # Technical implementations
│   │   ├── Persistence/
│   │   │   ├── BookstoreDbContext.cs
│   │   │   ├── Repositories/
│   │   │   │   ├── EFOrderRepository.cs
│   │   │   │   ├── EFCustomerRepository.cs
│   │   ├── Services/
│   │   │   ├── SmtpEmailService.cs
│   │   │   ├── OrderPricingService.cs
│   │   └── Configuration/
│   │       └── EmailConfiguration.cs
│   │
│   └── Bookstore.Api/                 # HTTP endpoints
│       ├── Controllers/
│       │   ├── OrdersController.cs
│       │   ├── CustomersController.cs
│       ├── Filters/
│       │   └── LoggingFilter.cs
│       ├── Middleware/
│       │   └── ExceptionHandlingMiddleware.cs
│       └── Program.cs
│
└── tests/
    ├── Bookstore.Domain.Tests/
    ├── Bookstore.Application.Tests/
    └── Bookstore.Api.Tests/
```

---

## 5. Testing Implications

Layered architecture enables effective testing at each level:

```csharp
// Unit test: Domain logic (no mocks needed)
public class OrderTests
{
    [Fact]
    public void AddItem_WithInvalidQuantity_ThrowsDomainException()
    {
        var order = Order.Create(customerId: 1);
        
        Assert.Throws<DomainException>(() => 
            order.AddItem(bookId: 1, quantity: -1, price: new Money(10, "USD"))
        );
    }
}

// Unit test: Application service (mock repositories)
public class PlaceOrderServiceTests
{
    private readonly Mock<IOrderRepository> _mockOrderRepository;
    private readonly Mock<IBookRepository> _mockBookRepository;
    private readonly PlaceOrderService _service;
    
    public PlaceOrderServiceTests()
    {
        _mockOrderRepository = new Mock<IOrderRepository>();
        _mockBookRepository = new Mock<IBookRepository>();
        _service = new PlaceOrderService(
            _mockOrderRepository.Object,
            _mockBookRepository.Object,
            // ... other dependencies
        );
    }
    
    [Fact]
    public async Task Execute_WithValidRequest_CreatesOrder()
    {
        // Arrange
        var request = new PlaceOrderRequest { /* ... */ };
        _mockBookRepository.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new Book { Id = 1, Price = new Money(10, "USD") });
        
        // Act
        var response = await _service.ExecuteAsync(request);
        
        // Assert
        Assert.NotNull(response);
        _mockOrderRepository.Verify(r => r.SaveAsync(It.IsAny<Order>()), Times.Once);
    }
}

// Integration test: API endpoint
public class OrdersControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    
    public OrdersControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task PlaceOrder_WithValidRequest_Returns201Created()
    {
        // Arrange
        var request = new { /* ... */ };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);
        
        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
```

---

## 6. Layered Architecture Best Practices

| Best Practice | Implementation |
|---------------|-----------------|
| **One-way dependencies** | Domain ← App ← Infra ← Presentation |
| **Interface segregation** | Interfaces in Domain, implementations in Infrastructure |
| **Clear separation** | Entities in Domain, DTOs in Application |
| **Thin presentation** | Controllers delegate to Application layer |
| **Testability** | Each layer tested independently |
| **Maintainability** | Easy to locate and modify business logic |

---

## 7. Anti-patterns to Avoid

| Anti-Pattern | Problem | Solution |
|--------------|---------|----------|
| **God Classes** | Single entity with too many responsibilities | Split into separate aggregates |
| **Anemic Domain** | No business logic in entities | Move logic from services to entities |
| **Infrastructure Leakage** | Domain logic depends on DB/APIs | Inject dependencies, use DDD patterns |
| **Fat Controllers** | Complex logic in presentation layer | Delegate to Application services |
| **Distributed Transactions** | Trying to update multiple aggregates atomically | Use eventual consistency with events |

---

## Summary

Layered architecture provides structure for enterprise systems:

1. **Domain Layer**: Pure business logic, no external dependencies
2. **Application Layer**: Orchestrates domain objects, implements use cases
3. **Infrastructure Layer**: Technical implementations (database, external services)
4. **Presentation Layer**: HTTP endpoints, thin controllers

Combined with DDD patterns (aggregates, events, bounded contexts), layered architecture creates maintainable, testable, scalable enterprise systems.

**Key principles:**
- Dependencies flow inward
- One abstraction per interface
- Each layer has clear responsibilities
- Testable at each level
- Supports domain-driven design naturally

With these three foundational topics (DDD, Domain Events & CQRS, Layered Architecture), you now have the architectural patterns needed for enterprise systems. The remaining topics build on this foundation with testing strategies, asynchronous patterns, observability, and deployment.
