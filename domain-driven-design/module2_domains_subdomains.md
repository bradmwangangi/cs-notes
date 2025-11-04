# Module 2: Domain and Subdomains

## Understanding the Domain

The domain is the sphere of knowledge and activity around which the application logic revolves. It's the business area your software serves. For example:

- An e-commerce platform's domain includes products, orders, customers, payments, shipping, inventory
- A healthcare system's domain includes patients, appointments, treatments, billing, insurance, medical records
- A banking system's domain includes accounts, transactions, loans, compliance, risk management, customer service
- A social media platform's domain includes users, posts, connections, feeds, notifications, advertising

The domain encompasses all the concepts, rules, processes, and constraints that make up the business problem your software solves. It's not just about data storage or user interfaces—it's about modeling the real business activities and rules.

### Domain Characteristics

**Complex Domains** typically have:
- **Multiple stakeholders** with different perspectives
- **Interrelated business rules** that affect each other
- **Evolving requirements** as business needs change
- **Specialized terminology** and concepts
- **Integration requirements** with external systems
- **Regulatory compliance** requirements

**Simple Domains** might have:
- Straightforward CRUD operations
- Minimal business rules
- Stable requirements
- Generic terminology

## The Importance of Domain Knowledge

DDD emphasizes deep understanding of the domain because:

- **Better software**: Domain knowledge leads to software that actually solves business problems and provides value
- **Easier maintenance**: When business rules change, developers who understand the domain can adapt quickly
- **Effective communication**: Shared understanding reduces misunderstandings between business and technical teams
- **Competitive advantage**: Software that deeply understands business needs can provide unique value
- **Reduced risk**: Better domain understanding reduces the risk of building the wrong features
- **Team productivity**: Clear domain understanding reduces time spent clarifying requirements

### Building Domain Knowledge

**Techniques for understanding the domain:**
- **Interviews with domain experts**: One-on-one sessions to understand business processes
- **Workshops and collaborative modeling**: Group sessions to build shared understanding
- **Observation**: Watching how work actually gets done (not how it's documented)
- **Document analysis**: Reviewing existing business documents, processes, and rules
- **Prototyping**: Building small models to test understanding
- **Event storming**: Collaborative discovery of business events and processes

## Identifying Subdomains

A large domain is typically composed of multiple subdomains. DDD categorizes subdomains into three types based on their strategic importance to the business:

### 1. Core Domain
The subdomain that provides the primary competitive advantage. This is where you should invest the most effort and apply the richest modeling.

**Characteristics:**
- Provides unique business value
- Differentiates you from competitors
- Requires deep domain expertise
- Should be built in-house
- Receives the most attention and resources

**Examples:**
- For an e-commerce company: Product recommendation engine, personalized shopping experience
- For a bank: Risk assessment algorithms, fraud detection systems
- For a social media platform: Content discovery and ranking algorithms
- For a healthcare provider: Treatment planning and patient care coordination
- For a logistics company: Route optimization and delivery scheduling

### 2. Supporting Subdomains
Important for business success but not core differentiators. They support the core domain but could potentially be outsourced or handled by third-party solutions.

**Characteristics:**
- Necessary for business operations
- Could be outsourced if cost-effective
- May use off-the-shelf solutions
- Requires less domain expertise
- Still important but not strategic differentiators

**Examples:**
- User authentication and authorization in any system
- Payroll processing in a manufacturing company
- Email delivery and communication in a platform
- Basic reporting and analytics
- User interface frameworks
- Data backup and recovery

### 3. Generic Subdomains
Standard problems that can be solved with off-the-shelf solutions. These are commodity problems that don't provide competitive advantage.

**Characteristics:**
- Well-understood problems
- Commercial or open-source solutions available
- Minimal customization needed
- Low strategic value
- Often outsourced or handled by infrastructure teams

**Examples:**
- Logging and monitoring infrastructure
- File storage and content delivery networks (CDN)
- Payment processing (using Stripe, PayPal, etc.)
- Message queuing systems
- Database administration
- Network infrastructure
- Security scanning and compliance tools

## Techniques for Identifying Subdomains

### 1. Business Capability Analysis
Analyze what the business does at a high level:
- **E-commerce**: Product catalog, order processing, inventory management, customer service
- **Healthcare**: Patient management, appointment scheduling, treatment planning, billing
- **Banking**: Account management, transaction processing, loan origination, compliance

### 2. Organizational Structure Analysis
Look at how the business is organized:
- Different departments often correspond to subdomains
- Team boundaries may indicate subdomain boundaries
- Reporting structures can reveal domain relationships

### 3. Process Analysis
Examine business processes and workflows:
- Identify the main business activities
- Look for processes that could operate independently
- Find integration points between processes

### 4. Data Analysis
Analyze how data flows through the system:
- Identify major data entities and their relationships
- Look for data that is used in different contexts
- Find data ownership and stewardship patterns

### 5. Stakeholder Analysis
Understand different user roles and their needs:
- Different user types may represent different subdomains
- Conflicting requirements may indicate separate contexts
- Different priorities may suggest different strategic importance

## Business Value Analysis

When classifying subdomains, consider:

### Strategic Value
- Does this provide competitive advantage?
- Is this unique to our business?
- Would losing this capability hurt our market position?

### Business Risk
- How critical is this to daily operations?
- What happens if this fails?
- How difficult is this to replace?

### Development Cost
- How complex is this to build?
- What expertise is required?
- How long will it take to develop?

### Maintenance Cost
- How often does this change?
- How complex is ongoing maintenance?
- What skills are needed for maintenance?

### Outsourcing Potential
- Are there good third-party solutions?
- Can this be safely outsourced?
- What are the integration requirements?

## Domain Modeling Basics

Domain modeling is the process of creating an abstraction of the domain that incorporates both data and behavior. Key aspects:

### 1. Entities vs Value Objects
- **Entities**: Objects with identity that change over time (have lifecycle)
- **Value Objects**: Objects without identity, defined by their attributes (immutable)

### 2. Associations and Relationships
Understanding how domain objects relate to each other:
- **One-to-one**: Customer has one primary address
- **One-to-many**: Order has many order items
- **Many-to-many**: Products belong to multiple categories

### 3. Business Rules and Invariants
Rules that must always be true within the domain:
- Order total must equal sum of item totals
- Account balance cannot be negative
- Customer must have valid email address

### 4. Business Processes
The workflows and operations that happen within the domain:
- Order fulfillment process
- Customer onboarding process
- Product approval workflow

### 5. Domain Events
Important business occurrences that domain experts care about:
- Order placed, order shipped, payment received
- Customer registered, customer upgraded
- Product added to catalog, product discontinued

## Domain Experts and Ubiquitous Language

### Domain Experts
These are the people who understand the business deeply:
- **Business analysts**: Document and analyze business processes
- **Product managers**: Define product vision and requirements
- **Subject matter experts**: Deep knowledge in specific business areas
- **Long-time employees**: Practical experience with business operations
- **Customers/users**: External perspective on business processes
- **Regulators**: Understanding of compliance requirements

### Working with Domain Experts
- **Regular collaboration**: Include domain experts in development process
- **Joint modeling sessions**: Work together to build domain models
- **Feedback loops**: Show work early and often for validation
- **Documentation**: Record decisions and rationales
- **Education**: Teach domain experts about technical constraints

### Ubiquitous Language
A shared language developed by domain experts and developers that:
- Uses business terminology consistently
- Is used in code, documentation, and conversations
- Evolves as understanding deepens
- Bridges the gap between business and technical teams
- Reduces translation errors and misunderstandings

### Building Ubiquitous Language
1. **Start with business terms**: Use words from the business domain
2. **Create glossary**: Document terms and their meanings
3. **Use in code**: Name classes, methods, and variables with business terms
4. **Refine through use**: Update language as understanding improves
5. **Document discrepancies**: Note when technical terms differ from business terms

## Code Example: Modeling a Simple Domain

Let's model a simple e-commerce domain with products, orders, and customers:

```typescript
// Value Object - represents a concept without identity
class Money {
    constructor(
        private readonly _amount: number,
        private readonly _currency: string
    ) {
        if (_amount < 0) {
            throw new Error("Amount cannot be negative");
        }
        if (!_currency) {
            throw new Error("Currency is required");
        }
    }

    get amount(): number {
        return this._amount;
    }

    get currency(): string {
        return this._currency;
    }

    add(other: Money): Money {
        this.ensureSameCurrency(other);
        return new Money(this._amount + other._amount, this._currency);
    }

    subtract(other: Money): Money {
        this.ensureSameCurrency(other);
        const result = this._amount - other._amount;
        if (result < 0) {
            throw new Error("Cannot subtract: result would be negative");
        }
        return new Money(result, this._currency);
    }

    multiply(factor: number): Money {
        if (factor < 0) {
            throw new Error("Cannot multiply by negative factor");
        }
        return new Money(this._amount * factor, this._currency);
    }

    equals(other: Money): boolean {
        return this._amount === other._amount && this._currency === other._currency;
    }

    toString(): string {
        return `${this._currency} ${this._amount.toFixed(2)}`;
    }

    private ensureSameCurrency(other: Money): void {
        if (this._currency !== other._currency) {
            throw new Error(`Cannot operate on different currencies: ${this._currency} vs ${other._currency}`);
        }
    }
}

// Value Object for Email
class Email {
    constructor(private readonly _value: string) {
        if (!this.isValidEmail(_value)) {
            throw new Error("Invalid email format");
        }
    }

    get value(): string {
        return this._value;
    }

    equals(other: Email): boolean {
        return this._value === other._value;
    }

    private isValidEmail(email: string): boolean {
        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        return emailRegex.test(email);
    }
}

// Entity - has identity and lifecycle
class Product {
    constructor(
        private readonly _id: string,
        private _name: string,
        private _price: Money,
        private _category: string,
        private _inStock: boolean = true
    ) {
        if (!_id) throw new Error("Product ID is required");
        if (!_name) throw new Error("Product name is required");
        if (!_category) throw new Error("Product category is required");
    }

    get id(): string {
        return this._id;
    }

    get name(): string {
        return this._name;
    }

    get price(): Money {
        return this._price;
    }

    get category(): string {
        return this._category;
    }

    get inStock(): boolean {
        return this._inStock;
    }

    // Business behavior
    changePrice(newPrice: Money): void {
        if (newPrice.amount <= 0) {
            throw new Error("Price must be positive");
        }
        this._price = newPrice;
    }

    discontinue(): void {
        this._inStock = false;
    }

    // Business rule: products must have valid pricing
    isValid(): boolean {
        return this._price.amount > 0 && this._name.trim().length > 0;
    }
}

// Another Entity with more complex behavior
class Customer {
    private readonly _addresses: Address[] = [];

    constructor(
        private readonly _id: string,
        private _name: string,
        private _email: Email,
        private _loyaltyPoints: number = 0
    ) {
        if (!_id) throw new Error("Customer ID is required");
        if (!_name) throw new Error("Customer name is required");
    }

    get id(): string {
        return this._id;
    }

    get name(): string {
        return this._name;
    }

    get email(): Email {
        return this._email;
    }

    get loyaltyPoints(): number {
        return this._loyaltyPoints;
    }

    get addresses(): readonly Address[] {
        return [...this._addresses];
    }

    // Business behavior
    updateEmail(newEmail: Email): void {
        // Business rule: email changes require verification
        // (simplified - would involve email verification in real system)
        this._email = newEmail;
    }

    addAddress(address: Address): void {
        // Business rule: limit to 5 addresses per customer
        if (this._addresses.length >= 5) {
            throw new Error("Customer cannot have more than 5 addresses");
        }
        this._addresses.push(address);
    }

    earnLoyaltyPoints(points: number): void {
        if (points < 0) {
            throw new Error("Cannot earn negative points");
        }
        this._loyaltyPoints += points;
    }

    spendLoyaltyPoints(points: number): void {
        if (points < 0) {
            throw new Error("Cannot spend negative points");
        }
        if (points > this._loyaltyPoints) {
            throw new Error("Insufficient loyalty points");
        }
        this._loyaltyPoints -= points;
    }
}

// Value Object for Address
class Address {
    constructor(
        private readonly _street: string,
        private readonly _city: string,
        private readonly _state: string,
        private readonly _zipCode: string,
        private readonly _country: string = "USA"
    ) {
        this.validate();
    }

    private validate(): void {
        if (!this._street?.trim()) throw new Error("Street is required");
        if (!this._city?.trim()) throw new Error("City is required");
        if (!this._state?.trim()) throw new Error("State is required");
        if (!this._zipCode?.trim()) throw new Error("Zip code is required");
    }

    get street(): string { return this._street; }
    get city(): string { return this._city; }
    get state(): string { return this._state; }
    get zipCode(): string { return this._zipCode; }
    get country(): string { return this._country; }

    equals(other: Address): boolean {
        return (
            this._street === other._street &&
            this._city === other._city &&
            this._state === other._state &&
            this._zipCode === other._zipCode &&
            this._country === other._country
        );
    }

    toString(): string {
        return `${this._street}, ${this._city}, ${this._state} ${this._zipCode}, ${this._country}`;
    }
}

// Usage demonstrating domain concepts
const product = new Product("prod-123", "Wireless Headphones", new Money(199.99, "USD"), "Electronics");
const customer = new Customer("cust-456", "Alice Johnson", new Email("alice@example.com"));

console.log(`Product: ${product.name} costs ${product.price.toString()}`);
console.log(`Customer: ${customer.name} has ${customer.loyaltyPoints} points`);

// Demonstrate business behavior
customer.earnLoyaltyPoints(100);
customer.spendLoyaltyPoints(50);
console.log(`Customer now has ${customer.loyaltyPoints} points`);

const address = new Address("123 Main St", "Anytown", "CA", "12345");
customer.addAddress(address);
console.log(`Customer address: ${customer.addresses[0].toString()}`);
```

## Case Study: Online Learning Platform

Let's analyze an online learning platform to identify subdomains:

### Overall Domain: Online Education Platform

**Core Domain:**
- Course content creation and management (rich authoring tools, content versioning)
- Student learning path optimization (adaptive learning algorithms)
- Assessment and certification (advanced testing, credentialing)
- Learning analytics and personalization

**Supporting Subdomains:**
- User management and authentication (registration, profiles, permissions)
- Video streaming infrastructure (adaptive bitrate, content delivery)
- Discussion forums and community features
- Progress tracking and reporting
- Instructor tools (grading, communication)

**Generic Subdomains:**
- Email notifications and communication
- Payment processing and subscription management
- Search and discovery (using Elasticsearch)
- Analytics and reporting infrastructure
- File storage and content management
- User interface frameworks

### Strategic Analysis

**Investment Focus:**
- 70% of development effort on core domain
- 20% on supporting subdomains (some can be outsourced)
- 10% on generic subdomains (mostly configuration and integration)

**Team Organization:**
- Core domain: Specialized learning platform engineers
- Supporting: Full-stack developers, some specialized roles
- Generic: Infrastructure team, DevOps

## Common Pitfalls

### 1. Misidentifying Core Domains
- Treating generic problems as core differentiators
- Failing to recognize true competitive advantages
- Over-investing in commodity functionality

### 2. Ignoring Domain Complexity
- Underestimating the complexity of business rules
- Not involving domain experts early enough
- Building generic CRUD applications for complex domains

### 3. Poor Ubiquitous Language
- Using technical jargon with business stakeholders
- Allowing different terms for the same concept
- Not maintaining consistency across the team

### 4. Subdomain Creep
- Allowing supporting subdomains to become too complex
- Not outsourcing generic functionality when appropriate
- Blurring boundaries between subdomain types

## Key Takeaways

1. The domain is the business area your software serves, including all concepts, rules, and processes
2. Complex domains should be broken into subdomains to manage complexity and focus effort
3. Focus your best efforts on the core domain where competitive advantage lies
4. Domain modeling includes both data structures and business behavior
5. Ubiquitous language ensures consistent communication between business and technical teams
6. Subdomain classification guides resource allocation and technology choices
7. Domain understanding requires ongoing collaboration with business experts

## Next Steps

In Module 3, we'll explore bounded contexts - how to define clear boundaries around different models within your system and manage relationships between them.

## Exercise

Choose a system you're familiar with and:

1. Define its overall domain and primary business purpose
2. Identify 3-5 subdomains and classify them as core/supporting/generic
3. For each subdomain, explain why it fits that classification
4. List 5-7 key business terms that would be part of the ubiquitous language
5. Sketch a simple domain model with 3-4 entities and their relationships
6. Identify who the domain experts would be for this system
7. Describe how you would approach building domain knowledge for this system
8. Based on your analysis, where would you recommend focusing development effort?

**Bonus:** If you have access to business stakeholders, conduct a short interview to validate your subdomain analysis.
