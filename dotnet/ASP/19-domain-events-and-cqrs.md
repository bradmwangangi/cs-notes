# 19. Domain Events & CQRS

## Overview
As your domain grows in complexity, you need mechanisms to express important business occurrences and organize how the system reads and writes data. Domain Events capture what happened in the business, while CQRS (Command Query Responsibility Segregation) separates how data is modified from how it's read.

---

## 1. Domain Events

### 1.1 Concept and Purpose

A **Domain Event** represents something meaningful that happened in the business domain. It's a record of a state change that other parts of the system might be interested in.

**Examples:**
- `OrderPlaced` - A customer placed a new order
- `PaymentReceived` - Payment was successfully processed
- `InventoryReserved` - Items were reserved from stock
- `CustomerRegistered` - A new customer signed up
- `ShipmentDispatched` - Order was shipped

**Why use Domain Events:**
- Decouples components (order system doesn't need to know about shipping)
- Enables audit trails (you know exactly what happened and when)
- Supports integration with external systems
- Enables temporal data analysis (how did business metrics change)
- Foundation for event sourcing

### 1.2 Implementing Domain Events

**Basic Domain Event Structure:**

```csharp
// Base class for all domain events
public abstract class DomainEvent
{
    public int AggregateId { get; protected set; }
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    
    // Optional: Correlation ID for distributed tracing
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
}

// Concrete domain events
public class OrderPlacedEvent : DomainEvent
{
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
    public List<OrderItemDetail> Items { get; set; }
    
    public OrderPlacedEvent(int orderId, int customerId, 
        decimal totalAmount, List<OrderItemDetail> items)
    {
        OrderId = orderId;
        CustomerId = customerId;
        TotalAmount = totalAmount;
        Items = items;
        AggregateId = orderId;
    }
}

public class OrderItemDetail
{
    public int BookId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public class PaymentProcessedEvent : DomainEvent
{
    public int OrderId { get; set; }
    public string TransactionId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    
    public PaymentProcessedEvent(int orderId, string transactionId, 
        decimal amount, PaymentStatus status)
    {
        OrderId = orderId;
        TransactionId = transactionId;
        Amount = amount;
        Status = status;
        AggregateId = orderId;
    }
}

public enum PaymentStatus
{
    Approved,
    Declined,
    Pending
}

public class InventoryReservedEvent : DomainEvent
{
    public int OrderId { get; set; }
    public List<InventoryReservation> Reservations { get; set; }
    
    public InventoryReservedEvent(int orderId, 
        List<InventoryReservation> reservations)
    {
        OrderId = orderId;
        Reservations = reservations;
        AggregateId = orderId;
    }
}

public class InventoryReservation
{
    public int BookId { get; set; }
    public int WarehouseId { get; set; }
    public int QuantityReserved { get; set; }
}
```

### 1.3 Raising Domain Events from Aggregates

Domain events should be raised from aggregate roots when important business actions occur:

```csharp
public class Order : AggregateRoot
{
    private List<DomainEvent> _domainEvents = new();
    
    public int Id { get; private set; }
    public int CustomerId { get; private set; }
    public List<OrderLine> OrderLines { get; private set; } = new();
    public Money Total { get; private set; }
    public OrderStatus Status { get; private set; }
    
    // Get and clear raised events for publishing
    public IReadOnlyList<DomainEvent> GetDomainEvents() => _domainEvents.AsReadOnly();
    
    public void ClearDomainEvents() => _domainEvents.Clear();
    
    // Factory method that creates order and raises event
    public static Order Create(int customerId, List<OrderLine> lines)
    {
        if (!lines.Any())
            throw new ArgumentException("Order must have at least one item");
        
        var order = new Order
        {
            Id = GenerateNewId(), // In real app, would be set by repository
            CustomerId = customerId,
            OrderLines = lines,
            Status = OrderStatus.Pending
        };
        
        order.CalculateTotal();
        
        // Raise event when order is created
        order._domainEvents.Add(new OrderPlacedEvent(
            order.Id,
            customerId,
            order.Total.Amount,
            order.OrderLines.Select(ol => new OrderItemDetail
            {
                BookId = ol.BookId,
                Quantity = ol.Quantity,
                Price = ol.UnitPrice.Amount
            }).ToList()
        ));
        
        return order;
    }
    
    // When payment is processed, raise event
    public void ApplyPayment(Payment payment)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Order already processed");
        
        Status = OrderStatus.PendingShipment;
        
        _domainEvents.Add(new PaymentProcessedEvent(
            Id,
            payment.TransactionId,
            payment.Amount.Amount,
            PaymentStatus.Approved
        ));
    }
    
    // When inventory is reserved, raise event
    public void ReserveInventory(List<InventoryReservation> reservations)
    {
        _domainEvents.Add(new InventoryReservedEvent(Id, reservations));
    }
    
    private void CalculateTotal()
    {
        Total = OrderLines.Aggregate(
            new Money(0, "USD"),
            (acc, line) => acc + line.Subtotal
        );
    }
    
    private static int GenerateNewId() => 
        (int)(DateTime.UtcNow.Ticks % int.MaxValue);
}

public class OrderLine
{
    public int BookId { get; set; }
    public int Quantity { get; set; }
    public Money UnitPrice { get; set; }
    
    public Money Subtotal => 
        new Money(UnitPrice.Amount * Quantity, UnitPrice.Currency);
}

public class Payment
{
    public string TransactionId { get; set; }
    public Money Amount { get; set; }
}

public abstract class AggregateRoot : Entity { }

public abstract class Entity
{
    public int Id { get; protected set; }
}
```

### 1.4 Publishing and Handling Domain Events

**Event Publisher Interface:**

```csharp
public interface IDomainEventPublisher
{
    Task PublishAsync<T>(T domainEvent) where T : DomainEvent;
    Task PublishAsync(IEnumerable<DomainEvent> domainEvents);
}

public interface IDomainEventHandler<T> where T : DomainEvent
{
    Task HandleAsync(T domainEvent);
}
```

**Example Handlers:**

```csharp
// When an order is placed, send a confirmation email
public class SendOrderConfirmationEmailHandler : 
    IDomainEventHandler<OrderPlacedEvent>
{
    private readonly IEmailService _emailService;
    private readonly ICustomerRepository _customerRepository;
    
    public SendOrderConfirmationEmailHandler(IEmailService emailService,
        ICustomerRepository customerRepository)
    {
        _emailService = emailService;
        _customerRepository = customerRepository;
    }
    
    public async Task HandleAsync(OrderPlacedEvent domainEvent)
    {
        var customer = await _customerRepository.GetByIdAsync(domainEvent.CustomerId);
        
        var emailContent = $@"
            Order Confirmation
            Order ID: {domainEvent.OrderId}
            Total: ${domainEvent.TotalAmount}
            
            Items:
            {string.Join("\n", domainEvent.Items.Select(i => 
                $"  - Book {i.BookId}: {i.Quantity} x ${i.Price}"))}
        ";
        
        await _emailService.SendAsync(
            customer.Email,
            "Order Confirmation",
            emailContent
        );
    }
}

// When an order is placed, reserve inventory
public class ReserveInventoryHandler : IDomainEventHandler<OrderPlacedEvent>
{
    private readonly IInventoryService _inventoryService;
    
    public ReserveInventoryHandler(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }
    
    public async Task HandleAsync(OrderPlacedEvent domainEvent)
    {
        var reservations = domainEvent.Items.Select(item =>
            new InventoryReservation
            {
                BookId = item.BookId,
                Quantity = item.Quantity
            }).ToList();
        
        await _inventoryService.ReserveAsync(domainEvent.OrderId, reservations);
    }
}

// When payment is processed, update order status
public class UpdateOrderStatusWhenPaymentProcessedHandler : 
    IDomainEventHandler<PaymentProcessedEvent>
{
    private readonly IOrderRepository _orderRepository;
    
    public UpdateOrderStatusWhenPaymentProcessedHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }
    
    public async Task HandleAsync(PaymentProcessedEvent domainEvent)
    {
        var order = await _orderRepository.GetByIdAsync(domainEvent.OrderId);
        
        if (order != null && domainEvent.Status == PaymentStatus.Approved)
        {
            // Order state is updated, this would raise another event if needed
            await _orderRepository.UpdateAsync(order);
        }
    }
}
```

**Publishing Events (in Application Service):**

```csharp
public class PlaceOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IDomainEventPublisher _eventPublisher;
    
    public PlaceOrderService(IOrderRepository orderRepository,
        IDomainEventPublisher eventPublisher)
    {
        _orderRepository = orderRepository;
        _eventPublisher = eventPublisher;
    }
    
    public async Task<int> ExecuteAsync(int customerId, List<OrderLine> lines)
    {
        var order = Order.Create(customerId, lines);
        
        await _orderRepository.SaveAsync(order);
        
        // Publish all events raised during order creation
        var events = order.GetDomainEvents();
        await _eventPublisher.PublishAsync(events);
        
        order.ClearDomainEvents();
        
        return order.Id;
    }
}
```

**Registering Handlers in DI Container:**

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDomainEventHandlers(
        this IServiceCollection services)
    {
        // Register all domain event handlers
        services.AddScoped<IDomainEventHandler<OrderPlacedEvent>,
            SendOrderConfirmationEmailHandler>();
        services.AddScoped<IDomainEventHandler<OrderPlacedEvent>,
            ReserveInventoryHandler>();
        services.AddScoped<IDomainEventHandler<PaymentProcessedEvent>,
            UpdateOrderStatusWhenPaymentProcessedHandler>();
        
        // Register publisher
        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
        
        return services;
    }
}
```

---

## 2. CQRS (Command Query Responsibility Segregation)

### 2.1 CQRS Principles

CQRS separates the model for updating (writes) from the model for reading (reads). The key insight:

**Write Model** (Command Side):
- Optimized for consistency and business rules
- Enforces invariants
- Highly normalized
- Complex domain logic

**Read Model** (Query Side):
- Optimized for query performance
- Denormalized for easy reads
- Fast, simple retrieval
- May lag behind write model

**When to Use:**
- System has different read and write patterns
- Complex business logic but simple queries
- High volume of reads vs. writes
- Multiple query types needed

**When NOT to Use:**
- Simple CRUD operations
- Equal complexity in reads and writes
- Team not familiar with eventual consistency

### 2.2 Command Side (Write Model)

Commands represent an intent to change the system state:

```csharp
// Base command
public abstract class Command
{
    public string CommandId { get; } = Guid.NewGuid().ToString();
    public DateTime IssuedAt { get; } = DateTime.UtcNow;
}

// Specific commands
public class PlaceOrderCommand : Command
{
    public int CustomerId { get; set; }
    public List<OrderItemInput> Items { get; set; }
}

public class OrderItemInput
{
    public int BookId { get; set; }
    public int Quantity { get; set; }
}

public class ProcessPaymentCommand : Command
{
    public int OrderId { get; set; }
    public string CardToken { get; set; }
    public decimal Amount { get; set; }
}

public class ShipOrderCommand : Command
{
    public int OrderId { get; set; }
    public string TrackingNumber { get; set; }
}

// Command handlers
public interface ICommandHandler<TCommand> where TCommand : Command
{
    Task HandleAsync(TCommand command);
}

public interface ICommandHandler<TCommand, TResult> where TCommand : Command
{
    Task<TResult> HandleAsync(TCommand command);
}

// Implementation
public class PlaceOrderCommandHandler : 
    ICommandHandler<PlaceOrderCommand, int>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IBookRepository _bookRepository;
    private readonly IDomainEventPublisher _eventPublisher;
    
    public PlaceOrderCommandHandler(IOrderRepository orderRepository,
        IBookRepository bookRepository,
        IDomainEventPublisher eventPublisher)
    {
        _orderRepository = orderRepository;
        _bookRepository = bookRepository;
        _eventPublisher = eventPublisher;
    }
    
    public async Task<int> HandleAsync(PlaceOrderCommand command)
    {
        // Validate command
        if (command.Items == null || !command.Items.Any())
            throw new InvalidOperationException("Order must have items");
        
        // Load aggregates from repositories
        var orderLines = new List<OrderLine>();
        foreach (var item in command.Items)
        {
            var book = await _bookRepository.GetByIdAsync(item.BookId);
            if (book == null)
                throw new InvalidOperationException($"Book {item.BookId} not found");
            
            orderLines.Add(new OrderLine
            {
                BookId = book.Id,
                Quantity = item.Quantity,
                UnitPrice = book.Price
            });
        }
        
        // Create aggregate (enforces business rules)
        var order = Order.Create(command.CustomerId, orderLines);
        
        // Persist
        await _orderRepository.SaveAsync(order);
        
        // Publish events
        var events = order.GetDomainEvents();
        await _eventPublisher.PublishAsync(events);
        order.ClearDomainEvents();
        
        return order.Id;
    }
}

public class ProcessPaymentCommandHandler : 
    ICommandHandler<ProcessPaymentCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IDomainEventPublisher _eventPublisher;
    
    public ProcessPaymentCommandHandler(IOrderRepository orderRepository,
        IPaymentGateway paymentGateway,
        IDomainEventPublisher eventPublisher)
    {
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
        _eventPublisher = eventPublisher;
    }
    
    public async Task HandleAsync(ProcessPaymentCommand command)
    {
        var order = await _orderRepository.GetByIdAsync(command.OrderId);
        if (order == null)
            throw new InvalidOperationException("Order not found");
        
        // Process payment through gateway
        var paymentResult = await _paymentGateway.ProcessAsync(
            command.CardToken,
            command.Amount
        );
        
        if (!paymentResult.IsSuccessful)
            throw new PaymentFailedException(paymentResult.ErrorMessage);
        
        // Update aggregate
        var payment = new Payment
        {
            TransactionId = paymentResult.TransactionId,
            Amount = new Money(command.Amount, "USD")
        };
        order.ApplyPayment(payment);
        
        // Persist
        await _orderRepository.UpdateAsync(order);
        
        // Publish events
        var events = order.GetDomainEvents();
        await _eventPublisher.PublishAsync(events);
        order.ClearDomainEvents();
    }
}
```

### 2.3 Query Side (Read Model)

Queries retrieve data without modifying it. The read model is typically denormalized:

```csharp
// Query definitions
public abstract class Query<TResult>
{
}

public class GetOrderDetailsQuery : Query<OrderDetailsDto>
{
    public int OrderId { get; set; }
}

public class GetCustomerOrdersQuery : Query<List<OrderSummaryDto>>
{
    public int CustomerId { get; set; }
}

public class SearchBooksQuery : Query<List<BookListItemDto>>
{
    public string SearchTerm { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

// DTOs for read model
public class OrderDetailsDto
{
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; }
    public List<OrderLineDto> Items { get; set; }
}

public class OrderLineDto
{
    public int BookId { get; set; }
    public string BookTitle { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; }
}

public class BookListItemDto
{
    public int BookId { get; set; }
    public string Title { get; set; }
    public string Author { get; set; }
    public decimal Price { get; set; }
    public int AvailableQuantity { get; set; }
}

// Query handlers
public interface IQueryHandler<TQuery, TResult> where TQuery : Query<TResult>
{
    Task<TResult> HandleAsync(TQuery query);
}

// Implementation using a denormalized read database
public class GetOrderDetailsQueryHandler : 
    IQueryHandler<GetOrderDetailsQuery, OrderDetailsDto>
{
    private readonly IReadModelContext _readContext;
    
    public GetOrderDetailsQueryHandler(IReadModelContext readContext)
    {
        _readContext = readContext;
    }
    
    public async Task<OrderDetailsDto> HandleAsync(GetOrderDetailsQuery query)
    {
        // Simple, direct query against denormalized read model
        var order = await _readContext.OrderDetails
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == query.OrderId);
        
        if (order == null)
            return null;
        
        return order;
    }
}

public class GetCustomerOrdersQueryHandler : 
    IQueryHandler<GetCustomerOrdersQuery, List<OrderSummaryDto>>
{
    private readonly IReadModelContext _readContext;
    
    public GetCustomerOrdersQueryHandler(IReadModelContext readContext)
    {
        _readContext = readContext;
    }
    
    public async Task<List<OrderSummaryDto>> HandleAsync(GetCustomerOrdersQuery query)
    {
        return await _readContext.OrderSummaries
            .AsNoTracking()
            .Where(o => o.CustomerId == query.CustomerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }
}

public class SearchBooksQueryHandler : 
    IQueryHandler<SearchBooksQuery, List<BookListItemDto>>
{
    private readonly IReadModelContext _readContext;
    
    public SearchBooksQueryHandler(IReadModelContext readContext)
    {
        _readContext = readContext;
    }
    
    public async Task<List<BookListItemDto>> HandleAsync(SearchBooksQuery query)
    {
        var pageSize = query.PageSize ?? 20;
        var pageNumber = query.Page ?? 1;
        var skip = (pageNumber - 1) * pageSize;
        
        return await _readContext.Books
            .AsNoTracking()
            .Where(b => b.Title.Contains(query.SearchTerm) || 
                       b.Author.Contains(query.SearchTerm))
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync();
    }
}
```

### 2.4 Synchronizing Read and Write Models

The read model must stay in sync with the write model. This is typically done through domain events:

```csharp
// When OrderPlacedEvent is raised, update the read model
public class UpdateOrderReadModelWhenPlacedHandler : 
    IDomainEventHandler<OrderPlacedEvent>
{
    private readonly IReadModelContext _readContext;
    
    public UpdateOrderReadModelWhenPlacedHandler(IReadModelContext readContext)
    {
        _readContext = readContext;
    }
    
    public async Task HandleAsync(OrderPlacedEvent domainEvent)
    {
        var orderSummary = new OrderSummaryDto
        {
            OrderId = domainEvent.OrderId,
            CustomerId = domainEvent.CustomerId,
            CreatedAt = domainEvent.OccurredAt,
            Total = domainEvent.TotalAmount,
            Status = "Pending"
        };
        
        var orderDetails = new OrderDetailsDto
        {
            OrderId = domainEvent.OrderId,
            CustomerId = domainEvent.CustomerId,
            CreatedAt = domainEvent.OccurredAt,
            Total = domainEvent.TotalAmount,
            Status = "Pending",
            Items = domainEvent.Items.Select(i => new OrderLineDto
            {
                BookId = i.BookId,
                Quantity = i.Quantity,
                UnitPrice = i.Price,
                Subtotal = i.Quantity * i.Price
            }).ToList()
        };
        
        _readContext.OrderSummaries.Add(orderSummary);
        _readContext.OrderDetails.Add(orderDetails);
        
        await _readContext.SaveChangesAsync();
    }
}

public interface IReadModelContext
{
    DbSet<OrderDetailsDto> OrderDetails { get; }
    DbSet<OrderSummaryDto> OrderSummaries { get; }
    DbSet<BookListItemDto> Books { get; }
    Task<int> SaveChangesAsync();
}
```

### 2.5 CQRS with Command Bus and Query Bus

For larger systems, use a mediator pattern:

```csharp
public interface ICommandBus
{
    Task ExecuteAsync<TCommand>(TCommand command) where TCommand : Command;
    Task<TResult> ExecuteAsync<TCommand, TResult>(TCommand command) 
        where TCommand : Command;
}

public interface IQueryBus
{
    Task<TResult> QueryAsync<TQuery, TResult>(TQuery query) 
        where TQuery : Query<TResult>;
}

public class CommandBus : ICommandBus
{
    private readonly IServiceProvider _serviceProvider;
    
    public CommandBus(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public async Task ExecuteAsync<TCommand>(TCommand command) where TCommand : Command
    {
        var handlerType = typeof(ICommandHandler<>).MakeGenericType(typeof(TCommand));
        var handler = _serviceProvider.GetService(handlerType);
        
        if (handler == null)
            throw new InvalidOperationException($"No handler for {typeof(TCommand).Name}");
        
        var method = handlerType.GetMethod("HandleAsync");
        await (Task)method.Invoke(handler, new object[] { command });
    }
    
    public async Task<TResult> ExecuteAsync<TCommand, TResult>(TCommand command) 
        where TCommand : Command
    {
        var handlerType = typeof(ICommandHandler<,>)
            .MakeGenericType(typeof(TCommand), typeof(TResult));
        var handler = _serviceProvider.GetService(handlerType);
        
        if (handler == null)
            throw new InvalidOperationException($"No handler for {typeof(TCommand).Name}");
        
        var method = handlerType.GetMethod("HandleAsync");
        return await (Task<TResult>)method.Invoke(handler, new object[] { command });
    }
}

// API Controller using CQRS
[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly ICommandBus _commandBus;
    private readonly IQueryBus _queryBus;
    
    public OrdersController(ICommandBus commandBus, IQueryBus queryBus)
    {
        _commandBus = commandBus;
        _queryBus = queryBus;
    }
    
    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        var command = new PlaceOrderCommand
        {
            CustomerId = request.CustomerId,
            Items = request.Items.Select(i => new OrderItemInput
            {
                BookId = i.BookId,
                Quantity = i.Quantity
            }).ToList()
        };
        
        var orderId = await _commandBus.ExecuteAsync<PlaceOrderCommand, int>(command);
        
        return CreatedAtAction(nameof(GetOrder), new { id = orderId });
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(int id)
    {
        var query = new GetOrderDetailsQuery { OrderId = id };
        var result = await _queryBus.QueryAsync<GetOrderDetailsQuery, OrderDetailsDto>(query);
        
        if (result == null)
            return NotFound();
        
        return Ok(result);
    }
}

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
```

---

## 3. Event Sourcing Basics

**Event Sourcing** stores all changes to application state as immutable events rather than just the current state.

```csharp
// Event store interface
public interface IEventStore
{
    Task AppendAsync(int aggregateId, IEnumerable<DomainEvent> events);
    Task<List<DomainEvent>> GetEventsAsync(int aggregateId);
}

// Reconstruct aggregate from events
public class OrderEventSourcingRepository : IOrderRepository
{
    private readonly IEventStore _eventStore;
    
    public async Task<Order> GetByIdAsync(int orderId)
    {
        var events = await _eventStore.GetEventsAsync(orderId);
        
        if (!events.Any())
            return null;
        
        // Replay all events to reconstruct current state
        var order = new Order();
        foreach (var @event in events)
        {
            order.Apply(@event);
        }
        
        return order;
    }
    
    public async Task SaveAsync(Order order)
    {
        var events = order.GetDomainEvents();
        await _eventStore.AppendAsync(order.Id, events);
    }
}

// Aggregate applies events to reconstruct state
public class Order : AggregateRoot
{
    public int Id { get; private set; }
    public int CustomerId { get; private set; }
    public List<OrderLine> OrderLines { get; private set; } = new();
    public Money Total { get; private set; }
    
    public void Apply(DomainEvent @event)
    {
        switch (@event)
        {
            case OrderPlacedEvent orderPlaced:
                ApplyOrderPlaced(orderPlaced);
                break;
            case PaymentProcessedEvent paymentProcessed:
                ApplyPaymentProcessed(paymentProcessed);
                break;
        }
    }
    
    private void ApplyOrderPlaced(OrderPlacedEvent @event)
    {
        Id = @event.OrderId;
        CustomerId = @event.CustomerId;
        Total = new Money(@event.TotalAmount, "USD");
    }
    
    private void ApplyPaymentProcessed(PaymentProcessedEvent @event)
    {
        // Update state based on payment
    }
}
```

---

## Summary

**Domain Events:**
- Capture important business occurrences
- Enable loose coupling between components
- Provide audit trail and temporal analysis
- Foundation for event-driven systems

**CQRS:**
- Separates read and write models
- Optimizes each side for its specific purpose
- Better scalability and performance
- Trades immediate consistency for eventual consistency

**Event Sourcing:**
- Stores events instead of current state
- Complete audit trail of all changes
- Ability to rebuild state at any point in time
- More complex but powerful for complex domains

Next topic: Layered Architecture provides the structural blueprint for organizing your code across these patterns.
