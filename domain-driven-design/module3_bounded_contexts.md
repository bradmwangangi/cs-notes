# Module 3: Bounded Contexts

## What Are Bounded Contexts?

A bounded context is an explicit boundary within which a particular domain model applies. It's a linguistic and organizational boundary that defines:

- **Scope**: What concepts, terms, and rules are relevant within this context
- **Language**: The meaning of terms and their relationships within this context
- **Model**: The abstractions, entities, and business rules that apply
- **Ownership**: Which team is responsible for this context
- **Evolution**: How this context changes over time

Think of bounded contexts as different "worlds" or "universes" within your system where the same word can mean different things, and different rules apply.

### Bounded Context Characteristics

- **Semantic boundary**: Defines what terms mean within the context
- **Ownership boundary**: Defines which team owns the model
- **Development boundary**: Defines what can be changed independently
- **Deployment boundary**: Often corresponds to microservices or modules
- **Evolution boundary**: Allows different parts to evolve at different rates

## Why Bounded Contexts Matter

### The Problem: Ubiquitous Confusion

Without bounded contexts, attempting to use the same terms and models everywhere leads to:

- **Semantic confusion**: "Customer" means different things to different teams
- **Integration problems**: Different parts expect different data formats
- **Maintenance nightmares**: Changes in one area break others unexpectedly
- **Team conflicts**: Different teams have different priorities and constraints
- **Technical debt accumulation**: Workarounds to handle conflicting requirements
- **Scalability issues**: Monolithic models become too complex to manage

### The Solution: Clear Boundaries

Bounded contexts provide:

- **Clarity**: Each context has its own well-defined model and language
- **Autonomy**: Teams can work independently within their context boundaries
- **Controlled Integration**: Explicit interfaces between contexts
- **Independent Evolution**: Contexts can evolve at different rates
- **Scalability**: Different contexts can use different technologies
- **Testability**: Each context can be tested in isolation

## Examples of Bounded Contexts

### E-commerce System

**Product Catalog Context:**
- Products, categories, pricing, inventory levels
- Focus: Product information management
- Users: Product managers, catalog administrators
- Key operations: Add product, update pricing, manage categories

**Order Processing Context:**
- Orders, order items, order status, payment processing
- Focus: Order fulfillment and payment
- Users: Customers, order processors
- Key operations: Place order, process payment, update status

**Customer Management Context:**
- Customer profiles, addresses, preferences, loyalty programs
- Focus: Customer relationship management
- Users: Customer service reps, marketing team
- Key operations: Register customer, update profile, manage preferences

**Shipping Context:**
- Shipments, tracking, carriers, delivery schedules
- Focus: Physical goods movement
- Users: Warehouse staff, shipping coordinators
- Key operations: Create shipment, update tracking, confirm delivery

**Marketing Context:**
- Campaigns, promotions, analytics, customer segments
- Focus: Customer acquisition and retention
- Users: Marketing team, analysts
- Key operations: Create campaign, analyze performance, segment customers

### Banking System

**Account Management Context:**
- Account creation, status changes, account types
- Focus: Account lifecycle management
- Rules: Account opening requirements, status transitions

**Transaction Processing Context:**
- Deposits, withdrawals, transfers, balance updates
- Focus: Financial transaction handling
- Rules: Transaction limits, balance validation, fraud detection

**Compliance Context:**
- Regulatory reporting, audit trails, risk assessment
- Focus: Regulatory compliance and risk management
- Rules: Reporting requirements, audit standards

**Customer Service Context:**
- Support tickets, inquiries, account assistance
- Focus: Customer support and service
- Rules: Service level agreements, escalation procedures

### Healthcare System

**Patient Management Context:**
- Patient records, demographics, medical history
- Focus: Patient information management
- Privacy rules: HIPAA compliance, access controls

**Appointment Scheduling Context:**
- Appointments, schedules, resource booking
- Focus: Healthcare delivery coordination
- Rules: Scheduling constraints, resource availability

**Clinical Care Context:**
- Treatments, diagnoses, care plans, medications
- Focus: Medical care delivery
- Rules: Clinical protocols, medication interactions

**Billing Context:**
- Claims, insurance processing, payments
- Focus: Healthcare financial management
- Rules: Insurance rules, reimbursement calculations

## Multiple Models for Different Contexts

It's perfectly acceptable (and often necessary) to have different models for the same real-world concept in different contexts. This is called **contextual ambiguity** and is a feature, not a bug.

### Example: "Order" in Different Contexts

```typescript
// Order in Sales Context - Customer-facing order management
namespace SalesContext {
    export class Order {
        constructor(
            public readonly id: string,
            public customer: Customer,
            public items: OrderItem[],
            public status: OrderStatus,
            public shippingAddress: Address,
            public billingAddress: Address,
            public orderDate: Date,
            public discounts: Discount[]
        ) {}

        get totalAmount(): Money {
            const subtotal = this.items.reduce(
                (sum, item) => sum.add(item.subtotal),
                Money.zero("USD")
            );

            const discountAmount = this.discounts.reduce(
                (sum, discount) => sum.add(discount.calculateDiscount(subtotal)),
                Money.zero("USD")
            );

            return subtotal.subtract(discountAmount);
        }

        place(): void {
            if (this.status !== OrderStatus.Draft) {
                throw new Error("Order can only be placed when in draft status");
            }
            if (this.items.length === 0) {
                throw new Error("Order must have at least one item");
            }
            this.status = OrderStatus.Placed;
        }

        cancel(): void {
            if (this.status === OrderStatus.Shipped) {
                throw new Error("Cannot cancel shipped orders");
            }
            this.status = OrderStatus.Cancelled;
        }
    }
}

// Order in Shipping Context - Logistics and delivery focus
namespace ShippingContext {
    export class Order {
        constructor(
            public readonly id: string,
            public shippingAddress: Address,
            public items: ShippingItem[],
            public carrier: string,
            public trackingNumber?: string,
            public estimatedDelivery?: Date,
            public actualDelivery?: Date
        ) {}

        assignCarrier(carrier: string): void {
            if (this.trackingNumber) {
                throw new Error("Cannot change carrier after shipment");
            }
            this.carrier = carrier;
        }

        ship(trackingNumber: string, estimatedDelivery: Date): void {
            if (this.trackingNumber) {
                throw new Error("Order already shipped");
            }
            this.trackingNumber = trackingNumber;
            this.estimatedDelivery = estimatedDelivery;
        }

        deliver(actualDelivery: Date): void {
            if (!this.trackingNumber) {
                throw new Error("Order must be shipped before delivery");
            }
            this.actualDelivery = actualDelivery;
        }

        isDelivered(): boolean {
            return this.actualDelivery !== undefined;
        }

        isOverdue(): boolean {
            if (!this.estimatedDelivery || this.isDelivered()) {
                return false;
            }
            return new Date() > this.estimatedDelivery;
        }
    }
}

// Order in Billing Context - Financial transaction focus
namespace BillingContext {
    export class Order {
        constructor(
            public readonly id: string,
            public customerId: string,
            public amount: Money,
            public paymentMethod: PaymentMethod,
            public invoiceDate: Date,
            public dueDate: Date,
            public payments: Payment[] = []
        ) {}

        get outstandingAmount(): Money {
            const paidAmount = this.payments.reduce(
                (sum, payment) => sum.add(payment.amount),
                Money.zero(this.amount.currency)
            );
            return this.amount.subtract(paidAmount);
        }

        isPaid(): boolean {
            return this.outstandingAmount.amount <= 0;
        }

        isOverdue(): boolean {
            return !this.isPaid() && new Date() > this.dueDate;
        }

        addPayment(payment: Payment): void {
            if (this.isPaid()) {
                throw new Error("Order is already fully paid");
            }
            if (payment.amount.amount > this.outstandingAmount.amount) {
                throw new Error("Payment amount exceeds outstanding amount");
            }
            this.payments.push(payment);
        }
    }
}
```

Each context has its own model of an "Order" with different attributes and behaviors relevant to that context's responsibilities.

## Communication Between Bounded Contexts

### Integration Challenges

When bounded contexts need to communicate, several challenges arise:

- **Different models**: Same concepts modeled differently
- **Different languages**: Same terms mean different things
- **Different priorities**: Different change frequencies and requirements
- **Technical differences**: Different technologies, protocols, data formats
- **Transactional boundaries**: Maintaining consistency across contexts

### Anti-Corruption Layer (ACL)

When integrating contexts, use an anti-corruption layer to:
- **Translate between different models**: Convert data from one context's format to another
- **Protect context integrity**: Prevent one context's changes from breaking another
- **Maintain loose coupling**: Allow contexts to evolve independently
- **Handle protocol differences**: Adapt to different communication styles

```typescript
// Anti-Corruption Layer between Sales and Billing
class SalesToBillingAdapter {
    constructor(private billingService: BillingService) {}

    async createInvoiceFromOrder(order: SalesContext.Order): Promise<string> {
        // Validate that order can be invoiced
        if (order.status !== SalesContext.OrderStatus.Placed) {
            throw new Error("Can only create invoice for placed orders");
        }

        // Transform Sales Order to Billing Order
        const billingOrder = new BillingContext.Order(
            order.id,
            order.customer.id,
            order.totalAmount,
            this.mapPaymentMethod(order.customer.preferredPaymentMethod),
            new Date(),
            new Date(Date.now() + 30 * 24 * 60 * 60 * 1000) // 30 days
        );

        // Create invoice in billing context
        return await this.billingService.createInvoice(billingOrder);
    }

    private mapPaymentMethod(salesMethod: SalesContext.PaymentMethod): BillingContext.PaymentMethod {
        switch (salesMethod) {
            case SalesContext.PaymentMethod.CreditCard:
                return BillingContext.PaymentMethod.CreditCard;
            case SalesContext.PaymentMethod.PayPal:
                return BillingContext.PaymentMethod.PayPal;
            default:
                return BillingContext.PaymentMethod.BankTransfer;
        }
    }
}
```

### Context Mapping Patterns

Common patterns for relating bounded contexts:

#### 1. Shared Kernel
- **When**: Teams have high trust, shared concepts are stable
- **Characteristics**: Shared subset of domain model, both teams can modify
- **Benefits**: Reduces duplication, ensures consistency
- **Risks**: Coupling between teams, coordination overhead

#### 2. Customer-Supplier
- **When**: Clear upstream/downstream relationship
- **Characteristics**: Upstream provides stable interfaces, downstream adapts
- **Benefits**: Clear responsibilities, managed dependencies
- **Risks**: Upstream becomes bottleneck if not responsive

#### 3. Conformist
- **When**: Downstream has less power, upstream model is good
- **Characteristics**: Downstream conforms to upstream's model
- **Benefits**: Minimal translation, simple integration
- **Risks**: Downstream loses autonomy, forced to accept upstream changes

#### 4. Anti-Corruption Layer
- **When**: Contexts have different models, need protection
- **Characteristics**: Translation layer isolates contexts
- **Benefits**: Maintains context integrity, enables independent evolution
- **Risks**: Additional complexity, maintenance overhead

#### 5. Open Host Service
- **When**: Context serves many external consumers
- **Characteristics**: Published protocols, well-documented APIs
- **Benefits**: Enables ecosystem, standardized integration
- **Risks**: API maintenance, versioning challenges

#### 6. Published Language
- **When**: Complex enterprise integration requirements
- **Characteristics**: Common language/schema for integration
- **Benefits**: Standardized communication, enterprise-wide consistency
- **Risks**: Governance overhead, slower evolution

## Identifying Bounded Contexts

### Strategic Analysis

1. **Business Capabilities**: What different business functions exist?
   - Look for different business processes or capabilities
   - Identify areas with different success metrics

2. **Team Boundaries**: Where do different teams work?
   - Existing team structures often indicate context boundaries
   - Consider Conway's Law: organizations design systems that mirror their structure

3. **Existing Systems**: What legacy systems need integration?
   - Legacy system boundaries often become context boundaries
   - Integration points indicate where contexts meet

4. **Change Frequency**: What parts change at different rates?
   - Stable parts vs. frequently changing parts
   - Different business cycles or release cadences

5. **Domain Complexity**: Where is complexity concentrated?
   - Areas with complex business rules
   - Parts requiring deep domain expertise

### Tactical Analysis

1. **Ubiquitous Language**: Where does language break down?
   - Where the same term means different things
   - Where communication becomes confusing

2. **Model Conflicts**: Where do concepts mean different things?
   - Different attributes for same concept
   - Different relationships or behaviors

3. **Integration Points**: Where do systems need to communicate?
   - Data sharing requirements
   - Workflow dependencies

4. **Data Ownership**: Who owns what data?
   - Different data stewardship responsibilities
   - Different privacy or compliance requirements

### Event Storming for Context Discovery

Event Storming is a collaborative workshop technique for discovering bounded contexts:

1. **Gather domain experts and technical team**
2. **Identify domain events** (orange sticky notes)
3. **Find commands** that trigger events (blue)
4. **Add aggregates** that handle commands (yellow)
5. **Identify read models** for queries (green)
6. **Note external systems** (purple)
7. **Group related elements** into bounded contexts
8. **Identify integration relationships** between contexts

## Bounded Contexts and Microservices

Bounded contexts and microservices are closely related but not identical:

### Relationship
- **Bounded contexts** are domain boundaries
- **Microservices** are technical boundaries that often align with bounded contexts
- A bounded context can be implemented as one or more microservices
- Multiple bounded contexts can share a microservice (though not recommended)

### Alignment Benefits
- **Independent deployment**: Contexts can be deployed separately
- **Technology diversity**: Different contexts can use different tech stacks
- **Team autonomy**: Teams can work independently
- **Scalability**: Contexts can be scaled independently

### Implementation Patterns
- **Context per service**: Each microservice implements one bounded context
- **Shared service**: Multiple related contexts in one service (temporary)
- **Context spanning services**: One context split across multiple services

## Code Example: Bounded Context Implementation

Let's implement a comprehensive example with three bounded contexts:

```typescript
// =================== SHARED KERNEL ===================

namespace SharedKernel {
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

        multiply(factor: number): Money {
            return new Money(this.amount * factor, this.currency);
        }

        private ensureSameCurrency(other: Money): void {
            if (this.currency !== other.currency) {
                throw new Error(`Currency mismatch: ${this.currency} vs ${other.currency}`);
            }
        }

        static zero(currency: string = "USD"): Money {
            return new Money(0, currency);
        }
    }

    export class Email extends ValueObject<string> {
        constructor(value: string) {
            super(value);
            if (!this.isValidEmail(value)) {
                throw new Error("Invalid email format");
            }
        }

        private isValidEmail(email: string): boolean {
            const regex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
            return regex.test(email);
        }
    }

    export abstract class DomainEvent {
        public readonly occurredOn: Date = new Date();
        public readonly eventId: string = crypto.randomUUID();
    }
}

// =================== SALES CONTEXT ===================

namespace SalesContext {
    export enum OrderStatus {
        Draft = "draft",
        Placed = "placed",
        Cancelled = "cancelled"
    }

    export enum PaymentMethod {
        CreditCard = "credit_card",
        PayPal = "paypal",
        BankTransfer = "bank_transfer"
    }

    export class Customer {
        constructor(
            public readonly id: string,
            public name: string,
            public email: SharedKernel.Email,
            public preferredPaymentMethod: PaymentMethod
        ) {}
    }

    export class Product {
        constructor(
            public readonly id: string,
            public name: string,
            public price: SharedKernel.Money,
            public inStock: boolean = true
        ) {}
    }

    export class Order {
        private _status: OrderStatus = OrderStatus.Draft;
        private domainEvents: SharedKernel.DomainEvent[] = [];

        constructor(
            public readonly id: string,
            public customer: Customer,
            public items: OrderItem[]
        ) {}

        get status(): OrderStatus {
            return this._status;
        }

        get totalAmount(): SharedKernel.Money {
            return this.items.reduce(
                (total, item) => total.add(item.subtotal),
                SharedKernel.Money.zero("USD")
            );
        }

        addItem(product: Product, quantity: number): void {
            if (this._status !== OrderStatus.Draft) {
                throw new Error("Can only add items to draft orders");
            }

            const existingItem = this.items.find(item => item.product.id === product.id);
            if (existingItem) {
                existingItem.quantity += quantity;
            } else {
                this.items.push(new OrderItem(product, quantity));
            }
        }

        place(): void {
            if (this._status !== OrderStatus.Draft) {
                throw new Error("Order can only be placed when draft");
            }
            if (this.items.length === 0) {
                throw new Error("Order must have at least one item");
            }

            this._status = OrderStatus.Placed;
            this.addDomainEvent(new OrderPlacedEvent(this.id, this.customer.id, this.totalAmount));
        }

        private addDomainEvent(event: SharedKernel.DomainEvent): void {
            this.domainEvents.push(event);
        }

        clearDomainEvents(): SharedKernel.DomainEvent[] {
            const events = [...this.domainEvents];
            this.domainEvents = [];
            return events;
        }
    }

    export class OrderItem {
        constructor(
            public product: Product,
            public quantity: number
        ) {
            if (quantity <= 0) throw new Error("Quantity must be positive");
        }

        get subtotal(): SharedKernel.Money {
            return this.product.price.multiply(this.quantity);
        }
    }

    export class OrderPlacedEvent extends SharedKernel.DomainEvent {
        constructor(
            public readonly orderId: string,
            public readonly customerId: string,
            public readonly totalAmount: SharedKernel.Money
        ) {
            super();
        }
    }
}

// =================== BILLING CONTEXT ===================

namespace BillingContext {
    export enum PaymentMethod {
        CreditCard = "credit_card",
        PayPal = "paypal",
        BankTransfer = "bank_transfer"
    }

    export class Invoice {
        private payments: Payment[] = [];

        constructor(
            public readonly id: string,
            public readonly orderId: string,
            public readonly customerId: string,
            public readonly amount: SharedKernel.Money,
            public readonly dueDate: Date
        ) {}

        get outstandingAmount(): SharedKernel.Money {
            const paidAmount = this.payments.reduce(
                (sum, payment) => sum.add(payment.amount),
                SharedKernel.Money.zero(this.amount.currency)
            );
            return this.amount.subtract(paidAmount);
        }

        isPaid(): boolean {
            return this.outstandingAmount.amount <= 0;
        }

        addPayment(payment: Payment): void {
            if (this.isPaid()) {
                throw new Error("Invoice is already paid");
            }
            this.payments.push(payment);
        }
    }

    export class Payment {
        constructor(
            public readonly id: string,
            public readonly invoiceId: string,
            public readonly amount: SharedKernel.Money,
            public readonly method: PaymentMethod,
            public readonly paidAt: Date = new Date()
        ) {}
    }
}

// =================== SHIPPING CONTEXT ===================

namespace ShippingContext {
    export class Shipment {
        constructor(
            public readonly id: string,
            public readonly orderId: string,
            public readonly address: Address,
            public readonly items: ShipmentItem[],
            public carrier?: string,
            public trackingNumber?: string,
            public estimatedDelivery?: Date,
            public actualDelivery?: Date
        ) {}

        ship(carrier: string, trackingNumber: string, estimatedDelivery: Date): void {
            if (this.trackingNumber) {
                throw new Error("Shipment already processed");
            }
            this.carrier = carrier;
            this.trackingNumber = trackingNumber;
            this.estimatedDelivery = estimatedDelivery;
        }

        deliver(actualDelivery: Date): void {
            if (!this.trackingNumber) {
                throw new Error("Cannot deliver unshipped order");
            }
            this.actualDelivery = actualDelivery;
        }

        isDelivered(): boolean {
            return this.actualDelivery !== undefined;
        }
    }

    export class ShipmentItem {
        constructor(
            public productId: string,
            public productName: string,
            public quantity: number
        ) {}
    }

    export class Address {
        constructor(
            public street: string,
            public city: string,
            public state: string,
            public zipCode: string
        ) {}
    }
}

// =================== ANTI-CORRUPTION LAYER ===================

namespace Integration {
    export class SalesToBillingAdapter {
        static createInvoiceFromOrder(order: SalesContext.Order): BillingContext.Invoice {
            if (order.status !== SalesContext.OrderStatus.Placed) {
                throw new Error("Can only create invoice for placed orders");
            }

            const invoiceId = `INV-${order.id}`;
            const dueDate = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000); // 30 days

            return new BillingContext.Invoice(
                invoiceId,
                order.id,
                order.customer.id,
                order.totalAmount,
                dueDate
            );
        }
    }

    export class SalesToShippingAdapter {
        static createShipmentFromOrder(order: SalesContext.Order, shippingAddress: ShippingContext.Address): ShippingContext.Shipment {
            if (order.status !== SalesContext.OrderStatus.Placed) {
                throw new Error("Can only create shipment for placed orders");
            }

            const shipmentId = `SHIP-${order.id}`;
            const shipmentItems = order.items.map(item =>
                new ShippingContext.ShipmentItem(
                    item.product.id,
                    item.product.name,
                    item.quantity
                )
            );

            return new ShippingContext.Shipment(
                shipmentId,
                order.id,
                shippingAddress,
                shipmentItems
            );
        }
    }
}
```

## Testing Bounded Contexts

### Context Isolation Testing
Each bounded context should be tested independently:

```typescript
describe("SalesContext Order", () => {
    it("should calculate total correctly", () => {
        const product = new SalesContext.Product("p1", "Test Product", new SharedKernel.Money(10, "USD"));
        const customer = new SalesContext.Customer("c1", "John", new SharedKernel.Email("john@test.com"), SalesContext.PaymentMethod.CreditCard);
        const order = new SalesContext.Order("o1", customer, []);

        order.addItem(product, 2);

        expect(order.totalAmount.amount).toBe(20);
        expect(order.totalAmount.currency).toBe("USD");
    });

    it("should not allow placing empty order", () => {
        const customer = new SalesContext.Customer("c1", "John", new SharedKernel.Email("john@test.com"), SalesContext.PaymentMethod.CreditCard);
        const order = new SalesContext.Order("o1", customer, []);

        expect(() => order.place()).toThrow("Order must have at least one item");
    });
});
```

### Integration Testing
Test anti-corruption layers and context communication:

```typescript
describe("SalesToBillingAdapter", () => {
    it("should create invoice from placed order", () => {
        const customer = new SalesContext.Customer("c1", "John", new SharedKernel.Email("john@test.com"), SalesContext.PaymentMethod.CreditCard);
        const product = new SalesContext.Product("p1", "Test Product", new SharedKernel.Money(10, "USD"));
        const order = new SalesContext.Order("o1", customer, [new SalesContext.OrderItem(product, 2)]);

        order.place();

        const invoice = Integration.SalesToBillingAdapter.createInvoiceFromOrder(order);

        expect(invoice.orderId).toBe("o1");
        expect(invoice.customerId).toBe("c1");
        expect(invoice.amount.amount).toBe(20);
    });
});
```

## Evolutionary Bounded Contexts

Bounded contexts are not static; they evolve over time:

### Splitting Contexts
As a context grows, it may need to be split:
- **Trigger**: Context becomes too large or complex
- **Process**: Identify subdomains within the context
- **Migration**: Gradually extract new contexts
- **Integration**: Set up communication between split contexts

### Merging Contexts
Sometimes contexts become too small or closely related:
- **Trigger**: Minimal integration overhead, shared team
- **Process**: Consolidate models and languages
- **Migration**: Merge codebases and update integrations

### Context Refactoring
- **Rename**: Update context names as understanding improves
- **Boundary adjustment**: Move concepts between contexts
- **Model evolution**: Update models within context boundaries

## Common Pitfalls

### 1. Too Many Contexts
- **Problem**: Over-segmenting leads to integration complexity
- **Solution**: Start with fewer contexts, split only when necessary
- **Indicator**: More time spent on integration than domain modeling

### 2. Too Few Contexts
- **Problem**: Under-segmenting leads to bloated, confusing models
- **Solution**: Split when language breaks down or teams conflict
- **Indicator**: Frequent conflicts over term meanings

### 3. Ignoring Integration
- **Problem**: Contexts become isolated silos
- **Solution**: Design integration patterns from the start
- **Indicator**: Business processes can't span context boundaries

### 4. Inconsistent Language
- **Problem**: Same terms mean different things within a context
- **Solution**: Regular refinement of ubiquitous language
- **Indicator**: Confusion during team discussions

### 5. Technical Boundaries as Context Boundaries
- **Problem**: Defining contexts by technology rather than domain
- **Solution**: Let domain drive boundaries, technology follows
- **Indicator**: Context boundaries change when technology changes

## Key Takeaways

1. Bounded contexts define the scope where a domain model applies
2. Different contexts can have different models for the same concepts
3. Clear boundaries enable team autonomy and independent evolution
4. Anti-corruption layers protect context integrity during integration
5. Context mapping patterns guide relationships between contexts
6. Bounded contexts often align with microservice boundaries
7. Contexts evolve over time as understanding improves
8. Test contexts independently, integrations separately

## Next Steps

In Module 4, we'll explore the fundamental building blocks of DDD: entities and value objects, with detailed TypeScript implementations.

## Exercise

Take the system you analyzed in the previous exercise and:

1. Identify 3-5 bounded contexts based on your subdomain analysis
2. For each context, define:
   - The key concepts/entities it contains
   - The primary business capabilities
   - The team that would own it
   - Key business rules or invariants
3. Identify integration points between contexts:
   - What data needs to be shared?
   - What events need to be communicated?
   - What queries span multiple contexts?
4. Choose appropriate integration patterns for each relationship
5. Design anti-corruption layer interfaces for 2 context pairs
6. Sketch how you would test the context boundaries
7. Consider how the contexts might evolve over time

**Bonus Challenges:**
- Conduct an event storming session (even if simulated) to validate your context boundaries
- Design context maps showing ownership and integration relationships
- Consider how microservices would align with your bounded contexts
- Identify potential pitfalls in your context design and how to mitigate them
