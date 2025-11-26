# 30. Microservices Patterns

## Overview
Microservices decompose large applications into small, independently deployable services. This architecture enables scaling, team autonomy, and technology flexibility, but introduces complexity in inter-service communication, data consistency, and operational management.

---

## 1. Microservices Fundamentals

### 1.1 Monolith vs Microservices

**Monolithic Architecture**
```
┌──────────────────────────┐
│   Order Service          │
│  ┌──────────────────┐    │
│  │ API Controller   │    │
│  ├──────────────────┤    │
│  │ Order Logic      │    │
│  ├──────────────────┤    │
│  │ Inventory Logic  │    │
│  ├──────────────────┤    │
│  │ Payment Logic    │    │
│  ├──────────────────┤    │
│  │ Database         │    │
│  └──────────────────┘    │
└──────────────────────────┘

Single deployment
Tight coupling
Shared database
```

**Microservices Architecture**
```
┌─────────────────┐  ┌──────────────────┐  ┌──────────────────┐
│ Order Service   │  │ Inventory Service│  │ Payment Service  │
├─────────────────┤  ├──────────────────┤  ├──────────────────┤
│ Order API       │  │ Inventory API    │  │ Payment API      │
│ Order Logic     │  │ Stock Logic      │  │ Payment Logic    │
│ Order DB        │  │ Inventory DB     │  │ Payment DB       │
└─────────────────┘  └──────────────────┘  └──────────────────┘
     │                      │                      │
     └──────────────────────┼──────────────────────┘
            Message Bus / Event Stream
```

### 1.2 Service Boundaries

Divide by business capability, not technical concern:

```csharp
// ❌ BAD: Technical boundaries
namespace Bookstore.Services
{
    public class UserService { }           // User data
    public class AuthenticationService { } // Authentication
    public class DatabaseService { }       // Database access
}

// ✅ GOOD: Business capability boundaries
namespace Bookstore.OrderService { }       // Order management
namespace Bookstore.InventoryService { }   // Stock management
namespace Bookstore.PaymentService { }     // Payment processing
namespace Bookstore.CustomerService { }    // Customer management
namespace Bookstore.NotificationService { }// Communications

// Each service owns:
// - Its business logic
// - Its data storage
// - Its API contracts
```

---

## 2. Inter-Service Communication

### 2.1 Synchronous: HTTP/REST and gRPC

**REST Communication**
```csharp
// Order Service calling Inventory Service
public class OrderService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OrderService> _logger;
    
    public async Task<int> PlaceOrderAsync(Order order)
    {
        // Check inventory synchronously
        var checkResult = await _httpClient.GetAsync(
            $"http://inventory-service/api/inventory/check?items={string.Join(",", order.Items)}"
        );
        
        if (!checkResult.IsSuccessStatusCode)
        {
            _logger.LogWarning("Inventory check failed");
            throw new OutOfStockException();
        }
        
        // Continue with order placement
        await SaveOrderAsync(order);
        
        return order.Id;
    }
}

// Advantages:
// - Simple, synchronous flow
// - Immediate response
// - Easy to understand

// Disadvantages:
// - Service coupling: Order depends on Inventory
// - Cascading failures: If Inventory down, Orders fail
// - Network latency
// - Must handle timeouts
```

**gRPC Communication**
```csharp
// More efficient binary protocol than REST
// Built on HTTP/2 for better performance

// Define service contract (inventory.proto)
/*
service InventoryService {
    rpc CheckAvailability (AvailabilityRequest) returns (AvailabilityResponse);
    rpc ReserveStock (ReservationRequest) returns (ReservationResponse);
}

message AvailabilityRequest {
    repeated ItemRequest items = 1;
}

message AvailabilityResponse {
    bool available = 1;
    string message = 2;
}
*/

public class OrderServiceWithGrpc
{
    private readonly InventoryService.InventoryServiceClient _inventoryClient;
    
    public async Task<int> PlaceOrderAsync(Order order)
    {
        var request = new AvailabilityRequest();
        request.Items.AddRange(order.Items.Select(i => new ItemRequest
        {
            BookId = i.BookId,
            Quantity = i.Quantity
        }));
        
        var response = await _inventoryClient.CheckAvailabilityAsync(request);
        
        if (!response.Available)
            throw new OutOfStockException(response.Message);
        
        return order.Id;
    }
}

// Advantages:
// - Faster than REST (binary, HTTP/2)
// - Strongly typed
// - Built-in code generation
// - Better for performance-critical paths
```

### 2.2 Asynchronous: Event-Driven Communication

```csharp
// Order Service publishes event
public class OrderService
{
    private readonly IEventPublisher _eventPublisher;
    
    public async Task<int> PlaceOrderAsync(Order order)
    {
        var newOrder = Order.Create(customerId, items);
        await _repository.SaveAsync(newOrder);
        
        // Publish event - don't wait for subscribers
        await _eventPublisher.PublishAsync(new OrderPlacedEvent
        {
            OrderId = newOrder.Id,
            CustomerId = customerId,
            Items = items,
            Total = newOrder.Total.Amount
        });
        
        return newOrder.Id;  // Return immediately
    }
}

// Inventory Service listens for events
public class InventoryEventHandler : IEventHandler<OrderPlacedEvent>
{
    private readonly IInventoryService _inventory;
    
    public async Task HandleAsync(OrderPlacedEvent @event)
    {
        // Process asynchronously
        try
        {
            await _inventory.ReserveAsync(@event.OrderId, @event.Items);
        }
        catch (InsufficientStockException ex)
        {
            // Publish event about failure
            await _eventPublisher.PublishAsync(new OrderRejectedEvent
            {
                OrderId = @event.OrderId,
                Reason = "Insufficient inventory"
            });
        }
    }
}

// Advantages:
// - Loose coupling: Services don't call each other
// - Resilience: If subscriber down, order still placed
// - Scalability: Multiple subscribers possible
// - Eventual consistency: Subscribers catch up

// Disadvantages:
// - Complexity: Event flow harder to track
// - Consistency: Temporary inconsistency
// - Requires event broker (message queue)
// - Harder to debug
```

---

## 3. Data Management Patterns

### 3.1 Database Per Service

```csharp
// Each service owns its database
// No shared database between services

namespace Bookstore.OrderService
{
    // Order Service owns Orders, OrderLines tables
    public class OrderDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderLine> OrderLines { get; set; }
        
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlServer("Server=order-db;Database=Orders");
        }
    }
}

namespace Bookstore.InventoryService
{
    // Inventory Service owns Products, Stock tables
    public class InventoryDbContext : DbContext
    {
        public DbSet<Product> Products { get; set; }
        public DbSet<Stock> Stock { get; set; }
        
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlServer("Server=inventory-db;Database=Inventory");
        }
    }
}

// Advantage: Independent scaling, technology choice
// Disadvantage: Distributed transactions, data duplication
```

### 3.2 Saga Pattern for Distributed Transactions

```csharp
// Orchestrate multi-step business transaction across services

public interface ISaga
{
    Task ExecuteAsync(SagaContext context);
    Task CompensateAsync(SagaContext context);  // Rollback
}

// Order Saga: Coordinates placing order across multiple services
public class PlaceOrderSaga : ISaga
{
    private readonly IOrderService _orderService;
    private readonly IInventoryService _inventoryService;
    private readonly IPaymentService _paymentService;
    
    public async Task ExecuteAsync(SagaContext context)
    {
        try
        {
            // Step 1: Create order (can fail if invalid)
            var orderId = await _orderService.CreateOrderAsync(context.CustomerId, context.Items);
            context.OrderId = orderId;
            
            // Step 2: Reserve inventory (can fail if out of stock)
            await _inventoryService.ReserveAsync(orderId, context.Items);
            
            // Step 3: Process payment (can fail if payment declined)
            await _paymentService.ProcessAsync(orderId, context.PaymentInfo);
            
            // Step 4: Confirm order
            await _orderService.ConfirmOrderAsync(orderId);
        }
        catch (Exception ex)
        {
            // Rollback on any failure
            await CompensateAsync(context);
            throw;
        }
    }
    
    public async Task CompensateAsync(SagaContext context)
    {
        // Undo steps in reverse order
        
        if (context.OrderId > 0)
        {
            // Refund payment if processed
            if (context.PaymentProcessed)
                await _paymentService.RefundAsync(context.OrderId);
            
            // Release reserved inventory
            if (context.InventoryReserved)
                await _inventoryService.ReleaseAsync(context.OrderId);
            
            // Cancel order
            await _orderService.CancelOrderAsync(context.OrderId);
        }
    }
}

// Saga Orchestrator coordinates execution
public class OrderSagaOrchestrator
{
    private readonly PlaceOrderSaga _saga;
    
    public async Task<int> PlaceOrderAsync(int customerId, List<OrderItem> items)
    {
        var context = new SagaContext
        {
            CustomerId = customerId,
            Items = items
        };
        
        await _saga.ExecuteAsync(context);
        return context.OrderId;
    }
}

// Or Choreography: Services trigger each other via events
public class OrderPlacedEventHandler : IEventHandler<OrderPlacedEvent>
{
    public async Task HandleAsync(OrderPlacedEvent @event)
    {
        // Inventory service handles event, publishes another event
        await _inventoryService.ReserveAsync(@event.OrderId, @event.Items);
        
        // Event triggers payment service
        // Which triggers notification service, etc.
    }
}
```

### 3.3 Event Sourcing in Microservices

```csharp
// Store events as source of truth

public class OrderEventStore
{
    private readonly IEventStoreRepository _repository;
    
    public async Task AppendEventAsync(int orderId, DomainEvent @event)
    {
        // Store immutable event
        await _repository.AppendAsync(new StoredEvent
        {
            AggregateId = orderId,
            EventType = @event.GetType().Name,
            EventData = JsonConvert.SerializeObject(@event),
            Timestamp = DateTime.UtcNow,
            Version = await GetNextVersionAsync(orderId)
        });
    }
    
    public async Task<List<DomainEvent>> GetEventsAsync(int orderId)
    {
        var events = await _repository.GetAsync(orderId);
        
        return events
            .OrderBy(e => e.Version)
            .Select(e => JsonConvert.DeserializeObject(
                e.EventData,
                Type.GetType(e.EventType)
            ) as DomainEvent)
            .ToList();
    }
}

// Benefits for microservices:
// - Complete audit trail
// - Reconstruct service state
// - Synchronize services via event streaming
```

---

## 4. Service Decomposition Strategies

### 4.1 Strangler Fig Pattern

Gradually migrate from monolith to microservices:

```csharp
// Phase 1: API Gateway routes new requests to new service
public class OrderServiceGateway
{
    private readonly HttpClient _legacyMonolith;
    private readonly HttpClient _newOrderService;
    
    [HttpGet("/api/orders/{id}")]
    public async Task<IActionResult> GetOrder(int id)
    {
        // Route to new service if available
        if (id > 10000)  // New orders
        {
            var response = await _newOrderService.GetAsync($"/api/orders/{id}");
            return Ok(response);
        }
        else  // Old orders still in monolith
        {
            var response = await _legacyMonolith.GetAsync($"/api/orders/{id}");
            return Ok(response);
        }
    }
}

// Phase 2: Gradually shift more requests to new service
// Phase 3: Once all data migrated, decommission old monolith
```

### 4.2 Domain-Driven Design for Services

Use DDD bounded contexts to identify service boundaries:

```csharp
namespace Bookstore.OrderManagement
{
    // Bounded Context: Everything related to orders
    public class Order : AggregateRoot { }
    public class OrderLine { }
    public interface IOrderRepository { }
    
    // This entire context becomes one microservice
}

namespace Bookstore.Inventory
{
    // Bounded Context: Stock management
    public class Product : AggregateRoot { }
    public class Stock { }
    public interface IProductRepository { }
    
    // This context becomes another microservice
}

// Services communicate through well-defined APIs
// Shared concepts (Product) have different meaning in each context
// - Order context: Product has price, description
// - Inventory context: Product has SKU, warehouse location
```

---

## 5. Resilience in Microservices

### 5.1 Service Discovery

```csharp
// Services need to locate each other dynamically

public interface IServiceRegistry
{
    Task RegisterAsync(ServiceInstance instance);
    Task<ServiceInstance> DiscoverAsync(string serviceName);
    Task DeregisterAsync(string serviceId);
}

// Kubernetes-based discovery
public class KubernetesServiceRegistry : IServiceRegistry
{
    // Kubernetes automatically handles service discovery
    // Service name: http://inventory-service (within cluster)
    
    public async Task<ServiceInstance> DiscoverAsync(string serviceName)
    {
        // Kubernetes DNS: servicename.namespace.svc.cluster.local
        return new ServiceInstance
        {
            Name = serviceName,
            Host = $"{serviceName}.default.svc.cluster.local",
            Port = 80
        };
    }
}

// Or Consul-based discovery
public class ConsulServiceRegistry : IServiceRegistry
{
    private readonly IConsulClient _consulClient;
    
    public async Task RegisterAsync(ServiceInstance instance)
    {
        await _consulClient.Agent.ServiceRegister(new AgentServiceRegistration
        {
            ID = instance.Id,
            Name = instance.Name,
            Address = instance.Host,
            Port = instance.Port,
            Check = new AgentServiceCheck
            {
                HTTP = $"http://{instance.Host}:{instance.Port}/health",
                Interval = TimeSpan.FromSeconds(10)
            }
        });
    }
    
    public async Task<ServiceInstance> DiscoverAsync(string serviceName)
    {
        var services = await _consulClient.Catalog.Service(serviceName);
        return services.Response.FirstOrDefault();
    }
}
```

### 5.2 Circuit Breaker Pattern

```csharp
// Prevent cascade failures by failing fast

public class CircuitBreakerHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly IAsyncPolicy<HttpResponseMessage> _policy;
    
    public CircuitBreakerHttpClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        
        // Define circuit breaker policy
        _policy = Policy
            .Handle<HttpRequestException>()
            .Or(r => !r.IsSuccessStatusCode)
            .OrResult<HttpResponseMessage>(r =>
                (int)r.StatusCode >= 500)
            .CircuitBreakerAsync<HttpResponseMessage>(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, duration) =>
                {
                    Console.WriteLine($"Circuit broken for {duration.TotalSeconds}s");
                }
            );
    }
    
    public async Task<HttpResponseMessage> GetAsync(string url)
    {
        return await _policy.ExecuteAsync(() =>
            _httpClient.GetAsync(url)
        );
    }
}

// States:
// 1. Closed: Normal operation, requests pass through
// 2. Open: Too many failures, requests fail immediately
// 3. Half-Open: Testing if service recovered, selective requests pass
```

---

## 6. Service Communication Anti-Patterns

### 6.1 Chatty Interfaces

```csharp
// ❌ BAD: Multiple round trips
public class OrderService
{
    public async Task<int> PlaceOrderAsync(Order order)
    {
        // Call 1: Get customer
        var customer = await _customerService.GetCustomerAsync(order.CustomerId);
        
        // Call 2: Get customer address
        var address = await _customerService.GetAddressAsync(order.CustomerId);
        
        // Call 3: Get customer preferences
        var prefs = await _customerService.GetPreferencesAsync(order.CustomerId);
        
        // Call 4: Check inventory
        var stock = await _inventoryService.CheckStockAsync(order.Items);
        
        // Many calls to different services = slow and fragile
    }
}

// ✅ GOOD: Batch calls or coarse-grained interfaces
public class OrderService
{
    public async Task<int> PlaceOrderAsync(Order order)
    {
        // Single call gets everything needed
        var customerData = await _customerService.GetFullCustomerDataAsync(order.CustomerId);
        
        // Check all inventory at once
        var stock = await _inventoryService.CheckStockAsync(order.Items);
        
        // Fewer calls = faster, more resilient
    }
}
```

### 6.2 Service Coupling

```csharp
// ❌ BAD: Tight coupling through shared database
public class OrderService
{
    public void PlaceOrder(Order order)
    {
        using (var connection = new SqlConnection("shared-db"))
        {
            // Order service writes to Orders table
            InsertOrder(connection, order);
            
            // Order service writes directly to Inventory table
            UpdateInventory(connection, order.Items);
        }
    }
}

// Problems:
// - Services are tightly coupled
// - Can't independently deploy
// - Schema changes break everything
// - Hard to test in isolation

// ✅ GOOD: Event-driven loose coupling
public class OrderService
{
    public async Task<int> PlaceOrderAsync(Order order)
    {
        await _repository.SaveAsync(order);
        
        // Publish event - Inventory service responds
        await _eventPublisher.PublishAsync(new OrderPlacedEvent { ... });
        
        // Services are decoupled
        // Can independently deploy
        // Easy to test
    }
}
```

---

## 7. Deployment Patterns

### 7.1 Service Per Container

```dockerfile
# Dockerfile for Order Service
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

WORKDIR /app
COPY bin/Release/net8.0 .

EXPOSE 80
ENTRYPOINT ["dotnet", "Bookstore.OrderService.dll"]
```

```yaml
# Kubernetes deployment
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
spec:
  replicas: 3
  selector:
    matchLabels:
      app: order-service
  template:
    metadata:
      labels:
        app: order-service
    spec:
      containers:
      - name: order-service
        image: bookstore/order-service:1.0.0
        ports:
        - containerPort: 80
        livenessProbe:
          httpGet:
            path: /health
            port: 80
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /ready
            port: 80
          initialDelaySeconds: 5
          periodSeconds: 5
```

### 7.2 Database Migrations

```csharp
// Each service manages its own database migrations

public class OrderServiceStartup
{
    public async Task MigrateAsync(IServiceProvider serviceProvider)
    {
        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            
            // Only Order service applies Order migrations
            await db.Database.MigrateAsync();
        }
    }
}

// Separate migration jobs per service
// Coordinated but independent
// Easy rollback per service
```

---

## Summary

Microservices enable:
- Independent scaling and deployment
- Technology diversity
- Team autonomy
- Isolated failure domains

But require:
- Service discovery
- Inter-service communication
- Distributed transactions
- Data consistency management
- Operational complexity

Key patterns:
- Database per service
- Event-driven communication
- Circuit breakers for resilience
- API gateways for routing
- Service mesh for observability

Microservices are not always the answer—consider carefully before adopting.

Next topic covers Caching Strategies for performance optimization.
