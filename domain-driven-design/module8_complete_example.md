# Module 8: Complete Example Application

## Introduction

This module brings together all the DDD concepts we've learned by building a complete e-commerce application. We'll implement:

- **Bounded Contexts**: Product Catalog, Order Management, Customer Management, Shipping, Payment
- **Domain Models**: Entities, value objects, aggregates with proper encapsulation
- **Services**: Domain and application services with cross-cutting concerns
- **Integration**: Anti-corruption layers, shared kernel, and event-driven communication
- **Infrastructure**: Repository implementations, event publishing, and external integrations
- **Architecture**: Clean architecture with dependency inversion
- **Testing**: Comprehensive unit, integration, and contract testing
- **Deployment**: Containerization and orchestration considerations

The example demonstrates real-world DDD implementation with TypeScript, following clean architecture principles.

## System Overview

### Bounded Contexts

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│ Product Catalog │◯──◯│ Order Management│◯──◯│Customer Mgmt    │    │   Shipping      │
│                 │    │                 │    │                 │    │                 │
│ - Products      │    │ - Orders        │    │ - Customers     │    │ - Shipments     │
│ - Categories    │    │ - Order Items   │    │ - Addresses     │    │ - Tracking      │
│ - Pricing       │    │ - Payments      │    │ - Preferences   │    │ - Carriers      │
└─────────────────┘    └─────────────────┘    └─────────────────┘    └─────────────────┘
                                                                 │
                                                                 │
┌─────────────────┐    ┌─────────────────┐                       │
│   Payment       │    │  Integration   │◯──────────────────────┘
│   Processing    │    │   Events       │
│                 │    │                │
│ - Transactions  │    │ - Publishers   │
│ - Methods       │    │ - Handlers     │
│ - Refunds       │    │ - Queues       │
└─────────────────┘    └─────────────────┘
```

### Context Relationships

- **Product Catalog → Order Management**: Anti-Corruption Layer (different product models)
- **Customer Management → Order Management**: Shared Kernel (common customer concepts)
- **Order Management → Shipping**: Anti-Corruption Layer (order to shipment transformation)
- **Order Management → Payment Processing**: Open Host Service (payment provider API)
- **All Contexts → Integration Events**: Published Language (event-driven communication)

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                     Application Layer                       │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────┐  │
│  │ Application     │  │ Application     │  │ Application │  │
│  │ Services        │  │ Services        │  │ Services    │  │
│  └─────────────────┘  └─────────────────┘  └─────────────┘  │
└─────────────────────────────────────────────────────────────┘
                                   │
┌─────────────────────────────────────────────────────────────┐
│                      Domain Layer                           │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────┐  │
│  │ Domain Services │  │ Entities &      │  │ Value       │  │
│  │                 │  │ Aggregates      │  │ Objects     │  │
│  └─────────────────┘  └─────────────────┘  └─────────────┘  │
└─────────────────────────────────────────────────────────────┘
                                   │
┌─────────────────────────────────────────────────────────────┐
│                   Infrastructure Layer                      │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────┐  │
│  │ Repositories    │  │ Event           │  │ External    │  │
│  │                 │  │ Publishers      │  │ Services    │  │
│  └─────────────────┘  └─────────────────┘  └─────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

## Shared Kernel

Let's start with the shared kernel used across multiple contexts:

```typescript
// shared-kernel/index.ts
export namespace SharedKernel {

    // Base classes and interfaces
    export abstract class ValueObject<T = any> {
        protected constructor(protected readonly _value: T) {}

        equals(other: ValueObject<T>): boolean {
            if (!other || !(other instanceof ValueObject)) return false;
            return JSON.stringify(this._value) === JSON.stringify(other._value);
        }

        toString(): string {
            return JSON.stringify(this._value);
        }

        protected get value(): T {
            return this._value;
        }
    }

    export abstract class Entity<T = string> {
        protected readonly _id: T;
        protected _version: number = 0;

        constructor(id: T) {
            this._id = id;
        }

        get id(): T {
            return this._id;
        }

        get version(): number {
            return this._version;
        }

        protected incrementVersion(): void {
            this._version++;
        }

        abstract equals(other: Entity<T>): boolean;
    }

    export abstract class DomainEvent {
        public readonly occurredOn: Date = new Date();
        public readonly eventId: string = crypto.randomUUID();
        public readonly eventVersion: number = 1;

        constructor(public readonly aggregateId: string) {}
    }

    export abstract class AggregateRoot<T = string> extends Entity<T> {
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

    // Value Objects
    export class Money extends ValueObject<{amount: number, currency: string}> {
        constructor(amount: number, currency: string = "USD") {
            super({amount, currency});
            this.validate();
        }

        private validate(): void {
            if (!Number.isFinite(this._value.amount) || this._value.amount < 0) {
                throw new DomainError("Amount must be a non-negative number");
            }
            if (!this._value.currency || this._value.currency.length !== 3) {
                throw new DomainError("Currency must be a 3-letter ISO code");
            }
        }

        get amount(): number { return this._value.amount; }
        get currency(): string { return this._value.currency; }

        add(other: Money): Money {
            this.ensureSameCurrency(other);
            return new Money(this.amount + other.amount, this.currency);
        }

        subtract(other: Money): Money {
            this.ensureSameCurrency(other);
            const result = this.amount - other.amount;
            if (result < 0) {
                throw new DomainError("Cannot subtract: result would be negative");
            }
            return new Money(result, this.currency);
        }

        multiply(factor: number): Money {
            if (!Number.isFinite(factor)) {
                throw new DomainError("Factor must be a valid number");
            }
            return new Money(this.amount * factor, this.currency);
        }

        isGreaterThan(other: Money): boolean {
            this.ensureSameCurrency(other);
            return this.amount > other.amount;
        }

        private ensureSameCurrency(other: Money): void {
            if (this.currency !== other.currency) {
                throw new DomainError(`Cannot operate on different currencies: ${this.currency} vs ${other.currency}`);
            }
        }

        static zero(currency: string = "USD"): Money {
            return new Money(0, currency);
        }
    }

    export class Email extends ValueObject<string> {
        constructor(value: string) {
            super(value.trim().toLowerCase());
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
        }

        get value(): string { return this._value; }
        get domain(): string { return this._value.split('@')[1]; }

        equals(other: Email): boolean {
            return this._value === other._value;
        }
    }

    export class Address extends ValueObject<{
        street: string,
        city: string,
        state: string,
        zipCode: string,
        country: string
    }> {
        constructor(street: string, city: string, state: string, zipCode: string, country: string = "USA") {
            super({street, city, state, zipCode, country});
            this.validate();
        }

        private validate(): void {
            const v = this._value;
            if (!v.street?.trim()) throw new DomainError("Street is required");
            if (!v.city?.trim()) throw new DomainError("City is required");
            if (!v.state?.trim()) throw new DomainError("State is required");
            if (!v.zipCode?.trim()) throw new DomainError("Zip code is required");
            if (!v.country?.trim()) throw new DomainError("Country is required");
        }

        get street(): string { return this._value.street; }
        get city(): string { return this._value.city; }
        get state(): string { return this._value.state; }
        get postalCode(): string { return this._value.zipCode; }
        get country(): string { return this._value.country; }

        get fullAddress(): string {
            return `${this._value.street}, ${this._value.city}, ${this._value.state} ${this._value.zipCode}, ${this._value.country}`;
        }

        equals(other: Address): boolean {
            if (!(other instanceof Address)) return false;
            return (
                this._value.street === other._value.street &&
                this._value.city === other._value.city &&
                this._value.state === other._value.state &&
                this._value.zipCode === other._value.zipCode &&
                this._value.country === other._value.country
            );
        }

        static createUSAddress(street: string, city: string, state: string, zipCode: string): Address {
            return new Address(street, city, state, zipCode, "USA");
        }
    }

    // Identity Types
    export class CustomerId extends ValueObject<string> {
        constructor(value: string) {
            super(value);
            if (!value?.trim()) throw new DomainError("Customer ID is required");
        }
    }

    export class ProductId extends ValueObject<string> {
        constructor(value: string) {
            super(value);
            if (!value?.trim()) throw new DomainError("Product ID is required");
        }
    }

    export class OrderId extends ValueObject<string> {
        constructor(value: string) {
            super(value);
            if (!value?.trim()) throw new DomainError("Order ID is required");
        }
    }

    // Enums
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

    // Common Interfaces
    export interface CustomerSummary {
        id: CustomerId;
        name: string;
        email: Email;
        status: CustomerStatus;
        loyaltyTier: LoyaltyTier;
    }

    export interface DomainEventPublisher {
        publish(event: DomainEvent): Promise<void>;
        publishEvents(events: DomainEvent[]): Promise<void>;
    }

    export interface UnitOfWork {
        begin(): Promise<void>;
        commit(): Promise<void>;
        rollback(): Promise<void>;
        getRepository<T extends AggregateRoot>(entityClass: new (...args: any[]) => T): Repository<T>;
    }

    export interface Repository<T extends AggregateRoot> {
        save(entity: T): Promise<void>;
        findById(id: string): Promise<T | null>;
        exists(id: string): Promise<boolean>;
        nextIdentity(): string;
    }

    // Error types
    export class DomainError extends Error {
        constructor(message: string) {
            super(message);
            this.name = "DomainError";
        }
    }

    export class NotFoundError extends DomainError {
        constructor(resource: string, id: string) {
            super(`${resource} with id ${id} not found`);
            this.name = "NotFoundError";
        }
    }

    export class ValidationError extends DomainError {
        constructor(message: string, public readonly field?: string) {
            super(message);
            this.name = "ValidationError";
        }
    }

    export class ConcurrencyError extends DomainError {
        constructor(message: string) {
            super(message);
            this.name = "ConcurrencyError";
        }
    }
}
```

## Product Catalog Context

```typescript
// product-catalog/domain.ts
import { SharedKernel } from '../shared-kernel';

export namespace ProductCatalog {

    export enum ProductStatus {
        Active = "active",
        Discontinued = "discontinued",
        OutOfStock = "out_of_stock"
    }

    export class Category extends SharedKernel.Entity<string> {
        constructor(
            id: string,
            private _name: string,
            private _description?: string,
            private _parentCategoryId?: string
        ) {
            super(id);
            this.validate();
        }

        private validate(): void {
            if (!this._name?.trim()) {
                throw new SharedKernel.DomainError("Category name is required");
            }
        }

        get name(): string { return this._name; }
        get description(): string | undefined { return this._description; }
        get parentCategoryId(): string | undefined { return this._parentCategoryId; }

        updateName(name: string): void {
            if (!name?.trim()) {
                throw new SharedKernel.DomainError("Category name cannot be empty");
            }
            this._name = name.trim();
        }

        equals(other: SharedKernel.Entity<string>): boolean {
            return this.id === other.id;
        }
    }

    export class Product extends SharedKernel.AggregateRoot<SharedKernel.ProductId> {
        constructor(
            id: SharedKernel.ProductId,
            private _name: string,
            private _description: string,
            private _price: SharedKernel.Money,
            private _categoryId: string,
            private _sku: string,
            private _status: ProductStatus = ProductStatus.Active,
            private _stockQuantity: number = 0,
            private _images: string[] = [],
            private _tags: string[] = []
        ) {
            super(id);
            this.validate();
        }

        private validate(): void {
            if (!this._name?.trim()) {
                throw new SharedKernel.DomainError("Product name is required");
            }
            if (!this._sku?.trim()) {
                throw new SharedKernel.DomainError("Product SKU is required");
            }
            if (this._stockQuantity < 0) {
                throw new SharedKernel.DomainError("Stock quantity cannot be negative");
            }
        }

        // Read-only properties
        get name(): string { return this._name; }
        get description(): string { return this._description; }
        get price(): SharedKernel.Money { return this._price; }
        get categoryId(): string { return this._categoryId; }
        get sku(): string { return this._sku; }
        get status(): ProductStatus { return this._status; }
        get stockQuantity(): number { return this._stockQuantity; }
        get images(): readonly string[] { return [...this._images]; }
        get tags(): readonly string[] { return [...this._tags]; }

        // Computed properties
        get isAvailable(): boolean {
            return this._status === ProductStatus.Active && this._stockQuantity > 0;
        }

        get isInStock(): boolean {
            return this._stockQuantity > 0;
        }

        // Business behavior
        updatePrice(newPrice: SharedKernel.Money): void {
            if (newPrice.amount <= 0) {
                throw new SharedKernel.DomainError("Price must be positive");
            }
            this._price = newPrice;
            this.addDomainEvent(new ProductPriceChangedEvent(this.id, newPrice));
        }

        updateStock(newQuantity: number): void {
            if (newQuantity < 0) {
                throw new SharedKernel.DomainError("Stock quantity cannot be negative");
            }

            const oldQuantity = this._stockQuantity;
            this._stockQuantity = newQuantity;

            if (oldQuantity === 0 && newQuantity > 0) {
                this.addDomainEvent(new ProductBackInStockEvent(this.id));
            } else if (oldQuantity > 0 && newQuantity === 0) {
                this.addDomainEvent(new ProductOutOfStockEvent(this.id));
            }
        }

        addImage(imageUrl: string): void {
            if (!imageUrl?.trim()) {
                throw new SharedKernel.DomainError("Image URL is required");
            }
            if (this._images.includes(imageUrl)) {
                throw new SharedKernel.DomainError("Image already exists");
            }
            this._images.push(imageUrl);
        }

        removeImage(imageUrl: string): void {
            const index = this._images.indexOf(imageUrl);
            if (index === -1) {
                throw new SharedKernel.DomainError("Image not found");
            }
            this._images.splice(index, 1);
        }

        addTag(tag: string): void {
            if (!tag?.trim()) {
                throw new SharedKernel.DomainError("Tag cannot be empty");
            }
            if (this._tags.includes(tag)) {
                return; // Tag already exists, ignore
            }
            this._tags.push(tag);
        }

        discontinue(): void {
            if (this._status === ProductStatus.Discontinued) {
                return;
            }
            this._status = ProductStatus.Discontinued;
            this.addDomainEvent(new ProductDiscontinuedEvent(this.id));
        }

        activate(): void {
            if (this._status === ProductStatus.Active) {
                return;
            }
            this._status = ProductStatus.Active;
            this.addDomainEvent(new ProductActivatedEvent(this.id));
        }

        equals(other: SharedKernel.Entity<SharedKernel.ProductId>): boolean {
            return this.id.equals(other.id);
        }
    }

    // Domain Events
    export class ProductPriceChangedEvent extends SharedKernel.DomainEvent {
        constructor(
            public readonly productId: SharedKernel.ProductId,
            public readonly newPrice: SharedKernel.Money
        ) {
            super(productId.toString());
        }
    }

    export class ProductBackInStockEvent extends SharedKernel.DomainEvent {
        constructor(public readonly productId: SharedKernel.ProductId) {
            super(productId.toString());
        }
    }

    export class ProductOutOfStockEvent extends SharedKernel.DomainEvent {
        constructor(public readonly productId: SharedKernel.ProductId) {
            super(productId.toString());
        }
    }

    export class ProductDiscontinuedEvent extends SharedKernel.DomainEvent {
        constructor(public readonly productId: SharedKernel.ProductId) {
            super(productId.toString());
        }
    }

    export class ProductActivatedEvent extends SharedKernel.DomainEvent {
        constructor(public readonly productId: SharedKernel.ProductId) {
            super(productId.toString());
        }
    }

    // Repository Interfaces
    export interface ProductRepository extends SharedKernel.Repository<Product> {
        findByCategory(categoryId: string): Promise<Product[]>;
        findBySku(sku: string): Promise<Product | null>;
        findAvailable(): Promise<Product[]>;
        searchByName(query: string): Promise<Product[]>;
        findByTags(tags: string[]): Promise<Product[]>;
    }

    export interface CategoryRepository extends SharedKernel.Repository<Category> {
        findByParent(parentId: string): Promise<Category[]>;
        findRootCategories(): Promise<Category[]>;
    }

    // Application Services
    export class ProductApplicationService {
        constructor(
            private readonly productRepository: ProductRepository,
            private readonly categoryRepository: CategoryRepository,
            private readonly eventPublisher: SharedKernel.DomainEventPublisher
        ) {}

        async createProduct(request: CreateProductRequest): Promise<SharedKernel.ProductId> {
            // Validate category exists
            const category = await this.categoryRepository.findById(request.categoryId);
            if (!category) {
                throw new SharedKernel.NotFoundError("Category", request.categoryId);
            }

            // Check SKU uniqueness
            const existingProduct = await this.productRepository.findBySku(request.sku);
            if (existingProduct) {
                throw new SharedKernel.ValidationError("SKU already exists", "sku");
            }

            const productId = new SharedKernel.ProductId(
                this.productRepository.nextIdentity()
            );

            const product = new Product(
                productId,
                request.name,
                request.description,
                new SharedKernel.Money(request.price, request.currency),
                request.categoryId,
                request.sku,
                ProductStatus.Active,
                request.initialStock,
                request.images,
                request.tags
            );

            await this.productRepository.save(product);

            // Publish domain events
            await this.eventPublisher.publishEvents(product.clearDomainEvents());

            return productId;
        }

        async updateProductPrice(
            productId: SharedKernel.ProductId,
            newPrice: number,
            currency: string = "USD"
        ): Promise<void> {
            const product = await this.productRepository.findById(productId.toString());
            if (!product) {
                throw new SharedKernel.NotFoundError("Product", productId.toString());
            }

            product.updatePrice(new SharedKernel.Money(newPrice, currency));
            await this.productRepository.save(product);
            await this.eventPublisher.publishEvents(product.clearDomainEvents());
        }

        async updateStock(
            productId: SharedKernel.ProductId,
            newStock: number
        ): Promise<void> {
            const product = await this.productRepository.findById(productId.toString());
            if (!product) {
                throw new SharedKernel.NotFoundError("Product", productId.toString());
            }

            product.updateStock(newStock);
            await this.productRepository.save(product);
            await this.eventPublisher.publishEvents(product.clearDomainEvents());
        }

        async searchProducts(query: string): Promise<ProductSummary[]> {
            const products = await this.productRepository.searchByName(query);
            return products.map(p => this.mapToSummary(p));
        }

        private mapToSummary(product: Product): ProductSummary {
            return {
                id: product.id.toString(),
                name: product.name,
                price: product.price,
                sku: product.sku,
                status: product.status,
                isAvailable: product.isAvailable,
                stockQuantity: product.stockQuantity,
                categoryId: product.categoryId
            };
        }
    }

    // DTOs
    export interface CreateProductRequest {
        name: string;
        description: string;
        price: number;
        currency: string;
        categoryId: string;
        sku: string;
        initialStock: number;
        images?: string[];
        tags?: string[];
    }

    export interface ProductSummary {
        id: string;
        name: string;
        price: SharedKernel.Money;
        sku: string;
        status: ProductStatus;
        isAvailable: boolean;
        stockQuantity: number;
        categoryId: string;
    }
}
```

## Customer Management Context

```typescript
// customer-management/domain.ts
import { SharedKernel } from '../shared-kernel';

export namespace CustomerManagement {

    export class Customer extends SharedKernel.AggregateRoot<SharedKernel.CustomerId> {
        constructor(
            id: SharedKernel.CustomerId,
            private _name: string,
            private _email: SharedKernel.Email,
            private _phone?: string,
            private _status: SharedKernel.CustomerStatus = SharedKernel.CustomerStatus.Active,
            private _loyaltyPoints: number = 0,
            private _addresses: SharedKernel.Address[] = [],
            private _defaultAddressIndex?: number,
            private readonly _createdAt: Date = new Date(),
            private _lastLoginAt?: Date
        ) {
            super(id);
            this.validate();
        }

        private validate(): void {
            if (!this._name?.trim()) {
                throw new SharedKernel.DomainError("Customer name is required");
            }
        }

        // Identity and equality
        equals(other: SharedKernel.Entity<SharedKernel.CustomerId>): boolean {
            return this.id.equals(other.id);
        }

        // Read-only properties
        get name(): string { return this._name; }
        get email(): SharedKernel.Email { return this._email; }
        get phone(): string | undefined { return this._phone; }
        get status(): SharedKernel.CustomerStatus { return this._status; }
        get loyaltyPoints(): number { return this._loyaltyPoints; }
        get addresses(): readonly SharedKernel.Address[] { return [...this._addresses]; }
        get defaultAddress(): SharedKernel.Address | undefined {
            return this._defaultAddressIndex !== undefined ? this._addresses[this._defaultAddressIndex] : undefined;
        }
        get createdAt(): Date { return this._createdAt; }
        get lastLoginAt(): Date | undefined { return this._lastLoginAt; }

        // Computed properties
        get loyaltyTier(): SharedKernel.LoyaltyTier {
            if (this._loyaltyPoints >= 10000) return SharedKernel.LoyaltyTier.Platinum;
            if (this._loyaltyPoints >= 5000) return SharedKernel.LoyaltyTier.Gold;
            if (this._loyaltyPoints >= 1000) return SharedKernel.LoyaltyTier.Silver;
            return SharedKernel.LoyaltyTier.Bronze;
        }

        get isActive(): boolean {
            return this._status === SharedKernel.CustomerStatus.Active;
        }

        // Business behavior - Profile management
        changeName(newName: string): void {
            this.ensureActive();
            if (!newName?.trim()) {
                throw new SharedKernel.DomainError("Name cannot be empty");
            }
            this._name = newName.trim();
        }

        changeEmail(newEmail: SharedKernel.Email): void {
            this.ensureActive();
            this._email = newEmail;
        }

        changePhone(newPhone: string): void {
            this.ensureActive();
            // Basic phone validation
            if (newPhone && !/^[\+]?[1-9][\d]{0,15}$/.test(newPhone.replace(/[\s\-\(\)]/g, ''))) {
                throw new SharedKernel.DomainError("Invalid phone number format");
            }
            this._phone = newPhone;
        }

        // Business behavior - Address management
        addAddress(address: SharedKernel.Address): void {
            this.ensureActive();

            // Check for duplicate addresses
            const exists = this._addresses.some(addr => addr.equals(address));
            if (exists) {
                throw new SharedKernel.DomainError("Address already exists");
            }

            this._addresses.push(address);

            // Set as default if it's the first address
            if (this._addresses.length === 1) {
                this._defaultAddressIndex = 0;
            }
        }

        removeAddress(address: SharedKernel.Address): void {
            this.ensureActive();

            const index = this._addresses.findIndex(addr => addr.equals(address));
            if (index === -1) {
                throw new SharedKernel.DomainError("Address not found");
            }

            this._addresses.splice(index, 1);

            // Adjust default address index if necessary
            if (this._defaultAddressIndex !== undefined) {
                if (this._defaultAddressIndex === index) {
                    this._defaultAddressIndex = this._addresses.length > 0 ? 0 : undefined;
                } else if (this._defaultAddressIndex > index) {
                    this._defaultAddressIndex--;
                }
            }
        }

        setDefaultAddress(address: SharedKernel.Address): void {
            this.ensureActive();

            const index = this._addresses.findIndex(addr => addr.equals(address));
            if (index === -1) {
                throw new SharedKernel.DomainError("Address not found");
            }

            this._defaultAddressIndex = index;
        }

        // Business behavior - Loyalty program
        earnLoyaltyPoints(points: number): void {
            this.ensureActive();
            if (points <= 0) {
                throw new SharedKernel.DomainError("Points must be positive");
            }
            this._loyaltyPoints += points;
        }

        spendLoyaltyPoints(points: number): void {
            this.ensureActive();
            if (points <= 0) {
                throw new SharedKernel.DomainError("Points must be positive");
            }
            if (points > this._loyaltyPoints) {
                throw new SharedKernel.DomainError("Insufficient loyalty points");
            }
            this._loyaltyPoints -= points;
        }

        // Business behavior - Status management
        activate(): void {
            if (this._status === SharedKernel.CustomerStatus.Active) {
                return;
            }
            this._status = SharedKernel.CustomerStatus.Active;
            this.addDomainEvent(new CustomerActivatedEvent(this.id));
        }

        suspend(reason?: string): void {
            if (this._status === SharedKernel.CustomerStatus.Suspended) {
                return;
            }
            this._status = SharedKernel.CustomerStatus.Suspended;
            this.addDomainEvent(new CustomerSuspendedEvent(this.id, reason));
        }

        recordLogin(): void {
            this._lastLoginAt = new Date();
        }

        // Business queries
        canPlaceOrder(): boolean {
            return this.isActive && (this.defaultAddress !== undefined);
        }

        getCreditLimit(): number {
            switch (this.loyaltyTier) {
                case SharedKernel.LoyaltyTier.Platinum: return 10000;
                case SharedKernel.LoyaltyTier.Gold: return 5000;
                case SharedKernel.LoyaltyTier.Silver: return 1000;
                default: return 100;
            }
        }

        private ensureActive(): void {
            if (!this.isActive) {
                throw new SharedKernel.DomainError("Operation requires active customer status");
            }
        }

        getSummary(): SharedKernel.CustomerSummary {
            return {
                id: this.id,
                name: this._name,
                email: this._email,
                status: this._status,
                loyaltyTier: this.loyaltyTier
            };
        }
    }

    // Domain Events
    export class CustomerActivatedEvent extends SharedKernel.DomainEvent {
        constructor(public readonly customerId: SharedKernel.CustomerId) {
            super(customerId.toString());
        }
    }

    export class CustomerSuspendedEvent extends SharedKernel.DomainEvent {
        constructor(
            public readonly customerId: SharedKernel.CustomerId,
            public readonly reason?: string
        ) {
            super(customerId.toString());
        }
    }

    // Repository Interface
    export interface CustomerRepository extends SharedKernel.Repository<Customer> {
        findByEmail(email: SharedKernel.Email): Promise<Customer | null>;
        findActiveCustomers(): Promise<Customer[]>;
        findByLoyaltyTier(tier: SharedKernel.LoyaltyTier): Promise<Customer[]>;
    }

    // Application Service
    export class CustomerApplicationService {
        constructor(
            private readonly customerRepository: CustomerRepository,
            private readonly eventPublisher: SharedKernel.DomainEventPublisher
        ) {}

        async registerCustomer(request: RegisterCustomerRequest): Promise<SharedKernel.CustomerId> {
            // Validate email uniqueness
            const existingCustomer = await this.customerRepository.findByEmail(
                new SharedKernel.Email(request.email)
            );
            if (existingCustomer) {
                throw new SharedKernel.ValidationError("Email already registered", "email");
            }

            const customerId = new SharedKernel.CustomerId(
                this.customerRepository.nextIdentity()
            );

            const customer = new Customer(
                customerId,
                request.name,
                new SharedKernel.Email(request.email),
                request.phone
            );

            // Add address if provided
            if (request.address) {
                const address = new SharedKernel.Address(
                    request.address.street,
                    request.address.city,
                    request.address.state,
                    request.address.zipCode,
                    request.address.country
                );
                customer.addAddress(address);
            }

            await this.customerRepository.save(customer);
            await this.eventPublisher.publishEvents(customer.clearDomainEvents());

            return customerId;
        }

        async updateCustomerProfile(
            customerId: SharedKernel.CustomerId,
            updates: UpdateCustomerProfileRequest
        ): Promise<void> {
            const customer = await this.customerRepository.findById(customerId.toString());
            if (!customer) {
                throw new SharedKernel.NotFoundError("Customer", customerId.toString());
            }

            // Apply updates
            if (updates.name) customer.changeName(updates.name);
            if (updates.phone) customer.changePhone(updates.phone);

            await this.customerRepository.save(customer);
        }

        async addCustomerAddress(
            customerId: SharedKernel.CustomerId,
            addressRequest: AddressRequest
        ): Promise<void> {
            const customer = await this.customerRepository.findById(customerId.toString());
            if (!customer) {
                throw new SharedKernel.NotFoundError("Customer", customerId.toString());
            }

            const address = new SharedKernel.Address(
                addressRequest.street,
                addressRequest.city,
                addressRequest.state,
                addressRequest.zipCode,
                addressRequest.country
            );

            customer.addAddress(address);
            await this.customerRepository.save(customer);
        }

        async getCustomerDetails(customerId: SharedKernel.CustomerId): Promise<CustomerDetailsDto> {
            const customer = await this.customerRepository.findById(customerId.toString());
            if (!customer) {
                throw new SharedKernel.NotFoundError("Customer", customerId.toString());
            }

            return {
                id: customer.id.toString(),
                name: customer.name,
                email: customer.email.value,
                phone: customer.phone,
                status: customer.status,
                loyaltyPoints: customer.loyaltyPoints,
                loyaltyTier: customer.loyaltyTier,
                addresses: customer.addresses.map(addr => ({
                    street: addr.street,
                    city: addr.city,
                    state: addr.state,
                    zipCode: addr.postalCode,
                    country: addr.country
                })),
                defaultAddressIndex: customer.addresses.findIndex(addr =>
                    customer.defaultAddress?.equals(addr)
                ),
                createdAt: customer.createdAt,
                lastLoginAt: customer.lastLoginAt
            };
        }
    }

    // DTOs
    export interface RegisterCustomerRequest {
        name: string;
        email: string;
        phone?: string;
        address?: AddressRequest;
    }

    export interface UpdateCustomerProfileRequest {
        name?: string;
        phone?: string;
    }

    export interface AddressRequest {
        street: string;
        city: string;
        state: string;
        zipCode: string;
        country?: string;
    }

    export interface CustomerDetailsDto {
        id: string;
        name: string;
        email: string;
        phone?: string;
        status: SharedKernel.CustomerStatus;
        loyaltyPoints: number;
        loyaltyTier: SharedKernel.LoyaltyTier;
        addresses: AddressDto[];
        defaultAddressIndex: number;
        createdAt: Date;
        lastLoginAt?: Date;
    }

    export interface AddressDto {
        street: string;
        city: string;
        state: string;
        zipCode: string;
        country: string;
    }
}
```

## Order Management Context

```typescript
// order-management/domain.ts
import { SharedKernel } from '../shared-kernel';
import { ProductCatalog } from '../product-catalog';
import { CustomerManagement } from '../customer-management';

export namespace OrderManagement {

    export enum OrderStatus {
        Draft = "draft",
        Placed = "placed",
        Confirmed = "confirmed",
        Preparing = "preparing",
        Shipped = "shipped",
        Delivered = "delivered",
        Cancelled = "cancelled",
        Returned = "returned"
    }

    export enum PaymentStatus {
        Pending = "pending",
        Authorized = "authorized",
        Captured = "captured",
        Failed = "failed",
        Refunded = "refunded"
    }

    export class OrderItem {
        constructor(
            private readonly _productId: SharedKernel.ProductId,
            private readonly _productName: string,
            private readonly _unitPrice: SharedKernel.Money,
            private _quantity: number,
            private readonly _addedAt: Date = new Date()
        ) {
            if (_quantity <= 0) {
                throw new SharedKernel.DomainError("Quantity must be positive");
            }
        }

        get productId(): SharedKernel.ProductId { return this._productId; }
        get productName(): string { return this._productName; }
        get unitPrice(): SharedKernel.Money { return this._unitPrice; }
        get quantity(): number { return this._quantity; }
        get addedAt(): Date { return this._addedAt; }

        get lineTotal(): SharedKernel.Money {
            return this._unitPrice.multiply(this._quantity);
        }

        increaseQuantity(amount: number): void {
            if (amount <= 0) {
                throw new SharedKernel.DomainError("Amount must be positive");
            }
            this._quantity += amount;
        }

        decreaseQuantity(amount: number): void {
            if (amount <= 0) {
                throw new SharedKernel.DomainError("Amount must be positive");
            }
            if (this._quantity - amount <= 0) {
                throw new SharedKernel.DomainError("Cannot reduce quantity below 1");
            }
            this._quantity -= amount;
        }

        changeQuantity(newQuantity: number): void {
            if (newQuantity <= 0) {
                throw new SharedKernel.DomainError("Quantity must be positive");
            }
            this._quantity = newQuantity;
        }
    }

    export class Order extends SharedKernel.AggregateRoot<SharedKernel.OrderId> {
        constructor(
            id: SharedKernel.OrderId,
            private _customerId: SharedKernel.CustomerId,
            private _status: OrderStatus,
            private _items: OrderItem[] = [],
            private _shippingAddress?: SharedKernel.Address,
            private _billingAddress?: SharedKernel.Address,
            private _paymentStatus: PaymentStatus = PaymentStatus.Pending,
            private readonly _createdAt: Date = new Date(),
            private _modifiedAt: Date = new Date()
        ) {
            super(id);
            this.validateInvariants();
        }

        // Read-only properties
        get customerId(): SharedKernel.CustomerId { return this._customerId; }
        get status(): OrderStatus { return this._status; }
        get paymentStatus(): PaymentStatus { return this._paymentStatus; }
        get items(): readonly OrderItem[] { return [...this._items]; }
        get shippingAddress(): SharedKernel.Address | undefined { return this._shippingAddress; }
        get billingAddress(): SharedKernel.Address | undefined { return this._billingAddress; }
        get createdAt(): Date { return this._createdAt; }
        get modifiedAt(): Date { return this._modifiedAt; }

        // Computed properties
        get totalAmount(): SharedKernel.Money {
            return this._items.reduce(
                (total, item) => total.add(item.lineTotal),
                SharedKernel.Money.zero("USD")
            );
        }

        get itemCount(): number {
            return this._items.reduce((count, item) => count + item.quantity, 0);
        }

        get canBeModified(): boolean {
            return this._status === OrderStatus.Draft;
        }

        get canBeCancelled(): boolean {
            return [OrderStatus.Placed, OrderStatus.Confirmed].includes(this._status);
        }

        get isPaid(): boolean {
            return [PaymentStatus.Authorized, PaymentStatus.Captured].includes(this._paymentStatus);
        }

        // Business behavior - Item management
        addItem(product: ProductCatalog.Product, quantity: number): void {
            this.ensureCanModify();

            if (!product.isAvailable) {
                throw new SharedKernel.DomainError(`Product ${product.name} is not available`);
            }

            if (quantity <= 0) {
                throw new SharedKernel.DomainError("Quantity must be positive");
            }

            if (product.stockQuantity < quantity) {
                throw new SharedKernel.DomainError(`Insufficient stock for ${product.name}`);
            }

            const existingItem = this._items.find(item =>
                item.productId.equals(product.id)
            );

            if (existingItem) {
                existingItem.increaseQuantity(quantity);
            } else {
                this._items.push(new OrderItem(
                    product.id,
                    product.name,
                    product.price,
                    quantity
                ));
            }

            this.updateModificationTime();
            this.validateInvariants();
        }

        removeItem(productId: SharedKernel.ProductId): void {
            this.ensureCanModify();

            const index = this._items.findIndex(item => item.productId.equals(productId));
            if (index === -1) {
                throw new SharedKernel.DomainError("Product not found in order");
            }

            this._items.splice(index, 1);
            this.updateModificationTime();
            this.validateInvariants();
        }

        updateItemQuantity(productId: SharedKernel.ProductId, newQuantity: number): void {
            this.ensureCanModify();

            const item = this._items.find(item => item.productId.equals(productId));
            if (!item) {
                throw new SharedKernel.DomainError("Product not found in order");
            }

            item.changeQuantity(newQuantity);
            this.updateModificationTime();
            this.validateInvariants();
        }

        // Business behavior - Address management
        setShippingAddress(address: SharedKernel.Address): void {
            this.ensureCanModify();
            this._shippingAddress = address;
            this.updateModificationTime();
        }

        setBillingAddress(address: SharedKernel.Address): void {
            this.ensureCanModify();
            this._billingAddress = address;
            this.updateModificationTime();
        }

        // Business behavior - Order lifecycle
        place(): void {
            if (this._status !== OrderStatus.Draft) {
                throw new SharedKernel.DomainError("Only draft orders can be placed");
            }

            if (this._items.length === 0) {
                throw new SharedKernel.DomainError("Order must have at least one item");
            }

            if (!this._shippingAddress) {
                throw new SharedKernel.DomainError("Shipping address is required");
            }

            if (this.totalAmount.amount <= 0) {
                throw new SharedKernel.DomainError("Order total must be positive");
            }

            this._status = OrderStatus.Placed;
            this.updateModificationTime();
            this.incrementVersion();

            this.addDomainEvent(new OrderPlacedEvent(
                this.id,
                this.customerId,
                this.totalAmount,
                this._items.map(item => ({
                    productId: item.productId.toString(),
                    productName: item.productName,
                    quantity: item.quantity,
                    unitPrice: item.unitPrice
                }))
            ));
        }

        confirm(): void {
            if (this._status !== OrderStatus.Placed) {
                throw new SharedKernel.DomainError("Only placed orders can be confirmed");
            }

            this._status = OrderStatus.Confirmed;
            this.updateModificationTime();
            this.incrementVersion();

            this.addDomainEvent(new OrderConfirmedEvent(this.id));
        }

        markAsPaid(): void {
            if (!this.isPaid) {
                throw new SharedKernel.DomainError("Order payment must be completed first");
            }

            if (this._status !== OrderStatus.Confirmed) {
                throw new SharedKernel.DomainError("Only confirmed orders can be marked as paid");
            }

            this._status = OrderStatus.Preparing;
            this.updateModificationTime();
            this.incrementVersion();

            this.addDomainEvent(new OrderPaidEvent(this.id));
        }

        ship(trackingNumber: string, carrier: string): void {
            if (this._status !== OrderStatus.Preparing) {
                throw new SharedKernel.DomainError("Only preparing orders can be shipped");
            }

            if (!trackingNumber?.trim()) {
                throw new SharedKernel.DomainError("Tracking number is required");
            }

            this._status = OrderStatus.Shipped;
            this.updateModificationTime();
            this.incrementVersion();

            this.addDomainEvent(new OrderShippedEvent(
                this.id,
                trackingNumber,
                carrier
            ));
        }

        deliver(): void {
            if (this._status !== OrderStatus.Shipped) {
                throw new SharedKernel.DomainError("Only shipped orders can be delivered");
            }

            this._status = OrderStatus.Delivered;
            this.updateModificationTime();
            this.incrementVersion();

            this.addDomainEvent(new OrderDeliveredEvent(this.id));
        }

        cancel(reason?: string): void {
            if ([OrderStatus.Delivered, OrderStatus.Returned].includes(this._status)) {
                throw new SharedKernel.DomainError("Order cannot be cancelled");
            }

            this._status = OrderStatus.Cancelled;
            this.updateModificationTime();
            this.incrementVersion();

            this.addDomainEvent(new OrderCancelledEvent(this.id, reason));
        }

        updatePaymentStatus(status: PaymentStatus): void {
            this._paymentStatus = status;
            this.updateModificationTime();

            // Trigger status changes based on payment
            if (status === PaymentStatus.Authorized && this._status === OrderStatus.Placed) {
                this.confirm();
            } else if (status === PaymentStatus.Captured && this._status === OrderStatus.Confirmed) {
                this.markAsPaid();
            }
        }

        // Private helper methods
        private ensureCanModify(): void {
            if (!this.canBeModified) {
                throw new SharedKernel.DomainError("Order cannot be modified in its current state");
            }
        }

        private validateInvariants(): void {
            // Invariant: Order must have a customer
            if (!this._customerId) {
                throw new SharedKernel.DomainError("Order must have a customer");
            }

            // Invariant: Total amount must be positive for non-draft orders
            if (this._status !== OrderStatus.Draft && this.totalAmount.amount <= 0) {
                throw new SharedKernel.DomainError("Order total must be positive");
            }

            // Invariant: All items must have positive quantities
            if (this._items.some(item => item.quantity <= 0)) {
                throw new SharedKernel.DomainError("All order items must have positive quantity");
            }

            // Invariant: Maximum 50 items per order
            if (this.itemCount > 50) {
                throw new SharedKernel.DomainError("Order cannot have more than 50 items");
            }

            // Invariant: Shipping address required for placed orders
            if (this._status !== OrderStatus.Draft && !this._shippingAddress) {
                throw new SharedKernel.DomainError("Shipping address is required");
            }
        }

        private updateModificationTime(): void {
            this._modifiedAt = new Date();
        }

        equals(other: SharedKernel.Entity<SharedKernel.OrderId>): boolean {
            return this.id.equals(other.id);
        }
    }

    // Domain Events
    export class OrderPlacedEvent extends SharedKernel.DomainEvent {
        constructor(
            public readonly orderId: SharedKernel.OrderId,
            public readonly customerId: SharedKernel.CustomerId,
            public readonly totalAmount: SharedKernel.Money,
            public readonly items: OrderItemSummary[]
        ) {
            super(orderId.toString());
        }
    }

    export class OrderConfirmedEvent extends SharedKernel.DomainEvent {
        constructor(public readonly orderId: SharedKernel.OrderId) {
            super(orderId.toString());
        }
    }

    export class OrderPaidEvent extends SharedKernel.DomainEvent {
        constructor(public readonly orderId: SharedKernel.OrderId) {
            super(orderId.toString());
        }
    }

    export class OrderShippedEvent extends SharedKernel.DomainEvent {
        constructor(
            public readonly orderId: SharedKernel.OrderId,
            public readonly trackingNumber: string,
            public readonly carrier: string
        ) {
            super(orderId.toString());
        }
    }

    export class OrderDeliveredEvent extends SharedKernel.DomainEvent {
        constructor(public readonly orderId: SharedKernel.OrderId) {
            super(orderId.toString());
        }
    }

    export class OrderCancelledEvent extends SharedKernel.DomainEvent {
        constructor(
            public readonly orderId: SharedKernel.OrderId,
            public readonly reason?: string
        ) {
            super(orderId.toString());
        }
    }

    export interface OrderItemSummary {
        productId: string;
        productName: string;
        quantity: number;
        unitPrice: SharedKernel.Money;
    }

    // Domain Services
    export interface PricingService {
        calculateTotal(order: Order): Promise<SharedKernel.Money>;
    }

    export class DefaultPricingService implements PricingService {
        constructor(
            private readonly discountService: DiscountService,
            private readonly taxService: TaxService
        ) {}

        async calculateTotal(order: Order): Promise<SharedKernel.Money> {
            let subtotal = order.totalAmount;

            // Apply discounts
            const discount = await this.discountService.calculateDiscount(order);
            subtotal = subtotal.subtract(discount);

            // Apply taxes
            const tax = await this.taxService.calculateTax(subtotal, order.shippingAddress);
            const total = subtotal.add(tax);

            return total;
        }
    }

    export interface DiscountService {
        calculateDiscount(order: Order): Promise<SharedKernel.Money>;
    }

    export interface TaxService {
        calculateTax(amount: SharedKernel.Money, address?: SharedKernel.Address): Promise<SharedKernel.Money>;
    }

    // Repository Interface
    export interface OrderRepository extends SharedKernel.Repository<Order> {
        findByCustomerId(customerId: SharedKernel.CustomerId): Promise<Order[]>;
        findByStatus(status: OrderStatus): Promise<Order[]>;
        findPlacedAfter(date: Date): Promise<Order[]>;
        findPendingPayment(): Promise<Order[]>;
    }

    // Application Service
    export class OrderApplicationService {
        constructor(
            private readonly orderRepository: OrderRepository,
            private readonly customerRepository: CustomerManagement.CustomerRepository,
            private readonly productRepository: ProductCatalog.ProductRepository,
            private readonly pricingService: PricingService,
            private readonly paymentService: PaymentService,
            private readonly shippingService: ShippingService,
            private readonly eventPublisher: SharedKernel.DomainEventPublisher,
            private readonly unitOfWork: SharedKernel.UnitOfWork
        ) {}

        async createOrder(request: CreateOrderRequest): Promise<CreateOrderResponse> {
            // Validate customer
            const customer = await this.customerRepository.findById(request.customerId);
            if (!customer) {
                throw new SharedKernel.NotFoundError("Customer", request.customerId);
            }

            if (!customer.canPlaceOrder()) {
                throw new SharedKernel.DomainError("Customer cannot place orders");
            }

            // Start transaction
            await this.unitOfWork.begin();

            try {
                // Create order aggregate
                const orderId = new SharedKernel.OrderId(this.orderRepository.nextIdentity());
                const order = new Order(orderId, request.customerId, OrderStatus.Draft);

                // Add items
                for (const itemRequest of request.items) {
                    const product = await this.productRepository.findById(itemRequest.productId);
                    if (!product) {
                        throw new SharedKernel.NotFoundError("Product", itemRequest.productId);
                    }
                    order.addItem(product, itemRequest.quantity);
                }

                // Set addresses
                if (request.shippingAddress) {
                    order.setShippingAddress(request.shippingAddress);
                } else if (customer.defaultAddress) {
                    order.setShippingAddress(customer.defaultAddress);
                }

                if (request.billingAddress) {
                    order.setBillingAddress(request.billingAddress);
                }

                await this.orderRepository.save(order);
                await this.unitOfWork.commit();

                return {
                    orderId,
                    orderNumber: this.generateOrderNumber(order),
                    totalAmount: order.totalAmount,
                    status: order.status
                };

            } catch (error) {
                await this.unitOfWork.rollback();
                throw error;
            }
        }

        async placeOrder(orderId: SharedKernel.OrderId): Promise<void> {
            const order = await this.orderRepository.findById(orderId.toString());
            if (!order) {
                throw new SharedKernel.NotFoundError("Order", orderId.toString());
            }

            // Calculate final pricing
            const finalTotal = await this.pricingService.calculateTotal(order);

            // Process payment
            const paymentResult = await this.paymentService.authorizePayment(order, finalTotal);
            if (!paymentResult.success) {
                throw new SharedKernel.DomainError("Payment authorization failed");
            }

            // Place order
            order.place();
            order.updatePaymentStatus(PaymentStatus.Authorized);

            await this.orderRepository.save(order);
            await this.eventPublisher.publishEvents(order.clearDomainEvents());
        }

        async getOrderDetails(orderId: SharedKernel.OrderId): Promise<OrderDetailsDto> {
            const order = await this.orderRepository.findById(orderId.toString());
            if (!order) {
                throw new SharedKernel.NotFoundError("Order", orderId.toString());
            }

            const customer = await this.customerRepository.findById(order.customerId.toString());
            if (!customer) {
                throw new SharedKernel.NotFoundError("Customer", order.customerId.toString());
            }

            return {
                orderId: order.id.toString(),
                orderNumber: this.generateOrderNumber(order),
                status: order.status,
                paymentStatus: order.paymentStatus,
                customer: {
                    id: customer.id.toString(),
                    name: customer.name,
                    email: customer.email.value
                },
                items: order.items.map(item => ({
                    productId: item.productId.toString(),
                    productName: item.productName,
                    quantity: item.quantity,
                    unitPrice: item.unitPrice,
                    lineTotal: item.lineTotal
                })),
                shippingAddress: order.shippingAddress,
                billingAddress: order.billingAddress,
                totalAmount: order.totalAmount,
                createdAt: order.createdAt,
                modifiedAt: order.modifiedAt
            };
        }

        private generateOrderNumber(order: Order): string {
            return `ORD-${order.id.toString().slice(-8).toUpperCase()}`;
        }
    }

    // Supporting interfaces (would be implemented in infrastructure)
    export interface PaymentService {
        authorizePayment(order: Order, amount: SharedKernel.Money): Promise<PaymentResult>;
        capturePayment(order: Order, amount: SharedKernel.Money): Promise<PaymentResult>;
        refundPayment(order: Order, amount: SharedKernel.Money): Promise<PaymentResult>;
    }

    export interface ShippingService {
        createShipment(order: Order): Promise<Shipment>;
        calculateDeliveryDate(order: Order): Promise<Date>;
    }

    export interface PaymentResult {
        success: boolean;
        transactionId?: string;
        errorMessage?: string;
    }

    export interface Shipment {
        trackingNumber: string;
        carrier: string;
        estimatedDelivery: Date;
    }

    // DTOs
    export interface CreateOrderRequest {
        customerId: string;
        items: OrderItemRequest[];
        shippingAddress?: SharedKernel.Address;
        billingAddress?: SharedKernel.Address;
    }

    export interface OrderItemRequest {
        productId: string;
        quantity: number;
    }

    export interface CreateOrderResponse {
        orderId: SharedKernel.OrderId;
        orderNumber: string;
        totalAmount: SharedKernel.Money;
        status: OrderStatus;
    }

    export interface OrderDetailsDto {
        orderId: string;
        orderNumber: string;
        status: OrderStatus;
        paymentStatus: PaymentStatus;
        customer: CustomerSummary;
        items: OrderItemDetails[];
        shippingAddress?: SharedKernel.Address;
        billingAddress?: SharedKernel.Address;
        totalAmount: SharedKernel.Money;
        createdAt: Date;
        modifiedAt: Date;
    }

    export interface CustomerSummary {
        id: string;
        name: string;
        email: string;
    }

    export interface OrderItemDetails {
        productId: string;
        productName: string;
        quantity: number;
        unitPrice: SharedKernel.Money;
        lineTotal: SharedKernel.Money;
    }
}
```

## Anti-Corruption Layer

```typescript
// acl/product-catalog-adapter.ts
import { SharedKernel } from '../shared-kernel';
import { ProductCatalog } from '../product-catalog';
import { OrderManagement } from '../order-management';

// ACL between Product Catalog and Order Management
export class ProductCatalogAdapter {
    constructor(private readonly productRepository: ProductCatalog.ProductRepository) {}

    async getProductForOrder(productId: SharedKernel.ProductId): Promise<OrderProductSummary> {
        try {
            const product = await this.productRepository.findById(productId.toString());
            if (!product) {
                return null;
            }

            return {
                id: product.id,
                name: product.name,
                price: product.price,
                sku: product.sku,
                isAvailable: product.isAvailable,
                stockQuantity: product.stockQuantity,
                categoryId: product.categoryId
            };
        } catch (error) {
            // Log error and return null to indicate product not available
            console.error(`Error fetching product ${productId}:`, error);
            return null;
        }
    }

    async getAvailableProducts(): Promise<OrderProductSummary[]> {
        try {
            const products = await this.productRepository.findAvailable();
            return products.map(product => ({
                id: product.id,
                name: product.name,
                price: product.price,
                sku: product.sku,
                isAvailable: true,
                stockQuantity: product.stockQuantity,
                categoryId: product.categoryId
            }));
        } catch (error) {
            console.error('Error fetching available products:', error);
            return [];
        }
    }

    async reserveStock(productId: SharedKernel.ProductId, quantity: number): Promise<boolean> {
        try {
            const product = await this.productRepository.findById(productId.toString());
            if (!product || !product.isAvailable || product.stockQuantity < quantity) {
                return false;
            }

            // In a real implementation, this would use optimistic locking
            // or a separate inventory service to reserve stock
            product.updateStock(product.stockQuantity - quantity);
            await this.productRepository.save(product);

            return true;
        } catch (error) {
            console.error(`Error reserving stock for ${productId}:`, error);
            return false;
        }
    }

    async releaseStock(productId: SharedKernel.ProductId, quantity: number): Promise<void> {
        try {
            const product = await this.productRepository.findById(productId.toString());
            if (product) {
                product.updateStock(product.stockQuantity + quantity);
                await this.productRepository.save(product);
            }
        } catch (error) {
            console.error(`Error releasing stock for ${productId}:`, error);
        }
    }
}

export interface OrderProductSummary {
    id: SharedKernel.ProductId;
    name: string;
    price: SharedKernel.Money;
    sku: string;
    isAvailable: boolean;
    stockQuantity: number;
    categoryId: string;
}
```

## Infrastructure Implementation

```typescript
// infrastructure/repositories.ts
import { SharedKernel } from '../shared-kernel';
import { ProductCatalog } from '../product-catalog';
import { CustomerManagement } from '../customer-management';
import { OrderManagement } from '../order-management';

// In-memory implementations for demonstration
// In production, these would use actual databases

export class InMemoryProductRepository implements ProductCatalog.ProductRepository {
    private products = new Map<string, ProductCatalog.Product>();

    async save(product: ProductCatalog.Product): Promise<void> {
        this.products.set(product.id.toString(), product);
    }

    async findById(id: string): Promise<ProductCatalog.Product | null> {
        return this.products.get(id) || null;
    }

    async findByCategory(categoryId: string): Promise<ProductCatalog.Product[]> {
        return Array.from(this.products.values())
            .filter(p => p.categoryId === categoryId);
    }

    async findBySku(sku: string): Promise<ProductCatalog.Product | null> {
        for (const product of this.products.values()) {
            if (product.sku === sku) {
                return product;
            }
        }
        return null;
    }

    async findAvailable(): Promise<ProductCatalog.Product[]> {
        return Array.from(this.products.values())
            .filter(p => p.isAvailable);
    }

    async searchByName(query: string): Promise<ProductCatalog.Product[]> {
        const lowerQuery = query.toLowerCase();
        return Array.from(this.products.values())
            .filter(p => p.name.toLowerCase().includes(lowerQuery));
    }

    async findByTags(tags: string[]): Promise<ProductCatalog.Product[]> {
        return Array.from(this.products.values())
            .filter(p => tags.some(tag => p.tags.includes(tag)));
    }

    nextIdentity(): string {
        return `prod-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
    }
}

export class InMemoryCustomerRepository implements CustomerManagement.CustomerRepository {
    private customers = new Map<string, CustomerManagement.Customer>();

    async save(customer: CustomerManagement.Customer): Promise<void> {
        this.customers.set(customer.id.toString(), customer);
    }

    async findById(id: string): Promise<CustomerManagement.Customer | null> {
        return this.customers.get(id) || null;
    }

    async findByEmail(email: SharedKernel.Email): Promise<CustomerManagement.Customer | null> {
        for (const customer of this.customers.values()) {
            if (customer.email.equals(email)) {
                return customer;
            }
        }
        return null;
    }

    async findActiveCustomers(): Promise<CustomerManagement.Customer[]> {
        return Array.from(this.customers.values())
            .filter(c => c.isActive);
    }

    async findByLoyaltyTier(tier: SharedKernel.LoyaltyTier): Promise<CustomerManagement.Customer[]> {
        return Array.from(this.customers.values())
            .filter(c => c.loyaltyTier === tier);
    }

    nextIdentity(): string {
        return `cust-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
    }
}

export class InMemoryOrderRepository implements OrderManagement.OrderRepository {
    private orders = new Map<string, OrderManagement.Order>();

    async save(order: OrderManagement.Order): Promise<void> {
        this.orders.set(order.id.toString(), order);
    }

    async findById(id: string): Promise<OrderManagement.Order | null> {
        return this.orders.get(id) || null;
    }

    async findByCustomerId(customerId: SharedKernel.CustomerId): Promise<OrderManagement.Order[]> {
        return Array.from(this.orders.values())
            .filter(o => o.customerId.equals(customerId));
    }

    async findByStatus(status: OrderManagement.OrderStatus): Promise<OrderManagement.Order[]> {
        return Array.from(this.orders.values())
            .filter(o => o.status === status);
    }

    async findPlacedAfter(date: Date): Promise<OrderManagement.Order[]> {
        return Array.from(this.orders.values())
            .filter(o => o.createdAt > date && o.status !== OrderManagement.OrderStatus.Draft);
    }

    async findPendingPayment(): Promise<OrderManagement.Order[]> {
        return Array.from(this.orders.values())
            .filter(o => o.paymentStatus === OrderManagement.PaymentStatus.Pending);
    }

    nextIdentity(): string {
        return `order-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
    }
}
```

## Application Composition Root

```typescript
// application/composition-root.ts
import { SharedKernel } from '../shared-kernel';
import { ProductCatalog } from '../product-catalog';
import { CustomerManagement } from '../customer-management';
import { OrderManagement } from '../order-management';
import { ProductCatalogAdapter } from '../acl/product-catalog-adapter';
import {
    InMemoryProductRepository,
    InMemoryCustomerRepository,
    InMemoryOrderRepository
} from '../infrastructure/repositories';

export class Application {
    // Repositories
    private readonly productRepository: ProductCatalog.ProductRepository;
    private readonly customerRepository: CustomerManagement.CustomerRepository;
    private readonly orderRepository: OrderManagement.OrderRepository;

    // Adapters
    private readonly productCatalogAdapter: ProductCatalogAdapter;

    // Domain services
    private readonly pricingService: OrderManagement.PricingService;

    // Application services
    private readonly productService: ProductCatalog.ProductApplicationService;
    private readonly customerService: CustomerManagement.CustomerApplicationService;
    private readonly orderService: OrderManagement.OrderApplicationService;

    // Infrastructure
    private readonly eventPublisher: SharedKernel.DomainEventPublisher;
    private readonly unitOfWork: SharedKernel.UnitOfWork;

    constructor() {
        this.initializeInfrastructure();
        this.initializeDomainServices();
        this.initializeApplicationServices();
    }

    private initializeInfrastructure(): void {
        // Initialize repositories
        this.productRepository = new InMemoryProductRepository();
        this.customerRepository = new InMemoryCustomerRepository();
        this.orderRepository = new InMemoryOrderRepository();

        // Initialize adapters
        this.productCatalogAdapter = new ProductCatalogAdapter(this.productRepository);

        // Initialize event publisher (simplified)
        this.eventPublisher = new InMemoryEventPublisher();

        // Initialize unit of work (simplified)
        this.unitOfWork = new InMemoryUnitOfWork();
    }

    private initializeDomainServices(): void {
        // Pricing service with mock implementations
        this.pricingService = new OrderManagement.DefaultPricingService(
            new MockDiscountService(),
            new MockTaxService()
        );
    }

    private initializeApplicationServices(): void {
        // Application services
        this.productService = new ProductCatalog.ProductApplicationService(
            this.productRepository,
            new InMemoryCategoryRepository(), // Would be injected
            this.eventPublisher
        );

        this.customerService = new CustomerManagement.CustomerApplicationService(
            this.customerRepository,
            this.eventPublisher
        );

        this.orderService = new OrderManagement.OrderApplicationService(
            this.orderRepository,
            this.customerRepository,
            this.productRepository,
            this.pricingService,
            new MockPaymentService(),
            new MockShippingService(),
            this.eventPublisher,
            this.unitOfWork
        );
    }

    // Public API
    getProductService(): ProductCatalog.ProductApplicationService {
        return this.productService;
    }

    getCustomerService(): CustomerManagement.CustomerApplicationService {
        return this.customerService;
    }

    getOrderService(): OrderManagement.OrderApplicationService {
        return this.orderService;
    }

    // Helper for testing - seed some data
    async seedData(): Promise<void> {
        // Create sample category
        const category = new ProductCatalog.Category("cat-1", "Electronics");

        // Create sample products
        await this.productService.createProduct({
            name: "Wireless Headphones",
            description: "High-quality wireless headphones",
            price: 199.99,
            currency: "USD",
            categoryId: "cat-1",
            sku: "WH-001",
            initialStock: 50,
            images: ["https://example.com/headphones.jpg"],
            tags: ["audio", "wireless"]
        });

        await this.productService.createProduct({
            name: "Bluetooth Speaker",
            description: "Portable Bluetooth speaker",
            price: 79.99,
            currency: "USD",
            categoryId: "cat-1",
            sku: "BS-001",
            initialStock: 30,
            images: ["https://example.com/speaker.jpg"],
            tags: ["audio", "portable"]
        });

        // Create sample customer
        await this.customerService.registerCustomer({
            name: "John Doe",
            email: "john@example.com",
            phone: "+1-555-0123",
            address: {
                street: "123 Main St",
                city: "Anytown",
                state: "CA",
                zipCode: "12345"
            }
        });

        console.log("Sample data seeded successfully");
    }
}

// Simplified implementations for demonstration
class InMemoryEventPublisher implements SharedKernel.DomainEventPublisher {
    async publish(event: SharedKernel.DomainEvent): Promise<void> {
        console.log(`Event published: ${event.constructor.name}`, event);
    }

    async publishEvents(events: SharedKernel.DomainEvent[]): Promise<void> {
        for (const event of events) {
            await this.publish(event);
        }
    }
}

class InMemoryUnitOfWork implements SharedKernel.UnitOfWork {
    async begin(): Promise<void> { /* simplified */ }
    async commit(): Promise<void> { /* simplified */ }
    async rollback(): Promise<void> { /* simplified */ }
    getRepository<T extends SharedKernel.AggregateRoot>(entityClass: new (...args: any[]) => T): SharedKernel.Repository<T> {
        throw new Error("Not implemented");
    }
}

// Mock implementations
class InMemoryCategoryRepository implements ProductCatalog.CategoryRepository {
    private categories = new Map<string, ProductCatalog.Category>();

    async save(category: ProductCatalog.Category): Promise<void> {
        this.categories.set(category.id, category);
    }

    async findById(id: string): Promise<ProductCatalog.Category | null> {
        return this.categories.get(id) || null;
    }

    async findByParent(parentId: string): Promise<ProductCatalog.Category[]> {
        return Array.from(this.categories.values())
            .filter(c => c.parentCategoryId === parentId);
    }

    async findRootCategories(): Promise<ProductCatalog.Category[]> {
        return Array.from(this.categories.values())
            .filter(c => !c.parentCategoryId);
    }

    nextIdentity(): string {
        return `cat-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
    }
}

class MockDiscountService implements OrderManagement.DiscountService {
    async calculateDiscount(order: OrderManagement.Order): Promise<SharedKernel.Money> {
        // Simple mock - no discounts
        return SharedKernel.Money.zero("USD");
    }
}

class MockTaxService implements OrderManagement.TaxService {
    async calculateTax(amount: SharedKernel.Money, address?: SharedKernel.Address): Promise<SharedKernel.Money> {
        // Simple mock - 8.25% tax for CA
        if (address?.state === "CA") {
            return amount.multiply(0.0825);
        }
        return SharedKernel.Money.zero(amount.currency);
    }
}

class MockPaymentService implements OrderManagement.PaymentService {
    async authorizePayment(order: OrderManagement.Order, amount: SharedKernel.Money): Promise<OrderManagement.PaymentResult> {
        // Mock successful payment
        return {
            success: true,
            transactionId: `txn-${Date.now()}`
        };
    }

    async capturePayment(order: OrderManagement.Order, amount: SharedKernel.Money): Promise<OrderManagement.PaymentResult> {
        return { success: true };
    }

    async refundPayment(order: OrderManagement.Order, amount: SharedKernel.Money): Promise<OrderManagement.PaymentResult> {
        return { success: true };
    }
}

class MockShippingService implements OrderManagement.ShippingService {
    async createShipment(order: OrderManagement.Order): Promise<OrderManagement.Shipment> {
        return {
            trackingNumber: `TRK-${Date.now()}`,
            carrier: "UPS",
            estimatedDelivery: new Date(Date.now() + 3 * 24 * 60 * 60 * 1000)
        };
    }

    async calculateDeliveryDate(order: OrderManagement.Order): Promise<Date> {
        return new Date(Date.now() + 3 * 24 * 60 * 60 * 1000);
    }
}
```

## Testing Strategy

### Unit Tests

```typescript
// Example unit test for Order aggregate
describe("Order Aggregate", () => {
    let order: OrderManagement.Order;
    let customerId: SharedKernel.CustomerId;
    let product: ProductCatalog.Product;

    beforeEach(() => {
        customerId = new SharedKernel.CustomerId("cust-123");
        product = new ProductCatalog.Product(
            new SharedKernel.ProductId("prod-456"),
            "Test Product",
            new SharedKernel.Money(100, "USD"),
            "Electronics",
            "TEST-001",
            ProductCatalog.ProductStatus.Active,
            10
        );

        order = new OrderManagement.Order(
            new SharedKernel.OrderId("order-789"),
            customerId,
            OrderManagement.OrderStatus.Draft
        );
    });

    describe("Adding Items", () => {
        it("should add item to empty order", () => {
            order.addItem(product, 2);

            expect(order.items).toHaveLength(1);
            expect(order.totalAmount.amount).toBe(200);
            expect(order.itemCount).toBe(2);
        });

        it("should not allow adding items to placed order", () => {
            order.addItem(product, 1);
            order.setShippingAddress(SharedKernel.Address.createUSAddress(
                "123 Main St", "Anytown", "CA", "12345"
            ));
            order.place();

            expect(() => order.addItem(product, 1))
                .toThrow("Order cannot be modified");
        });

        it("should validate business invariants", () => {
            const outOfStockProduct = new ProductCatalog.Product(
                new SharedKernel.ProductId("prod-999"),
                "Out of Stock",
                new SharedKernel.Money(50, "USD"),
                "Test",
                "OOS-001",
                ProductCatalog.ProductStatus.Active,
                0
            );

            expect(() => order.addItem(outOfStockProduct, 1))
                .toThrow("Product Out of Stock is not available");
        });
    });

    describe("Order Lifecycle", () => {
        beforeEach(() => {
            order.addItem(product, 1);
            order.setShippingAddress(SharedKernel.Address.createUSAddress(
                "123 Main St", "Anytown", "CA", "12345"
            ));
        });

        it("should place order successfully", () => {
            order.place();

            expect(order.status).toBe(OrderManagement.OrderStatus.Placed);
            expect(order.version).toBe(1);
        });

        it("should not place order without items", () => {
            const emptyOrder = new OrderManagement.Order(
                new SharedKernel.OrderId("order-empty"),
                customerId,
                OrderManagement.OrderStatus.Draft
            );

            expect(() => emptyOrder.place())
                .toThrow("Order must have at least one item");
        });
    });
});

// Example integration test
describe("Order Application Service", () => {
    let app: Application;
    let orderService: OrderManagement.OrderApplicationService;

    beforeEach(async () => {
        app = new Application();
        await app.seedData();
        orderService = app.getOrderService();
    });

    it("should create and place order successfully", async () => {
        const createResponse = await orderService.createOrder({
            customerId: "cust-sample", // Would be actual ID from seeded data
            items: [
                { productId: "prod-sample-1", quantity: 2 }
            ]
        });

        expect(createResponse.orderId).toBeDefined();
        expect(createResponse.totalAmount.amount).toBeGreaterThan(0);

        // Place the order
        await orderService.placeOrder(createResponse.orderId);

        const orderDetails = await orderService.getOrderDetails(createResponse.orderId);
        expect(orderDetails.status).toBe(OrderManagement.OrderStatus.Placed);
    });
});
```

## Deployment Considerations

### Containerization

```dockerfile
# Dockerfile for Order Management Service
FROM node:18-alpine

WORKDIR /app

# Copy package files
COPY package*.json ./
RUN npm ci --only=production

# Copy source code
COPY . .

# Build TypeScript
RUN npm run build

# Expose port
EXPOSE 3000

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:3000/health || exit 1

# Start application
CMD ["npm", "start"]
```

### Docker Compose for Development

```yaml
version: '3.8'

services:
  order-management:
    build: .
    ports:
      - "3000:3000"
    environment:
      - NODE_ENV=development
      - DATABASE_URL=postgresql://user:password@db:5432/orders
    depends_on:
      - db
      - message-bus

  db:
    image: postgres:15
    environment:
      - POSTGRES_DB=orders
      - POSTGRES_USER=user
      - POSTGRES_PASSWORD=password
    volumes:
      - postgres_data:/var/lib/postgresql/data

  message-bus:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"

volumes:
  postgres_data:
```

### Kubernetes Deployment

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-management
spec:
  replicas: 3
  selector:
    matchLabels:
      app: order-management
  template:
    metadata:
      labels:
        app: order-management
    spec:
      containers:
      - name: order-management
        image: myregistry/order-management:latest
        ports:
        - containerPort: 3000
        env:
        - name: DATABASE_URL
          valueFrom:
            secretKeyRef:
              name: db-secret
              key: url
        - name: RABBITMQ_URL
          valueFrom:
            secretKeyRef:
              name: mq-secret
              key: url
        livenessProbe:
          httpGet:
            path: /health
            port: 3000
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /ready
            port: 3000
          initialDelaySeconds: 5
          periodSeconds: 5
```

### Monitoring and Observability

```typescript
// Application monitoring
import { collectDefaultMetrics } from 'prom-client';

collectDefaultMetrics();

// Custom metrics
const orderCreatedCounter = new promClient.Counter({
    name: 'orders_created_total',
    help: 'Total number of orders created'
});

const orderProcessingDuration = new promClient.Histogram({
    name: 'order_processing_duration_seconds',
    help: 'Time taken to process orders'
});

// Middleware for tracking
app.use((req, res, next) => {
    const start = Date.now();
    res.on('finish', () => {
        const duration = Date.now() - start;
        orderProcessingDuration.observe(duration / 1000);
    });
    next();
});

// Health checks
app.get('/health', (req, res) => {
    // Check database connectivity, message bus, etc.
    res.status(200).json({ status: 'healthy' });
});

app.get('/ready', (req, res) => {
    // Check if service is ready to accept traffic
    res.status(200).json({ status: 'ready' });
});

// Structured logging
import winston from 'winston';

const logger = winston.createLogger({
    level: 'info',
    format: winston.format.json(),
    defaultMeta: { service: 'order-management' },
    transports: [
        new winston.transports.Console(),
        new winston.transports.File({ filename: 'error.log', level: 'error' }),
        new winston.transports.File({ filename: 'combined.log' }),
    ],
});

// Usage
logger.info('Order created', { orderId: '123', customerId: '456' });
logger.error('Payment failed', { error: new Error('Card declined'), orderId: '123' });
```

## Evolution Planning

### System Growth Strategies

1. **Vertical Scaling**: Add more resources to existing services
2. **Horizontal Scaling**: Add more instances of services
3. **Functional Decomposition**: Split services by business capability
4. **Data Partitioning**: Split data across multiple databases
5. **CQRS Implementation**: Separate read and write models
6. **Event Sourcing**: Store events instead of current state

### Migration Strategies

1. **Strangler Fig Pattern**: Gradually replace legacy components
2. **Parallel Run**: Run old and new systems simultaneously
3. **Feature Flags**: Enable new features gradually
4. **Blue-Green Deployment**: Switch traffic between versions
5. **Canary Releases**: Roll out to subset of users first

### Monitoring Evolution

- **Performance Metrics**: Response times, throughput, error rates
- **Business Metrics**: Order completion rate, customer satisfaction
- **Technical Metrics**: Memory usage, CPU utilization, database connections
- **Domain Metrics**: Aggregate sizes, event frequencies, boundary violations

## Key Takeaways

1. **Complete Implementation**: This example shows a full DDD implementation with all building blocks
2. **Bounded Contexts**: Clear separation of concerns with proper integration patterns
3. **Domain Models**: Rich entities, value objects, and aggregates with business logic
4. **Services**: Proper separation of domain and application services
5. **Infrastructure**: Repository pattern, event publishing, and unit of work
6. **Testing**: Comprehensive unit and integration testing strategies
7. **Architecture**: Clean architecture with dependency inversion
8. **Evolution**: Planning for system growth and change management

## Next Steps

This complete example provides a solid foundation for building DDD applications. The patterns and practices demonstrated here can be applied to real-world systems of any size. Remember that DDD is a journey - start with the fundamentals and evolve your approach as you gain experience.

Key areas to explore further:
- Event sourcing for complex domains
- CQRS for high-performance systems
- Microservices deployment and orchestration
- Distributed system patterns and challenges
- Advanced testing strategies and test automation
