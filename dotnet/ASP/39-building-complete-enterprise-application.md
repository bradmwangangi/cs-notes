# 38. Building a Complete Enterprise Application

## Overview
This topic demonstrates synthesizing all previous knowledge into a production-grade enterprise application. We'll build an Order Management System covering architecture, implementation, testing, and deployment—demonstrating best practices from earlier topics in an integrated whole.

---

## 1. Requirements and Architecture

### 1.1 System Requirements

```
ORDER MANAGEMENT SYSTEM

Functional Requirements:
├─ Create and manage orders
├─ Track inventory availability
├─ Process payments
├─ Handle shipping
├─ Send notifications
├─ Provide analytics/reporting
└─ Support customer self-service

Non-Functional Requirements:
├─ Availability: 99.95% uptime
├─ Latency: P95 < 200ms, P99 < 500ms
├─ Scalability: Handle 1000+ orders/minute
├─ Reliability: < 0.1% error rate
├─ Security: PCI-DSS compliant
├─ Compliance: GDPR, audit trails
└─ Cost: < $5K/month infrastructure
```

### 1.2 Domain Model

```csharp
// Domain layer: Core business logic

namespace OrderManagement.Domain
{
    // Aggregate Root: Order
    public class Order : AggregateRoot
    {
        public int Id { get; private set; }
        public int CustomerId { get; private set; }
        public OrderStatus Status { get; private set; }
        public List<OrderLine> Items { get; private set; }
        public Money Total { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? ShippedAt { get; private set; }
        
        // Domain events
        public List<DomainEvent> DomainEvents { get; private set; }
        
        public static Order Create(int customerId, List<OrderLine> items, Money total)
        {
            var order = new Order
            {
                CustomerId = customerId,
                Items = items,
                Total = total,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                DomainEvents = new()
            };
            
            // Publish event
            order.DomainEvents.Add(new OrderCreatedEvent(order.Id, customerId, items));
            
            return order;
        }
        
        public void Confirm()
        {
            if (Status != OrderStatus.Pending)
                throw new InvalidOperationException("Can only confirm pending orders");
            
            Status = OrderStatus.Confirmed;
            DomainEvents.Add(new OrderConfirmedEvent(Id));
        }
        
        public void Ship(string trackingNumber)
        {
            if (Status != OrderStatus.Confirmed)
                throw new InvalidOperationException("Can only ship confirmed orders");
            
            Status = OrderStatus.Shipped;
            ShippedAt = DateTime.UtcNow;
            DomainEvents.Add(new OrderShippedEvent(Id, trackingNumber));
        }
        
        public void Cancel()
        {
            if (Status == OrderStatus.Shipped || Status == OrderStatus.Delivered)
                throw new InvalidOperationException("Cannot cancel shipped/delivered orders");
            
            Status = OrderStatus.Cancelled;
            DomainEvents.Add(new OrderCancelledEvent(Id));
        }
    }
    
    // Value Object: OrderLine
    public class OrderLine
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public Money UnitPrice { get; set; }
        public Money Total => UnitPrice.Amount * Quantity;
        
        public OrderLine(int productId, int quantity, Money unitPrice)
        {
            if (quantity <= 0) throw new ArgumentException("Quantity must be positive");
            if (unitPrice.Amount <= 0) throw new ArgumentException("Price must be positive");
            
            ProductId = productId;
            Quantity = quantity;
            UnitPrice = unitPrice;
        }
    }
    
    // Value Object: Money
    public class Money : IEquatable<Money>
    {
        public decimal Amount { get; }
        public string Currency { get; }
        
        public Money(decimal amount, string currency = "USD")
        {
            if (amount < 0) throw new ArgumentException("Amount cannot be negative");
            
            Amount = amount;
            Currency = currency;
        }
        
        public static Money operator +(Money a, Money b)
        {
            if (a.Currency != b.Currency)
                throw new InvalidOperationException("Cannot add different currencies");
            
            return new Money(a.Amount + b.Amount, a.Currency);
        }
        
        public bool Equals(Money other) => other != null && Amount == other.Amount && Currency == other.Currency;
        public override bool Equals(object obj) => Equals(obj as Money);
        public override int GetHashCode() => HashCode.Combine(Amount, Currency);
    }
    
    // Bounded contexts
    public enum OrderStatus
    {
        Pending,
        Confirmed,
        Processing,
        Shipped,
        Delivered,
        Cancelled
    }
    
    // Domain events
    public abstract class DomainEvent
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
    
    public class OrderCreatedEvent : DomainEvent
    {
        public int OrderId { get; }
        public int CustomerId { get; }
        public List<OrderLine> Items { get; }
        
        public OrderCreatedEvent(int orderId, int customerId, List<OrderLine> items)
        {
            OrderId = orderId;
            CustomerId = customerId;
            Items = items;
        }
    }
}
```

---

## 2. Application Layer

### 2.1 Use Cases and Commands

```csharp
// Application layer: Use case orchestration

namespace OrderManagement.Application
{
    // Commands
    public class CreateOrderCommand
    {
        public int CustomerId { get; set; }
        public List<OrderItemDto> Items { get; set; }
    }
    
    public class ConfirmOrderCommand
    {
        public int OrderId { get; set; }
        public int CustomerId { get; set; }  // For validation
    }
    
    public class ShipOrderCommand
    {
        public int OrderId { get; set; }
        public string TrackingNumber { get; set; }
    }
    
    // Command handlers
    public interface ICommandHandler<in TCommand, TResult>
    {
        Task<TResult> HandleAsync(TCommand command);
    }
    
    public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, int>
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IInventoryService _inventoryService;
        private readonly IPaymentService _paymentService;
        private readonly IMessagePublisher _eventPublisher;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<CreateOrderCommandHandler> _logger;
        
        public async Task<int> HandleAsync(CreateOrderCommand command)
        {
            try
            {
                // Step 1: Validate customer
                var customer = await ValidateCustomerAsync(command.CustomerId);
                if (customer == null)
                    throw new InvalidOperationException("Invalid customer");
                
                // Step 2: Check inventory
                var items = command.Items.Select(i => new OrderLine(
                    i.ProductId,
                    i.Quantity,
                    new Money(i.Price)
                )).ToList();
                
                var available = await _inventoryService.CheckAvailabilityAsync(items);
                if (!available)
                    throw new InsufficientStockException("Some items out of stock");
                
                // Step 3: Create order (domain logic)
                var total = items.Aggregate(
                    new Money(0),
                    (sum, item) => sum + new Money(item.Total)
                );
                
                var order = Order.Create(command.CustomerId, items, total);
                
                // Step 4: Reserve inventory
                await _inventoryService.ReserveAsync(order.Id, items);
                
                // Step 5: Process payment
                var paymentResult = await _paymentService.ProcessAsync(
                    command.CustomerId,
                    total
                );
                
                if (!paymentResult.Success)
                    throw new PaymentException(paymentResult.ErrorMessage);
                
                // Step 6: Persist order
                await _orderRepository.SaveAsync(order);
                
                // Step 7: Publish domain events
                foreach (var @event in order.DomainEvents)
                {
                    await _eventPublisher.PublishAsync(@event);
                }
                
                await _unitOfWork.CommitAsync();
                
                _logger.LogInformation(
                    "Order created: OrderId={OrderId}, CustomerId={CustomerId}, Total={Total}",
                    order.Id,
                    command.CustomerId,
                    total.Amount
                );
                
                return order.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create order");
                throw;
            }
        }
    }
    
    // Queries
    public class GetOrderQuery
    {
        public int OrderId { get; set; }
    }
    
    public class GetOrdersQuery
    {
        public int CustomerId { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
    
    // Query handlers
    public interface IQueryHandler<in TQuery, TResult>
    {
        Task<TResult> HandleAsync(TQuery query);
    }
    
    public class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderDto>
    {
        private readonly IOrderRepository _repository;
        
        public async Task<OrderDto> HandleAsync(GetOrderQuery query)
        {
            var order = await _repository.GetByIdAsync(query.OrderId);
            
            if (order == null)
                throw new OrderNotFoundException(query.OrderId);
            
            return OrderDto.FromDomain(order);
        }
    }
}
```

### 2.2 Service Layer

```csharp
// Application services: Coordinate domain and infrastructure

namespace OrderManagement.Application.Services
{
    public interface IOrderService
    {
        Task<int> CreateOrderAsync(CreateOrderCommand command);
        Task<OrderDto> GetOrderAsync(int orderId);
        Task<PagedResult<OrderDto>> GetCustomerOrdersAsync(int customerId, int page, int pageSize);
        Task ConfirmOrderAsync(int orderId);
        Task CancelOrderAsync(int orderId);
    }
    
    public class OrderService : IOrderService
    {
        private readonly IMediator _mediator;
        private readonly ICache _cache;
        
        public async Task<int> CreateOrderAsync(CreateOrderCommand command)
        {
            // Use CQRS pattern via mediator
            return await _mediator.Send(new CreateOrderCommand { ... });
        }
        
        public async Task<OrderDto> GetOrderAsync(int orderId)
        {
            var cacheKey = $"order-{orderId}";
            
            // Try cache first
            if (await _cache.TryGetAsync<OrderDto>(cacheKey, out var cached))
            {
                return cached;
            }
            
            // Query from database
            var order = await _mediator.Send(new GetOrderQuery { OrderId = orderId });
            
            // Cache result
            await _cache.SetAsync(cacheKey, order, TimeSpan.FromHours(1));
            
            return order;
        }
        
        public async Task<PagedResult<OrderDto>> GetCustomerOrdersAsync(
            int customerId,
            int page,
            int pageSize)
        {
            // Complex query with sorting and filtering
            return await _mediator.Send(new GetOrdersQuery
            {
                CustomerId = customerId,
                PageNumber = page,
                PageSize = pageSize
            });
        }
        
        public async Task ConfirmOrderAsync(int orderId)
        {
            await _mediator.Send(new ConfirmOrderCommand { OrderId = orderId });
            
            // Invalidate cache
            await _cache.RemoveAsync($"order-{orderId}");
        }
        
        public async Task CancelOrderAsync(int orderId)
        {
            await _mediator.Send(new CancelOrderCommand { OrderId = orderId });
            
            // Cascade invalidation
            await _cache.RemoveByPatternAsync($"order-*");
        }
    }
}
```

---

## 3. Infrastructure Layer

### 3.1 Data Access

```csharp
// Infrastructure layer: Technical implementations

namespace OrderManagement.Infrastructure.Persistence
{
    // Repository
    public interface IOrderRepository
    {
        Task<Order> GetByIdAsync(int id);
        Task<List<Order>> GetByCustomerIdAsync(int customerId);
        Task SaveAsync(Order order);
        Task UpdateAsync(Order order);
        Task DeleteAsync(int id);
    }
    
    public class OrderRepository : IOrderRepository
    {
        private readonly OrderDbContext _context;
        
        public async Task<Order> GetByIdAsync(int id)
        {
            return await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);
        }
        
        public async Task<List<Order>> GetByCustomerIdAsync(int customerId)
        {
            return await _context.Orders
                .Where(o => o.CustomerId == customerId)
                .Include(o => o.Items)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }
        
        public async Task SaveAsync(Order order)
        {
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();
        }
        
        public async Task UpdateAsync(Order order)
        {
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();
        }
        
        public async Task DeleteAsync(int id)
        {
            var order = await GetByIdAsync(id);
            if (order != null)
            {
                _context.Orders.Remove(order);
                await _context.SaveChangesAsync();
            }
        }
    }
    
    // Unit of Work
    public interface IUnitOfWork
    {
        Task CommitAsync();
        Task RollbackAsync();
    }
    
    public class UnitOfWork : IUnitOfWork
    {
        private readonly OrderDbContext _context;
        
        public async Task CommitAsync()
        {
            await _context.SaveChangesAsync();
        }
        
        public async Task RollbackAsync()
        {
            _context.ChangeTracker.Entries().ToList().ForEach(x => x.Reload());
        }
    }
}
```

### 3.2 External Services

```csharp
// External service integrations

namespace OrderManagement.Infrastructure.Services
{
    public interface IInventoryService
    {
        Task<bool> CheckAvailabilityAsync(List<OrderLine> items);
        Task ReserveAsync(int orderId, List<OrderLine> items);
        Task ReleaseAsync(int orderId);
    }
    
    public class InventoryServiceClient : IInventoryService
    {
        private readonly HttpClient _httpClient;
        private readonly IAsyncPolicy<HttpResponseMessage> _policy;
        
        public async Task<bool> CheckAvailabilityAsync(List<OrderLine> items)
        {
            var request = new { items = items.Select(i => new { i.ProductId, i.Quantity }) };
            
            var response = await _policy.ExecuteAsync(async () =>
                await _httpClient.PostAsJsonAsync("https://inventory-service/check", request)
            );
            
            return response.IsSuccessStatusCode;
        }
        
        public async Task ReserveAsync(int orderId, List<OrderLine> items)
        {
            var request = new { orderId, items };
            
            var response = await _policy.ExecuteAsync(async () =>
                await _httpClient.PostAsJsonAsync("https://inventory-service/reserve", request)
            );
            
            response.EnsureSuccessStatusCode();
        }
        
        public async Task ReleaseAsync(int orderId)
        {
            var response = await _policy.ExecuteAsync(async () =>
                await _httpClient.PostAsJsonAsync(
                    $"https://inventory-service/release/{orderId}",
                    null
                )
            );
            
            response.EnsureSuccessStatusCode();
        }
    }
    
    public interface IPaymentService
    {
        Task<PaymentResult> ProcessAsync(int customerId, Money amount);
        Task<bool> RefundAsync(int orderId);
    }
    
    public class PaymentServiceGateway : IPaymentService
    {
        private readonly IPaymentGateway _paymentGateway;
        private readonly ILogger<PaymentServiceGateway> _logger;
        
        public async Task<PaymentResult> ProcessAsync(int customerId, Money amount)
        {
            try
            {
                var result = await _paymentGateway.ChargeAsync(
                    customerId,
                    amount.Amount,
                    amount.Currency
                );
                
                return result;
            }
            catch (PaymentException ex)
            {
                _logger.LogError(ex, "Payment failed for customer {CustomerId}", customerId);
                
                return new PaymentResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        public async Task<bool> RefundAsync(int orderId)
        {
            return await _paymentGateway.RefundAsync(orderId);
        }
    }
}
```

---

## 4. API Layer

### 4.1 REST Endpoints

```csharp
// API layer: HTTP endpoints

namespace OrderManagement.API.Controllers
{
    [ApiController]
    [Route("api/orders")]
    [Authorize]
    public class OrdersController : ControllerBase
    {
        private readonly IOrderService _orderService;
        private readonly ILogger<OrdersController> _logger;
        
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<int>> CreateOrder([FromBody] CreateOrderRequest request)
        {
            try
            {
                var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                
                var command = new CreateOrderCommand
                {
                    CustomerId = int.Parse(customerId),
                    Items = request.Items
                };
                
                var orderId = await _orderService.CreateOrderAsync(command);
                
                return CreatedAtAction(nameof(GetOrder), new { id = orderId }, orderId);
            }
            catch (InsufficientStockException ex)
            {
                _logger.LogWarning(ex, "Stock unavailable");
                
                return BadRequest(new ProblemDetails
                {
                    Title = "Out of Stock",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
            catch (PaymentException ex)
            {
                _logger.LogWarning(ex, "Payment failed");
                
                return BadRequest(new ProblemDetails
                {
                    Title = "Payment Failed",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }
        
        [HttpGet("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<OrderDto>> GetOrder(int id)
        {
            try
            {
                var order = await _orderService.GetOrderAsync(id);
                return Ok(order);
            }
            catch (OrderNotFoundException ex)
            {
                _logger.LogInformation(ex, "Order not found: {OrderId}", id);
                
                return NotFound(new ProblemDetails
                {
                    Title = "Order Not Found",
                    Status = StatusCodes.Status404NotFound
                });
            }
        }
        
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<PagedResult<OrderDto>>> GetOrders(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var customerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            var result = await _orderService.GetCustomerOrdersAsync(
                int.Parse(customerId),
                page,
                pageSize
            );
            
            return Ok(result);
        }
        
        [HttpPost("{id}/confirm")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ConfirmOrder(int id)
        {
            try
            {
                await _orderService.ConfirmOrderAsync(id);
                return Ok();
            }
            catch (OrderNotFoundException)
            {
                return NotFound();
            }
        }
        
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CancelOrder(int id)
        {
            try
            {
                await _orderService.CancelOrderAsync(id);
                return NoContent();
            }
            catch (OrderNotFoundException)
            {
                return NotFound();
            }
        }
    }
}
```

### 4.2 Startup Configuration

```csharp
// Program.cs: DI and pipeline configuration

public class Startup
{
    private readonly IConfiguration _configuration;
    
    public void ConfigureServices(IServiceCollection services)
    {
        // Application configuration
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ICommandHandler<CreateOrderCommand, int>, CreateOrderCommandHandler>();
        services.AddScoped<IQueryHandler<GetOrderQuery, OrderDto>, GetOrderQueryHandler>();
        
        // Infrastructure
        services.AddDbContext<OrderDbContext>(options =>
            options.UseSqlServer(_configuration.GetConnectionString("DefaultConnection"))
        );
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        
        // External services with resilience
        services.AddHttpClient<IInventoryService, InventoryServiceClient>()
            .AddTransientHttpErrorPolicy(p =>
                p.WaitAndRetryAsync(3, _ => TimeSpan.FromSeconds(1))
            )
            .AddTransientHttpErrorPolicy(p =>
                p.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30))
            );
        
        services.AddScoped<IPaymentService, PaymentServiceGateway>();
        
        // Messaging
        services.AddScoped<IMessagePublisher, ServiceBusPublisher>();
        
        // Caching
        services.AddMemoryCache();
        services.AddStackExchangeRedisCache(options =>
            options.Configuration = _configuration.GetConnectionString("Redis")
        );
        
        // Logging
        services.AddLogging(config =>
            config.AddApplicationInsights()
        );
        
        // Authentication
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = _configuration["Auth:Authority"];
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = true,
                    ValidAudience = _configuration["Auth:Audience"]
                };
            });
        
        // API
        services.AddControllers();
        services.AddOpenApi();
    }
    
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapOpenApi();
        });
    }
}
```

---

## 5. Testing Strategy

### 5.1 Unit Tests

```csharp
// Domain-driven unit tests

public class OrderTests
{
    [Fact]
    public void CreateOrder_ValidInput_CreatesSuccessfully()
    {
        // Arrange
        var customerId = 123;
        var items = new List<OrderLine>
        {
            new(productId: 1, quantity: 2, unitPrice: new Money(50))
        };
        var total = new Money(100);
        
        // Act
        var order = Order.Create(customerId, items, total);
        
        // Assert
        Assert.NotNull(order);
        Assert.Equal(customerId, order.CustomerId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Single(order.DomainEvents);
        Assert.IsType<OrderCreatedEvent>(order.DomainEvents[0]);
    }
    
    [Fact]
    public void Confirm_PendingOrder_TransitionsToConfirmed()
    {
        // Arrange
        var order = Order.Create(123, new(), new Money(0));
        
        // Act
        order.Confirm();
        
        // Assert
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }
    
    [Fact]
    public void Cancel_ShippedOrder_ThrowsException()
    {
        // Arrange
        var order = Order.Create(123, new(), new Money(0));
        order.Confirm();
        order.Ship("TRACK123");
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => order.Cancel());
    }
}
```

### 5.2 Integration Tests

```csharp
// Integration tests with database

public class OrderServiceIntegrationTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Startup> _factory;
    private HttpClient _httpClient;
    
    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Startup>();
        _httpClient = _factory.CreateClient();
    }
    
    public async Task DisposeAsync()
    {
        _factory.Dispose();
    }
    
    [Fact]
    public async Task CreateOrder_ValidRequest_ReturnsCreatedOrder()
    {
        // Arrange
        var request = new CreateOrderRequest
        {
            Items = new()
            {
                new() { ProductId = 1, Quantity = 1, Price = 99.99m }
            }
        };
        
        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/orders", request);
        
        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        
        var orderId = await response.Content.ReadAsAsync<int>();
        orderId.Should().BeGreaterThan(0);
    }
    
    [Fact]
    public async Task GetOrder_ExistingOrder_ReturnsOrder()
    {
        // Arrange
        var request = new CreateOrderRequest { Items = new() };
        var createResponse = await _httpClient.PostAsJsonAsync("/api/orders", request);
        var orderId = await createResponse.Content.ReadAsAsync<int>();
        
        // Act
        var getResponse = await _httpClient.GetAsync($"/api/orders/{orderId}");
        
        // Assert
        getResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        
        var order = await getResponse.Content.ReadAsAsync<OrderDto>();
        order.Id.Should().Be(orderId);
    }
}
```

---

## 6. Deployment

### 6.1 Docker Containerization

```dockerfile
# Multi-stage build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

WORKDIR /src

COPY ["OrderManagement.API/OrderManagement.API.csproj", "OrderManagement.API/"]
COPY ["OrderManagement.Application/OrderManagement.Application.csproj", "OrderManagement.Application/"]
COPY ["OrderManagement.Infrastructure/OrderManagement.Infrastructure.csproj", "OrderManagement.Infrastructure/"]
COPY ["OrderManagement.Domain/OrderManagement.Domain.csproj", "OrderManagement.Domain/"]

RUN dotnet restore "OrderManagement.API/OrderManagement.API.csproj"

COPY . .

RUN dotnet build -c Release -o /app/build

FROM builder AS publish
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 80

HEALTHCHECK --interval=30s --timeout=3s --start-period=40s --retries=3 \
  CMD dotnet /app/HealthCheck.dll || exit 1

ENTRYPOINT ["dotnet", "OrderManagement.API.dll"]
```

### 6.2 Kubernetes Deployment

```yaml
# Kubernetes manifests

apiVersion: v1
kind: Namespace
metadata:
  name: order-service

---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
  namespace: order-service
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
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: Production
        - name: ConnectionStrings__DefaultConnection
          valueFrom:
            secretKeyRef:
              name: order-service-secrets
              key: connection-string
        livenessProbe:
          httpGet:
            path: /health/live
            port: 80
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 80
          initialDelaySeconds: 5
          periodSeconds: 5
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"

---
apiVersion: v1
kind: Service
metadata:
  name: order-service
  namespace: order-service
spec:
  type: ClusterIP
  selector:
    app: order-service
  ports:
  - port: 80
    targetPort: 80

---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: order-service-hpa
  namespace: order-service
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: order-service
  minReplicas: 3
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
```

---

## Summary

A complete enterprise application requires:

**Architecture:**
- Domain-driven design with clear boundaries
- Clean separation of concerns (Domain → Application → Infrastructure → API)
- Event-driven communication for loose coupling
- CQRS for complex queries

**Implementation:**
- Aggregate roots for consistency
- Domain events for integration
- Repository pattern for data access
- Service layer for orchestration
- REST API following standards

**Testing:**
- Unit tests for domain logic
- Integration tests for API contracts
- Load tests for performance validation

**Deployment:**
- Docker containerization
- Kubernetes orchestration
- Multi-environment promotion (dev → staging → prod)
- Automatic scaling based on metrics

**Production Readiness:**
- Comprehensive logging and monitoring
- Health checks and graceful degradation
- Resilience patterns (retry, circuit breaker)
- Security (authentication, authorization, encryption)
- Cost optimization

This integration demonstrates all previous topics working together for an enterprise-grade system.

Next topics cover Enterprise Patterns and Team Management.
