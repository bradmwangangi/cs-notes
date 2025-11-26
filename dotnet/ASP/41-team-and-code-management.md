# 40. Team & Code Management

## Overview
Enterprise systems aren't built by individuals—they're built by teams. Code management practices, workflows, documentation, and onboarding processes determine if a team can effectively collaborate, maintain quality, and scale development. This topic covers the organizational and process aspects that make large teams effective.

---

## 1. Git Workflows

### 1.1 Branch Strategy: Git Flow

```
Main Branches:
├─ main: Production-ready code
│  └─ Tag: v1.0.0, v1.0.1, etc.
│
└─ develop: Integration branch
   └─ Contains latest development changes

Supporting Branches:
├─ feature/order-tracking: New features
│  ├─ Branches from: develop
│  └─ Merges back to: develop via PR
│
├─ bugfix/payment-timeout: Bug fixes
│  ├─ Branches from: develop
│  └─ Merges back to: develop via PR
│
├─ hotfix/security-patch: Production fixes
│  ├─ Branches from: main
│  ├─ Merges back to: main + develop
│  └─ Tag: v1.0.1 (patch version)
│
└─ release/1.1.0: Release preparation
   ├─ Branches from: develop
   ├─ Merges to: main + develop
   └─ Only bug fixes allowed
```

### 1.2 Workflow Example

```bash
# Create feature branch
git checkout -b feature/order-notifications develop

# Work on feature
git commit -m "feat: add order notification service"
git commit -m "test: add notification tests"

# Push and create pull request
git push origin feature/order-notifications

# After code review and approval:
# Pull request is merged to develop
# Branch is deleted

# For release:
git checkout -b release/1.1.0 develop

# Only bug fixes and version bumps
git commit -m "chore: bump version to 1.1.0"

# Merge to main
git checkout main
git merge --no-ff release/1.1.0
git tag -a v1.1.0 -m "Release version 1.1.0"

# Merge back to develop
git checkout develop
git merge --no-ff release/1.1.0

# Delete release branch
git branch -d release/1.1.0

# For hotfix:
git checkout -b hotfix/security-issue main

git commit -m "fix: security vulnerability in payment processing"

# Merge to main
git checkout main
git merge --no-ff hotfix/security-issue
git tag -a v1.0.1 -m "Hotfix version 1.0.1"

# Merge back to develop
git checkout develop
git merge --no-ff hotfix/security-issue

# Delete hotfix branch
git branch -d hotfix/security-issue
```

### 1.3 Commit Message Convention

```
Conventional Commits format:

<type>(<scope>): <subject>

<body>

<footer>

Types:
- feat: New feature
- fix: Bug fix
- docs: Documentation changes
- style: Code style (formatting, missing semicolons)
- refactor: Code refactoring without feature changes
- perf: Performance improvements
- test: Adding/updating tests
- chore: Build, dependencies, tooling

Examples:
feat(orders): add order cancellation feature
fix(payment): handle timeout in payment gateway
docs(api): update API documentation
test(inventory): add inventory reservation tests
perf(database): optimize order query with indexes

Benefits:
- Readable git history
- Automated changelog generation
- Easy bisect for bug tracking
- Clear PR descriptions
```

---

## 2. Code Review Process

### 2.1 Pull Request Template

```markdown
# Description
Brief explanation of changes

## Type of Change
- [ ] New feature
- [ ] Bug fix
- [ ] Breaking change
- [ ] Documentation update

## Related Issues
Closes #123

## Changes Made
- Change 1
- Change 2
- Change 3

## Testing
- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Tested locally
- [ ] Tested in staging

## Performance Impact
No significant performance changes expected

## Database Changes
- [ ] No database changes
- [ ] Schema migration included (see migration file)
- [ ] Migration is backward compatible

## Breaking Changes
None

## Checklist
- [ ] Code follows project style guidelines
- [ ] No new warnings generated
- [ ] Tested against supported .NET versions
- [ ] Documentation updated
- [ ] CHANGELOG.md updated
- [ ] No hard-coded secrets/credentials
```

### 2.2 Code Review Checklist

```csharp
// Code Review Guidelines

/*
CORRECTNESS
□ Does the code solve the stated problem?
□ Are all edge cases handled?
□ Are null checks present where needed?
□ Are exceptions caught and handled appropriately?
□ Is error handling consistent with codebase patterns?

DESIGN & ARCHITECTURE
□ Does code follow DDD/SOLID principles?
□ Is code well-structured and maintainable?
□ Are dependencies correctly injected?
□ Does the code fit the overall architecture?
□ Are new patterns introduced justified?

PERFORMANCE
□ Could there be n+1 query problems?
□ Are appropriate indexes used?
□ Is caching leveraged where beneficial?
□ Are there obvious memory leaks?
□ Are async/await used appropriately?

SECURITY
□ Are inputs validated?
□ Could there be SQL injection?
□ Are secrets hardcoded?
□ Is authentication/authorization checked?
□ Is sensitive data logged?
□ Are there OWASP vulnerabilities?

TESTING
□ Are tests comprehensive?
□ Do tests verify actual behavior?
□ Are edge cases tested?
□ Is code coverage acceptable (75%+)?
□ Are tests readable and maintainable?

DOCUMENTATION
□ Is code self-documenting?
□ Are complex sections commented?
□ Is API documentation updated?
□ Are migration guides provided for breaking changes?

CONVENTIONS
□ Does code follow team style guide?
□ Are naming conventions consistent?
□ Are files organized logically?
□ Are imports organized?

REVIEW ATTITUDE
□ "Why is this done this way?" (curiosity)
□ "I see a potential issue..." (constructive)
□ "Great approach to..." (positive reinforcement)
□ Avoid: "This is wrong", "You should know this"
*/
```

### 2.3 Review Process Workflow

```
Developer submits PR
        ↓
Automated checks run
├─ Build succeeds? ✓
├─ Tests pass? ✓
├─ Code analysis passes? ✓
└─ Coverage meets threshold? ✓
        ↓
Code review requested from team members
        ↓
Reviewers examine code
├─ Leave suggestions
├─ Request changes
└─ Approve
        ↓
If changes requested:
├─ Developer updates code
├─ Automated checks run again
└─ Reviewers review changes
        ↓
Approval from 2+ reviewers
        ↓
PR merged to develop
        ↓
Deployed to staging for testing
        ↓
Merged to main for production release
```

---

## 3. Code Quality Standards

### 3.1 SOLID Principles Enforcement

```csharp
// SOLID principles with examples

namespace OrderManagement.CodeQuality
{
    // SINGLE RESPONSIBILITY PRINCIPLE
    // Each class has one reason to change
    
    // ❌ BAD: Order class handles everything
    public class BadOrder
    {
        public void Create() { }
        public void Validate() { }
        public void SaveToDatabase() { }
        public void SendEmail() { }
        public void GenerateReport() { }
    }
    
    // ✅ GOOD: Separated concerns
    public class Order { }  // Domain logic only
    public class OrderValidator { }  // Validation
    public class OrderRepository { }  // Persistence
    public class OrderNotificationService { }  // Communication
    public class OrderReportGenerator { }  // Reporting
    
    
    // OPEN/CLOSED PRINCIPLE
    // Open for extension, closed for modification
    
    // ❌ BAD: Need to modify existing code
    public class PaymentProcessor
    {
        public void Process(string provider)
        {
            if (provider == "Stripe")
                // Process with Stripe
            else if (provider == "PayPal")
                // Process with PayPal
            else if (provider == "Square")
                // Process with Square
        }
    }
    
    // ✅ GOOD: Extend with new implementations
    public interface IPaymentGateway
    {
        Task<PaymentResult> ProcessAsync(Payment payment);
    }
    
    public class StripeGateway : IPaymentGateway { }
    public class PayPalGateway : IPaymentGateway { }
    public class SquareGateway : IPaymentGateway { }
    
    
    // LISKOV SUBSTITUTION PRINCIPLE
    // Subtypes must be substitutable for base types
    
    // ❌ BAD: Square doesn't fit rectangle interface
    public class Rectangle
    {
        public virtual int Width { get; set; }
        public virtual int Height { get; set; }
    }
    
    public class Square : Rectangle
    {
        public override int Width
        {
            get { return base.Width; }
            set { base.Width = base.Height = value; }  // Violates contract
        }
    }
    
    // ✅ GOOD: Use composition or rethink hierarchy
    public abstract class Shape { }
    public class Rectangle : Shape { }
    public class Square : Shape { }
    
    
    // INTERFACE SEGREGATION PRINCIPLE
    // Clients shouldn't depend on interfaces they don't use
    
    // ❌ BAD: Force implementations to implement everything
    public interface IOrderService
    {
        Task<Order> GetOrderAsync(int id);
        Task CreateOrderAsync(Order order);
        Task CancelOrderAsync(int id);
        Task GenerateReportAsync();
        Task SendEmailAsync();
        void PrintLabel();
    }
    
    // ✅ GOOD: Segregate into focused interfaces
    public interface IOrderQuery
    {
        Task<Order> GetOrderAsync(int id);
    }
    
    public interface IOrderCommand
    {
        Task CreateOrderAsync(Order order);
        Task CancelOrderAsync(int id);
    }
    
    public interface IOrderReporting
    {
        Task GenerateReportAsync();
    }
    
    
    // DEPENDENCY INVERSION PRINCIPLE
    // Depend on abstractions, not concretions
    
    // ❌ BAD: Direct dependency on concrete class
    public class OrderService
    {
        private readonly OrderRepository _repository = new();  // Tightly coupled
        
        public async Task<Order> GetOrderAsync(int id)
        {
            return await _repository.GetByIdAsync(id);
        }
    }
    
    // ✅ GOOD: Depend on abstraction
    public class OrderService
    {
        private readonly IOrderRepository _repository;
        
        public OrderService(IOrderRepository repository)
        {
            _repository = repository;
        }
        
        public async Task<Order> GetOrderAsync(int id)
        {
            return await _repository.GetByIdAsync(id);
        }
    }
}
```

### 3.2 Code Metrics

```csharp
// Monitor code quality with metrics

namespace OrderManagement.Metrics
{
    public class CodeQualityMetrics
    {
        // Cyclomatic Complexity: Number of distinct paths through code
        // Goal: < 10 per method
        
        // ❌ HIGH COMPLEXITY
        public string GetOrderStatus(Order order)
        {
            if (order.Status == OrderStatus.Pending)
            {
                if (order.Items.Count > 0)
                {
                    if (order.Total.Amount > 0)
                    {
                        if (DateTime.UtcNow - order.CreatedAt > TimeSpan.FromDays(1))
                        {
                            return "Pending (old)";
                        }
                        return "Pending";
                    }
                }
            }
            // ... many more conditions
        }
        
        // ✅ LOW COMPLEXITY
        public string GetOrderStatus(Order order)
        {
            return order switch
            {
                _ when IsOldPendingOrder(order) => "Pending (old)",
                _ when order.Status == OrderStatus.Pending => "Pending",
                _ => order.Status.ToString()
            };
        }
        
        // Depth of Inheritance: How deep is inheritance tree?
        // Goal: < 3 levels deep
        
        // Maintainability Index (0-100)
        // Goal: > 70 (maintainable)
        // Factors: Lines of code, complexity, coverage
    }
}

// SonarQube rules (example):
/*
Critical:
- Missing null check
- SQL injection vulnerability
- Hard-coded credentials

Major:
- Cognitive complexity > 15
- Code duplication > 3%
- Test coverage < 75%

Minor:
- Variable naming conventions
- Method size > 50 lines
- Unused imports
*/
```

---

## 4. Documentation

### 4.1 Architecture Decision Records (ADR)

```markdown
# ADR-001: Using CQRS Pattern for Order Queries

Date: 2024-01-15
Status: Accepted

## Context
The order querying subsystem needs to handle:
- Complex filtering and sorting
- High read load (10x writes)
- Real-time reporting requirements

Monolithic read/write model was becoming bottleneck.
Database queries were slow (join-heavy).

## Decision
Implement CQRS (Command Query Responsibility Segregation):
- Commands: Write to primary normalized model
- Queries: Read from denormalized read model
- Event handlers: Synchronize read model

## Consequences

### Positive
- Query performance improved 10x
- Independent scaling of read/write paths
- Cleaner separation of concerns
- Easier to optimize queries

### Negative
- Eventual consistency (consistency window ~100ms)
- Increased operational complexity
- Data synchronization failures possible
- Team needs to understand event patterns

### Mitigation
- Monitoring for read model lag
- Automatic read model rebuilding
- Clear documentation on consistency windows
- Team training on CQRS patterns

## Alternatives Considered
1. Database query optimization with indexes
   - Would help but insufficient for scale
   
2. Caching layer only
   - Doesn't address fundamental schema mismatch

## Related Decisions
- ADR-002: Event sourcing for audit trail
- ADR-003: Event handlers for integration
```

### 4.2 API Documentation

```csharp
// OpenAPI/Swagger documentation

namespace OrderManagement.API.Documentation
{
    [ApiController]
    [Route("api/orders")]
    [Tags("Orders")]
    public class OrdersController : ControllerBase
    {
        /// <summary>
        /// Creates a new order
        /// </summary>
        /// <remarks>
        /// Creates an order for the authenticated customer.
        /// 
        /// Sample request:
        ///     POST /api/orders
        ///     {
        ///       "items": [
        ///         { "productId": 1, "quantity": 2, "price": 99.99 }
        ///       ]
        ///     }
        /// </remarks>
        /// <param name="request">Order creation request</param>
        /// <returns>Created order ID</returns>
        /// <response code="201">Order created successfully</response>
        /// <response code="400">Invalid input or insufficient inventory</response>
        /// <response code="401">Unauthorized</response>
        /// <response code="409">Payment declined</response>
        [HttpPost]
        [ProducesResponseType(typeof(int), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<int>> CreateOrder(
            [FromBody] CreateOrderRequest request)
        {
            // Implementation
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// Gets order details
        /// </summary>
        /// <param name="id">Order ID</param>
        /// <returns>Order details</returns>
        /// <response code="200">Order found</response>
        /// <response code="404">Order not found</response>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<OrderDto>> GetOrder(int id)
        {
            throw new NotImplementedException();
        }
    }
}

// Swagger configuration
public class SwaggerConfiguration
{
    public static void ConfigureSwagger(IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Order Management API",
                Version = "v1",
                Description = "API for managing orders",
                Contact = new OpenApiContact
                {
                    Name = "Dev Team",
                    Email = "dev@company.com"
                }
            });
            
            // Include XML comments
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            options.IncludeXmlComments(xmlPath);
            
            // Add JWT authentication
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecurityScheme { Reference = new OpenApiReference { 
                    Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, 
                    new string[] { } }
            });
        });
    }
}
```

---

## 5. Onboarding Process

### 5.1 Developer Onboarding Checklist

```markdown
# Developer Onboarding Checklist

## Week 1: Environment Setup
- [ ] Clone repositories
- [ ] Install required tools (Visual Studio, Docker, etc.)
- [ ] Setup database locally
- [ ] Run tests locally
- [ ] Setup Git pre-commit hooks
- [ ] Configure IDE with project style settings
- [ ] Add to team communication channels (Slack, etc.)
- [ ] Get access to bug tracking system
- [ ] Read CONTRIBUTING.md
- [ ] Read project README

## Week 2: Codebase Understanding
- [ ] Review architecture documentation
- [ ] Walk through main application flow
- [ ] Understand folder structure
- [ ] Study key domain models
- [ ] Review existing design patterns
- [ ] Understand testing approach
- [ ] Look at recent commits to understand current work
- [ ] Study deployment process
- [ ] Review security requirements
- [ ] Understand coding standards

## Week 3: First Contribution
- [ ] Pick starter issue (labeled "good first issue")
- [ ] Write code following team patterns
- [ ] Submit pull request
- [ ] Incorporate review feedback
- [ ] See PR merged
- [ ] Deploy to staging
- [ ] Observe behavior in staging

## Week 4: Ramping Up
- [ ] Take on slightly more complex issue
- [ ] Pair program with senior developer
- [ ] Review others' pull requests
- [ ] Attend architecture discussion
- [ ] Ask questions without hesitation
- [ ] Contribute to documentation improvements
- [ ] Setup local monitoring/debugging
- [ ] Learn about on-call rotation

## Ongoing
- [ ] Attend team meetings
- [ ] Participate in code reviews
- [ ] Learn from more experienced developers
- [ ] Document what you learned
- [ ] Suggest process improvements
```

### 5.2 Knowledge Transfer

```
KNOWLEDGE TRANSFER SESSIONS

1. Architecture Overview (1 hour)
   - System design high-level
   - Key components and their responsibilities
   - Technology choices and why

2. Domain Deep Dive (2 hours)
   - Core business concepts
   - Domain models
   - Key bounded contexts

3. Data Access & Queries (1.5 hours)
   - Database schema
   - Key queries and optimization
   - ORM patterns
   - Testing data access

4. API & Integration (1.5 hours)
   - API design patterns
   - Authentication/authorization
   - External service integrations
   - Error handling

5. Testing & Quality (1 hour)
   - Testing strategy
   - How to write tests
   - CI/CD pipeline
   - Code review process

6. Deployment & Operations (1 hour)
   - How to deploy
   - Monitoring and alerting
   - On-call responsibilities
   - Incident response
```

---

## Summary

Team and code management fundamentals:

**Git Workflows:**
- Clear branching strategy (Git Flow)
- Conventional commits
- Feature flags for safe releases

**Code Review:**
- Comprehensive review checklist
- Focus on correctness, design, security
- Constructive feedback culture

**Code Quality:**
- SOLID principles enforcement
- Complexity limits
- Code coverage > 75%

**Documentation:**
- Architecture Decision Records
- API documentation with Swagger
- Clear README and contributing guides

**Onboarding:**
- Structured checklist
- Gradual complexity increase
- Knowledge transfer sessions
- Pair programming

**Key Metrics:**
- Code review turnaround time (< 24h)
- PR merge time (< 1 week)
- Deployment frequency (daily+)
- Time to on-board (< 1 month productive)
- Code review participation (all developers)

Best practices:
- Automate what can be automated
- Review code, not people
- Document decisions
- Make onboarding smooth
- Invest in developer experience
- Measure and improve continuously

Common mistakes:
- No review standards (quality degrades)
- Siloed knowledge (bus factor)
- Poor documentation
- Slow review cycle
- Inconsistent code style
- Unclear commit messages

Next topics cover Production Readiness and real-world case studies.
