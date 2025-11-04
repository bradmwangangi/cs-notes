# Module 6: Domain Services & Application Services

## Introduction

Not all business logic fits neatly into entities and aggregates. DDD provides two service patterns to handle different types of business logic:

- **Domain Services**: Business logic that involves multiple domain objects or requires coordination across aggregates
- **Application Services**: Orchestrate use cases, manage transactions, and handle cross-cutting concerns

This module explores when and how to use these patterns effectively, along with related patterns like domain events, CQRS, and event sourcing.

## Domain Services

### What Are Domain Services?

Domain services contain business logic that naturally belongs to the domain but doesn't fit within a single entity or aggregate. They orchestrate operations that involve multiple domain objects.

**Key Characteristics:**
- **Stateless**: No instance variables maintained between calls
- **Pure Functions**: Same inputs always produce same outputs (when possible)
- **Domain Focus**: Express business concepts, not technical concerns
- **Testable**: Easy to unit test in isolation
- **Interface Segregation**: Depend on abstractions, not concretions
- **Side-Effect Free**: Don't modify state directly (usually)

### When to Use Domain Services

Use domain services when you encounter these situations:

1. **Multi-Object Operations**: Logic that requires coordinating multiple entities/aggregates
2. **Business Rules**: Complex rules that span multiple aggregates
3. **Calculations**: Domain-specific calculations involving multiple concepts
4. **Validations**: Cross-aggregate validations
5. **Decisions**: Business decisions based on multiple domain objects
6. **Coordinations**: Operations that need to maintain consistency across aggregates

### Domain Service Examples

#### Funds Transfer Service

```typescript
// Domain Service Interface
interface FundsTransferService {
    transfer(
        fromAccountId: AccountId,
        toAccountId: AccountId,
        amount: Money,
        description?: string
    ): Promise<TransferResult>;
}

// Domain Service Implementation
class DefaultFundsTransferService implements FundsTransferService {
    constructor(
        private readonly accountRepository: AccountRepository,
        private readonly transferRepository: TransferRepository
    ) {}

    async transfer(
        fromAccountId: AccountId,
        toAccountId: AccountId,
        amount: Money,
        description: string = ""
    ): Promise<TransferResult> {
        // Business Rule: Cannot transfer to same account
        if (fromAccountId.equals(toAccountId)) {
            throw new DomainError("Cannot transfer to the same account");
        }

        // Load both accounts (within same transaction boundary)
        const fromAccount = await this.accountRepository.findById(fromAccountId);
        const toAccount = await this.accountRepository.findById(toAccountId);

        if (!fromAccount || !toAccount) {
            throw new DomainError("Account not found");
        }

        // Business Rule: Accounts must be active
        if (!fromAccount.isActive() || !toAccount.isActive()) {
            throw new DomainError("Both accounts must be active");
        }

        // Business Rule: Sufficient funds
        if (!fromAccount.hasSufficientFunds(amount)) {
            throw new DomainError("Insufficient funds");
        }

        // Business Rule: Same currency
        if (!fromAccount.currency.equals(toAccount.currency)) {
            throw new DomainError("Cannot transfer between different currencies");
        }

        // Business Rule: Transfer limits
        if (!fromAccount.canTransfer(amount)) {
            throw new DomainError("Transfer amount exceeds daily limit");
        }

        // Perform transfer atomically
        const transferId = this.transferRepository.nextIdentity();
        const transfer = Transfer.create(
            transferId,
            fromAccountId,
            toAccountId,
            amount,
            description
        );

        fromAccount.debit(amount, transferId);
        toAccount.credit(amount, transferId);

        // Save all changes
        await this.transferRepository.save(transfer);
        await this.accountRepository.save(fromAccount);
        await this.accountRepository.save(toAccount);

        return {
            transferId,
            status: TransferStatus.Completed,
            fromAccountBalance: fromAccount.balance,
            toAccountBalance: toAccount.balance
        };
    }
}

interface TransferResult {
    transferId: TransferId;
    status: TransferStatus;
    fromAccountBalance: Money;
    toAccountBalance: Money;
}
```

#### Pricing Service

```typescript
interface PricingService {
    calculateTotal(
        order: Order,
        pricingContext: PricingContext
    ): Promise<Money>;

    applyDiscounts(
        order: Order,
        customer: Customer
    ): Promise<Order>;
}

class DefaultPricingService implements PricingService {
    constructor(
        private readonly discountRepository: DiscountRepository,
        private readonly taxService: TaxService,
        private readonly loyaltyProgram: LoyaltyProgram
    ) {}

    async calculateTotal(
        order: Order,
        pricingContext: PricingContext
    ): Promise<Money> {
        let subtotal = order.items.reduce(
            (sum, item) => sum.add(item.lineTotal),
            Money.zero(order.currency)
        );

        // Apply product-specific discounts
        const applicableDiscounts = await this.discountRepository
            .findApplicableDiscounts(order.items, pricingContext.date);

        for (const discount of applicableDiscounts) {
            subtotal = discount.applyTo(subtotal, order.items);
        }

        // Apply customer loyalty discounts
        const loyaltyDiscount = await this.loyaltyProgram
            .calculateDiscount(order.customerId, subtotal);

        subtotal = subtotal.subtract(loyaltyDiscount);

        // Apply taxes
        const taxAmount = await this.taxService.calculateTax(
            subtotal,
            pricingContext.shippingAddress
        );

        return subtotal.add(taxAmount);
    }

    async applyDiscounts(
        order: Order,
        customer: Customer
    ): Promise<Order> {
        // Business logic for applying customer-specific discounts
        const loyaltyDiscount = await this.loyaltyProgram
            .calculateDiscount(customer.id, order.subtotal);

        if (loyaltyDiscount.amount > 0) {
            // Create a new order with discount applied
            // This would involve creating discount line items or adjusting prices
            return this.applyLoyaltyDiscount(order, loyaltyDiscount);
        }

        return order;
    }

    private applyLoyaltyDiscount(order: Order, discount: Money): Order {
        // Implementation would create new order with discount applied
        // This is simplified for the example
        return order;
    }
}

interface PricingContext {
    date: Date;
    shippingAddress: Address;
    customerSegment?: CustomerSegment;
}
```

#### Notification Service

```typescript
interface NotificationService {
    sendOrderConfirmation(order: Order, customer: Customer): Promise<void>;
    sendShippingNotification(order: Order, customer: Customer): Promise<void>;
    sendPaymentFailureNotification(order: Order, customer: Customer): Promise<void>;
}

class EmailNotificationService implements NotificationService {
    constructor(
        private readonly emailGateway: EmailGateway,
        private readonly templateEngine: TemplateEngine
    ) {}

    async sendOrderConfirmation(order: Order, customer: Customer): Promise<void> {
        const template = await this.templateEngine.loadTemplate("order-confirmation");
        const content = template.render({
            customerName: customer.name,
            orderId: order.id.toString(),
            orderTotal: order.totalAmount.toString(),
            items: order.items.map(item => ({
                name: item.productName,
                quantity: item.quantity,
                price: item.lineTotal.toString()
            }))
        });

        await this.emailGateway.send({
            to: customer.email.value,
            subject: `Order Confirmation - ${order.id}`,
            html: content
        });
    }

    async sendShippingNotification(order: Order, customer: Customer): Promise<void> {
        // Similar implementation for shipping notifications
    }

    async sendPaymentFailureNotification(order: Order, customer: Customer): Promise<void> {
        // Similar implementation for payment failure notifications
    }
}
```

### Domain Service Design Patterns

#### Strategy Pattern for Complex Calculations

```typescript
interface TaxCalculationStrategy {
    calculateTax(amount: Money, location: TaxLocation): Promise<Money>;
}

class USTaxCalculationStrategy implements TaxCalculationStrategy {
    async calculateTax(amount: Money, location: TaxLocation): Promise<Money> {
        // US tax calculation logic
        const taxRate = await this.getTaxRate(location.state, location.zipCode);
        return amount.multiply(taxRate);
    }

    private async getTaxRate(state: string, zipCode: string): Promise<number> {
        // Implementation would call tax service API
        return 0.08; // 8% example rate
    }
}

class InternationalTaxCalculationStrategy implements TaxCalculationStrategy {
    async calculateTax(amount: Money, location: TaxLocation): Promise<Money> {
        // International tax calculation logic
        return Money.zero(amount.currency); // No tax for international
    }
}

class TaxService {
    constructor(
        private readonly strategies: Map<string, TaxCalculationStrategy>
    ) {}

    async calculateTax(amount: Money, location: TaxLocation): Promise<Money> {
        const strategy = this.strategies.get(location.country) ||
                        this.strategies.get("default");

        return strategy!.calculateTax(amount, location);
    }
}
```

#### Specification Pattern for Complex Business Rules

```typescript
interface OrderSpecification {
    isSatisfiedBy(order: Order): boolean;
}

class OrderCanBeShippedSpecification implements OrderSpecification {
    isSatisfiedBy(order: Order): boolean {
        return order.status === OrderStatus.Paid &&
               order.shippingAddress !== undefined &&
               order.items.length > 0 &&
               order.items.every(item => item.quantity > 0);
    }
}

class OrderRequiresApprovalSpecification implements OrderSpecification {
    constructor(private readonly approvalThreshold: Money) {}

    isSatisfiedBy(order: Order): boolean {
        return order.totalAmount.amount >= this.approvalThreshold.amount;
    }
}

class OrderEligibilityService {
    constructor(
        private readonly canBeShippedSpec: OrderCanBeShippedSpecification,
        private readonly requiresApprovalSpec: OrderRequiresApprovalSpecification
    ) {}

    canShipOrder(order: Order): boolean {
        return this.canBeShippedSpec.isSatisfiedBy(order);
    }

    requiresApproval(order: Order): boolean {
        return this.requiresApprovalSpec.isSatisfiedBy(order);
    }

    getShippingEligibilityDetails(order: Order): ShippingEligibility {
        const canShip = this.canShipOrder(order);
        const requiresApproval = this.requiresApproval(order);

        return {
            canShip,
            requiresApproval,
            reasons: this.getReasons(order, canShip, requiresApproval)
        };
    }

    private getReasons(order: Order, canShip: boolean, requiresApproval: boolean): string[] {
        const reasons: string[] = [];

        if (!canShip) {
            if (order.status !== OrderStatus.Paid) {
                reasons.push("Order must be paid before shipping");
            }
            if (!order.shippingAddress) {
                reasons.push("Shipping address is required");
            }
            // Add more reasons...
        }

        if (requiresApproval) {
            reasons.push(`Order total exceeds approval threshold`);
        }

        return reasons;
    }
}

interface ShippingEligibility {
    canShip: boolean;
    requiresApproval: boolean;
    reasons: string[];
}
```

## Application Services

### What Are Application Services?

Application services orchestrate use cases and coordinate domain objects. They handle cross-cutting concerns and provide a clear API for client applications.

**Key Characteristics:**
- **Use Case Orchestration**: Implement business use cases
- **Transaction Management**: Handle database transactions
- **Security**: Authorization and authentication
- **Input Validation**: Basic validation of external inputs
- **DTO Translation**: Convert between external and domain representations
- **Event Publishing**: Publish domain events
- **Thin Layer**: Delegate to domain objects and services

### Application Service Responsibilities

1. **Use Case Implementation**: Orchestrate complex business operations
2. **Transaction Boundaries**: Manage database transactions
3. **Security & Authorization**: Check permissions and validate access
4. **Input/Output Translation**: Convert DTOs to domain objects and vice versa
5. **Cross-Cutting Concerns**: Logging, monitoring, auditing
6. **Error Handling**: Translate domain errors to application errors
7. **Event Coordination**: Publish events from domain operations

### Application Service Anti-Patterns

❌ **Fat Application Services**: Don't put business logic in application services
❌ **Data Access in Application Services**: Use repositories instead
❌ **Direct Entity Manipulation**: Go through domain objects
❌ **Multiple Responsibilities**: Keep services focused on single use cases
❌ **Business Logic**: Delegate business rules to domain layer

### Comprehensive Application Service Example

```typescript
// Application Service
class OrderApplicationService {
    constructor(
        private readonly orderRepository: OrderRepository,
        private readonly customerRepository: CustomerRepository,
        private readonly productRepository: ProductRepository,
        private readonly pricingService: PricingService,
        private readonly paymentService: PaymentService,
        private readonly shippingService: ShippingService,
        private readonly notificationService: NotificationService,
        private readonly eventPublisher: DomainEventPublisher,
        private readonly unitOfWork: UnitOfWork,
        private readonly securityContext: SecurityContext
    ) {}

    async placeOrder(request: PlaceOrderRequest): Promise<PlaceOrderResponse> {
        // Authorization
        this.ensureAuthenticated();
        this.ensurePermission("place_order");

        // Input validation
        this.validateRequest(request);

        // Start transaction
        await this.unitOfWork.begin();

        try {
            // Load domain objects
            const customer = await this.customerRepository.findById(request.customerId);
            if (!customer) {
                throw new ApplicationError("Customer not found", "CUSTOMER_NOT_FOUND");
            }

            // Verify customer can place orders
            this.verifyCustomerCanPlaceOrder(customer);

            // Create order aggregate
            const orderId = this.orderRepository.nextIdentity();
            const order = new Order(orderId, request.customerId, OrderStatus.Draft);

            // Add items with product validation
            for (const itemRequest of request.items) {
                const product = await this.productRepository.findById(itemRequest.productId);
                if (!product) {
                    throw new ApplicationError(
                        `Product ${itemRequest.productId} not found`,
                        "PRODUCT_NOT_FOUND"
                    );
                }

                if (!product.isAvailable()) {
                    throw new ApplicationError(
                        `Product ${product.name} is not available`,
                        "PRODUCT_UNAVAILABLE"
                    );
                }

                if (product.stockQuantity < itemRequest.quantity) {
                    throw new ApplicationError(
                        `Insufficient stock for ${product.name}`,
                        "INSUFFICIENT_STOCK"
                    );
                }

                order.addItem(product, itemRequest.quantity);
            }

            // Set addresses
            if (request.shippingAddress) {
                const shippingAddress = this.createAddressFromRequest(request.shippingAddress);
                order.setShippingAddress(shippingAddress);
            } else if (customer.defaultAddress) {
                order.setShippingAddress(customer.defaultAddress);
            }

            if (request.billingAddress) {
                const billingAddress = this.createAddressFromRequest(request.billingAddress);
                order.setBillingAddress(billingAddress);
            }

            // Apply pricing and discounts
            const pricedOrder = await this.pricingService.applyDiscounts(order, customer);

            // Place the order (domain logic)
            pricedOrder.place();

            // Process payment if requested
            if (request.paymentInfo) {
                const paymentResult = await this.paymentService.processPayment(
                    pricedOrder,
                    request.paymentInfo
                );

                if (!paymentResult.success) {
                    throw new ApplicationError(
                        "Payment processing failed",
                        "PAYMENT_FAILED",
                        paymentResult.errorDetails
                    );
                }

                pricedOrder.markAsPaid();
            }

            // Save aggregate
            await this.orderRepository.save(pricedOrder);

            // Publish domain events
            await this.eventPublisher.publishEvents(pricedOrder.clearDomainEvents());

            // Send confirmation notification
            await this.notificationService.sendOrderConfirmation(pricedOrder, customer);

            // Commit transaction
            await this.unitOfWork.commit();

            // Audit logging
            await this.logOrderPlaced(pricedOrder, customer);

            return {
                orderId: pricedOrder.id.toString(),
                orderNumber: this.generateOrderNumber(pricedOrder),
                totalAmount: pricedOrder.totalAmount,
                status: pricedOrder.status,
                estimatedDelivery: await this.shippingService.calculateDeliveryDate(pricedOrder)
            };

        } catch (error) {
            await this.unitOfWork.rollback();

            // Log error
            await this.logError("place_order_failed", error, request);

            throw error;
        }
    }

    async cancelOrder(request: CancelOrderRequest): Promise<void> {
        // Authorization
        this.ensureAuthenticated();
        this.ensureOrderOwnership(request.orderId);

        await this.unitOfWork.begin();

        try {
            const order = await this.orderRepository.findById(new OrderId(request.orderId));
            if (!order) {
                throw new ApplicationError("Order not found", "ORDER_NOT_FOUND");
            }

            // Business rule: can only cancel certain statuses
            if (!order.canBeCancelled()) {
                throw new ApplicationError(
                    "Order cannot be cancelled in its current status",
                    "ORDER_NOT_CANCELLABLE"
                );
            }

            // Domain operation
            order.cancel(request.reason);

            // Process refund if paid
            if (order.status === OrderStatus.Paid) {
                await this.paymentService.processRefund(order, request.reason);
            }

            // Update inventory
            for (const item of order.items) {
                await this.productRepository.adjustStock(item.productId, item.quantity);
            }

            await this.orderRepository.save(order);
            await this.eventPublisher.publishEvents(order.clearDomainEvents());

            await this.unitOfWork.commit();

            // Send cancellation notification
            const customer = await this.customerRepository.findById(order.customerId);
            if (customer) {
                await this.notificationService.sendOrderCancellation(order, customer);
            }

        } catch (error) {
            await this.unitOfWork.rollback();
            throw error;
        }
    }

    async getOrderDetails(orderId: string): Promise<OrderDetailsDto> {
        // Authorization
        this.ensureAuthenticated();
        this.ensureOrderOwnership(orderId);

        const order = await this.orderRepository.findById(new OrderId(orderId));
        if (!order) {
            throw new ApplicationError("Order not found", "ORDER_NOT_FOUND");
        }

        const customer = await this.customerRepository.findById(order.customerId);
        if (!customer) {
            throw new ApplicationError("Customer not found", "CUSTOMER_NOT_FOUND");
        }

        // Load product details for items
        const itemsWithProducts = await Promise.all(
            order.items.map(async (item) => {
                const product = await this.productRepository.findById(item.productId);
                return {
                    productId: item.productId.toString(),
                    productName: item.productName,
                    quantity: item.quantity,
                    unitPrice: item.unitPrice,
                    lineTotal: item.lineTotal,
                    productImage: product?.imageUrl
                };
            })
        );

        return {
            orderId: order.id.toString(),
            orderNumber: this.generateOrderNumber(order),
            status: order.status,
            customer: {
                id: customer.id.toString(),
                name: customer.name,
                email: customer.email.value
            },
            items: itemsWithProducts,
            shippingAddress: order.shippingAddress,
            billingAddress: order.billingAddress,
            totalAmount: order.totalAmount,
            createdAt: order.createdAt,
            modifiedAt: order.modifiedAt
        };
    }

    // Private helper methods
    private ensureAuthenticated(): void {
        if (!this.securityContext.isAuthenticated()) {
            throw new AuthenticationError("User must be authenticated");
        }
    }

    private ensurePermission(permission: string): void {
        if (!this.securityContext.hasPermission(permission)) {
            throw new AuthorizationError(`Missing permission: ${permission}`);
        }
    }

    private ensureOrderOwnership(orderId: string): void {
        const currentUserId = this.securityContext.getCurrentUserId();
        // Implementation would check if user owns the order
    }

    private validateRequest(request: PlaceOrderRequest): void {
        if (!request.customerId) {
            throw new ValidationError("Customer ID is required");
        }
        if (!request.items || request.items.length === 0) {
            throw new ValidationError("Order must have at least one item");
        }
        if (request.items.length > 100) {
            throw new ValidationError("Order cannot have more than 100 items");
        }

        // Additional validation...
    }

    private verifyCustomerCanPlaceOrder(customer: Customer): void {
        if (customer.status !== CustomerStatus.Active) {
            throw new ApplicationError("Customer account is not active", "CUSTOMER_INACTIVE");
        }

        // Check for outstanding payments, credit limits, etc.
    }

    private createAddressFromRequest(request: AddressRequest): Address {
        return new Address(
            request.street,
            request.city,
            request.state,
            request.zipCode,
            request.country || "USA"
        );
    }

    private generateOrderNumber(order: Order): string {
        // Implementation would generate a user-friendly order number
        return `ORD-${order.id.toString().slice(-8).toUpperCase()}`;
    }

    private async logOrderPlaced(order: Order, customer: Customer): Promise<void> {
        // Implementation would log to audit system
        console.log(`Order ${order.id} placed by customer ${customer.id}`);
    }

    private async logError(operation: string, error: any, request: any): Promise<void> {
        // Implementation would log to error monitoring system
        console.error(`Error in ${operation}:`, error, request);
    }
}

// DTOs and Request/Response types
interface PlaceOrderRequest {
    customerId: string;
    items: OrderItemRequest[];
    shippingAddress?: AddressRequest;
    billingAddress?: AddressRequest;
    paymentInfo?: PaymentInfo;
}

interface OrderItemRequest {
    productId: string;
    quantity: number;
}

interface PlaceOrderResponse {
    orderId: string;
    orderNumber: string;
    totalAmount: Money;
    status: OrderStatus;
    estimatedDelivery?: Date;
}

interface CancelOrderRequest {
    orderId: string;
    reason?: string;
}

interface OrderDetailsDto {
    orderId: string;
    orderNumber: string;
    status: OrderStatus;
    customer: CustomerSummary;
    items: OrderItemDetails[];
    shippingAddress?: Address;
    billingAddress?: Address;
    totalAmount: Money;
    createdAt: Date;
    modifiedAt: Date;
}

interface CustomerSummary {
    id: string;
    name: string;
    email: string;
}

interface OrderItemDetails {
    productId: string;
    productName: string;
    quantity: number;
    unitPrice: Money;
    lineTotal: Money;
    productImage?: string;
}

// Custom error types
class ApplicationError extends Error {
    constructor(
        message: string,
        public readonly code: string,
        public readonly details?: any
    ) {
        super(message);
        this.name = "ApplicationError";
    }
}

class AuthenticationError extends ApplicationError {
    constructor(message: string) {
        super(message, "AUTHENTICATION_FAILED");
    }
}

class AuthorizationError extends ApplicationError {
    constructor(message: string) {
        super(message, "AUTHORIZATION_FAILED");
    }
}

class ValidationError extends ApplicationError {
    constructor(message: string) {
        super(message, "VALIDATION_FAILED");
    }
}
```

## Service Layer Architecture

### Layer Separation

```
┌─────────────────┐
│   Application   │  ← Application Services, DTOs, Cross-cutting concerns
├─────────────────┤
│     Domain      │  ← Domain Services, Entities, Value Objects, Domain Events
├─────────────────┤
│  Infrastructure │  ← Repository Implementations, External Service Adapters
└─────────────────┘
```

### Dependency Direction

- **Application Services** depend on Domain Services and Repositories
- **Domain Services** depend only on other domain objects
- **Infrastructure** implements interfaces defined in Domain and Application layers
- **Dependency Inversion**: Higher-level modules don't depend on lower-level modules

### Dependency Injection

```typescript
// Composition Root
class Application {
    private readonly orderService: OrderApplicationService;

    constructor() {
        // Infrastructure implementations
        const dbConnection = new PostgreSQLConnection();
        const eventBus = new RabbitMQEventBus();
        const emailGateway = new SendGridEmailGateway();

        // Repository implementations
        const orderRepository = new PostgreSQLOrderRepository(dbConnection);
        const customerRepository = new PostgreSQLCustomerRepository(dbConnection);
        const productRepository = new PostgreSQLProductRepository(dbConnection);

        // Domain services
        const pricingService = new DefaultPricingService(
            new PostgreSQLDiscountRepository(dbConnection),
            new TaxService()
        );

        const notificationService = new EmailNotificationService(
            emailGateway,
            new HandlebarsTemplateEngine()
        );

        // Application services
        this.orderService = new OrderApplicationService(
            orderRepository,
            customerRepository,
            productRepository,
            pricingService,
            new PaymentService(),
            new ShippingService(),
            notificationService,
            eventBus,
            new UnitOfWork(dbConnection),
            new SecurityContext()
        );
    }

    getOrderService(): OrderApplicationService {
        return this.orderService;
    }
}
```

## Domain Events

### What Are Domain Events?

Domain events represent important business occurrences that have happened in the past. They enable decoupling between aggregates and provide audit trails.

**Key Characteristics:**
- **Immutable**: Events represent facts that cannot be changed
- **Descriptive**: Contain all information needed to understand what happened
- **Named in Past Tense**: "OrderPlaced", "PaymentProcessed", "CustomerActivated"
- **Small**: Focused on single business occurrence
- **Versioned**: Support schema evolution

### Domain Event Patterns

#### Basic Domain Event

```typescript
abstract class DomainEvent {
    public readonly occurredOn: Date = new Date();
    public readonly eventId: string = crypto.randomUUID();
    public readonly eventVersion: number = 1;

    constructor(public readonly aggregateId: string) {}
}

class OrderPlacedEvent extends DomainEvent {
    constructor(
        public readonly orderId: string,
        public readonly customerId: string,
        public readonly totalAmount: Money,
        public readonly currency: string,
        public readonly items: OrderItemSummary[]
    ) {
        super(orderId);
    }
}

class OrderShippedEvent extends DomainEvent {
    constructor(
        public readonly orderId: string,
        public readonly trackingNumber: string,
        public readonly carrier: string,
        public readonly shippedAt: Date = new Date()
    ) {
        super(orderId);
    }
}

interface OrderItemSummary {
    productId: string;
    productName: string;
    quantity: number;
    unitPrice: Money;
}
```

#### Aggregate with Domain Events

```typescript
abstract class AggregateRoot<T> extends Entity<T> {
    private domainEvents: DomainEvent[] = [];

    protected addDomainEvent(event: DomainEvent): void {
        this.domainEvents.push(event);
    }

    clearDomainEvents(): DomainEvent[] {
        const events = [...this.domainEvents];
        this.domainEvents = [];
        return events;
    }

    get uncommittedEvents(): readonly DomainEvent[] {
        return [...this.domainEvents];
    }
}

class Order extends AggregateRoot<OrderId> {
    // ... existing code ...

    place(): void {
        // ... validation ...

        this._status = OrderStatus.Placed;
        this.updateModificationTime();
        this.incrementVersion();

        // Publish domain event
        this.addDomainEvent(new OrderPlacedEvent(
            this.id.toString(),
            this.customerId.toString(),
            this.totalAmount,
            this.totalAmount.currency,
            this.items.map(item => ({
                productId: item.productId.toString(),
                productName: item.productName,
                quantity: item.quantity,
                unitPrice: item.unitPrice
            }))
        ));
    }

    ship(trackingNumber: string, carrier: string): void {
        // ... validation ...

        this._status = OrderStatus.Shipped;
        this.updateModificationTime();
        this.incrementVersion();

        this.addDomainEvent(new OrderShippedEvent(
            this.id.toString(),
            trackingNumber,
            carrier
        ));
    }
}
```

#### Domain Event Handlers

```typescript
interface DomainEventHandler<T extends DomainEvent> {
    handle(event: T): Promise<void>;
}

class OrderPlacedEventHandler implements DomainEventHandler<OrderPlacedEvent> {
    constructor(
        private readonly inventoryService: InventoryService,
        private readonly analyticsService: AnalyticsService
    ) {}

    async handle(event: OrderPlacedEvent): Promise<void> {
        // Update inventory
        for (const item of event.items) {
            await this.inventoryService.reserveStock(
                item.productId,
                item.quantity
            );
        }

        // Send analytics event
        await this.analyticsService.trackOrderPlaced({
            orderId: event.orderId,
            customerId: event.customerId,
            totalAmount: event.totalAmount,
            currency: event.currency,
            itemCount: event.items.length
        });
    }
}

class OrderShippedEventHandler implements DomainEventHandler<OrderShippedEvent> {
    constructor(
        private readonly notificationService: NotificationService,
        private readonly customerRepository: CustomerRepository,
        private readonly orderRepository: OrderRepository
    ) {}

    async handle(event: OrderShippedEvent): Promise<void> {
        // Load order and customer
        const order = await this.orderRepository.findById(new OrderId(event.orderId));
        const customer = await this.customerRepository.findById(order!.customerId);

        // Send shipping notification
        if (customer) {
            await this.notificationService.sendShippingNotification(order!, customer);
        }
    }
}
```

#### Event Publisher

```typescript
interface DomainEventPublisher {
    publish(event: DomainEvent): Promise<void>;
    publishEvents(events: DomainEvent[]): Promise<void>;
}

class InMemoryDomainEventPublisher implements DomainEventPublisher {
    constructor(
        private readonly handlers: Map<string, DomainEventHandler<any>[]>
    ) {}

    async publish(event: DomainEvent): Promise<void> {
        const eventType = event.constructor.name;
        const eventHandlers = this.handlers.get(eventType) || [];

        for (const handler of eventHandlers) {
            try {
                await handler.handle(event);
            } catch (error) {
                // Log error but don't stop processing other handlers
                console.error(`Error handling event ${eventType}:`, error);
            }
        }
    }

    async publishEvents(events: DomainEvent[]): Promise<void> {
        for (const event of events) {
            await this.publish(event);
        }
    }
}

// Usage in repository
class OrderRepositoryImpl implements OrderRepository {
    constructor(
        private readonly db: DatabaseConnection,
        private readonly eventPublisher: DomainEventPublisher
    ) {}

    async save(order: Order): Promise<void> {
        // Save to database...

        // Publish events after successful save
        const events = order.clearDomainEvents();
        await this.eventPublisher.publishEvents(events);
    }
}
```

## CQRS (Command Query Responsibility Segregation)

### What is CQRS?

CQRS separates read and write operations into different models. Commands change state, queries return data.

**Benefits:**
- **Performance**: Optimize reads and writes separately
- **Scalability**: Scale read and write workloads independently
- **Complexity**: Simpler models for specific purposes
- **Security**: Different security models for commands and queries

### CQRS with Domain Services

```typescript
// Commands - Write Model
interface OrderCommands {
    placeOrder(command: PlaceOrderCommand): Promise<OrderId>;
    cancelOrder(command: CancelOrderCommand): Promise<void>;
    shipOrder(command: ShipOrderCommand): Promise<void>;
}

class OrderCommandService implements OrderCommands {
    constructor(
        private readonly orderRepository: OrderRepository,
        private readonly eventStore: EventStore
    ) {}

    async placeOrder(command: PlaceOrderCommand): Promise<OrderId> {
        // Create and validate order aggregate
        const order = new Order(/* ... */);
        order.place();

        // Save to repository
        await this.orderRepository.save(order);

        // Store events for potential replay
        await this.eventStore.saveEvents(order.id.toString(), order.clearDomainEvents());

        return order.id;
    }
}

// Queries - Read Model
interface OrderQueries {
    getOrderDetails(orderId: OrderId): Promise<OrderDetailsDto>;
    getCustomerOrders(customerId: CustomerId): Promise<OrderSummaryDto[]>;
    getOrdersByStatus(status: OrderStatus): Promise<OrderSummaryDto[]>;
}

class OrderQueryService implements OrderQueries {
    constructor(
        private readonly readModelStore: ReadModelStore
    ) {}

    async getOrderDetails(orderId: OrderId): Promise<OrderDetailsDto> {
        // Query optimized read model
        return await this.readModelStore.getOrderDetails(orderId.toString());
    }

    async getCustomerOrders(customerId: CustomerId): Promise<OrderSummaryDto[]> {
        // Query optimized for customer order history
        return await this.readModelStore.getCustomerOrders(customerId.toString());
    }
}

// Read Model Updater - Updates read model from domain events
class OrderReadModelUpdater {
    constructor(private readonly readModelStore: ReadModelStore) {}

    @EventHandler(OrderPlacedEvent)
    async handleOrderPlaced(event: OrderPlacedEvent): Promise<void> {
        await this.readModelStore.insertOrder({
            id: event.orderId,
            customerId: event.customerId,
            status: OrderStatus.Placed,
            totalAmount: event.totalAmount,
            currency: event.currency,
            itemCount: event.items.length,
            placedAt: event.occurredOn
        });
    }

    @EventHandler(OrderShippedEvent)
    async handleOrderShipped(event: OrderShippedEvent): Promise<void> {
        await this.readModelStore.updateOrderStatus(
            event.orderId,
            OrderStatus.Shipped
        );
    }
}
```

## Testing Services

### Domain Service Testing

```typescript
describe("FundsTransferService", () => {
    let service: FundsTransferService;
    let accountRepo: Mock<AccountRepository>;
    let transferRepo: Mock<TransferRepository>;

    beforeEach(() => {
        accountRepo = mock<AccountRepository>();
        transferRepo = mock<TransferRepository>();
        service = new DefaultFundsTransferService(accountRepo, transferRepo);
    });

    it("should transfer funds between accounts", async () => {
        const fromAccount = new Account("acc1", new Money(1000, "USD"));
        const toAccount = new Account("acc2", new Money(500, "USD"));

        accountRepo.findById
            .mockResolvedValueOnce(fromAccount)
            .mockResolvedValueOnce(toAccount);

        const result = await service.transfer("acc1", "acc2", new Money(200, "USD"));

        expect(result.status).toBe(TransferStatus.Completed);
        expect(fromAccount.balance.amount).toBe(800);
        expect(toAccount.balance.amount).toBe(700);
        expect(accountRepo.save).toHaveBeenCalledTimes(2);
    });

    it("should reject transfer to same account", async () => {
        await expect(
            service.transfer("acc1", "acc1", new Money(100, "USD"))
        ).rejects.toThrow("Cannot transfer to the same account");
    });

    it("should reject transfer with insufficient funds", async () => {
        const fromAccount = new Account("acc1", new Money(50, "USD"));
        accountRepo.findById.mockResolvedValue(fromAccount);

        await expect(
            service.transfer("acc1", "acc2", new Money(100, "USD"))
        ).rejects.toThrow("Insufficient funds");
    });
});
```

### Application Service Testing

```typescript
describe("OrderApplicationService", () => {
    let service: OrderApplicationService;
    let orderRepo: Mock<OrderRepository>;
    let customerRepo: Mock<CustomerRepository>;
    let productRepo: Mock<ProductRepository>;
    let unitOfWork: Mock<UnitOfWork>;

    beforeEach(() => {
        orderRepo = mock<OrderRepository>();
        customerRepo = mock<CustomerRepository>();
        productRepo = mock<ProductRepository>();
        unitOfWork = mock<UnitOfWork>();

        service = new OrderApplicationService(
            orderRepo,
            customerRepo,
            productRepo,
            mock<PricingService>(),
            mock<PaymentService>(),
            mock<ShippingService>(),
            mock<NotificationService>(),
            mock<DomainEventPublisher>(),
            unitOfWork,
            mock<SecurityContext>()
        );
    });

    it("should place order for valid customer", async () => {
        const customer = new Customer("cust1", "John Doe", new Email("john@test.com"));
        const product = new Product("prod1", "Test Product", new Money(100, "USD"), true, 10);

        customerRepo.findById.mockResolvedValue(customer);
        productRepo.findById.mockResolvedValue(product);
        orderRepo.nextIdentity.mockReturnValue(new OrderId("order1"));
        unitOfWork.begin.mockResolvedValue(undefined);
        unitOfWork.commit.mockResolvedValue(undefined);

        const request: PlaceOrderRequest = {
            customerId: "cust1",
            items: [{ productId: "prod1", quantity: 2 }]
        };

        const result = await service.placeOrder(request);

        expect(result.orderId).toBe("order1");
        expect(result.totalAmount.amount).toBe(200);
        expect(orderRepo.save).toHaveBeenCalled();
        expect(unitOfWork.commit).toHaveBeenCalled();
    });

    it("should rollback transaction on error", async () => {
        customerRepo.findById.mockRejectedValue(new Error("Database error"));

        const request: PlaceOrderRequest = {
            customerId: "cust1",
            items: [{ productId: "prod1", quantity: 1 }]
        };

        await expect(service.placeOrder(request))
            .rejects.toThrow("Database error");

        expect(unitOfWork.rollback).toHaveBeenCalled();
    });
});
```

## Common Pitfalls

### Domain Service Pitfalls

1. **Anemic Domain Model**: Services doing all the work, entities are just data holders
2. **Service Bloat**: Services becoming too large and doing too many things
3. **Domain Logic in Services**: Business rules scattered across services instead of entities
4. **Stateless Overuse**: Making everything stateless when state is needed
5. **Service Dependencies**: Services depending on infrastructure instead of abstractions

### Application Service Pitfalls

1. **Business Logic Leakage**: Business rules creeping into application services
2. **Transaction Bloat**: Transactions spanning too many operations
3. **Error Handling Issues**: Not properly translating domain errors
4. **Security Neglect**: Missing authorization checks
5. **Thick Application Services**: Doing too much work instead of orchestrating

### General Service Pitfalls

1. **Inconsistent Naming**: Different naming conventions across services
2. **Missing Interfaces**: Services not using interfaces for testability
3. **Tight Coupling**: Services directly depending on implementations
4. **Error Swallowing**: Catching and ignoring important errors
5. **Performance Issues**: Services doing expensive operations without caching

## Key Takeaways

1. **Domain Services** handle business logic that spans multiple domain objects
2. **Application Services** orchestrate use cases and manage cross-cutting concerns
3. Services should be stateless, focused, and follow single responsibility principle
4. Use dependency injection to manage service dependencies
5. Domain events enable loose coupling between aggregates and services
6. CQRS can improve performance and scalability for complex domains
7. Test services in isolation using mocks for dependencies
8. Proper error handling and transaction management are crucial

## Next Steps

In Module 7, we'll explore Strategic Patterns - high-level patterns for managing relationships between bounded contexts and organizing teams around domain boundaries.

## Exercise

1. **Identify Domain Services**: Analyze your domain and identify 3-5 operations that would be good candidates for domain services.

2. **Implement Domain Services**: Create domain services with proper interfaces and implement business logic that coordinates multiple domain objects.

3. **Create Application Services**: Build application services that orchestrate complex use cases, handle transactions, and manage cross-cutting concerns.

4. **Implement Domain Events**: Add domain event publishing to your aggregates and create event handlers for cross-aggregate communication.

5. **Add CQRS (Optional)**: Implement separate command and query services for improved performance and scalability.

6. **Comprehensive Testing**: Write unit tests for domain services and integration tests for application services.

7. **Error Handling**: Implement proper error handling and transaction rollback in application services.

**Bonus Challenges:**
- Implement event sourcing for audit trails
- Add distributed tracing for service calls
- Create a service bus for handling domain events
- Implement circuit breakers for external service calls
- Add comprehensive logging and monitoring
