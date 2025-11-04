# Module 7: Strategic Patterns

## Introduction

Strategic patterns in DDD help manage relationships between bounded contexts at the system level. These patterns address:

- **Context Integration**: How bounded contexts communicate and share data
- **Team Organization**: How development teams align with bounded contexts
- **System Evolution**: How systems grow and change over time
- **Political Challenges**: Managing different stakeholders and priorities

This module covers the main strategic patterns and when to apply them, with detailed examples and case studies.

## Context Mapping

### What is Context Mapping?

Context mapping is the process of:
- **Identifying bounded contexts** in your system
- **Understanding relationships** between them
- **Defining integration patterns** for each relationship
- **Creating a map** of how contexts interact
- **Documenting team ownership** and responsibilities

Context maps serve as:
- Communication tools for teams and stakeholders
- Decision guides for integration strategies
- Documentation of system architecture
- Early warning systems for integration issues
- Evolution planning tools

### Context Mapping Process

1. **Identify Contexts**: Use domain analysis to find bounded contexts
2. **Define Relationships**: Determine how contexts interact
3. **Choose Patterns**: Select appropriate integration patterns
4. **Document Teams**: Assign team ownership and responsibilities
5. **Plan Evolution**: Consider how relationships might change over time

### Context Mapping Symbols

```
┌─────────────┐     ┌─────────────┐
│ Context A   │────▶│ Context B   │  Upstream/Downstream
└─────────────┘     └─────────────┘

┌─────────────┐     ┌─────────────┐
│ Context A   │◯───◯│ Context B   │  Anti-Corruption Layer
└─────────────┘     └─────────────┘

┌─────────────┐     ┌─────────────┐
│ Shared      │     │ Context A   │
│ Kernel      │◯───◯│             │  Shared Kernel
└─────────────┘     └─────────────┘

┌─────────────┐     ┌─────────────┐
│ Context A   │⇄───⇄│ Context B   │  Published Language
└─────────────┘     └─────────────┘

┌─────────────┐     ┌─────────────┐
│ Context A   │▷───▷│ Context B   │  Customer-Supplier
└─────────────┘     └─────────────┘

┌─────────────┐     ┌─────────────┐
│ Context A   │⊑───⊑│ Context B   │  Conformist
└─────────────┘     └─────────────┘
```

## Integration Patterns

### 1. Shared Kernel

**When to Use:**
- Teams have high trust and good communication
- Shared concepts are truly stable and unlikely to change
- Both contexts benefit from shared model consistency
- Political barriers between teams are low
- Development velocity is more important than strict separation

**Characteristics:**
- Shared subset of the domain model used by multiple contexts
- Both teams can modify the shared code
- Requires high coordination and communication
- Good for closely related contexts that evolve together
- Often used within the same team or closely collaborating teams

**Example:**
```typescript
// Shared Kernel - common concepts used by multiple contexts
export namespace SharedKernel {

    export abstract class ValueObject<T> {
        protected constructor(protected readonly _value: T) {}

        equals(other: ValueObject<T>): boolean {
            return JSON.stringify(this._value) === JSON.stringify(other._value);
        }
    }

    export class Money extends ValueObject<{amount: number, currency: string}> {
        constructor(amount: number, currency: string = "USD") {
            super({amount, currency});
            if (amount < 0) throw new Error("Amount cannot be negative");
        }

        get amount(): number { return this._value.amount; }
        get currency(): string { return this._value.currency; }

        add(other: Money): Money {
            this.ensureSameCurrency(other);
            return new Money(this.amount + other.amount, this.currency);
        }

        private ensureSameCurrency(other: Money): void {
            if (this.currency !== other.currency) {
                throw new Error(`Currency mismatch: ${this.currency} vs ${other.currency}`);
            }
        }
    }

    export class Email extends ValueObject<string> {
        constructor(value: string) {
            super(value.trim().toLowerCase());
            if (!this.isValidEmail(value)) {
                throw new Error("Invalid email format");
            }
        }

        private isValidEmail(email: string): boolean {
            const regex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
            return regex.test(email);
        }

        get value(): string { return this._value; }
    }

    export class CustomerId extends ValueObject<string> {
        constructor(value: string) {
            super(value);
            if (!value?.trim()) throw new Error("Customer ID is required");
        }
    }

    // Common interfaces used across contexts
    export interface CustomerSummary {
        id: CustomerId;
        name: string;
        email: Email;
        status: CustomerStatus;
        loyaltyTier: LoyaltyTier;
    }

    export enum CustomerStatus {
        Active = "active",
        Inactive = "inactive",
        Suspended = "suspended"
    }

    export enum LoyaltyTier {
        Bronze = "bronze",
        Silver = "silver",
        Gold = "gold",
        Platinum = "platinum"
    }
}

// Order Context uses shared kernel
import { SharedKernel } from '../shared-kernel';

class Order {
    constructor(
        private _customerId: SharedKernel.CustomerId,
        private _total: SharedKernel.Money
    ) {}

    get customerId(): SharedKernel.CustomerId { return this._customerId; }
    get total(): SharedKernel.Money { return this._total; }
}

// Customer Context provides data to shared kernel
class Customer {
    constructor(
        private _id: SharedKernel.CustomerId,
        private _name: string,
        private _email: SharedKernel.Email,
        private _status: SharedKernel.CustomerStatus,
        private _loyaltyPoints: number
    ) {}

    getSummary(): SharedKernel.CustomerSummary {
        return {
            id: this._id,
            name: this._name,
            email: this._email,
            status: this._status,
            loyaltyTier: this.calculateLoyaltyTier()
        };
    }

    private calculateLoyaltyTier(): SharedKernel.LoyaltyTier {
        if (this._loyaltyPoints >= 10000) return SharedKernel.LoyaltyTier.Platinum;
        if (this._loyaltyPoints >= 5000) return SharedKernel.LoyaltyTier.Gold;
        if (this._loyaltyPoints >= 1000) return SharedKernel.LoyaltyTier.Silver;
        return SharedKernel.LoyaltyTier.Bronze;
    }
}
```

**Advantages:**
- Consistency across contexts
- Reduced duplication
- Easier testing and maintenance
- Faster development for related features

**Disadvantages:**
- Coupling between teams
- Requires coordination for changes
- Can become bloated if not managed carefully
- May hinder independent evolution

**Best Practices:**
- Keep shared kernel minimal
- Clearly document ownership and change process
- Use automated tests to prevent regressions
- Regular sync meetings between teams

### 2. Customer-Supplier

**When to Use:**
- Clear upstream/downstream relationship exists
- Downstream team depends on upstream team's output
- Upstream team has multiple downstream consumers
- Need to manage dependencies and release coordination
- Different development cadences between teams

**Characteristics:**
- Upstream provides stable interfaces and contracts
- Downstream adapts to upstream's model and APIs
- Upstream considers downstream needs in planning
- Downstream has some influence on upstream priorities
- Contract testing ensures compatibility

**Example:**
```typescript
// Upstream Context - Product Catalog Service
interface ProductCatalogService {
    getProduct(id: ProductId): Promise<ProductSummary>;
    getProductsByCategory(categoryId: CategoryId): Promise<ProductSummary[]>;
    searchProducts(query: string): Promise<ProductSearchResult>;
    getProductAvailability(productId: ProductId): Promise<ProductAvailability>;
}

class ProductCatalogApplicationService implements ProductCatalogService {
    constructor(private productRepository: ProductRepository) {}

    async getProduct(id: ProductId): Promise<ProductSummary> {
        const product = await this.productRepository.findById(id);
        if (!product) {
            throw new ProductNotFoundError(id);
        }

        return {
            id: product.id.toString(),
            name: product.name,
            price: product.price,
            category: product.category.name,
            description: product.description,
            images: product.images,
            isAvailable: product.isAvailable(),
            stockQuantity: product.stockQuantity
        };
    }

    // ... other methods
}

// Downstream Context - Order Management
class OrderApplicationService {
    constructor(
        private orderRepository: OrderRepository,
        private productCatalogService: ProductCatalogService, // Upstream dependency
        private pricingService: PricingService
    ) {}

    async createOrder(request: CreateOrderRequest): Promise<OrderId> {
        // Validate products exist using upstream service
        for (const item of request.items) {
            const product = await this.productCatalogService.getProduct(item.productId);
            if (!product.isAvailable) {
                throw new DomainError(`Product ${product.name} is not available`);
            }

            const availability = await this.productCatalogService.getProductAvailability(item.productId);
            if (availability.stockQuantity < item.quantity) {
                throw new DomainError(`Insufficient stock for ${product.name}`);
            }
        }

        // Create order with validated product data
        const orderId = this.orderRepository.nextIdentity();
        const order = new Order(orderId, request.customerId, OrderStatus.Draft);

        // Add items using upstream product data
        for (const item of request.items) {
            const product = await this.productCatalogService.getProduct(item.productId);
            order.addItem(product, item.quantity);
        }

        // Calculate pricing
        const pricedOrder = await this.pricingService.calculateTotal(order);

        await this.orderRepository.save(pricedOrder);
        return orderId;
    }
}

// Contract tests to ensure upstream compatibility
describe("ProductCatalogService Contract", () => {
    it("should return product summary with required fields", async () => {
        const product = await productCatalogService.getProduct("prod-123");

        expect(product).toHaveProperty("id");
        expect(product).toHaveProperty("name");
        expect(product).toHaveProperty("price");
        expect(typeof product.price.amount).toBe("number");
        expect(typeof product.price.currency).toBe("string");
    });

    it("should throw ProductNotFoundError for non-existent product", async () => {
        await expect(
            productCatalogService.getProduct("non-existent")
        ).rejects.toThrow(ProductNotFoundError);
    });
});
```

**Advantages:**
- Clear responsibilities and expectations
- Managed dependencies and release cycles
- Downstream influence on upstream priorities
- Easier testing with contract tests

**Disadvantages:**
- Upstream becomes a bottleneck for downstream teams
- Political challenges in priority setting
- Coupling through API contracts

**Best Practices:**
- Use semantic versioning for APIs
- Implement contract testing
- Regular sync meetings between teams
- Clear SLA for upstream service availability

### 3. Anti-Corruption Layer (ACL)

**When to Use:**
- Contexts have different models for the same concepts
- Need to protect one context from changes in another
- Integration is complex or unstable
- Teams have different priorities and release cycles
- Legacy system integration
- Third-party system integration

**Characteristics:**
- Translation layer between different contexts
- Protects downstream context integrity
- Translates between different models and protocols
- Can be complex but provides strong isolation
- May involve data transformation, protocol conversion, and caching

**Example:**
```typescript
// Legacy CRM System (upstream)
interface LegacyCRM {
    getCustomer(customerId: string): Promise<LegacyCustomerData>;
    updateCustomer(customerId: string, data: LegacyCustomerUpdate): Promise<void>;
}

interface LegacyCustomerData {
    id: string;
    full_name: string;
    email_addr: string;
    phone_num?: string;
    status: "A" | "I" | "S"; // Active, Inactive, Suspended
    loyalty_pts: number;
    created_date: string;
}

// Modern Customer Context (downstream)
enum CustomerStatus {
    Active = "active",
    Inactive = "inactive",
    Suspended = "suspended"
}

class Customer {
    constructor(
        private _id: CustomerId,
        private _name: string,
        private _email: Email,
        private _phone?: PhoneNumber,
        private _status: CustomerStatus,
        private _loyaltyPoints: number,
        private _createdAt: Date
    ) {}

    // ... business methods
}

// Anti-Corruption Layer
class CustomerAdapter {
    constructor(private legacyCRM: LegacyCRM) {}

    async getCustomer(customerId: CustomerId): Promise<Customer> {
        try {
            const legacyData = await this.legacyCRM.getCustomer(customerId.toString());

            return new Customer(
                customerId,
                this.transformName(legacyData.full_name),
                new Email(legacyData.email_addr),
                legacyData.phone_num ? new PhoneNumber(legacyData.phone_num) : undefined,
                this.transformStatus(legacyData.status),
                legacyData.loyalty_pts,
                new Date(legacyData.created_date)
            );
        } catch (error) {
            if (error instanceof LegacyCustomerNotFoundError) {
                throw new CustomerNotFoundError(customerId);
            }
            throw new CustomerServiceUnavailableError(error.message);
        }
    }

    async updateCustomer(customer: Customer): Promise<void> {
        const legacyData: LegacyCustomerUpdate = {
            full_name: customer.name,
            email_addr: customer.email.value,
            phone_num: customer.phone?.value,
            status: this.reverseTransformStatus(customer.status),
            loyalty_pts: customer.loyaltyPoints
        };

        try {
            await this.legacyCRM.updateCustomer(customer.id.toString(), legacyData);
        } catch (error) {
            // Log error and potentially queue for retry
            throw new CustomerUpdateFailedError(customer.id, error.message);
        }
    }

    private transformName(fullName: string): string {
        // Handle legacy name formatting (e.g., "DOE, JOHN" -> "John Doe")
        const parts = fullName.split(', ');
        return parts.length === 2 ? `${parts[1]} ${parts[0]}` : fullName;
    }

    private transformStatus(legacyStatus: string): CustomerStatus {
        switch (legacyStatus) {
            case "A": return CustomerStatus.Active;
            case "I": return CustomerStatus.Inactive;
            case "S": return CustomerStatus.Suspended;
            default: return CustomerStatus.Inactive;
        }
    }

    private reverseTransformStatus(status: CustomerStatus): string {
        switch (status) {
            case CustomerStatus.Active: return "A";
            case CustomerStatus.Inactive: return "I";
            case CustomerStatus.Suspended: return "S";
            default: return "I";
        }
    }
}

// Usage in Customer Application Service
class CustomerApplicationService {
    constructor(
        private customerAdapter: CustomerAdapter,
        private customerRepository: CustomerRepository // For caching/optimization
    ) {}

    async getCustomer(customerId: CustomerId): Promise<Customer> {
        // Try cache first
        let customer = await this.customerRepository.findById(customerId);

        if (!customer) {
            // Fetch from legacy system via ACL
            customer = await this.customerAdapter.getCustomer(customerId);

            // Cache for future use
            await this.customerRepository.save(customer);
        }

        return customer;
    }

    async updateCustomer(customerId: CustomerId, updates: CustomerUpdates): Promise<void> {
        // Load customer
        const customer = await this.getCustomer(customerId);

        // Apply updates (business logic)
        if (updates.name) customer.changeName(updates.name);
        if (updates.email) customer.changeEmail(new Email(updates.email));

        // Save locally first
        await this.customerRepository.save(customer);

        // Sync to legacy system via ACL
        await this.customerAdapter.updateCustomer(customer);
    }
}

// Circuit breaker for resilience
class ResilientCustomerAdapter implements CustomerAdapter {
    constructor(
        private adapter: CustomerAdapter,
        private circuitBreaker: CircuitBreaker
    ) {}

    async getCustomer(customerId: CustomerId): Promise<Customer> {
        return this.circuitBreaker.execute(() =>
            this.adapter.getCustomer(customerId)
        );
    }

    async updateCustomer(customer: Customer): Promise<void> {
        return this.circuitBreaker.execute(() =>
            this.adapter.updateCustomer(customer)
        );
    }
}
```

**Advantages:**
- Strong isolation between contexts
- Protects from upstream changes
- Enables independent evolution
- Can handle complex translations
- Resilience patterns can be applied

**Disadvantages:**
- Additional complexity and maintenance
- Potential performance overhead
- Duplication of some concepts
- Requires careful design and testing

**Best Practices:**
- Keep ACL focused and minimal
- Use comprehensive tests for translations
- Implement monitoring and error handling
- Consider caching to improve performance
- Document transformation rules clearly

### 4. Conformist

**When to Use:**
- Downstream has less power or influence than upstream
- Upstream model is well-designed and stable
- Cost of translation outweighs benefits
- Need to minimize maintenance burden
- Downstream team is small or has limited resources

**Characteristics:**
- Downstream conforms to upstream's model
- Minimal to no translation layer
- Downstream accepts upstream's concepts and terminology
- Good when upstream is trustworthy and stable

**Example:**
```typescript
// Upstream Context - Enterprise Product Service
interface EnterpriseProductService {
    getProduct(sku: string): Promise<EnterpriseProduct>;
    getProductsByCategory(categoryCode: string): Promise<EnterpriseProduct[]>;
}

interface EnterpriseProduct {
    sku: string;
    name: string;
    description: string;
    price: {
        amount: number;
        currency: string;
        effectiveDate: string;
    };
    category: {
        code: string;
        name: string;
        hierarchy: string[];
    };
    inventory: {
        onHand: number;
        allocated: number;
        available: number;
    };
    status: "ACTIVE" | "DISCONTINUED" | "SEASONAL";
}

// Downstream Context - E-commerce Website (Conformist)
class ProductDisplayService {
    constructor(private enterpriseProductService: EnterpriseProductService) {}

    async getProductForDisplay(sku: string): Promise<ProductDisplay> {
        // Use upstream model directly with minimal transformation
        const product = await this.enterpriseProductService.getProduct(sku);

        return {
            sku: product.sku,
            name: product.name,
            description: product.description,
            price: product.price, // Use upstream price structure
            category: product.category, // Use upstream category structure
            availability: product.inventory.available > 0 ? "IN_STOCK" : "OUT_OF_STOCK",
            status: product.status,
            // Add downstream-specific fields
            displayPrice: this.formatPrice(product.price),
            seoTitle: this.generateSEOTitle(product)
        };
    }

    private formatPrice(price: EnterpriseProduct['price']): string {
        return `${price.currency} ${price.amount.toFixed(2)}`;
    }

    private generateSEOTitle(product: EnterpriseProduct): string {
        return `${product.name} - ${product.category.name}`;
    }
}

// The downstream context accepts upstream's data structures
interface ProductDisplay extends EnterpriseProduct {
    displayPrice: string;
    seoTitle: string;
}
```

**Advantages:**
- Minimal code and maintenance
- Fast to implement
- No translation complexity
- Direct use of upstream capabilities

**Disadvantages:**
- Tight coupling to upstream
- Downstream polluted with upstream concepts
- Difficult to change if upstream changes
- Limited flexibility for downstream needs

**Best Practices:**
- Only use when upstream is truly stable and well-designed
- Add minimal extensions rather than modifications
- Monitor upstream changes closely
- Have migration plan if coupling becomes problematic

### 5. Open Host Service

**When to Use:**
- Context serves many external consumers
- Need to provide stable, well-documented APIs
- Want to encourage ecosystem development
- Have resources to maintain public interfaces
- Context provides core business value

**Characteristics:**
- Published service interfaces with clear contracts
- Well-documented protocols and data formats
- Versioned APIs with deprecation policies
- SDKs and documentation for consumers
- May include multiple integration options (REST, GraphQL, messaging)

**Example:**
```typescript
// Open Host Service - Order Management API
@RestController("/api/v1/orders")
class OrderController {
    constructor(private orderApplicationService: OrderApplicationService) {}

    @Post("/")
    @Authorize("place_order")
    async placeOrder(@Body request: PlaceOrderRequest): Promise<OrderCreatedResponse> {
        this.validateRequest(request);

        const orderId = await this.orderApplicationService.placeOrder(request);

        return {
            orderId: orderId.toString(),
            status: "created",
            links: {
                self: `/api/v1/orders/${orderId}`,
                status: `/api/v1/orders/${orderId}/status`,
                cancel: `/api/v1/orders/${orderId}/cancel`
            }
        };
    }

    @Get("/{orderId}")
    @Authorize("view_order")
    async getOrder(@Path("orderId") orderId: string): Promise<OrderDetails> {
        this.validateOrderId(orderId);

        const order = await this.orderApplicationService.getOrderDetails(orderId);

        return {
            ...order,
            _links: {
                self: { href: `/api/v1/orders/${orderId}` },
                update: { href: `/api/v1/orders/${orderId}`, method: "PUT" },
                cancel: { href: `/api/v1/orders/${orderId}/cancel`, method: "POST" }
            }
        };
    }

    @Post("/{orderId}/cancel")
    @Authorize("cancel_order")
    async cancelOrder(
        @Path("orderId") orderId: string,
        @Body request: CancelOrderRequest
    ): Promise<OrderCancelledResponse> {
        this.validateOrderId(orderId);

        await this.orderApplicationService.cancelOrder({
            orderId,
            reason: request.reason
        });

        return {
            orderId,
            status: "cancelled",
            cancelledAt: new Date().toISOString()
        };
    }
}

// Published Language - Well-defined schemas
interface PlaceOrderRequest {
    customerId: string;
    items: OrderItemRequest[];
    shippingAddress: Address;
    billingAddress?: Address;
    paymentMethod: PaymentMethod;
}

interface OrderCreatedResponse {
    orderId: string;
    status: string;
    links: {
        self: string;
        status: string;
        cancel: string;
    };
}

// SDK for consumers
class OrderServiceSDK {
    constructor(private baseUrl: string, private apiKey: string) {}

    async placeOrder(request: PlaceOrderRequest): Promise<OrderCreatedResponse> {
        const response = await fetch(`${this.baseUrl}/api/v1/orders`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Authorization": `Bearer ${this.apiKey}`
            },
            body: JSON.stringify(request)
        });

        if (!response.ok) {
            throw new OrderAPIError(response.status, await response.text());
        }

        return response.json();
    }

    async getOrder(orderId: string): Promise<OrderDetails> {
        const response = await fetch(`${this.baseUrl}/api/v1/orders/${orderId}`, {
            headers: {
                "Authorization": `Bearer ${this.apiKey}`
            }
        });

        if (!response.ok) {
            throw new OrderAPIError(response.status, await response.text());
        }

        return response.json();
    }
}

// Contract tests for API compatibility
describe("Order API Contract", () => {
    it("should accept valid order placement", async () => {
        const request: PlaceOrderRequest = {
            customerId: "cust-123",
            items: [
                { productId: "prod-456", quantity: 2 }
            ],
            shippingAddress: {
                street: "123 Main St",
                city: "Anytown",
                state: "CA",
                zipCode: "12345"
            },
            paymentMethod: { type: "credit_card", token: "tok_123" }
        };

        const response = await orderAPI.placeOrder(request);

        expect(response).toHaveProperty("orderId");
        expect(response.status).toBe("created");
        expect(response.links).toHaveProperty("self");
    });

    it("should reject invalid requests", async () => {
        const invalidRequest = { customerId: "" };

        await expect(orderAPI.placeOrder(invalidRequest))
            .rejects.toThrow(OrderAPIError);
    });
});
```

**Advantages:**
- Enables ecosystem development
- Clear contracts and expectations
- Multiple integration options
- Professional API management

**Disadvantages:**
- High maintenance overhead
- Version management complexity
- Documentation and SDK maintenance
- Security and rate limiting concerns

**Best Practices:**
- Use semantic versioning
- Provide comprehensive documentation
- Offer multiple integration options
- Implement proper authentication and authorization
- Monitor API usage and performance

### 6. Published Language

**When to Use:**
- Multiple teams need to integrate through messaging
- No single team controls the integration standards
- Need standardized communication format across enterprise
- Complex enterprise integration requirements
- Event-driven architectures

**Characteristics:**
- Common language/schema for integration messages
- Well-defined and versioned schemas
- Governed by standards committee or architecture team
- Supports multiple transport mechanisms
- May include canonical data models

**Example:**
```typescript
// Published Language - Order Events Schema
export namespace OrderEvents {

    export interface OrderEvent {
        eventId: string;
        eventType: string;
        aggregateId: string;
        occurredOn: string;
        eventVersion: number;
        correlationId?: string;
        causationId?: string;
    }

    export interface OrderPlaced extends OrderEvent {
        eventType: "OrderPlaced";
        customerId: string;
        totalAmount: Money;
        currency: string;
        items: OrderItem[];
        shippingAddress: Address;
        billingAddress?: Address;
    }

    export interface OrderShipped extends OrderEvent {
        eventType: "OrderShipped";
        trackingNumber: string;
        carrier: string;
        shippedAt: string;
        estimatedDelivery?: string;
    }

    export interface OrderDelivered extends OrderEvent {
        eventType: "OrderDelivered";
        deliveredAt: string;
        deliveryNotes?: string;
    }

    // Canonical data structures
    export interface Money {
        amount: number;
        currency: string;
    }

    export interface OrderItem {
        productId: string;
        productName: string;
        quantity: number;
        unitPrice: Money;
        lineTotal: Money;
    }

    export interface Address {
        street: string;
        city: string;
        state: string;
        zipCode: string;
        country: string;
    }
}

// Message Router using Published Language
class OrderEventRouter {
    constructor(
        private eventBus: EventBus,
        private handlers: Map<string, EventHandler[]>
    ) {}

    async routeEvent(event: OrderEvents.OrderEvent): Promise<void> {
        const eventHandlers = this.handlers.get(event.eventType) || [];

        for (const handler of eventHandlers) {
            try {
                await handler.handle(event);
            } catch (error) {
                // Log error and potentially send to dead letter queue
                await this.handleProcessingError(event, error);
            }
        }
    }

    private async handleProcessingError(event: OrderEvents.OrderEvent, error: any): Promise<void> {
        console.error(`Error processing event ${event.eventType}:`, error);

        // Send to dead letter queue or retry queue
        await this.eventBus.publish("dead-letter-queue", {
            originalEvent: event,
            error: error.message,
            retryCount: 0,
            firstFailedAt: new Date().toISOString()
        });
    }
}

// Context publishes events in published language
class OrderEventPublisher {
    constructor(
        private eventBus: EventBus,
        private correlationIdGenerator: CorrelationIdGenerator
    ) {}

    async publishOrderPlaced(order: Order): Promise<void> {
        const event: OrderEvents.OrderPlaced = {
            eventId: crypto.randomUUID(),
            eventType: "OrderPlaced",
            aggregateId: order.id.toString(),
            occurredOn: new Date().toISOString(),
            eventVersion: 1,
            correlationId: this.correlationIdGenerator.generate(),
            customerId: order.customerId.toString(),
            totalAmount: order.totalAmount.amount,
            currency: order.totalAmount.currency,
            items: order.items.map(item => ({
                productId: item.productId.toString(),
                productName: item.productName,
                quantity: item.quantity,
                unitPrice: {
                    amount: item.unitPrice.amount,
                    currency: item.unitPrice.currency
                },
                lineTotal: {
                    amount: item.lineTotal.amount,
                    currency: item.lineTotal.currency
                }
            })),
            shippingAddress: {
                street: order.shippingAddress.street,
                city: order.shippingAddress.city,
                state: order.shippingAddress.state,
                zipCode: order.shippingAddress.zipCode,
                country: order.shippingAddress.country
            },
            billingAddress: order.billingAddress ? {
                street: order.billingAddress.street,
                city: order.billingAddress.city,
                state: order.billingAddress.state,
                zipCode: order.billingAddress.zipCode,
                country: order.billingAddress.country
            } : undefined
        };

        await this.eventBus.publish("order-events", event);
    }
}

// Consumer uses published language
class InventoryEventHandler implements EventHandler<OrderEvents.OrderPlaced> {
    constructor(private inventoryService: InventoryService) {}

    async handle(event: OrderEvents.OrderPlaced): Promise<void> {
        for (const item of event.items) {
            await this.inventoryService.reserveStock(
                item.productId,
                item.quantity
            );
        }
    }
}
```

**Advantages:**
- Standardized enterprise integration
- Loose coupling through events
- Supports complex integration scenarios
- Governance and consistency

**Disadvantages:**
- Governance overhead
- Schema evolution challenges
- Learning curve for teams
- Potential for over-standardization

**Best Practices:**
- Start with core business events
- Use semantic versioning for schemas
- Provide schema registry and documentation
- Implement comprehensive testing
- Monitor event processing and failures

## Choosing Integration Patterns

### Decision Framework

1. **Assess Relationship Dynamics**
   - How much influence do teams have over each other?
   - What's the trust level between teams?
   - How frequently do requirements change?

2. **Evaluate Context Stability**
   - How stable are the context boundaries?
   - How often do models within contexts change?
   - What's the maturity level of each context?

3. **Consider Integration Complexity**
   - How different are the models and languages?
   - What's the volume and frequency of data exchange?
   - Are there real-time requirements?

4. **Analyze Team Resources**
   - How much time can teams spend on integration?
   - What's the technical expertise level?
   - Are there dedicated integration teams?

### Pattern Selection Guide

| Scenario | Recommended Pattern | Rationale |
|----------|-------------------|-----------|
| Same team, closely related contexts | Shared Kernel | High trust, easy coordination |
| Different teams, stable upstream | Customer-Supplier | Clear power dynamics, managed dependencies |
| Different models, need isolation | Anti-Corruption Layer | Protection from changes, independent evolution |
| Stable upstream, simple needs | Conformist | Minimal effort, good when upstream is reliable |
| Many consumers, core business value | Open Host Service | Professional API management, ecosystem enablement |
| Enterprise messaging, complex integration | Published Language | Standardized communication, governance |

## Team Organization Patterns

### Context-Aligned Teams
- Each team owns one or more bounded contexts
- Teams have end-to-end responsibility
- Good for autonomy and ownership
- Matches DDD's strategic patterns

### Feature Teams
- Teams organized around business features
- Cross context boundaries
- Good for delivery speed
- May cause coordination issues

### Hybrid Approach
- Core teams own bounded contexts
- Feature teams for specific initiatives
- Balances ownership and delivery speed
- Requires clear governance

## Big Picture Event Storming

### What is Event Storming?

Event Storming is a collaborative workshop format for:
- **Discovering domain events** that occur in the business
- **Identifying bounded contexts** from event flows
- **Understanding system flow** from trigger to outcome
- **Building shared understanding** across teams

### Process

1. **Gather domain experts and technical team** (6-12 people)
2. **Identify domain events** (orange sticky notes) - things that happened
3. **Find commands** that trigger events (blue sticky notes)
4. **Add aggregates** that handle commands (yellow sticky notes)
5. **Identify read models** for queries (green sticky notes)
6. **Note external systems** (purple sticky notes)
7. **Define bounded contexts** by grouping related elements
8. **Identify integration relationships** between contexts

### Benefits

- Rapid domain understanding (1-2 days)
- Identifies bounded contexts naturally
- Uncovers integration points and dependencies
- Builds ubiquitous language collaboratively
- Fun and engaging for participants
- Produces concrete outputs (context maps, event models)

### Tips for Success

- Include real domain experts (not just managers)
- Use large walls and plenty of sticky notes
- Don't allow laptops or phones during discovery
- Take breaks every 45-60 minutes
- End with concrete next steps and owners

## Evolutionary Architecture

### Strangulation Pattern

**When to Use:**
- Migrating from legacy system gradually
- Can't replace entire system at once
- Need to maintain business continuity
- Legacy system is too large to rewrite

**Characteristics:**
- Gradually replace legacy components
- Route requests to new or old system based on rules
- Eventually "strangle" the old system completely
- Requires careful routing and feature parity

**Example:**
```typescript
// API Gateway routes requests during migration
class MigrationGateway {
    constructor(
        private legacyService: LegacyOrderService,
        private newOrderService: OrderApplicationService,
        private migrationRules: MigrationRules
    ) {}

    async handleOrderRequest(request: OrderRequest): Promise<OrderResponse> {
        if (this.migrationRules.shouldUseNewSystem(request)) {
            return await this.newOrderService.placeOrder(request);
        } else {
            const legacyResponse = await this.legacyService.placeOrder(request);

            // Transform legacy response to new format
            return this.transformLegacyResponse(legacyResponse);
        }
    }

    async getOrderDetails(orderId: string): Promise<OrderDetails> {
        // Check which system contains the order
        if (await this.migrationRules.isOrderInNewSystem(orderId)) {
            return await this.newOrderService.getOrderDetails(orderId);
        } else {
            const legacyOrder = await this.legacyService.getOrder(orderId);
            return this.transformLegacyOrder(legacyOrder);
        }
    }
}

class MigrationRules {
    // Simple rule: orders after migration date use new system
    shouldUseNewSystem(request: OrderRequest): boolean {
        return new Date() >= new Date("2024-01-01");
    }

    // Check if order exists in new system
    async isOrderInNewSystem(orderId: string): Promise<boolean> {
        // Implementation would check new system database
        return false; // Placeholder
    }
}
```

### Parallel Run

**When to Use:**
- High-risk system changes
- Need to validate new system behavior against old
- Regulatory requirements for testing
- Gradual confidence building

**Characteristics:**
- Run old and new systems in parallel
- Compare results for consistency
- Gradually increase traffic to new system
- Monitor for discrepancies and errors

### Context Evolution

Bounded contexts should evolve as understanding improves:

- **Split contexts** when they become too large or serve different purposes
- **Merge contexts** when integration overhead outweighs separation benefits
- **Rename contexts** when better names emerge
- **Adjust boundaries** as domain understanding deepens

## Case Studies

### E-commerce Platform

**Contexts Identified:**
- Product Catalog (core)
- Order Management (core)
- Customer Management (supporting)
- Shipping (supporting)
- Payment Processing (generic)

**Integration Patterns:**
- Product Catalog → Order Management: Customer-Supplier
- Customer Management → Order Management: Shared Kernel
- Order Management → Shipping: Anti-Corruption Layer
- Order Management → Payment: Open Host Service

**Evolution:**
- Started with monolithic context
- Split into separate contexts as team grew
- Added ACLs for third-party integrations
- Implemented event-driven communication

### Banking System

**Contexts Identified:**
- Account Management (core)
- Transaction Processing (core)
- Compliance & Regulatory (supporting)
- Customer Service (supporting)
- Risk Management (supporting)

**Integration Patterns:**
- All contexts use Published Language for events
- Shared Kernel for common financial concepts
- Anti-Corruption Layers for legacy integration
- Open Host Service for external APIs

**Challenges:**
- Heavy regulatory requirements
- Need for audit trails and compliance
- Complex integration with external systems

## Key Takeaways

1. **Context Mapping** visualizes relationships between bounded contexts and guides integration decisions
2. **Integration Patterns** (Shared Kernel, ACL, etc.) provide proven solutions for different relationship types
3. **Team Organization** should align with bounded context boundaries for optimal autonomy
4. **Event Storming** helps discover contexts and integration points collaboratively
5. **Evolutionary Patterns** enable system growth and migration without big-bang rewrites
6. Choose patterns based on team dynamics, context stability, and integration complexity
7. Document context maps and keep them updated as system evolves

## Next Steps

In Module 8, we'll build a complete example application that demonstrates all the DDD patterns we've learned, from strategic context mapping to tactical implementation.

## Exercise

1. **Context Mapping**: Create a context map for a system you're familiar with or design one for an e-commerce platform.

2. **Pattern Selection**: For each relationship in your map, choose an appropriate integration pattern and explain why.

3. **Team Organization**: Design team structure aligned with your bounded contexts.

4. **Event Storming**: Conduct a simulated event storming session for a simple domain (e.g., library book lending).

5. **Integration Implementation**: Implement one of the integration patterns (e.g., Anti-Corruption Layer) for two contexts.

6. **Evolution Planning**: Consider how your context map might evolve over time as the system grows.

**Bonus Challenges:**
- Implement a simple event-driven system using Published Language
- Create contract tests for an Open Host Service
- Design a strangulation pattern for migrating a legacy system
- Build a context map visualization tool
