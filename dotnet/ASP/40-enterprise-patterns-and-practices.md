# 39. Enterprise Patterns & Practices

## Overview
Enterprise patterns solve recurring problems in large systems. These patterns enhance maintainability, scalability, and reliability. Understanding when and how to apply them is critical for building production systems that scale with teams and feature complexity.

---

## 1. Specification Pattern

### 1.1 Specification for Queries

```csharp
// Domain logic for filtering moved out of queries

namespace OrderManagement.Domain.Specifications
{
    // Base specification
    public abstract class Specification<T>
    {
        public Expression<Func<T, bool>> Criteria { get; protected set; }
        public List<Expression<Func<T, object>>> Includes { get; } = new();
        public List<string> IncludeStrings { get; } = new();
        
        public int Take { get; private set; }
        public int Skip { get; private set; }
        public bool IsPagingEnabled { get; private set; }
    }
    
    // Specific specifications
    public class PendingOrdersSpecification : Specification<Order>
    {
        public PendingOrdersSpecification()
        {
            Criteria = o => o.Status == OrderStatus.Pending;
            Includes.Add(o => o.Items);
            Includes.Add(o => o.Customer);
        }
    }
    
    public class OrdersByCustomerSpecification : Specification<Order>
    {
        public OrdersByCustomerSpecification(int customerId)
        {
            Criteria = o => o.CustomerId == customerId;
            Includes.Add(o => o.Items);
            
            AddPaging(20, 0);  // 20 items per page
        }
        
        protected void AddPaging(int take, int skip)
        {
            Take = take;
            Skip = skip;
            IsPagingEnabled = true;
        }
    }
    
    public class RecentOrdersSpecification : Specification<Order>
    {
        public RecentOrdersSpecification(int days = 30)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-days);
            
            Criteria = o => o.CreatedAt >= cutoffDate;
            Includes.Add(o => o.Items);
        }
    }
}

// Generic repository using specifications
public interface IRepository<T> where T : AggregateRoot
{
    Task<T> FirstOrDefaultAsync(Specification<T> spec);
    Task<List<T>> ListAsync(Specification<T> spec);
    Task<int> CountAsync(Specification<T> spec);
}

public class EFRepository<T> : IRepository<T> where T : AggregateRoot
{
    private readonly DbContext _context;
    
    public async Task<T> FirstOrDefaultAsync(Specification<T> spec)
    {
        return await ApplySpecification(spec).FirstOrDefaultAsync();
    }
    
    public async Task<List<T>> ListAsync(Specification<T> spec)
    {
        return await ApplySpecification(spec).ToListAsync();
    }
    
    public async Task<int> CountAsync(Specification<T> spec)
    {
        return await ApplySpecification(spec).CountAsync();
    }
    
    private IQueryable<T> ApplySpecification(Specification<T> spec)
    {
        var query = _context.Set<T>().AsQueryable();
        
        // Add criteria
        if (spec.Criteria != null)
            query = query.Where(spec.Criteria);
        
        // Add includes
        query = spec.Includes.Aggregate(query, (current, include) =>
            current.Include(include)
        );
        
        // Add string-based includes
        query = spec.IncludeStrings.Aggregate(query, (current, include) =>
            current.Include(include)
        );
        
        // Add paging
        if (spec.IsPagingEnabled)
        {
            query = query.Skip(spec.Skip).Take(spec.Take);
        }
        
        return query;
    }
}

// Usage
public class OrderQueryService
{
    private readonly IRepository<Order> _repository;
    
    public async Task<List<Order>> GetPendingOrdersAsync()
    {
        var spec = new PendingOrdersSpecification();
        return await _repository.ListAsync(spec);
    }
    
    public async Task<List<Order>> GetCustomerOrdersAsync(int customerId)
    {
        var spec = new OrdersByCustomerSpecification(customerId);
        return await _repository.ListAsync(spec);
    }
}
```

---

## 2. CQRS Pattern

### 2.1 Command Query Responsibility Segregation

```csharp
namespace OrderManagement.Application.CQRS
{
    // Separate read and write models
    
    // Command: Modifies state
    public class CreateOrderCommand : IRequest<int>
    {
        public int CustomerId { get; set; }
        public List<OrderItemDto> Items { get; set; }
    }
    
    // Command handler
    public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, int>
    {
        private readonly IOrderRepository _repository;
        private readonly IUnitOfWork _unitOfWork;
        
        public async Task<int> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
        {
            var order = Order.Create(request.CustomerId, request.Items, request.Total);
            await _repository.SaveAsync(order);
            await _unitOfWork.CommitAsync();
            
            return order.Id;
        }
    }
    
    // Query: Reads state
    public class GetOrderDetailsQuery : IRequest<OrderDetailsDto>
    {
        public int OrderId { get; set; }
    }
    
    // Query handler (uses optimized read model)
    public class GetOrderDetailsQueryHandler : IRequestHandler<GetOrderDetailsQuery, OrderDetailsDto>
    {
        private readonly IOrderReadRepository _readRepository;
        
        public async Task<OrderDetailsDto> Handle(
            GetOrderDetailsQuery request,
            CancellationToken cancellationToken)
        {
            // Read from denormalized view optimized for queries
            return await _readRepository.GetOrderDetailsAsync(request.OrderId);
        }
    }
    
    // Separate read model optimized for queries
    public class OrderReadModel
    {
        public int OrderId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; }
        public int ItemCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    
    public interface IOrderReadRepository
    {
        Task<OrderDetailsDto> GetOrderDetailsAsync(int orderId);
        Task<List<OrderSummaryDto>> GetCustomerOrdersAsync(int customerId);
    }
    
    public class OrderReadRepository : IOrderReadRepository
    {
        private readonly IQueryable<OrderReadModel> _readModels;
        
        public async Task<OrderDetailsDto> GetOrderDetailsAsync(int orderId)
        {
            // Query denormalized view - fast!
            var readModel = await _readModels
                .FirstOrDefaultAsync(m => m.OrderId == orderId);
            
            return MapToDto(readModel);
        }
        
        public async Task<List<OrderSummaryDto>> GetCustomerOrdersAsync(int customerId)
        {
            return await _readModels
                .Where(m => m.CustomerId == customerId)
                .Select(m => new OrderSummaryDto { ... })
                .ToListAsync();
        }
    }
}

// Synchronize read model with write model via events
public class OrderEventHandler : 
    INotificationHandler<OrderCreatedEvent>,
    INotificationHandler<OrderConfirmedEvent>
{
    private readonly OrderReadDbContext _readDb;
    
    public async Task Handle(OrderCreatedEvent @event, CancellationToken ct)
    {
        var readModel = new OrderReadModel
        {
            OrderId = @event.OrderId,
            CustomerId = @event.CustomerId,
            Status = "Pending",
            CreatedAt = @event.OccurredAt
        };
        
        _readDb.OrderReadModels.Add(readModel);
        await _readDb.SaveChangesAsync();
    }
    
    public async Task Handle(OrderConfirmedEvent @event, CancellationToken ct)
    {
        var readModel = await _readDb.OrderReadModels
            .FirstOrDefaultAsync(m => m.OrderId == @event.OrderId);
        
        readModel.Status = "Confirmed";
        
        await _readDb.SaveChangesAsync();
    }
}
```

---

## 3. Event Sourcing

### 3.1 Event Store Implementation

```csharp
namespace OrderManagement.Domain.Events
{
    // Event sourcing: Store events instead of current state
    
    public interface IEventStore
    {
        Task AppendAsync<T>(int aggregateId, DomainEvent @event) where T : AggregateRoot;
        Task<List<DomainEvent>> GetEventsAsync(int aggregateId);
        Task<T> GetAggregateAsync<T>(int aggregateId) where T : AggregateRoot;
    }
    
    public class EventStore : IEventStore
    {
        private readonly EventStoreDbContext _context;
        
        public async Task AppendAsync<T>(int aggregateId, DomainEvent @event) 
            where T : AggregateRoot
        {
            var storedEvent = new StoredEvent
            {
                AggregateId = aggregateId,
                AggregateType = typeof(T).Name,
                EventType = @event.GetType().Name,
                EventData = JsonConvert.SerializeObject(@event),
                OccurredAt = @event.OccurredAt,
                Version = await GetNextVersionAsync(aggregateId)
            };
            
            _context.StoredEvents.Add(storedEvent);
            await _context.SaveChangesAsync();
        }
        
        public async Task<List<DomainEvent>> GetEventsAsync(int aggregateId)
        {
            var events = await _context.StoredEvents
                .Where(e => e.AggregateId == aggregateId)
                .OrderBy(e => e.Version)
                .ToListAsync();
            
            return events
                .Select(e => JsonConvert.DeserializeObject(
                    e.EventData,
                    Type.GetType(e.EventType)
                ) as DomainEvent)
                .ToList();
        }
        
        public async Task<T> GetAggregateAsync<T>(int aggregateId) 
            where T : AggregateRoot
        {
            var events = await GetEventsAsync(aggregateId);
            
            // Reconstruct aggregate from events
            var aggregate = Activator.CreateInstance<T>();
            aggregate.LoadFromHistory(events);
            
            return aggregate;
        }
        
        private async Task<int> GetNextVersionAsync(int aggregateId)
        {
            var maxVersion = await _context.StoredEvents
                .Where(e => e.AggregateId == aggregateId)
                .MaxAsync(e => (int?)e.Version) ?? 0;
            
            return maxVersion + 1;
        }
    }
    
    // Aggregate with event sourcing
    public class Order : AggregateRoot
    {
        public int Id { get; private set; }
        public int CustomerId { get; private set; }
        public List<OrderLine> Items { get; private set; }
        public Money Total { get; private set; }
        public OrderStatus Status { get; private set; }
        
        public void LoadFromHistory(List<DomainEvent> events)
        {
            foreach (var @event in events)
            {
                Apply(@event);
            }
        }
        
        private void Apply(DomainEvent @event)
        {
            switch (@event)
            {
                case OrderCreatedEvent e:
                    Id = e.OrderId;
                    CustomerId = e.CustomerId;
                    Items = e.Items;
                    Status = OrderStatus.Pending;
                    break;
                
                case OrderConfirmedEvent e:
                    Status = OrderStatus.Confirmed;
                    break;
                
                case OrderShippedEvent e:
                    Status = OrderStatus.Shipped;
                    break;
            }
        }
    }
}

// Benefits of event sourcing:
// - Complete audit trail
// - Temporal queries (state at any point in time)
// - Event replay for testing
// - Eventual consistency support
// - Microservice integration through events
```

---

## 4. Saga Pattern

### 4.1 Orchestrated Saga

```csharp
// Orchestrator-based saga for distributed transactions

namespace OrderManagement.Application.Sagas
{
    public class PlaceOrderSagaOrchestrator
    {
        private readonly IOrderService _orderService;
        private readonly IInventoryService _inventoryService;
        private readonly IPaymentService _paymentService;
        private readonly IShippingService _shippingService;
        private readonly ILogger<PlaceOrderSagaOrchestrator> _logger;
        
        public async Task<SagaResult> ExecuteSagaAsync(PlaceOrderRequest request)
        {
            var context = new SagaContext { Request = request };
            
            try
            {
                // Step 1: Create order
                _logger.LogInformation("Step 1: Creating order");
                context.OrderId = await _orderService.CreateOrderAsync(request);
                
                // Step 2: Reserve inventory
                _logger.LogInformation("Step 2: Reserving inventory");
                context.InventoryReserved = await _inventoryService.ReserveAsync(
                    context.OrderId,
                    request.Items
                );
                
                if (!context.InventoryReserved)
                {
                    throw new InsufficientStockException();
                }
                
                // Step 3: Process payment
                _logger.LogInformation("Step 3: Processing payment");
                context.PaymentProcessed = await _paymentService.ProcessAsync(
                    request.CustomerId,
                    request.Total
                );
                
                if (!context.PaymentProcessed)
                {
                    throw new PaymentFailedException();
                }
                
                // Step 4: Create shipment
                _logger.LogInformation("Step 4: Creating shipment");
                await _shippingService.CreateShipmentAsync(context.OrderId);
                
                // All steps successful
                _logger.LogInformation("Order saga completed successfully");
                
                return new SagaResult { Success = true, OrderId = context.OrderId };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order saga failed, compensating");
                
                // Compensate (rollback)
                await CompensateAsync(context);
                
                return new SagaResult { Success = false, ErrorMessage = ex.Message };
            }
        }
        
        private async Task CompensateAsync(SagaContext context)
        {
            // Undo steps in reverse order
            
            if (context.PaymentProcessed)
            {
                _logger.LogInformation("Compensating: Refunding payment");
                await _paymentService.RefundAsync(context.OrderId);
            }
            
            if (context.InventoryReserved)
            {
                _logger.LogInformation("Compensating: Releasing inventory");
                await _inventoryService.ReleaseAsync(context.OrderId);
            }
            
            if (context.OrderId > 0)
            {
                _logger.LogInformation("Compensating: Cancelling order");
                await _orderService.CancelOrderAsync(context.OrderId);
            }
        }
    }
    
    public class SagaContext
    {
        public PlaceOrderRequest Request { get; set; }
        public int OrderId { get; set; }
        public bool InventoryReserved { get; set; }
        public bool PaymentProcessed { get; set; }
    }
}
```

### 4.2 Choreography-Based Saga

```csharp
// Event-based saga: Services react to events from other services

namespace OrderManagement.Application.Sagas
{
    // Step 1: Order service publishes OrderPlacedEvent
    public class OrderPlacedEventHandler : INotificationHandler<OrderPlacedEvent>
    {
        private readonly IMessagePublisher _messagePublisher;
        
        public async Task Handle(OrderPlacedEvent @event, CancellationToken ct)
        {
            // Publish to message bus
            await _messagePublisher.PublishAsync(new OrderPlacedMessage
            {
                OrderId = @event.OrderId,
                Items = @event.Items
            });
        }
    }
    
    // Step 2: Inventory service receives OrderPlaced and reserves stock
    public class OrderPlacedMessageHandler : IMessageHandler<OrderPlacedMessage>
    {
        private readonly IInventoryService _inventoryService;
        private readonly IMessagePublisher _messagePublisher;
        
        public async Task HandleAsync(OrderPlacedMessage message)
        {
            try
            {
                await _inventoryService.ReserveAsync(message.OrderId, message.Items);
                
                // Publish success event
                await _messagePublisher.PublishAsync(new InventoryReservedMessage
                {
                    OrderId = message.OrderId
                });
            }
            catch (InsufficientStockException)
            {
                // Publish failure event
                await _messagePublisher.PublishAsync(new InventoryReservationFailedMessage
                {
                    OrderId = message.OrderId
                });
            }
        }
    }
    
    // Step 3: Payment service receives InventoryReserved and charges customer
    public class InventoryReservedMessageHandler : IMessageHandler<InventoryReservedMessage>
    {
        private readonly IPaymentService _paymentService;
        private readonly IMessagePublisher _messagePublisher;
        
        public async Task HandleAsync(InventoryReservedMessage message)
        {
            try
            {
                var result = await _paymentService.ProcessAsync(message.OrderId);
                
                await _messagePublisher.PublishAsync(new PaymentProcessedMessage
                {
                    OrderId = message.OrderId,
                    Success = result
                });
            }
            catch (PaymentException)
            {
                await _messagePublisher.PublishAsync(new PaymentFailedMessage
                {
                    OrderId = message.OrderId
                });
            }
        }
    }
    
    // Step 4: If payment fails, compensate
    public class PaymentFailedMessageHandler : IMessageHandler<PaymentFailedMessage>
    {
        private readonly IInventoryService _inventoryService;
        private readonly IOrderService _orderService;
        
        public async Task HandleAsync(PaymentFailedMessage message)
        {
            // Release inventory
            await _inventoryService.ReleaseAsync(message.OrderId);
            
            // Cancel order
            await _orderService.CancelOrderAsync(message.OrderId);
        }
    }
}
```

---

## 5. Anti-Corruption Layer

### 5.1 External System Integration

```csharp
// Isolate external system differences from domain

namespace OrderManagement.Infrastructure.ExternalSystems
{
    // External legacy system API (incompatible)
    public interface ILegacyInventoryService
    {
        // Old API returns XML
        string CheckStock(string productCode, int qty);
    }
    
    // Anti-corruption layer: Adapter
    public class LegacyInventoryAdapter : IInventoryService
    {
        private readonly ILegacyInventoryService _legacyService;
        private readonly ILogger<LegacyInventoryAdapter> _logger;
        
        public async Task<bool> CheckAvailabilityAsync(List<OrderLine> items)
        {
            try
            {
                foreach (var item in items)
                {
                    // Call legacy service
                    var xmlResponse = _legacyService.CheckStock(
                        ConvertProductIdToCode(item.ProductId),
                        item.Quantity
                    );
                    
                    // Parse and convert response
                    var available = ParseLegacyResponse(xmlResponse);
                    
                    if (!available)
                    {
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Legacy inventory service error");
                throw new InventoryServiceException("Failed to check stock", ex);
            }
        }
        
        private string ConvertProductIdToCode(int productId)
        {
            // Map modern ID to legacy code system
            return $"PROD-{productId:D8}";
        }
        
        private bool ParseLegacyResponse(string xml)
        {
            // Parse XML response and convert to boolean
            var doc = XDocument.Parse(xml);
            return doc.Root?.Element("status")?.Value == "AVAILABLE";
        }
    }
    
    // Keep domain clean from external system mess
    public interface IInventoryService
    {
        Task<bool> CheckAvailabilityAsync(List<OrderLine> items);
    }
}
```

---

## 6. Feature Flags

### 6.1 Progressive Rollout

```csharp
// Decouple deployment from release

namespace OrderManagement.Application.FeatureManagement
{
    public interface IFeatureManager
    {
        Task<bool> IsEnabledAsync(string featureName, int userId = 0);
        Task<T> GetVariantAsync<T>(string featureName, T defaultValue);
    }
    
    public class FeatureManager : IFeatureManager
    {
        private readonly IFeatureFlagStore _store;
        
        public async Task<bool> IsEnabledAsync(string featureName, int userId = 0)
        {
            var flag = await _store.GetAsync(featureName);
            
            if (flag == null)
                return false;
            
            return flag.IsEnabled && (flag.Rollout == 100 || IsUserInRollout(userId, flag.Rollout));
        }
        
        public async Task<T> GetVariantAsync<T>(string featureName, T defaultValue)
        {
            var flag = await _store.GetAsync(featureName);
            
            return flag?.Variant != null
                ? JsonConvert.DeserializeObject<T>(flag.Variant)
                : defaultValue;
        }
        
        private bool IsUserInRollout(int userId, int rolloutPercentage)
        {
            // Consistent hashing: same user always in same cohort
            return (userId % 100) < rolloutPercentage;
        }
    }
    
    // Usage in code
    public class NewOrderProcessingService : IOrderService
    {
        private readonly IFeatureManager _featureManager;
        private readonly IOrderService _newImplementation;
        private readonly IOrderService _legacyImplementation;
        
        public async Task<int> CreateOrderAsync(CreateOrderRequest request)
        {
            var useNewImplementation = await _featureManager.IsEnabledAsync("new-order-processing");
            
            var service = useNewImplementation
                ? _newImplementation
                : _legacyImplementation;
            
            return await service.CreateOrderAsync(request);
        }
    }
    
    // Feature flags for A/B testing
    public async Task<OrderSummaryDto> GetOrderAsync(int orderId, int userId)
    {
        var variant = await _featureManager.GetVariantAsync<OrderDisplayVariant>(
            "order-display-variant",
            new OrderDisplayVariant { ShowEstimatedDelivery = true }
        );
        
        var order = await _repository.GetByIdAsync(orderId);
        
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            Total = order.Total.Amount,
            ShowEstimatedDelivery = variant.ShowEstimatedDelivery  // A/B variant
        };
    }
}
```

---

## Summary

Enterprise patterns address common challenges:

**Specification Pattern:**
- Encapsulate domain logic for filtering
- Reusable query definitions
- Cleaner repositories

**CQRS:**
- Separate read and write models
- Optimize queries independently
- Eventual consistency support

**Event Sourcing:**
- Complete audit trail
- Temporal queries
- Event replay

**Saga Pattern:**
- Distributed transactions
- Orchestration or choreography
- Compensation logic

**Anti-Corruption Layer:**
- Isolate external system differences
- Protect domain from pollution
- Strategic integration

**Feature Flags:**
- Decouple deployment from release
- Progressive rollout
- A/B testing capability

Key principles:
- Choose patterns based on complexity
- Don't over-engineer simple domains
- Balance consistency vs availability
- Design for team understanding
- Measure impact

Common mistakes:
- Using too many patterns
- Inappropriate event sourcing (overkill)
- Not handling saga compensation
- Ignoring external system evolution
- Over-relying on feature flags

Next topic covers Team Management and development practices.
