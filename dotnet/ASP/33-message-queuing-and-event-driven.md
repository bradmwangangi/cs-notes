# 32. Message Queuing & Event-Driven Architecture

## Overview
Message queuing enables asynchronous communication between services, decoupling producers from consumers. This is foundational for building scalable, resilient distributed systems where services process events at their own pace and handle temporary outages gracefully.

---

## 1. Message Queuing Fundamentals

### 1.1 Synchronous vs Asynchronous Communication

```
SYNCHRONOUS (Blocking)
┌─────────────┐        Request        ┌──────────────┐
│   Service A ├────────────────────→  │  Service B   │
│             ├←────────────────────  │              │
└─────────────┘      Response         └──────────────┘
  Waits for response  Failure cascades  Tight coupling

ASYNCHRONOUS (Non-blocking)
┌─────────────┐        Message       ┌──────────────┐
│   Service A ├───────────────────→  │ Message      │
│             │   Returns            │ Queue        │  ┌──────────────┐
└─────────────┘   Immediately        └──────────────┘  │  Service B   │
                                            │ ←──────── │              │
                                       Processes        └──────────────┘
                                         When Ready
```

### 1.2 When to Use Async Messaging

**Good candidates:**
- Sending emails/notifications
- Image processing/transcoding
- Report generation
- Order processing pipelines
- Event notifications
- Batch operations

**Poor candidates:**
- Immediate user feedback needed
- Real-time validation
- Payment authorization
- Authentication/authorization

---

## 2. RabbitMQ for Message Queuing

### 2.1 RabbitMQ Concepts

```csharp
// RabbitMQ Architecture
/*
┌──────────────┐
│  Publisher   │  Sends messages
└────────┬─────┘
         │
         ↓
    ┌────────┐      Named exchange
    │Exchange│──┐   Routes messages
    └────────┘  │
                │   Bindings
                ↓   Connect exchange
            ┌───────┐  to queue
            │ Queue │──┐  
            └───────┘  │  Persists
                       │  messages
                       ↓
                  ┌──────────┐
                  │ Consumer │  Processes
                  └──────────┘  messages

Exchange Types:
- Direct: Key-based routing
- Fanout: Broadcast to all queues
- Topic: Pattern-based routing
*/

public class RabbitMQSetup
{
    public static void ConfigureRabbitMQ(IServiceCollection services, IConfiguration config)
    {
        var rabbitConfig = config.GetSection("RabbitMQ");
        
        services.AddSingleton<IConnectionFactory>(new ConnectionFactory
        {
            HostName = rabbitConfig["HostName"],
            UserName = rabbitConfig["UserName"],
            Password = rabbitConfig["Password"],
            DispatchConsumersAsync = true
        });
        
        // Dependency injection for RabbitMQ
        services.AddScoped<IMessagePublisher, RabbitMQPublisher>();
        services.AddScoped<IMessageConsumer, RabbitMQConsumer>();
    }
}

// Configuration in appsettings.json:
/*
{
  "RabbitMQ": {
    "HostName": "localhost",
    "UserName": "guest",
    "Password": "guest",
    "Exchange": "bookstore.exchange",
    "Queue": "order.queue",
    "RoutingKey": "order.*"
  }
}
*/
```

### 2.2 Publishing Messages

```csharp
public interface IMessagePublisher
{
    Task PublishAsync<T>(T message, string routingKey) where T : class;
}

public class RabbitMQPublisher : IMessagePublisher
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<RabbitMQPublisher> _logger;
    
    public async Task PublishAsync<T>(T message, string routingKey) where T : class
    {
        using (var connection = _connectionFactory.CreateConnection())
        using (var channel = connection.CreateModel())
        {
            var exchangeName = _config["RabbitMQ:Exchange"];
            
            // Declare exchange (idempotent)
            channel.ExchangeDeclare(
                exchange: exchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false
            );
            
            // Serialize message
            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
            
            // Create properties for durability
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.MessageId = Guid.NewGuid().ToString();
            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            
            // Publish message
            channel.BasicPublish(
                exchange: exchangeName,
                routingKey: routingKey,
                basicProperties: properties,
                body: body
            );
            
            _logger.LogInformation(
                "Message published: Type={MessageType}, RoutingKey={RoutingKey}, MessageId={MessageId}",
                typeof(T).Name,
                routingKey,
                properties.MessageId
            );
        }
    }
}

// Usage
public class OrderService
{
    private readonly IMessagePublisher _messagePublisher;
    
    public async Task<int> PlaceOrderAsync(Order order)
    {
        var orderId = await _repository.SaveAsync(order);
        
        // Publish event asynchronously
        await _messagePublisher.PublishAsync(
            new OrderPlacedEvent
            {
                OrderId = orderId,
                CustomerId = order.CustomerId,
                Total = order.Total.Amount,
                CreatedAt = DateTime.UtcNow
            },
            routingKey: "order.placed"
        );
        
        return orderId;
    }
}
```

### 2.3 Consuming Messages

```csharp
public interface IMessageConsumer
{
    Task SubscribeAsync<T>(string queueName, Func<T, Task> handler) where T : class;
}

public class RabbitMQConsumer : IMessageConsumer, IAsyncDisposable
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<RabbitMQConsumer> _logger;
    private IConnection _connection;
    private IModel _channel;
    
    public async Task SubscribeAsync<T>(string queueName, Func<T, Task> handler) 
        where T : class
    {
        _connection = _connectionFactory.CreateConnection();
        _channel = _connection.CreateModel();
        
        var exchangeName = _config["RabbitMQ:Exchange"];
        var routingKey = _config["RabbitMQ:RoutingKey"];
        
        // Declare queue (idempotent)
        _channel.QueueDeclare(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                // Dead-letter handling
                { "x-dead-letter-exchange", "dlx.exchange" },
                { "x-dead-letter-routing-key", "dead-letter" },
                // TTL: 24 hours
                { "x-message-ttl", 86400000 }
            }
        );
        
        // Bind queue to exchange
        _channel.QueueBind(
            queue: queueName,
            exchange: exchangeName,
            routingKey: routingKey
        );
        
        // Set QoS: Process one message at a time
        _channel.BasicQos(0, 1, false);
        
        // Create consumer
        var consumer = new AsyncEventingBasicConsumer(_channel);
        
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var message = Encoding.UTF8.GetString(ea.Body.ToArray());
                var @event = JsonConvert.DeserializeObject<T>(message);
                
                _logger.LogInformation(
                    "Message received: Type={MessageType}, MessageId={MessageId}",
                    typeof(T).Name,
                    ea.BasicProperties?.MessageId
                );
                
                // Process message
                await handler(@event);
                
                // Acknowledge message (remove from queue)
                _channel.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                
                // Negative acknowledge: Requeue for retry
                _channel.BasicNack(ea.DeliveryTag, false, requeue: true);
            }
        };
        
        // Start consuming
        _channel.BasicConsume(
            queue: queueName,
            autoAck: false,
            consumerTag: typeof(T).Name,
            consumer: consumer
        );
        
        _logger.LogInformation("Subscribed to queue: {QueueName}", queueName);
    }
    
    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}

// Hosted service for background consumption
public class OrderEventConsumerService : BackgroundService
{
    private readonly IMessageConsumer _consumer;
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<OrderEventConsumerService> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _consumer.SubscribeAsync<OrderPlacedEvent>(
            "order.inventory.queue",
            async @event =>
            {
                try
                {
                    await _inventoryService.ReserveAsync(@event.OrderId, @event.Items);
                    _logger.LogInformation("Inventory reserved for order {OrderId}", @event.OrderId);
                }
                catch (InsufficientStockException ex)
                {
                    _logger.LogWarning(ex, "Insufficient stock for order {OrderId}", @event.OrderId);
                    // Publish OrderRejectedEvent
                }
            }
        );
    }
}
```

---

## 3. Azure Service Bus

### 3.1 Service Bus Configuration

```csharp
public class ServiceBusConfiguration
{
    public static void ConfigureServiceBus(IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("ServiceBus");
        
        // Register Service Bus client
        services.AddAzureClients(builder =>
        {
            builder.AddServiceBusClient(connectionString);
        });
        
        services.AddScoped<IMessagePublisher, ServiceBusPublisher>();
        services.AddScoped<IMessageConsumer, ServiceBusConsumer>();
    }
}

/*
Connection string format:
Endpoint=sb://[namespace].servicebus.windows.net/;
SharedAccessKeyName=RootManageSharedAccessKey;
SharedAccessKey=[key]
*/
```

### 3.2 Publishing with Service Bus

```csharp
public class ServiceBusPublisher : IMessagePublisher
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IConfiguration _config;
    private readonly ILogger<ServiceBusPublisher> _logger;
    
    public async Task PublishAsync<T>(T message, string routingKey) where T : class
    {
        try
        {
            var topicName = _config["ServiceBus:TopicName"];
            var sender = _serviceBusClient.CreateSender(topicName);
            
            // Serialize message
            var body = JsonConvert.SerializeObject(message);
            var busMessage = new ServiceBusMessage(body)
            {
                Subject = typeof(T).Name,
                MessageId = Guid.NewGuid().ToString(),
                CorrelationId = Activity.Current?.Id ?? "",
                ApplicationProperties =
                {
                    { "Type", typeof(T).Name },
                    { "RoutingKey", routingKey }
                }
            };
            
            // Publish
            await sender.SendMessageAsync(busMessage);
            
            _logger.LogInformation(
                "Message published to Service Bus: {MessageType}",
                typeof(T).Name
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message");
            throw;
        }
    }
}
```

### 3.3 Consuming from Service Bus

```csharp
public class ServiceBusConsumer : IMessageConsumer, IAsyncDisposable
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IConfiguration _config;
    private readonly ILogger<ServiceBusConsumer> _logger;
    private ServiceBusProcessor _processor;
    
    public async Task SubscribeAsync<T>(string subscriptionName, Func<T, Task> handler) 
        where T : class
    {
        var topicName = _config["ServiceBus:TopicName"];
        
        // Create processor
        _processor = _serviceBusClient.CreateProcessor(
            topicName: topicName,
            subscriptionName: subscriptionName,
            options: new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 1
            }
        );
        
        // Set up handlers
        _processor.ProcessMessageAsync += async args =>
        {
            try
            {
                var body = args.Message.Body.ToString();
                var @event = JsonConvert.DeserializeObject<T>(body);
                
                _logger.LogInformation(
                    "Processing message: {MessageType}",
                    typeof(T).Name
                );
                
                await handler(@event);
                
                // Complete (acknowledge)
                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                
                // Dead-letter for manual review
                await args.DeadLetterMessageAsync(args.Message);
            }
        };
        
        _processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Error in Service Bus processor");
            return Task.CompletedTask;
        };
        
        // Start processing
        await _processor.StartProcessingAsync();
        
        _logger.LogInformation(
            "Subscribed to topic {TopicName}, subscription {SubscriptionName}",
            topicName,
            subscriptionName
        );
    }
    
    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (_processor != null)
        {
            await _processor.StopProcessingAsync();
            await _processor.DisposeAsync();
        }
    }
}
```

---

## 4. Dead-Letter Queues and Error Handling

### 4.1 Dead-Letter Handling

```csharp
public class DeadLetterConfiguration
{
    // RabbitMQ Dead Letter Exchange
    public static void SetupDeadLetterExchange(IModel channel)
    {
        // Declare DLX
        channel.ExchangeDeclare(
            exchange: "dlx.exchange",
            type: ExchangeType.Direct,
            durable: true
        );
        
        // Declare DLQ
        channel.QueueDeclare(
            queue: "dead-letter-queue",
            durable: true,
            exclusive: false,
            autoDelete: false
        );
        
        // Bind DLQ to DLX
        channel.QueueBind(
            queue: "dead-letter-queue",
            exchange: "dlx.exchange",
            routingKey: "dead-letter"
        );
    }
}

public class DeadLetterHandler
{
    private readonly IMessageConsumer _consumer;
    private readonly ILogger<DeadLetterHandler> _logger;
    private readonly DeadLetterRepository _dlRepository;
    
    public async Task HandleDeadLettersAsync()
    {
        await _consumer.SubscribeAsync<dynamic>(
            "dead-letter-queue",
            async dlMessage =>
            {
                _logger.LogError(
                    "Dead letter received: {Message}",
                    JsonConvert.SerializeObject(dlMessage)
                );
                
                // Store for analysis
                await _dlRepository.RecordDeadLetterAsync(new DeadLetterRecord
                {
                    Message = JsonConvert.SerializeObject(dlMessage),
                    ReceivedAt = DateTime.UtcNow,
                    Status = DeadLetterStatus.Pending
                });
                
                // Alert operations team
                await SendAlertAsync($"Dead letter: {dlMessage}");
            }
        );
    }
}
```

### 4.2 Retry Policies

```csharp
public class RetryPolicy
{
    private readonly IMessageConsumer _consumer;
    private readonly int _maxRetries = 3;
    
    public async Task ConsumeWithRetryAsync<T>(
        string queueName, 
        Func<T, Task> handler) 
        where T : class
    {
        await _consumer.SubscribeAsync<T>(
            queueName,
            async message =>
            {
                int attempt = 0;
                TimeSpan delay = TimeSpan.FromSeconds(1);
                
                while (attempt < _maxRetries)
                {
                    try
                    {
                        await handler(message);
                        return;  // Success
                    }
                    catch (TransientException ex) when (attempt < _maxRetries - 1)
                    {
                        attempt++;
                        
                        // Exponential backoff
                        await Task.Delay(delay);
                        delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    }
                    catch (Exception ex)
                    {
                        // Permanent failure, dead-letter
                        throw;
                    }
                }
            }
        );
    }
}
```

---

## 5. Event Sourcing with Messaging

### 5.1 Event Store with Message Publishing

```csharp
public class EventStore
{
    private readonly DbContext _dbContext;
    private readonly IMessagePublisher _messagePublisher;
    private readonly ILogger<EventStore> _logger;
    
    public async Task AppendEventAsync<TAggregate>(
        int aggregateId,
        DomainEvent @event)
        where TAggregate : AggregateRoot
    {
        // Store event persistently
        var storedEvent = new StoredDomainEvent
        {
            AggregateId = aggregateId,
            AggregateType = typeof(TAggregate).Name,
            EventType = @event.GetType().Name,
            EventData = JsonConvert.SerializeObject(@event),
            Timestamp = @event.OccurredAt,
            Version = await GetNextVersionAsync(aggregateId)
        };
        
        _dbContext.Add(storedEvent);
        await _dbContext.SaveChangesAsync();
        
        // Publish to message queue for other services
        await _messagePublisher.PublishAsync(
            @event,
            routingKey: $"{typeof(TAggregate).Name.ToLower()}.{@event.GetType().Name}"
        );
        
        _logger.LogInformation(
            "Event stored and published: {EventType}, AggregateId: {AggregateId}",
            @event.GetType().Name,
            aggregateId
        );
    }
    
    public async Task<List<DomainEvent>> GetEventsAsync(int aggregateId)
    {
        var events = await _dbContext.Set<StoredDomainEvent>()
            .Where(e => e.AggregateId == aggregateId)
            .OrderBy(e => e.Version)
            .ToListAsync();
        
        return events
            .Select(e => JsonConvert.DeserializeObject(
                e.EventData,
                Type.GetType($"{e.AggregateType}.{e.EventType}")
            ) as DomainEvent)
            .ToList();
    }
}
```

---

## 6. Idempotency in Message Processing

### 6.1 Idempotent Processing

```csharp
// Problem: Messages may be processed multiple times
// Solution: Make message handlers idempotent

public interface IIdempotencyService
{
    Task<bool> IsProcessedAsync(string messageId);
    Task MarkAsProcessedAsync(string messageId);
}

public class IdempotencyService : IIdempotencyService
{
    private readonly DbContext _dbContext;
    
    public async Task<bool> IsProcessedAsync(string messageId)
    {
        return await _dbContext.Set<ProcessedMessage>()
            .AnyAsync(m => m.MessageId == messageId);
    }
    
    public async Task MarkAsProcessedAsync(string messageId)
    {
        var processed = new ProcessedMessage
        {
            MessageId = messageId,
            ProcessedAt = DateTime.UtcNow
        };
        
        _dbContext.Add(processed);
        await _dbContext.SaveChangesAsync();
    }
}

// Handler with idempotency
public class OrderEventHandler
{
    private readonly IInventoryService _inventoryService;
    private readonly IIdempotencyService _idempotency;
    
    public async Task HandleOrderPlacedAsync(OrderPlacedEvent @event, string messageId)
    {
        // Check if already processed
        if (await _idempotency.IsProcessedAsync(messageId))
        {
            return;  // Already processed, skip
        }
        
        try
        {
            // Process only once
            await _inventoryService.ReserveAsync(@event.OrderId, @event.Items);
            
            // Mark as processed
            await _idempotency.MarkAsProcessedAsync(messageId);
        }
        catch (Exception ex)
        {
            // Error: Will retry on next message
            throw;
        }
    }
}
```

### 6.2 Avoiding Duplicate Processing

```csharp
public class OrderService
{
    public async Task<int> PlaceOrderAsync(CreateOrderCommand command)
    {
        // Use command ID for idempotency
        var order = new Order
        {
            Id = command.CommandId,  // Idempotency key
            CustomerId = command.CustomerId,
            Items = command.Items,
            CreatedAt = DateTime.UtcNow
        };
        
        try
        {
            await _repository.SaveAsync(order);
        }
        catch (UniqueConstraintException) when (await _repository.ExistsAsync(command.CommandId))
        {
            // Order already created with this command ID
            return command.CommandId;
        }
        
        return order.Id;
    }
}
```

---

## 7. Eventual Consistency

### 7.1 Managing Consistency Windows

```csharp
// Services eventually consistent through events

public class OrderService
{
    public async Task<int> PlaceOrderAsync(CreateOrderCommand command)
    {
        // Create order immediately (strong consistency within service)
        var order = await _repository.SaveAsync(new Order
        {
            CustomerId = command.CustomerId,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        
        // Publish event (other services eventually consistent)
        await _eventPublisher.PublishAsync(
            new OrderPlacedEvent { OrderId = order.Id, ... },
            "order.placed"
        );
        
        return order.Id;  // Return immediately
    }
}

public class InventoryService
{
    public async Task HandleOrderPlacedAsync(OrderPlacedEvent @event)
    {
        // May process seconds/minutes later
        // Temporary inconsistency acceptable
        await _repository.ReserveStockAsync(@event.OrderId, @event.Items);
    }
}

// What about queries during consistency window?
public class OrderQueryService
{
    public async Task<OrderDetailsViewModel> GetOrderAsync(int orderId)
    {
        var order = await _orderRepository.GetAsync(orderId);
        
        // Inventory may not be reserved yet
        var inventory = await _inventoryRepository.GetReservationAsync(orderId)
            ?? new UnreservedInventory();  // Handle missing data
        
        return new OrderDetailsViewModel
        {
            OrderId = order.Id,
            Status = order.Status,
            // Inventory status may be missing during consistency window
            InventoryReserved = inventory.IsReserved
        };
    }
}
```

---

## Summary

Message queuing enables:
- **Decoupling**: Services don't depend on each other
- **Resilience**: Failures isolated to individual services
- **Scalability**: Process at own pace, handle spikes
- **Traceability**: Events create audit trail

Key patterns:
- **Publish-Subscribe**: One-to-many communication
- **Point-to-Point**: Queues with single consumer
- **Sagas**: Distributed transactions via events
- **Event Sourcing**: Events as source of truth

Tools:
- **RabbitMQ**: Open-source, flexible
- **Azure Service Bus**: Managed, enterprise
- **Kafka**: Streaming, high throughput
- **AWS SQS/SNS**: Cloud-native

Considerations:
- Eventual consistency complexity
- Message ordering guarantees
- Failure handling and retries
- Monitoring and observability

Next topic covers Resilience Patterns for robust distributed systems.
