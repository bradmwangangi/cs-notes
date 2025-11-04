# Module 4: DDD Building Blocks - Part 1 (Entities & Value Objects)

## Introduction to DDD Building Blocks

DDD provides a set of tactical patterns that help implement domain models. These building blocks capture different aspects of domain concepts and provide consistency in how we model and implement domain logic:

- **Entities**: Objects with identity that change over time
- **Value Objects**: Objects without identity, defined by their attributes
- **Aggregates**: Consistency boundaries around entities and value objects
- **Repositories**: Data access abstractions that maintain domain integrity
- **Domain Services**: Business logic that involves multiple domain objects
- **Application Services**: Use case orchestration and cross-cutting concerns
- **Factories**: Object creation logic with complex construction rules

We'll start with the two most fundamental building blocks: Entities and Value Objects. These form the foundation of rich domain models.

## Entities

### What Are Entities?

Entities are domain objects that have a distinct identity that persists over time, even as their attributes change. They represent things that exist independently in the domain and have a lifecycle.

**Key Characteristics:**
- **Identity**: A unique identifier that distinguishes them from other objects
- **Continuity**: They exist over time and maintain their identity through state changes
- **Mutable State**: Their attributes can change while preserving identity
- **Lifecycle**: Created, modified, and possibly destroyed
- **Behavior**: Rich business methods that express domain operations

### Entity Identity

Identity can be:
- **Surrogate ID**: Database-generated, no business meaning (e.g., UUID, auto-increment)
- **Natural Key**: Business-meaningful identifier (e.g., email, social security number)
- **Composite Key**: Combination of multiple attributes

```typescript
// Different identity strategies
class User {
    constructor(
        private readonly _id: string, // Surrogate ID
        private _email: Email,        // Natural key (unique)
        private _username: string     // Another natural key
    ) {}

    // Identity based on surrogate ID
    equals(other: User): boolean {
        return this._id === other._id;
    }
}

class Product {
    constructor(
        private readonly _sku: string, // Natural key as primary identity
        private _name: string,
        private _price: Money
    ) {}

    // Identity based on business key
    equals(other: Product): boolean {
        return this._sku === other._sku;
    }
}
```

### Entity vs Database Records

**Important Distinction:**
- Entities are domain concepts first, persistence concerns second
- The database schema should serve the domain model, not vice versa
- Entities can exist in memory without being persisted
- Multiple persistence strategies are possible for the same entity

### Entity Design Principles

1. **Encapsulation**: Hide internal state, expose behavior through methods
2. **Validation**: Ensure invariants are maintained through all state changes
3. **Identity Management**: Clear, consistent rules for identity and equality
4. **Business Focus**: Methods should express domain operations, not technical CRUD
5. **Side-Effect Control**: Methods should be deterministic and predictable
6. **Lifecycle Awareness**: Understand when entities can and cannot change

### Example: Customer Entity

```typescript
enum CustomerStatus {
    Active = "active",
    Inactive = "inactive",
    Suspended = "suspended"
}

class Customer {
    constructor(
        private readonly _id: string,
        private _name: string,
        private _email: Email,
        private _status: CustomerStatus = CustomerStatus.Active,
        private _loyaltyPoints: number = 0,
        private readonly _createdAt: Date = new Date()
    ) {
        this.validate();
    }

    private validate(): void {
        if (!this._id?.trim()) {
            throw new DomainError("Customer ID is required");
        }
        if (!this._name?.trim()) {
            throw new DomainError("Customer name is required");
        }
        if (this._loyaltyPoints < 0) {
            throw new DomainError("Loyalty points cannot be negative");
        }
    }

    // Identity and equality
    get id(): string { return this._id; }

    equals(other: Customer): boolean {
        return this._id === other._id;
    }

    // Read-only properties
    get name(): string { return this._name; }
    get email(): Email { return this._email; }
    get status(): CustomerStatus { return this._status; }
    get loyaltyPoints(): number { return this._loyaltyPoints; }
    get createdAt(): Date { return this._createdAt; }

    // Business behavior - name management
    changeName(newName: string): void {
        this.ensureActive();
        if (!newName?.trim()) {
            throw new DomainError("Name cannot be empty");
        }
        this._name = newName.trim();
    }

    // Business behavior - email management
    changeEmail(newEmail: Email): void {
        this.ensureActive();
        // In a real system, you'd check for uniqueness here
        // This would typically involve a domain service
        this._email = newEmail;
    }

    // Business behavior - status management
    activate(): void {
        if (this._status === CustomerStatus.Suspended) {
            throw new DomainError("Suspended customers cannot be reactivated directly");
        }
        this._status = CustomerStatus.Active;
    }

    deactivate(): void {
        this._status = CustomerStatus.Inactive;
    }

    suspend(reason: string): void {
        this._status = CustomerStatus.Suspended;
        // Could publish domain event here
    }

    // Business behavior - loyalty program
    earnLoyaltyPoints(points: number): void {
        this.ensureActive();
        if (points <= 0) {
            throw new DomainError("Points must be positive");
        }
        this._loyaltyPoints += points;
    }

    spendLoyaltyPoints(points: number): void {
        this.ensureActive();
        if (points <= 0) {
            throw new DomainError("Points must be positive");
        }
        if (points > this._loyaltyPoints) {
            throw new DomainError("Insufficient loyalty points");
        }
        this._loyaltyPoints -= points;
    }

    // Business rule enforcement
    private ensureActive(): void {
        if (this._status !== CustomerStatus.Active) {
            throw new DomainError("Operation requires active customer status");
        }
    }

    // Business calculations
    get loyaltyTier(): string {
        if (this._loyaltyPoints >= 10000) return "Platinum";
        if (this._loyaltyPoints >= 5000) return "Gold";
        if (this._loyaltyPoints >= 1000) return "Silver";
        return "Bronze";
    }

    canMakePurchase(amount: Money): boolean {
        // Business rule: customers can have credit limits based on loyalty
        const creditLimit = this.getCreditLimit();
        return amount.amount <= creditLimit;
    }

    private getCreditLimit(): number {
        switch (this.loyaltyTier) {
            case "Platinum": return 10000;
            case "Gold": return 5000;
            case "Silver": return 1000;
            default: return 100;
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

### Entity Lifecycle

Entities have defined states and valid transitions:

```typescript
enum OrderStatus {
    Draft = "draft",
    Confirmed = "confirmed",
    Paid = "paid",
    Preparing = "preparing",
    Shipped = "shipped",
    Delivered = "delivered",
    Cancelled = "cancelled",
    Returned = "returned"
}

class Order {
    constructor(
        private readonly _id: string,
        private _status: OrderStatus = OrderStatus.Draft,
        // ... other properties
    ) {}

    // Valid state transitions
    confirm(): void {
        if (this._status !== OrderStatus.Draft) {
            throw new DomainError("Only draft orders can be confirmed");
        }
        this._status = OrderStatus.Confirmed;
    }

    pay(): void {
        if (this._status !== OrderStatus.Confirmed) {
            throw new DomainError("Only confirmed orders can be paid");
        }
        this._status = OrderStatus.Paid;
    }

    prepare(): void {
        if (this._status !== OrderStatus.Paid) {
            throw new DomainError("Only paid orders can be prepared");
        }
        this._status = OrderStatus.Preparing;
    }

    ship(): void {
        if (this._status !== OrderStatus.Preparing) {
            throw new DomainError("Only prepared orders can be shipped");
        }
        this._status = OrderStatus.Shipped;
    }

    deliver(): void {
        if (this._status !== OrderStatus.Shipped) {
            throw new DomainError("Only shipped orders can be delivered");
        }
        this._status = OrderStatus.Delivered;
    }

    cancel(): void {
        if ([OrderStatus.Delivered, OrderStatus.Returned].includes(this._status)) {
            throw new DomainError("Delivered or returned orders cannot be cancelled");
        }
        this._status = OrderStatus.Cancelled;
    }

    return(): void {
        if (this._status !== OrderStatus.Delivered) {
            throw new DomainError("Only delivered orders can be returned");
        }
        this._status = OrderStatus.Returned;
    }
}
```

## Value Objects

### What Are Value Objects?

Value objects are immutable objects that represent descriptive aspects of the domain. They have no identity separate from their attributes and are defined entirely by their values.

**Key Characteristics:**
- **No Identity**: Equality based on attribute values, not separate ID
- **Immutability**: Cannot be modified after creation
- **Self-Validating**: Validate invariants during construction
- **Side-Effect Free**: Operations return new instances
- **Replaceable**: Can be replaced with another instance of the same value

### When to Use Value Objects

Use value objects for:
- **Measurements**: Money, Weight, Distance, Temperature, Duration
- **Quantities**: Count, Percentage, Rate, Ratio
- **Ranges**: DateRange, PriceRange, AgeRange
- **Descriptions**: Address, Name, PhoneNumber, Email
- **Codes**: ProductCode, PostalCode, CurrencyCode, CountryCode
- **Coordinates**: Geographic coordinates, screen coordinates
- **Schedules**: Time periods, recurring patterns

### Value Object Design Principles

1. **Immutability**: No setters, all properties readonly
2. **Validation**: Strict validation in constructor
3. **Value Equality**: Equality based on all significant attributes
4. **Self-Contained**: No external dependencies for validation
5. **Behavior**: Rich behavior through methods that return new instances
6. **Small Scope**: Focus on single responsibility

### Example: Money Value Object

```typescript
class Money {
    constructor(
        private readonly _amount: number,
        private readonly _currency: string
    ) {
        this.validate();
    }

    private validate(): void {
        if (!Number.isFinite(this._amount)) {
            throw new DomainError("Amount must be a valid number");
        }
        if (this._amount < 0) {
            throw new DomainError("Amount cannot be negative");
        }
        if (!this._currency || this._currency.length !== 3) {
            throw new DomainError("Currency must be a 3-letter ISO code");
        }
        if (!/^[A-Z]{3}$/.test(this._currency)) {
            throw new DomainError("Currency must be uppercase letters only");
        }
    }

    get amount(): number { return this._amount; }
    get currency(): string { return this._currency; }

    // Value-based equality
    equals(other: Money): boolean {
        return this._amount === other._amount && this._currency === other._currency;
    }

    // Arithmetic operations return new instances
    add(other: Money): Money {
        this.ensureSameCurrency(other);
        return new Money(this._amount + other._amount, this._currency);
    }

    subtract(other: Money): Money {
        this.ensureSameCurrency(other);
        const result = this._amount - other._amount;
        if (result < 0) {
            throw new DomainError("Cannot subtract: result would be negative");
        }
        return new Money(result, this._currency);
    }

    multiply(factor: number): Money {
        if (!Number.isFinite(factor)) {
            throw new DomainError("Factor must be a valid number");
        }
        return new Money(this._amount * factor, this._currency);
    }

    divide(divisor: number): Money {
        if (!Number.isFinite(divisor) || divisor === 0) {
            throw new DomainError("Divisor must be a non-zero number");
        }
        return new Money(this._amount / divisor, this._currency);
    }

    // Comparison operations
    isGreaterThan(other: Money): boolean {
        this.ensureSameCurrency(other);
        return this._amount > other._amount;
    }

    isLessThan(other: Money): boolean {
        this.ensureSameCurrency(other);
        return this._amount < other._amount;
    }

    // Formatting
    toString(): string {
        return `${this._currency} ${this._amount.toFixed(2)}`;
    }

    toJSON(): { amount: number; currency: string } {
        return { amount: this._amount, currency: this._currency };
    }

    // Factory methods
    static zero(currency: string = "USD"): Money {
        return new Money(0, currency);
    }

    static fromJSON(data: { amount: number; currency: string }): Money {
        return new Money(data.amount, data.currency);
    }

    private ensureSameCurrency(other: Money): void {
        if (this._currency !== other._currency) {
            throw new DomainError(`Cannot operate on different currencies: ${this._currency} vs ${other._currency}`);
        }
    }
}
```

### Example: Address Value Object

```typescript
class Address {
    constructor(
        private readonly _street: string,
        private readonly _city: string,
        private readonly _state: string,
        private readonly _postalCode: string,
        private readonly _country: string = "USA"
    ) {
        this.validate();
    }

    private validate(): void {
        if (!this._street?.trim()) {
            throw new DomainError("Street address is required");
        }
        if (!this._city?.trim()) {
            throw new DomainError("City is required");
        }
        if (!this._state?.trim()) {
            throw new DomainError("State is required");
        }
        if (!this._postalCode?.trim()) {
            throw new DomainError("Postal code is required");
        }
        if (!this._country?.trim()) {
            throw new DomainError("Country is required");
        }

        // Validate postal code format for US
        if (this._country === "USA" && !/^\d{5}(-\d{4})?$/.test(this._postalCode)) {
            throw new DomainError("Invalid US postal code format");
        }
    }

    get street(): string { return this._street; }
    get city(): string { return this._city; }
    get state(): string { return this._state; }
    get postalCode(): string { return this._postalCode; }
    get country(): string { return this._country; }

    // Value equality - all attributes must match
    equals(other: Address): boolean {
        return (
            this._street === other._street &&
            this._city === other._city &&
            this._state === other._state &&
            this._postalCode === other._postalCode &&
            this._country === other._country
        );
    }

    // Computed properties
    get fullAddress(): string {
        return `${this._street}, ${this._city}, ${this._state} ${this._postalCode}, ${this._country}`;
    }

    get isUSAddress(): boolean {
        return this._country === "USA";
    }

    // Modification operations return new instances
    changeStreet(newStreet: string): Address {
        return new Address(newStreet, this._city, this._state, this._postalCode, this._country);
    }

    changeCity(newCity: string): Address {
        return new Address(this._street, newCity, this._state, this._postalCode, this._country);
    }

    // Factory methods for common cases
    static createUSAddress(street: string, city: string, state: string, zipCode: string): Address {
        return new Address(street, city, state, zipCode, "USA");
    }

    static createCanadianAddress(street: string, city: string, province: string, postalCode: string): Address {
        return new Address(street, city, province, postalCode, "CANADA");
    }

    // Parsing from formatted strings
    static parseUSAddress(fullAddress: string): Address {
        // Simple parsing logic - in real app, use proper address parsing library
        const parts = fullAddress.split(',');
        if (parts.length < 4) {
            throw new DomainError("Invalid address format");
        }

        const street = parts[0].trim();
        const city = parts[1].trim();
        const stateZip = parts[2].trim().split(' ');
        const state = stateZip[0];
        const zipCode = stateZip.slice(1).join(' ');
        const country = parts[3]?.trim() || "USA";

        return new Address(street, city, state, zipCode, country);
    }
}
```

### Advanced Value Object Patterns

#### Value Object with Validation Rules

```typescript
class Email {
    private readonly _value: string;

    constructor(value: string) {
        this._value = value.trim().toLowerCase();
        this.validate();
    }

    private validate(): void {
        if (!this._value) {
            throw new DomainError("Email is required");
        }

        const emailRegex = /^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$/;

        if (!emailRegex.test(this._value)) {
            throw new DomainError("Invalid email format");
        }

        // Additional business rules
        if (this._value.length > 254) {
            throw new DomainError("Email is too long");
        }
    }

    get value(): string { return this._value; }

    get domain(): string {
        return this._value.split('@')[1];
    }

    get isCorporate(): boolean {
        const corporateDomains = ['company.com', 'business.org'];
        return corporateDomains.includes(this.domain);
    }

    equals(other: Email): boolean {
        return this._value === other._value;
    }

    toString(): string {
        return this._value;
    }
}
```

#### Value Object with Business Logic

```typescript
class DateRange {
    constructor(
        private readonly _start: Date,
        private readonly _end: Date
    ) {
        this.validate();
    }

    private validate(): void {
        if (this._start >= this._end) {
            throw new DomainError("Start date must be before end date");
        }

        const maxDuration = 365 * 24 * 60 * 60 * 1000; // 1 year
        if (this._end.getTime() - this._start.getTime() > maxDuration) {
            throw new DomainError("Date range cannot exceed one year");
        }
    }

    get start(): Date { return new Date(this._start); }
    get end(): Date { return new Date(this._end); }
    get durationInDays(): number {
        return Math.ceil((this._end.getTime() - this._start.getTime()) / (24 * 60 * 60 * 1000));
    }

    equals(other: DateRange): boolean {
        return this._start.getTime() === other._start.getTime() &&
               this._end.getTime() === other._end.getTime();
    }

    contains(date: Date): boolean {
        return date >= this._start && date <= this._end;
    }

    overlaps(other: DateRange): boolean {
        return this._start <= other._end && this._end >= other._start;
    }

    extendDays(days: number): DateRange {
        const newEnd = new Date(this._end);
        newEnd.setDate(newEnd.getDate() + days);
        return new DateRange(this._start, newEnd);
    }

    static forMonth(year: number, month: number): DateRange {
        const start = new Date(year, month - 1, 1);
        const end = new Date(year, month, 0); // Last day of month
        return new DateRange(start, end);
    }

    static nextWeek(): DateRange {
        const today = new Date();
        const start = new Date(today);
        start.setDate(today.getDate() - today.getDay() + 1); // Monday

        const end = new Date(start);
        end.setDate(start.getDate() + 6); // Sunday

        return new DateRange(start, end);
    }
}
```

## Entities vs Value Objects: Decision Guide

### Use Entity When:
- You need to track the object's identity over time
- The object has a lifecycle with state changes
- You need to distinguish between different instances even if attributes are identical
- The object represents something that exists independently in the domain
- The object has complex behavior that changes its state
- You need to maintain historical records of changes

### Use Value Object When:
- You care about the attributes/values, not the object's identity
- The object represents a value, measurement, or description
- Immutability makes sense (money, addresses, dates)
- Two instances with identical values are completely interchangeable
- The object doesn't have its own lifecycle or state changes
- The object is small and focused on a single responsibility
- Equality should be based on value, not identity

### Decision Framework

Ask these questions:

1. **Does it have identity?** If yes → Entity
2. **Does it change over time?** If yes → Entity
3. **Can it be replaced with another identical instance?** If yes → Value Object
4. **Is it defined entirely by its attributes?** If yes → Value Object
5. **Is it immutable in the domain?** If yes → Value Object

## Base Classes and Infrastructure

### Abstract Base Classes

```typescript
// Base Entity class
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

// Base Value Object class
abstract class ValueObject {
    abstract equals(other: ValueObject): boolean;
}

// Usage
class Customer extends Entity<string> {
    constructor(
        id: string,
        private _name: string,
        private _email: Email
    ) {
        super(id);
    }

    equals(other: Entity<string>): boolean {
        return this.id === other.id;
    }
}

class Money extends ValueObject {
    constructor(
        private readonly _amount: number,
        private readonly _currency: string
    ) {
        super();
    }

    equals(other: ValueObject): boolean {
        if (!(other instanceof Money)) return false;
        return this._amount === other._amount && this._currency === other._currency;
    }
}
```

## Putting It Together: A Complete Example

```typescript
// Domain model for an e-commerce order system
enum OrderStatus {
    Draft = "draft",
    Placed = "placed",
    Paid = "paid",
    Preparing = "preparing",
    Shipped = "shipped",
    Delivered = "delivered",
    Cancelled = "cancelled"
}

class Order extends Entity<string> {
    constructor(
        id: string,
        private _customerId: string,
        private _items: OrderItem[] = [],
        private _status: OrderStatus = OrderStatus.Draft,
        private _shippingAddress?: Address,
        private _billingAddress?: Address,
        private readonly _createdAt: Date = new Date()
    ) {
        super(id);
        this.validateInvariants();
    }

    get customerId(): string { return this._customerId; }
    get items(): readonly OrderItem[] { return [...this._items]; }
    get status(): OrderStatus { return this._status; }
    get shippingAddress(): Address | undefined { return this._shippingAddress; }
    get billingAddress(): Address | undefined { return this._billingAddress; }
    get createdAt(): Date { return this._createdAt; }

    get totalAmount(): Money {
        return this._items.reduce(
            (total, item) => total.add(item.subtotal),
            Money.zero("USD")
        );
    }

    get itemCount(): number {
        return this._items.reduce((count, item) => count + item.quantity, 0);
    }

    // Business behavior
    addItem(product: Product, quantity: number): void {
        this.ensureCanModify();

        if (!product.isAvailable) {
            throw new DomainError(`Product ${product.name} is not available`);
        }

        if (quantity <= 0) {
            throw new DomainError("Quantity must be positive");
        }

        const existingItem = this._items.find(item =>
            item.productId === product.id
        );

        if (existingItem) {
            existingItem.increaseQuantity(quantity);
        } else {
            this._items.push(new OrderItem(product.id, product.name, product.price, quantity));
        }

        this.validateInvariants();
    }

    removeItem(productId: string): void {
        this.ensureCanModify();

        const index = this._items.findIndex(item => item.productId === productId);
        if (index === -1) {
            throw new DomainError("Product not found in order");
        }

        this._items.splice(index, 1);
        this.validateInvariants();
    }

    setShippingAddress(address: Address): void {
        this.ensureCanModify();
        this._shippingAddress = address;
    }

    setBillingAddress(address: Address): void {
        this.ensureCanModify();
        this._billingAddress = address;
    }

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

        this._status = OrderStatus.Placed;
    }

    markAsPaid(): void {
        if (this._status !== OrderStatus.Placed) {
            throw new DomainError("Only placed orders can be paid");
        }
        this._status = OrderStatus.Paid;
    }

    ship(): void {
        if (this._status !== OrderStatus.Paid) {
            throw new DomainError("Only paid orders can be shipped");
        }
        this._status = OrderStatus.Shipped;
    }

    deliver(): void {
        if (this._status !== OrderStatus.Shipped) {
            throw new DomainError("Only shipped orders can be delivered");
        }
        this._status = OrderStatus.Delivered;
    }

    cancel(): void {
        if ([OrderStatus.Shipped, OrderStatus.Delivered].includes(this._status)) {
            throw new DomainError("Order cannot be cancelled at this stage");
        }
        this._status = OrderStatus.Cancelled;
    }

    // Invariant validation
    private validateInvariants(): void {
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
    }

    private ensureCanModify(): void {
        if (this._status !== OrderStatus.Draft) {
            throw new DomainError("Order cannot be modified");
        }
    }

    equals(other: Entity<string>): boolean {
        return this.id === other.id;
    }
}

class OrderItem {
    constructor(
        private readonly _productId: string,
        private readonly _productName: string,
        private readonly _unitPrice: Money,
        private _quantity: number
    ) {
        if (_quantity <= 0) {
            throw new DomainError("Quantity must be positive");
        }
    }

    get productId(): string { return this._productId; }
    get productName(): string { return this._productName; }
    get unitPrice(): Money { return this._unitPrice; }
    get quantity(): number { return this._quantity; }

    get subtotal(): Money {
        return this._unitPrice.multiply(this._quantity);
    }

    increaseQuantity(amount: number): void {
        if (amount <= 0) {
            throw new DomainError("Amount must be positive");
        }
        this._quantity += amount;
    }

    decreaseQuantity(amount: number): void {
        if (amount <= 0) {
            throw new DomainError("Amount must be positive");
        }
        if (this._quantity - amount <= 0) {
            throw new DomainError("Cannot reduce quantity below 1");
        }
        this._quantity -= amount;
    }
}

class Product extends Entity<string> {
    constructor(
        id: string,
        private _name: string,
        private _price: Money,
        private _isAvailable: boolean = true,
        private _stockQuantity: number = 0
    ) {
        super(id);
        this.validate();
    }

    private validate(): void {
        if (!this._name?.trim()) {
            throw new DomainError("Product name is required");
        }
        if (this._stockQuantity < 0) {
            throw new DomainError("Stock quantity cannot be negative");
        }
    }

    get name(): string { return this._name; }
    get price(): Money { return this._price; }
    get isAvailable(): boolean { return this._isAvailable && this._stockQuantity > 0; }
    get stockQuantity(): number { return this._stockQuantity; }

    updateStock(quantity: number): void {
        if (quantity < 0) {
            throw new DomainError("Stock quantity cannot be negative");
        }
        this._stockQuantity = quantity;
    }

    changePrice(newPrice: Money): void {
        if (newPrice.amount <= 0) {
            throw new DomainError("Price must be positive");
        }
        this._price = newPrice;
    }

    discontinue(): void {
        this._isAvailable = false;
    }

    equals(other: Entity<string>): boolean {
        return this.id === other.id;
    }
}

// Usage example
const customer = new Customer("cust-123", "John Doe", new Email("john@example.com"));
const product = new Product("prod-456", "Wireless Headphones", new Money(199.99, "USD"));
product.updateStock(10);

const address = Address.createUSAddress("123 Main St", "Anytown", "CA", "12345");

const order = new Order("order-789", customer.id);
order.addItem(product, 2);
order.setShippingAddress(address);
order.place();

console.log(`Order ${order.id} placed for ${order.totalAmount.toString()}`);
```

## Testing Entities and Value Objects

### Entity Testing

```typescript
describe("Customer", () => {
    it("should create valid customer", () => {
        const email = new Email("john@example.com");
        const customer = new Customer("123", "John Doe", email);

        expect(customer.id).toBe("123");
        expect(customer.name).toBe("John Doe");
        expect(customer.email).toBe(email);
        expect(customer.status).toBe(CustomerStatus.Active);
    });

    it("should not create customer with invalid data", () => {
        expect(() => new Customer("", "John Doe", new Email("john@example.com")))
            .toThrow("Customer ID is required");
    });

    it("should change name when active", () => {
        const customer = new Customer("123", "John Doe", new Email("john@example.com"));
        customer.changeName("Jane Doe");
        expect(customer.name).toBe("Jane Doe");
    });

    it("should not change name when inactive", () => {
        const customer = new Customer("123", "John Doe", new Email("john@example.com"));
        customer.deactivate();

        expect(() => customer.changeName("Jane Doe"))
            .toThrow("Operation requires active customer status");
    });

    it("should earn loyalty points", () => {
        const customer = new Customer("123", "John Doe", new Email("john@example.com"));
        customer.earnLoyaltyPoints(100);
        expect(customer.loyaltyPoints).toBe(100);
        expect(customer.loyaltyTier).toBe("Bronze");
    });

    it("should have correct tier for high points", () => {
        const customer = new Customer("123", "John Doe", new Email("john@example.com"));
        customer.earnLoyaltyPoints(15000);
        expect(customer.loyaltyTier).toBe("Platinum");
    });
});
```

### Value Object Testing

```typescript
describe("Money", () => {
    it("should create valid money", () => {
        const money = new Money(100.50, "USD");
        expect(money.amount).toBe(100.50);
        expect(money.currency).toBe("USD");
    });

    it("should not create money with invalid currency", () => {
        expect(() => new Money(100, "usd")).toThrow("Currency must be uppercase letters only");
        expect(() => new Money(100, "US")).toThrow("Currency must be a 3-letter ISO code");
    });

    it("should add money correctly", () => {
        const money1 = new Money(100, "USD");
        const money2 = new Money(50, "USD");
        const result = money1.add(money2);

        expect(result.amount).toBe(150);
        expect(result.currency).toBe("USD");
        expect(money1.amount).toBe(100); // Original unchanged
    });

    it("should not add different currencies", () => {
        const usd = new Money(100, "USD");
        const eur = new Money(50, "EUR");

        expect(() => usd.add(eur)).toThrow("Cannot operate on different currencies");
    });

    it("should format correctly", () => {
        const money = new Money(1234.56, "USD");
        expect(money.toString()).toBe("USD 1234.56");
    });
});

describe("Address", () => {
    it("should create valid address", () => {
        const address = new Address("123 Main St", "Anytown", "CA", "12345", "USA");
        expect(address.street).toBe("123 Main St");
        expect(address.city).toBe("Anytown");
        expect(address.fullAddress).toBe("123 Main St, Anytown, CA 12345, USA");
    });

    it("should validate US postal code", () => {
        expect(() => new Address("123 Main St", "Anytown", "CA", "123456", "USA"))
            .toThrow("Invalid US postal code format");
    });

    it("should be equal when all fields match", () => {
        const address1 = new Address("123 Main St", "Anytown", "CA", "12345", "USA");
        const address2 = new Address("123 Main St", "Anytown", "CA", "12345", "USA");
        const address3 = new Address("456 Oak St", "Anytown", "CA", "12345", "USA");

        expect(address1.equals(address2)).toBe(true);
        expect(address1.equals(address3)).toBe(false);
    });

    it("should create new instance when modified", () => {
        const address = new Address("123 Main St", "Anytown", "CA", "12345", "USA");
        const newAddress = address.changeStreet("456 Oak St");

        expect(address.street).toBe("123 Main St");
        expect(newAddress.street).toBe("456 Oak St");
        expect(address.city).toBe(newAddress.city); // Other fields unchanged
    });
});
```

## Common Pitfalls

### Entity Pitfalls

1. **Anemic Entities**: Entities with only getters/setters, no business logic
2. **Identity Confusion**: Using wrong attributes for identity
3. **Validation Scattered**: Validation logic spread across layers
4. **Mutable Identity**: Allowing identity changes (dangerous!)
5. **God Entities**: Entities trying to do too much

### Value Object Pitfalls

1. **Mutable Value Objects**: Allowing state changes
2. **Missing Validation**: Not validating in constructor
3. **Large Value Objects**: Trying to make everything a value object
4. **Primitive Obsession**: Using primitives instead of value objects
5. **Inconsistent Equality**: Not implementing proper equality

### General Pitfalls

1. **Technical Thinking**: Designing for database instead of domain
2. **Inconsistent Naming**: Mixing different naming conventions
3. **Missing Invariants**: Not enforcing business rules
4. **Over-Engineering**: Making simple things complex

## Performance Considerations

### Entity Performance
- **Lazy Loading**: Load related entities on demand
- **Snapshot Pattern**: Cache entity state for change detection
- **Event Sourcing**: Store changes instead of current state
- **CQRS**: Separate read and write models

### Value Object Performance
- **Flyweight Pattern**: Reuse common value object instances
- **Immutability Benefits**: Safe sharing between threads
- **Memory Efficiency**: Small objects, can be pooled
- **Serialization**: Easy to cache and transfer

## Key Takeaways

1. **Entities** have identity and mutable state; they represent domain concepts that change over time
2. **Value Objects** are immutable and defined by their attributes; they represent values or descriptions
3. Both should validate their invariants and express domain concepts through behavior
4. Choose the right building block based on whether identity and mutability matter
5. Entities focus on lifecycle and state changes; value objects focus on data and behavior
6. Proper encapsulation and validation ensure domain integrity
7. Test entities and value objects thoroughly to maintain domain rules

## Next Steps

In Module 5, we'll explore Aggregates and Repositories - patterns for managing consistency boundaries and data access in domain models.

## Exercise

1. **Identify entities and value objects** in a domain you're familiar with. Create a list of 5-7 entities and 5-7 value objects.

2. **Implement value objects** with proper validation:
   - Create a `PhoneNumber` value object with country code support
   - Create a `CreditCard` value object with validation and formatting
   - Create a `DateTimeRange` value object for scheduling

3. **Implement an entity** that uses these value objects:
   - Create a `Reservation` entity for a restaurant booking system
   - Include proper validation, business rules, and state management
   - Ensure the entity maintains its invariants

4. **Add business methods** that demonstrate domain behavior:
   - Implement reservation confirmation, modification, and cancellation
   - Add business rules like maximum party size, advance booking requirements
   - Include time-based validation (e.g., can't book in the past)

5. **Write comprehensive tests** for your entities and value objects:
   - Test validation rules and error conditions
   - Test business logic and state transitions
   - Test equality and immutability

6. **Consider edge cases**:
   - What happens with invalid data?
   - How do you handle concurrent modifications?
   - What are the performance implications of your design?

**Bonus:** Implement a simple repository interface for your entity and create an in-memory implementation.
