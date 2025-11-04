# Module 5: DDD Building Blocks - Part 2 (Aggregates & Repositories)

## Introduction

In this module, we'll explore two more fundamental DDD building blocks that are crucial for managing complexity in domain models:

- **Aggregates**: Consistency boundaries that group related entities and value objects
- **Repositories**: Abstractions for data access that maintain domain integrity

These patterns work together to ensure transactional consistency, encapsulation, and clean separation between domain logic and persistence concerns.

## Aggregates

### What Are Aggregates?

An aggregate is a cluster of domain objects (entities and value objects) that are treated as a single unit for data consistency and transaction boundaries. Key concepts:

- **Aggregate Root**: The main entity that controls access to the aggregate and enforces invariants
- **Consistency Boundary**: Everything inside the aggregate must be consistent at all times
- **Transaction Boundary**: Changes to the aggregate happen atomically (all or nothing)
- **Encapsulation**: External objects can only reference the aggregate root, not internal entities
- **Identity Scope**: The aggregate root provides the primary identity for the entire aggregate

### Why Aggregates Matter

Aggregates solve several critical problems in domain modeling:

- **Consistency**: Ensure related objects remain in valid states through business invariants
- **Performance**: Reduce database round trips by loading related objects together
- **Complexity Management**: Hide internal relationships and complexity from external objects
- **Transactional Integrity**: Define clear boundaries for atomic operations
- **Concurrency Control**: Prevent race conditions by controlling concurrent access
- **Memory Management**: Control object graph size and lazy loading boundaries

### Aggregate Rules (The Holy Trinity)

Eric Evans' three fundamental rules for aggregates:

1. **Reference Only Root**: External objects hold references only to the aggregate root
2. **Traverse via Root**: Access to internal objects goes through the root entity
3. **Invariant Enforcement**: The root ensures aggregate invariants are maintained

Additional important rules:
4. **Delete Cascade**: Deleting the root deletes the entire aggregate
5. **Transaction Scope**: All changes to an aggregate happen in one transaction
6. **Version Control**: Aggregates should support optimistic concurrency control

### Example: Order Aggregate with Comprehensive Business Logic

```typescript
// Aggregate Root - Order
class Order extends Entity<OrderId> {
    constructor(
        id: OrderId,
        private _customerId: CustomerId,
        private _status: OrderStatus,
        private _items: OrderItem[] = [],
        private _shippingAddress?: Address,
        private _billingAddress?: Address,
        private _version: number = 0, // For optimistic concurrency
        private readonly _createdAt: Date = new Date(),
        private _modifiedAt: Date = new Date()
    ) {
        super(id);
        this.validateInvariants();
    }

    // Read-only properties
    get customerId(): CustomerId { return this._customerId; }
    get status(): OrderStatus { return this._status; }
    get items(): readonly OrderItem[] { return [...this._items]; }
    get shippingAddress(): Address | undefined { return this._shippingAddress; }
    get billingAddress(): Address | undefined { return this._billingAddress; }
    get version(): number { return this._version; }
    get createdAt(): Date { return this._createdAt; }
    get modifiedAt(): Date { return this._modifiedAt; }

    get totalAmount(): Money {
        return this._items.reduce(
            (total, item) => total.add(item.lineTotal),
            Money.zero("USD")
        );
    }

    get itemCount(): number {
        return this._items.reduce((count, item) => count + item.quantity, 0);
    }

    // Business behavior - Item management
    addItem(product: Product, quantity: number): void {
        this.ensureCanModify();
        this.validateProduct(product, quantity);

        const existingItem = this._items.find(item => item.productId.equals(product.id));
        if (existingItem) {
            existingItem.increaseQuantity(quantity);
        } else {
            this._items.push(new OrderItem(product.id, product.name, product.price, quantity));
        }

        this.updateModificationTime();
        this.validateInvariants();
    }

    removeItem(productId: ProductId): void {
        this.ensureCanModify();

        const index = this._items.findIndex(item => item.productId.equals(productId));
        if (index === -1) {
            throw new DomainError("Product not found in order");
        }

        this._items.splice(index, 1);
        this.updateModificationTime();
        this.validateInvariants();
    }

    updateItemQuantity(productId: ProductId, newQuantity: number): void {
        this.ensureCanModify();

        if (newQuantity <= 0) {
            this.removeItem(productId);
            return;
        }

        const item = this._items.find(item => item.productId.equals(productId));
        if (!item) {
            throw new DomainError("Product not found in order");
        }

        item.changeQuantity(newQuantity);
        this.updateModificationTime();
        this.validateInvariants();
    }

    // Business behavior - Address management
    setShippingAddress(address: Address): void {
        this.ensureCanModify();
        this._shippingAddress = address;
        this.updateModificationTime();
    }

    setBillingAddress(address: Address): void {
        this.ensureCanModify();
        this._billingAddress = address;
        this.updateModificationTime();
    }

    // Business behavior - Order lifecycle
    place(): void {
        if (this._status !== OrderStatus.Draft) {
            throw new DomainError("Only draft orders can be placed");
        }
        if (this._items.length === 0) {
            throw new DomainError("Order must have at least one item");
        }
        if (!this._shippingAddress) {
            throw new DomainError("Shipping address is required");
        }
        if (this.totalAmount.amount <= 0) {
            throw new DomainError("Order total must be positive");
        }

        this._status = OrderStatus.Placed;
        this.updateModificationTime();
        this.incrementVersion();
    }

    confirm(): void {
        if (this._status !== OrderStatus.Placed) {
            throw new DomainError("Only placed orders can be confirmed");
        }
        this._status = OrderStatus.Confirmed;
        this.updateModificationTime();
        this.incrementVersion();
    }

    ship(trackingNumber: string): void {
        if (this._status !== OrderStatus.Confirmed) {
            throw new DomainError("Only confirmed orders can be shipped");
        }
        if (!trackingNumber?.trim()) {
            throw new DomainError("Tracking number is required");
        }

        this._status = OrderStatus.Shipped;
        this.updateModificationTime();
        this.incrementVersion();
    }

    deliver(): void {
        if (this._status !== OrderStatus.Shipped) {
            throw new DomainError("Only shipped orders can be delivered");
        }
        this._status = OrderStatus.Delivered;
        this.updateModificationTime();
        this.incrementVersion();
    }

    cancel(reason?: string): void {
        if ([OrderStatus.Delivered].includes(this._status)) {
            throw new DomainError("Delivered orders cannot be cancelled");
        }
        this._status = OrderStatus.Cancelled;
        this.updateModificationTime();
        this.incrementVersion();
    }

    // Business queries
    canBeModified(): boolean {
        return this._status === OrderStatus.Draft;
    }

    canBeCancelled(): boolean {
        return ![OrderStatus.Shipped, OrderStatus.Delivered, OrderStatus.Cancelled].includes(this._status);
    }

    isOverdue(): boolean {
        if (this._status !== OrderStatus.Shipped) return false;

        const shippedDate = this._modifiedAt; // Simplified - would need proper tracking
        const maxDeliveryDays = 30;
        const overdueDate = new Date(shippedDate.getTime() + maxDeliveryDays * 24 * 60 * 60 * 1000);

        return new Date() > overdueDate;
    }

    // Private helper methods
    private ensureCanModify(): void {
        if (!this.canBeModified()) {
            throw new DomainError("Order cannot be modified in its current state");
        }
    }

    private validateProduct(product: Product, quantity: number): void {
        if (!product.isAvailable()) {
            throw new DomainError(`Product ${product.name} is not available`);
        }
        if (quantity <= 0) {
            throw new DomainError("Quantity must be positive");
        }
        if (product.stockQuantity < quantity) {
            throw new DomainError(`Insufficient stock for ${product.name}`);
        }
    }

    private validateInvariants(): void {
        // Invariant: Order must have a customer
        if (!this._customerId) {
            throw new DomainError("Order must have a customer");
        }

        // Invariant: Total amount must be positive for non-draft orders
        if (this._status !== OrderStatus.Draft && this.totalAmount.amount <= 0) {
            throw new DomainError("Order total must be positive");
        }

        // Invariant: All items must have positive quantities
        if (this._items.some(item => item.quantity <= 0)) {
            throw new DomainError("All order items must have positive quantity");
        }

        // Invariant: Maximum 50 items per order
        if (this.itemCount > 50) {
            throw new DomainError("Order cannot have more than 50 items");
        }

        // Invariant: Shipping address required for placed orders
        if (this._status !== OrderStatus.Draft && !this._shippingAddress) {
            throw new DomainError("Shipping address is required");
        }
    }

    private updateModificationTime(): void {
        this._modifiedAt = new Date();
    }

    private incrementVersion(): void {
        this._version++;
    }

    equals(other: Entity<OrderId>): boolean {
        return this.id.equals(other.id);
    }
}

// Internal Entity - OrderItem
class OrderItem extends Entity<OrderItemId> {
    constructor(
        id: OrderItemId,
        private readonly _productId: ProductId,
        private readonly _productName: string,
        private readonly _unitPrice: Money,
        private _quantity: number,
        private readonly _addedAt: Date = new Date()
    ) {
        super(id);
        if (_quantity <= 0) throw new DomainError("Quantity must be positive");
    }

    get productId(): ProductId { return this._productId; }
    get productName(): string { return this._productName; }
    get unitPrice(): Money { return this._unitPrice; }
    get quantity(): number { return this._quantity; }
    get addedAt(): Date { return this._addedAt; }

    get lineTotal(): Money {
        return this._unitPrice.multiply(this._quantity);
    }

    increaseQuantity(amount: number): void {
        if (amount <= 0) throw new DomainError("Amount must be positive");
        this._quantity += amount;
    }

    decreaseQuantity(amount: number): void {
        if (amount <= 0) throw new DomainError("Amount must be positive");
        if (this._quantity - amount <= 0) {
            throw new DomainError("Cannot reduce quantity below 1");
        }
        this._quantity -= amount;
    }

    changeQuantity(newQuantity: number): void {
        if (newQuantity <= 0) {
            throw new DomainError("Quantity must be positive");
        }
        this._quantity = newQuantity;
    }

    equals(other: Entity<OrderItemId>): boolean {
        return this.id.equals(other.id);
    }
}

// Supporting Value Objects and Enums
enum OrderStatus {
    Draft = "draft",
    Placed = "placed",
    Confirmed = "confirmed",
    Preparing = "preparing",
    Shipped = "shipped",
    Delivered = "delivered",
    Cancelled = "cancelled",
    Returned = "returned"
}

class OrderId extends ValueObject<string> {
    constructor(value: string) {
        super(value);
        if (!value || value.trim().length === 0) {
            throw new DomainError("Order ID cannot be empty");
        }
    }
}

class OrderItemId extends ValueObject<string> {
    constructor(value: string) {
        super(value);
        if (!value || value.trim().length === 0) {
            throw new DomainError("Order item ID cannot be empty");
        }
    }
}

class CustomerId extends ValueObject<string> {
    constructor(value: string) {
        super(value);
        if (!value || value.trim().length === 0) {
            throw new DomainError("Customer ID cannot be empty");
        }
    }
}

class ProductId extends ValueObject<string> {
    constructor(value: string) {
        super(value);
        if (!value || value.trim().length === 0) {
            throw new DomainError("Product ID cannot be empty");
        }
    }
}

// Base classes for reuse
abstract class Entity<T> {
    protected readonly _id: T;

    constructor(id: T) {
        this._id = id;
    }

    get id(): T {
        return this._id;
    }

    abstract equals(other: Entity<T>): boolean;
}

abstract class ValueObject<T> {
    protected constructor(protected readonly _value: T) {}

    equals(other: ValueObject<T>): boolean {
        return JSON.stringify(this._value) === JSON.stringify(other._value);
    }

    toString(): string {
        return String(this._value);
    }
}

class Money extends ValueObject<{ amount: number; currency: string }> {
    constructor(amount: number, currency: string = "USD") {
        super({ amount, currency });
        if (amount < 0) throw new DomainError("Amount cannot be negative");
        if (!currency || currency.length !== 3) throw new DomainError("Invalid currency");
    }

    get amount(): number { return this._value.amount; }
    get currency(): string { return this._value.currency; }

    static zero(currency: string = "USD"): Money {
        return new Money(0, currency);
    }

    add(other: Money): Money {
        this.ensureSameCurrency(other);
        return new Money(this.amount + other.amount, this.currency);
    }

    multiply(factor: number): Money {
        return new Money(this.amount * factor, this.currency);
    }

    private ensureSameCurrency(other: Money): void {
        if (this.currency !== other.currency) {
            throw new DomainError("Cannot operate on different currencies");
        }
    }
}

class DomainError extends Error {
    constructor(message: string) {
        super(message);
        this.name = "DomainError";
    }
}
```

## Advanced Aggregate Patterns

### Aggregate with Domain Events

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
}

class Order extends AggregateRoot<OrderId> {
    // ... existing code ...

    place(): void {
        // ... existing validation ...

        this._status = OrderStatus.Placed;
        this.updateModificationTime();
        this.incrementVersion();

        // Publish domain event
        this.addDomainEvent(new OrderPlacedEvent(this.id, this.customerId, this.totalAmount));
    }

    ship(trackingNumber: string): void {
        // ... existing validation ...

        this._status = OrderStatus.Shipped;
        this.updateModificationTime();
        this.incrementVersion();

        this.addDomainEvent(new OrderShippedEvent(this.id, trackingNumber));
    }
}

abstract class DomainEvent {
    public readonly occurredOn: Date = new Date();
    public readonly eventId: string = crypto.randomUUID();
}

class OrderPlacedEvent extends DomainEvent {
    constructor(
        public readonly orderId: OrderId,
        public readonly customerId: CustomerId,
        public readonly totalAmount: Money
    ) {
        super();
    }
}

class OrderShippedEvent extends DomainEvent {
    constructor(
        public readonly orderId: OrderId,
        public readonly trackingNumber: string
    ) {
        super();
    }
}
```

### Snapshot Pattern for Large Aggregates

```typescript
class OrderSnapshot {
    constructor(
        public readonly orderId: OrderId,
        public readonly version: number,
        public readonly status: OrderStatus,
        public readonly totalAmount: Money,
        public readonly itemCount: number,
        public readonly createdAt: Date,
        public readonly modifiedAt: Date
    ) {}
}

class Order extends AggregateRoot<OrderId> {
    // ... existing code ...

    createSnapshot(): OrderSnapshot {
        return new OrderSnapshot(
            this.id,
            this.version,
            this.status,
            this.totalAmount,
            this.itemCount,
            this.createdAt,
            this.modifiedAt
        );
    }

    static fromSnapshot(snapshot: OrderSnapshot, items: OrderItem[], shippingAddress?: Address): Order {
        return new Order(
            snapshot.orderId,
            snapshot.customerId, // Would need to be stored in snapshot
            snapshot.status,
            items,
            shippingAddress,
            snapshot.version,
            snapshot.createdAt,
            snapshot.modifiedAt
        );
    }
}
```

### Concurrency Control with Versioning

```typescript
class Order extends AggregateRoot<OrderId> {
    // ... existing code ...

    // Optimistic concurrency control
    ensureVersion(expectedVersion: number): void {
        if (this._version !== expectedVersion) {
            throw new ConcurrencyError(`Order version conflict: expected ${expectedVersion}, got ${this._version}`);
        }
    }

    // Pessimistic concurrency control
    lock(): void {
        // Implementation would depend on persistence mechanism
        this._isLocked = true;
    }

    unlock(): void {
        this._isLocked = false;
    }

    isLocked(): boolean {
        return this._isLocked;
    }
}

class ConcurrencyError extends DomainError {
    constructor(message: string) {
        super(message);
        this.name = "ConcurrencyError";
    }
}
```

## Repositories

### What Are Repositories?

Repositories are abstractions that mediate between the domain and data mapping layers. They encapsulate data access logic and ensure that domain objects are properly reconstructed from persistence.

**Key Responsibilities:**
- **Encapsulate data access logic**: Hide persistence details from the domain
- **Return aggregate roots**: Always work with complete aggregates
- **Maintain domain integrity**: Ensure business rules during data operations
- **Support domain queries**: Provide ways to find aggregates
- **Handle transactions**: Coordinate with unit of work
- **Manage identity**: Ensure object identity across requests

### Repository Interface vs Implementation

**Interface (Domain Layer):**
- Expressed in domain terms
- Uses domain objects and value objects
- Defines contracts for domain operations
- Independent of persistence technology

**Implementation (Infrastructure Layer):**
- Handles actual data persistence
- Translates between domain and data models
- Implements performance optimizations
- Manages database connections and transactions

### Comprehensive Repository Example

```typescript
// Domain Layer - Repository Interface
interface OrderRepository {
    // Aggregate operations
    save(order: Order): Promise<void>;
    findById(id: OrderId): Promise<Order | null>;
    findByIdWithVersion(id: OrderId, expectedVersion: number): Promise<Order | null>;
    exists(id: OrderId): Promise<boolean>;
    nextIdentity(): OrderId;

    // Domain queries
    findByCustomerId(customerId: CustomerId): Promise<Order[]>;
    findByStatus(status: OrderStatus): Promise<Order[]>;
    findPlacedAfter(date: Date): Promise<Order[]>;
    findOverdue(): Promise<Order[]>;

    // Advanced queries
    findByCustomerAndStatus(customerId: CustomerId, status: OrderStatus): Promise<Order[]>;
    findByTotalAmountGreaterThan(amount: Money): Promise<Order[]>;

    // Specification-based queries
    findSatisfying(spec: OrderSpecification): Promise<Order[]>;

    // Bulk operations
    saveAll(orders: Order[]): Promise<void>;
    delete(id: OrderId): Promise<void>;

    // Snapshot operations for large aggregates
    saveSnapshot(snapshot: OrderSnapshot): Promise<void>;
    findSnapshotById(id: OrderId): Promise<OrderSnapshot | null>;
}

// Specification pattern for complex queries
interface OrderSpecification {
    isSatisfiedBy(order: Order): boolean;
}

class OrdersPlacedAfterSpecification implements OrderSpecification {
    constructor(private readonly date: Date) {}

    isSatisfiedBy(order: Order): boolean {
        return order.status === OrderStatus.Placed && order.createdAt > this.date;
    }
}

class HighValueOrdersSpecification implements OrderSpecification {
    constructor(private readonly threshold: Money) {}

    isSatisfiedBy(order: Order): boolean {
        return order.totalAmount.amount >= this.threshold.amount;
    }
}

// Infrastructure Layer - Repository Implementation
class SqlOrderRepository implements OrderRepository {
    constructor(
        private readonly db: DatabaseConnection,
        private readonly eventPublisher: DomainEventPublisher
    ) {}

    async save(order: Order): Promise<void> {
        const orderData = this.mapOrderToData(order);

        await this.db.transaction(async (trx) => {
            // Save order
            await trx.orders.upsert(orderData);

            // Save order items
            await trx.orderItems.delete({ orderId: order.id.toString() });
            for (const item of order.items) {
                await trx.orderItems.insert(this.mapOrderItemToData(item, order.id));
            }

            // Publish domain events
            const events = order.clearDomainEvents();
            for (const event of events) {
                await this.eventPublisher.publish(event);
            }
        });
    }

    async findById(id: OrderId): Promise<Order | null> {
        const orderData = await this.db.orders.findById(id.toString());
        if (!orderData) return null;

        const itemData = await this.db.orderItems.findByOrderId(id.toString());
        const items = itemData.map(item => this.mapDataToOrderItem(item));

        return this.mapDataToOrder(orderData, items);
    }

    async findByCustomerId(customerId: CustomerId): Promise<Order[]> {
        const ordersData = await this.db.orders.findByCustomerId(customerId.toString());
        const orders: Order[] = [];

        for (const orderData of ordersData) {
            const itemData = await this.db.orderItems.findByOrderId(orderData.id);
            const items = itemData.map(item => this.mapDataToOrderItem(item));
            orders.push(this.mapDataToOrder(orderData, items));
        }

        return orders;
    }

    async findSatisfying(spec: OrderSpecification): Promise<Order[]> {
        // Load all orders and filter in memory (for simplicity)
        // In production, you'd translate specifications to SQL
        const allOrders = await this.findAll();
        return allOrders.filter(order => spec.isSatisfiedBy(order));
    }

    nextIdentity(): OrderId {
        return new OrderId(`order-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`);
    }

    // Private mapping methods
    private mapOrderToData(order: Order): any {
        return {
            id: order.id.toString(),
            customerId: order.customerId.toString(),
            status: order.status,
            shippingAddress: order.shippingAddress?.toString(),
            billingAddress: order.billingAddress?.toString(),
            version: order.version,
            createdAt: order.createdAt,
            modifiedAt: order.modifiedAt
        };
    }

    private mapOrderItemToData(item: OrderItem, orderId: OrderId): any {
        return {
            id: item.id.toString(),
            orderId: orderId.toString(),
            productId: item.productId.toString(),
            productName: item.productName,
            unitPrice: item.unitPrice.amount,
            currency: item.unitPrice.currency,
            quantity: item.quantity,
            addedAt: item.addedAt
        };
    }

    private mapDataToOrder(data: any, items: OrderItem[]): Order {
        return new Order(
            new OrderId(data.id),
            new CustomerId(data.customerId),
            OrderStatus[data.status as keyof typeof OrderStatus],
            items,
            data.shippingAddress ? this.parseAddress(data.shippingAddress) : undefined,
            data.billingAddress ? this.parseAddress(data.billingAddress) : undefined,
            data.version,
            new Date(data.createdAt),
            new Date(data.modifiedAt)
        );
    }

    private mapDataToOrderItem(data: any): OrderItem {
        return new OrderItem(
            new OrderItemId(data.id),
            new ProductId(data.productId),
            data.productName,
            new Money(data.unitPrice, data.currency),
            data.quantity,
            new Date(data.addedAt)
        );
    }

    private parseAddress(addressString: string): Address {
        // Implementation would parse address string
        return {} as Address;
    }
}
```

## Repository Design Patterns

### Collection-Oriented vs Persistence-Oriented

**Collection-Oriented:**
- Repository behaves like an in-memory collection
- Methods like `add()`, `remove()`, `findById()`, `findAll()`
- Good for simple scenarios and in-memory implementations
- Easy to understand and test

**Persistence-Oriented:**
- Repository exposes persistence-specific operations
- Methods like `save()`, `update()`, `delete()`, `findByCriteria()`
- Better for complex persistence needs
- Supports advanced querying and optimization

### Query Patterns

```typescript
interface OrderRepository {
    // Direct queries
    findById(id: OrderId): Promise<Order | null>;
    findAll(): Promise<Order[]>;

    // Parameterized queries
    findByStatus(status: OrderStatus): Promise<Order[]>;
    findByCustomerAndDateRange(customerId: CustomerId, startDate: Date, endDate: Date): Promise<Order[]>;

    // Specification pattern
    findSatisfying(spec: OrderSpecification): Promise<Order[]>;

    // Query objects for complex scenarios
    findByQuery(query: OrderQuery): Promise<Order[]>;
}

class OrderQuery {
    constructor(
        public readonly customerId?: CustomerId,
        public readonly status?: OrderStatus,
        public readonly minAmount?: Money,
        public readonly maxAmount?: Money,
        public readonly placedAfter?: Date,
        public readonly placedBefore?: Date,
        public readonly sortBy?: 'date' | 'amount' | 'status',
        public readonly sortOrder?: 'asc' | 'desc',
        public readonly limit?: number,
        public readonly offset?: number
    ) {}
}
```

### Unit of Work Pattern

```typescript
interface UnitOfWork {
    begin(): Promise<void>;
    commit(): Promise<void>;
    rollback(): Promise<void>;

    registerNew<T extends AggregateRoot<any>>(entity: T): void;
    registerDirty<T extends AggregateRoot<any>>(entity: T): void;
    registerDeleted<T extends AggregateRoot<any>>(entity: T): void;

    getRepository<T extends AggregateRoot<any>>(entityClass: new (...args: any[]) => T): Repository<T>;
}

class DefaultUnitOfWork implements UnitOfWork {
    private newEntities: AggregateRoot<any>[] = [];
    private dirtyEntities: AggregateRoot<any>[] = [];
    private deletedEntities: AggregateRoot<any>[] = [];

    constructor(private readonly db: DatabaseConnection) {}

    async begin(): Promise<void> {
        await this.db.beginTransaction();
    }

    async commit(): Promise<void> {
        try {
            // Insert new entities
            for (const entity of this.newEntities) {
                await this.saveEntity(entity);
            }

            // Update dirty entities
            for (const entity of this.dirtyEntities) {
                await this.saveEntity(entity);
            }

            // Delete entities
            for (const entity of this.deletedEntities) {
                await this.deleteEntity(entity);
            }

            await this.db.commit();
            this.clear();
        } catch (error) {
            await this.rollback();
            throw error;
        }
    }

    async rollback(): Promise<void> {
        await this.db.rollback();
        this.clear();
    }

    registerNew<T extends AggregateRoot<any>>(entity: T): void {
        this.newEntities.push(entity);
    }

    registerDirty<T extends AggregateRoot<any>>(entity: T): void {
        this.dirtyEntities.push(entity);
    }

    registerDeleted<T extends AggregateRoot<any>>(entity: T): void {
        this.deletedEntities.push(entity);
    }

    private clear(): void {
        this.newEntities = [];
        this.dirtyEntities = [];
        this.deletedEntities = [];
    }

    private async saveEntity(entity: AggregateRoot<any>): Promise<void> {
        // Implementation would delegate to appropriate repository
    }

    private async deleteEntity(entity: AggregateRoot<any>): Promise<void> {
        // Implementation would delegate to appropriate repository
    }
}
```

## Aggregate Design Guidelines

### Size Matters
- **Small aggregates**: Easier to manage, better performance, simpler testing
- **Large aggregates**: May cause concurrency issues, complex loading, memory problems

**Guidelines:**
- Keep aggregates under 10-15 entities when possible
- Consider splitting if aggregate loading becomes a performance bottleneck
- Use snapshots for very large aggregates

### Finding Aggregate Boundaries

1. **Business Invariants**: What rules must be consistent together?
   - "Order total must equal sum of item totals"
   - "Account balance cannot be negative"
   - "Meeting cannot have overlapping time slots"

2. **Transaction Frequency**: How often are related objects modified together?
   - Objects modified in the same transaction likely belong together
   - Objects rarely modified together may belong in separate aggregates

3. **Consistency Requirements**: What needs strong vs eventual consistency?
   - Strong consistency within aggregate boundaries
   - Eventual consistency between aggregates

4. **Performance Needs**: What data is accessed together?
   - Frequently accessed together → same aggregate
   - Rarely accessed together → separate aggregates

5. **Concurrency Patterns**: How do multiple users interact with the data?
   - High contention → smaller aggregates
   - Low contention → larger aggregates acceptable

### Common Aggregate Patterns

**Single Entity Aggregate:**
```typescript
class User extends AggregateRoot<UserId> {
    // User is both aggregate root and the only entity
    // Simple case with no internal entities
}
```

**Master-Detail Aggregate:**
```typescript
class PurchaseOrder extends AggregateRoot<OrderId> {
    private items: OrderItem[]; // Internal entities for line items
    // Order manages the lifecycle of its items
}
```

**Complex Aggregate with Multiple Entities:**
```typescript
class InsurancePolicy extends AggregateRoot<PolicyId> {
    private policyHolder: PolicyHolder;
    private beneficiaries: Beneficiary[];
    private coverage: Coverage[];
    private claims: Claim[];
    // Complex relationships and invariants
}
```

## Repository Implementation Strategies

### Repository per Aggregate
- One repository per aggregate root type
- Clear separation of concerns
- Easier testing and maintenance
- Can optimize for specific aggregate patterns

### Generic Repository
```typescript
interface Repository<T extends AggregateRoot<any>> {
    save(entity: T): Promise<void>;
    findById(id: string): Promise<T | null>;
    findAll(): Promise<T[]>;
    delete(id: string): Promise<void>;
    exists(id: string): Promise<boolean>;
}

class GenericRepository<T extends AggregateRoot<any>> implements Repository<T> {
    constructor(
        private readonly entityClass: new (...args: any[]) => T,
        private readonly tableName: string,
        private readonly db: DatabaseConnection
    ) {}

    async save(entity: T): Promise<void> {
        const data = this.mapEntityToData(entity);
        await this.db.table(this.tableName).upsert(data);
    }

    async findById(id: string): Promise<T | null> {
        const data = await this.db.table(this.tableName).findById(id);
        return data ? this.mapDataToEntity(data) : null;
    }

    // Additional generic methods...
}
```

### Repository with Caching
```typescript
class CachedOrderRepository implements OrderRepository {
    private cache = new Map<string, { order: Order; expires: Date }>();

    constructor(
        private readonly decoratedRepository: OrderRepository,
        private readonly cacheExpirationMinutes: number = 5
    ) {}

    async findById(id: OrderId): Promise<Order | null> {
        const cacheKey = id.toString();
        const cached = this.cache.get(cacheKey);

        if (cached && cached.expires > new Date()) {
            return cached.order;
        }

        const order = await this.decoratedRepository.findById(id);
        if (order) {
            this.cache.set(cacheKey, {
                order,
                expires: new Date(Date.now() + this.cacheExpirationMinutes * 60 * 1000)
            });
        }

        return order;
    }

    async save(order: Order): Promise<void> {
        await this.decoratedRepository.save(order);
        // Invalidate cache
        this.cache.delete(order.id.toString());
    }

    // Delegate other methods to decorated repository
}
```

## Testing Aggregates and Repositories

### Aggregate Testing

```typescript
describe("Order Aggregate", () => {
    let order: Order;
    let customerId: CustomerId;
    let product: Product;

    beforeEach(() => {
        customerId = new CustomerId("cust-123");
        product = new Product(
            new ProductId("prod-456"),
            "Test Product",
            new Money(100, "USD"),
            true,
            10
        );

        order = new Order(
            new OrderId("order-789"),
            customerId,
            OrderStatus.Draft
        );
    });

    describe("Adding Items", () => {
        it("should add item to empty order", () => {
            order.addItem(product, 2);

            expect(order.items).toHaveLength(1);
            expect(order.totalAmount.amount).toBe(200);
            expect(order.itemCount).toBe(2);
        });

        it("should increase quantity for existing item", () => {
            order.addItem(product, 2);
            order.addItem(product, 3);

            expect(order.items).toHaveLength(1);
            expect(order.itemCount).toBe(5);
        });

        it("should not allow adding items to placed order", () => {
            order.addItem(product, 1);
            order.setShippingAddress({} as Address);
            order.place();

            expect(() => order.addItem(product, 1))
                .toThrow("Order cannot be modified");
        });

        it("should validate business invariants", () => {
            const outOfStockProduct = new Product(
                new ProductId("prod-999"),
                "Out of Stock",
                new Money(50, "USD"),
                false,
                0
            );

            expect(() => order.addItem(outOfStockProduct, 1))
                .toThrow("Product Out of Stock is not available");
        });
    });

    describe("Order Lifecycle", () => {
        beforeEach(() => {
            order.addItem(product, 1);
            order.setShippingAddress({} as Address);
        });

        it("should place order successfully", () => {
            order.place();

            expect(order.status).toBe(OrderStatus.Placed);
            expect(order.version).toBe(1);
        });

        it("should not place order without items", () => {
            const emptyOrder = new Order(
                new OrderId("order-999"),
                customerId,
                OrderStatus.Draft
            );

            expect(() => emptyOrder.place())
                .toThrow("Order must have at least one item");
        });

        it("should not place order without shipping address", () => {
            const orderWithoutAddress = new Order(
                new OrderId("order-999"),
                customerId,
                OrderStatus.Draft
            );
            orderWithoutAddress.addItem(product, 1);

            expect(() => orderWithoutAddress.place())
                .toThrow("Shipping address is required");
        });
    });

    describe("Business Queries", () => {
        it("should identify modifiable orders", () => {
            expect(order.canBeModified()).toBe(true);

            order.addItem(product, 1);
            order.setShippingAddress({} as Address);
            order.place();

            expect(order.canBeModified()).toBe(false);
        });

        it("should calculate total correctly", () => {
            const product2 = new Product(
                new ProductId("prod-789"),
                "Second Product",
                new Money(50, "USD"),
                true,
                5
            );

            order.addItem(product, 2);  // 200
            order.addItem(product2, 1); // 50
                                                // Total: 250

            expect(order.totalAmount.amount).toBe(250);
        });
    });
});
```

### Repository Testing

```typescript
describe("OrderRepository", () => {
    let repository: OrderRepository;
    let db: DatabaseConnection;

    beforeEach(() => {
        db = createTestDatabase();
        repository = new SqlOrderRepository(db);
    });

    afterEach(async () => {
        await db.cleanup();
    });

    describe("Saving and Loading", () => {
        it("should save and retrieve order", async () => {
            const order = createTestOrder();
            await repository.save(order);

            const retrieved = await repository.findById(order.id);
            expect(retrieved?.id).toEqual(order.id);
            expect(retrieved?.totalAmount).toEqual(order.totalAmount);
        });

        it("should handle concurrent modifications", async () => {
            const order = createTestOrder();
            await repository.save(order);

            // Simulate concurrent modification
            const order1 = await repository.findById(order.id);
            const order2 = await repository.findById(order.id);

            order1!.addItem(createTestProduct(), 1);
            order2!.addItem(createTestProduct(), 2);

            await repository.save(order1!);

            // Second save should fail due to version conflict
            await expect(repository.save(order2!))
                .rejects.toThrow("Concurrency conflict");
        });
    });

    describe("Queries", () => {
        beforeEach(async () => {
            // Setup test data
            const orders = [
                createTestOrder("customer1", OrderStatus.Draft),
                createTestOrder("customer1", OrderStatus.Placed),
                createTestOrder("customer2", OrderStatus.Placed),
            ];

            for (const order of orders) {
                await repository.save(order);
            }
        });

        it("should find orders by customer", async () => {
            const orders = await repository.findByCustomerId(new CustomerId("customer1"));
            expect(orders).toHaveLength(2);
        });

        it("should find orders by status", async () => {
            const orders = await repository.findByStatus(OrderStatus.Placed);
            expect(orders).toHaveLength(2);
        });

        it("should support specification queries", async () => {
            const spec = new OrdersPlacedAfterSpecification(new Date('2023-01-01'));
            const orders = await repository.findSatisfying(spec);

            expect(orders.length).toBeGreaterThan(0);
            orders.forEach(order => {
                expect(order.status).toBe(OrderStatus.Placed);
            });
        });
    });
});
```

## Common Pitfalls

### Aggregate Pitfalls

1. **Wrong Aggregate Boundaries**: Including entities that don't need to be consistent
2. **Missing Invariants**: Not enforcing business rules that span the aggregate
3. **Too Large Aggregates**: Performance issues and concurrency conflicts
4. **Anemic Aggregates**: Aggregates without rich behavior
5. **Exposed Internal State**: Allowing external modification of internal entities

### Repository Pitfalls

1. **Domain Logic in Repositories**: Business logic leaking into data access layer
2. **Lazy Loading Issues**: N+1 queries and performance problems
3. **Inconsistent Interfaces**: Different repositories with different method signatures
4. **Missing Transactions**: Data integrity issues across aggregate operations
5. **Tight Coupling**: Repositories depending on specific database implementations

## Performance Considerations

### Aggregate Performance
- **Size vs Performance Trade-off**: Smaller aggregates vs fewer database round trips
- **Snapshot Pattern**: For large aggregates that change infrequently
- **Event Sourcing**: Store changes instead of current state
- **CQRS**: Separate read and write models

### Repository Performance
- **Query Optimization**: Use database indexes and optimized queries
- **Caching Strategies**: Cache frequently accessed aggregates
- **Batch Operations**: Support bulk save/update operations
- **Connection Pooling**: Efficient database connection management
- **Read Replicas**: Use read replicas for query-heavy operations

## Key Takeaways

1. **Aggregates** define consistency and transaction boundaries around related domain objects
2. **Aggregate Roots** control access and enforce invariants for their aggregates
3. **Repositories** provide data access abstractions that maintain domain integrity
4. Design aggregates based on business invariants, not technical convenience
5. Repository interfaces belong in the domain layer; implementations in infrastructure
6. Use domain events to communicate changes between aggregates
7. Test aggregates and repositories thoroughly to maintain domain integrity
8. Consider performance implications when designing aggregate boundaries

## Next Steps

In Module 6, we'll explore Domain Services and Application Services - patterns for organizing business logic that doesn't fit in entities or aggregates.

## Exercise

1. **Refine Aggregate Boundaries**: Take an existing system and analyze whether your aggregate boundaries are correct based on business invariants.

2. **Implement Domain Events**: Add domain event publishing to your aggregates and create event handlers for cross-aggregate communication.

3. **Create Repository Implementations**: Implement repositories for your aggregates with proper error handling and transaction management.

4. **Add Concurrency Control**: Implement optimistic concurrency control using versioning in your aggregates and repositories.

5. **Write Comprehensive Tests**: Create unit tests for your aggregates and integration tests for your repositories.

6. **Performance Analysis**: Analyze the performance implications of your aggregate design and consider optimizations.

**Bonus Challenges:**
- Implement the Unit of Work pattern for coordinating changes across multiple aggregates
- Add snapshot support for large aggregates
- Implement repository caching and query optimization
- Create a generic repository base class
- Implement event sourcing for audit trails
