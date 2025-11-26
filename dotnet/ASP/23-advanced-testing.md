# 23. Advanced Testing

## Overview
Advanced testing techniques go beyond basic unit and integration tests. They include property-based testing for exhaustive input validation, performance testing for scalability verification, and code quality metrics that inform architectural decisions.

---

## 1. Property-Based Testing with FsCheck

Property-based testing generates many random inputs to verify invariants hold universally.

### 1.1 Fundamentals

Traditional test: "For these 3 specific inputs, code works"
Property-based test: "For ANY valid input that matches these properties, code works"

```csharp
// Install: dotnet add package FsCheck.Xunit

[Property]
public void Money_Addition_IsCommutative(decimal amount1, decimal amount2)
{
    // PROPERTY: a + b = b + a for all valid amounts
    var money1 = new Money(Math.Abs(amount1), "USD");
    var money2 = new Money(Math.Abs(amount2), "USD");
    
    var result1 = money1 + money2;
    var result2 = money2 + money1;
    
    result1.Should().Be(result2);
}

[Property]
public void Money_Addition_IsAssociative(decimal amount1, decimal amount2, decimal amount3)
{
    // PROPERTY: (a + b) + c = a + (b + c) for all valid amounts
    var m1 = new Money(Math.Abs(amount1), "USD");
    var m2 = new Money(Math.Abs(amount2), "USD");
    var m3 = new Money(Math.Abs(amount3), "USD");
    
    var result1 = (m1 + m2) + m3;
    var result2 = m1 + (m2 + m3);
    
    result1.Should().Be(result2);
}

[Property]
public void Money_Addition_WithZero_IsIdentity(decimal amount)
{
    // PROPERTY: a + 0 = a for all amounts
    var money = new Money(Math.Abs(amount), "USD");
    var zero = new Money(0, "USD");
    
    var result = money + zero;
    
    result.Should().Be(money);
}

[Property]
public void EmailAddress_Equality_IsReflexive(string emailStr)
{
    // PROPERTY: an email is always equal to itself
    if (!IsValidEmail(emailStr))
        return;
    
    var email = new EmailAddress(emailStr);
    
    (email == email).Should().BeTrue();
}

[Property]
public void EmailAddress_Equality_IsSymmetric(string email1Str, string email2Str)
{
    // PROPERTY: if a = b then b = a
    if (!IsValidEmail(email1Str) || !IsValidEmail(email2Str))
        return;
    
    var email1 = new EmailAddress(email1Str);
    var email2 = new EmailAddress(email2Str);
    
    var forward = email1 == email2;
    var backward = email2 == email1;
    
    forward.Should().Be(backward);
}

[Property]
public void Order_AddItem_DoesNotChangeExistingItems(
    PositiveInt bookId1, PositiveInt quantity1, PositiveInt bookId2, PositiveInt quantity2)
{
    // PROPERTY: Adding item B doesn't affect item A's quantity
    var order = Order.Create(1, new List<OrderLine>
    {
        new OrderLine(new BookId(bookId1.Item), quantity1.Item, new Money(50, "USD"))
    });
    
    var originalQuantity = order.OrderLines[0].Quantity;
    
    if (bookId1.Item != bookId2.Item)
    {
        order.AddItem(new BookId(bookId2.Item), quantity2.Item, new Money(30, "USD"));
        
        var finalQuantity = order.OrderLines
            .First(l => l.BookId == new BookId(bookId1.Item))
            .Quantity;
        
        finalQuantity.Should().Be(originalQuantity);
    }
}
```

### 1.2 Custom Generators

Generate realistic test data:

```csharp
public static class CustomGenerators
{
    // Generator for positive integers
    public static Arbitrary<PositiveAmount> PositiveAmounts()
    {
        return Arb.From(
            Gen.Choose(1, 1000000)
                .Select(x => new PositiveAmount { Amount = x / 100m })
        );
    }
    
    // Generator for valid email addresses
    public static Arbitrary<ValidEmail> ValidEmails()
    {
        var localPartGen = Gen.Elements(
            new[] { "john", "jane", "test", "user", "admin" }
        );
        
        var domainGen = Gen.Elements(
            new[] { "example.com", "test.com", "domain.org" }
        );
        
        return Arb.From(
            Gen.Tuple2(localPartGen, domainGen)
                .Select(t => new ValidEmail { Email = $"{t.Item1}@{t.Item2}" })
        );
    }
    
    // Generator for order line items
    public static Arbitrary<OrderLineData> OrderLines()
    {
        var bookIdGen = Gen.Choose(1, 1000).Select(i => i);
        var quantityGen = Gen.Choose(1, 100);
        var priceGen = Gen.Choose(1, 10000).Select(p => (decimal)p / 100);
        
        return Arb.From(
            Gen.Tuple3(bookIdGen, quantityGen, priceGen)
                .Select(t => new OrderLineData 
                { 
                    BookId = t.Item1, 
                    Quantity = t.Item2, 
                    Price = t.Item3 
                })
        );
    }
}

public class PositiveAmount
{
    public decimal Amount { get; set; }
}

public class ValidEmail
{
    public string Email { get; set; }
}

public class OrderLineData
{
    public int BookId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

// Usage with custom generators
[Property(Arbitrary = new[] { typeof(CustomGenerators) })]
public void Money_WithPositiveAmount_AlwaysSucceeds(PositiveAmount amount)
{
    var money = new Money(amount.Amount, "USD");
    money.Amount.Should().BeGreaterThan(0);
}
```

### 1.3 Shrinking

FsCheck automatically shrinks failing examples to minimal cases:

```csharp
[Property]
public void Order_Total_NeverNegative(List<OrderLineData> lines)
{
    if (!lines.Any())
        return;
    
    var orderLines = lines
        .Where(l => l.Quantity > 0 && l.Price > 0)
        .Select(l => new OrderLine(
            new BookId(Math.Abs(l.BookId) + 1),
            l.Quantity,
            new Money(l.Price, "USD")
        ))
        .ToList();
    
    if (!orderLines.Any())
        return;
    
    var order = Order.Create(1, orderLines);
    
    // PROPERTY: Order total should never be negative
    order.Total.Amount.Should().BeGreaterThanOrEqualTo(0);
}

// If this fails, FsCheck finds the MINIMAL failing case:
// Instead of reporting a complex list of 100 items,
// it reports just the simplest case that fails
```

---

## 2. Performance Testing

### 2.1 Benchmarking with BenchmarkDotNet

```csharp
// Install: dotnet add package BenchmarkDotNet

[MemoryDiagnoser]
public class OrderCalculationBenchmarks
{
    private Order _order;
    
    [GlobalSetup]
    public void Setup()
    {
        // Create a test order with many items
        var lines = Enumerable.Range(1, 100)
            .Select(i => new OrderLine(
                new BookId(i),
                i % 10 + 1,
                new Money(9.99m * i, "USD")
            ))
            .ToList();
        
        _order = Order.Create(1, lines);
    }
    
    [Benchmark]
    public Money CalculateTotal()
    {
        // Benchmark: How long does total calculation take?
        return _order.OrderLines.Aggregate(
            new Money(0, "USD"),
            (acc, line) => acc + line.Subtotal
        );
    }
    
    [Benchmark]
    public void AddItem()
    {
        // Benchmark: How long to add an item?
        _order.AddItem(new BookId(101), 5, new Money(50, "USD"));
    }
}

public class EFQueryBenchmarks
{
    private readonly BookstoreDbContext _dbContext;
    private readonly IOrderRepository _repository;
    
    public EFQueryBenchmarks()
    {
        var services = new ServiceCollection();
        services.AddDbContext<BookstoreDbContext>(options =>
            options.UseInMemoryDatabase("BenchmarkDb")
        );
        services.AddScoped<IOrderRepository, EFOrderRepository>();
        
        var serviceProvider = services.BuildServiceProvider();
        _dbContext = serviceProvider.GetRequiredService<BookstoreDbContext>();
        _repository = serviceProvider.GetRequiredService<IOrderRepository>();
    }
    
    [GlobalSetup]
    public async Task Setup()
    {
        // Seed test data
        for (int i = 1; i <= 1000; i++)
        {
            var order = Order.Create(i % 10 + 1, new List<OrderLine>
            {
                new OrderLine(new BookId(1), i % 5 + 1, new Money(29.99m, "USD"))
            });
            
            _dbContext.Orders.Add(order);
        }
        
        await _dbContext.SaveChangesAsync();
    }
    
    [Benchmark]
    public async Task GetOrder()
    {
        // Benchmark: How long to fetch an order?
        await _repository.GetByIdAsync(500);
    }
    
    [Benchmark]
    public async Task QueryManyOrders()
    {
        // Benchmark: How long to query multiple orders?
        var orders = _dbContext.Orders
            .Where(o => o.CustomerId == 1)
            .ToList();
    }
}

// Run benchmarks:
// dotnet run --configuration Release -- --job short
```

### 2.2 Load Testing

Verify the system handles expected loads:

```csharp
// Install: dotnet add package NBomber

public class OrderApiLoadTest
{
    [Fact]
    public void LoadTest_OrderPlacementUnder1000RPS()
    {
        var httpClientFactory = new HttpClientFactory();
        var client = httpClientFactory.CreateClient();
        
        var scenario = Scenario.Create("place_order", async context =>
        {
            var request = new PlaceOrderRequest
            {
                CustomerId = Random.Shared.Next(1, 100),
                Items = new List<OrderItemRequest>
                {
                    new OrderItemRequest 
                    { 
                        BookId = Random.Shared.Next(1, 1000), 
                        Quantity = Random.Shared.Next(1, 10) 
                    }
                }
            };
            
            var response = await client.PostAsJsonAsync("/api/orders", request);
            
            return response.IsSuccessStatusCode
                ? Response.Ok()
                : Response.Fail();
        })
        .WithoutWarmup()
        .WithLoadSimulations(
            Simulation.Inject(rate: 1000, interval: TimeSpan.FromSeconds(1), 
                duration: TimeSpan.FromSeconds(60))
        );
        
        var nbomberRunner = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
        
        // Assert
        var stats = nbomberRunner.ScenarioStats.First();
        stats.RPS.Should().BeGreaterThan(900);  // 90% success rate
        stats.StatusCodes.Should().AllSatisfy(code =>
            code.Key.IsSuccess.Should().BeTrue()
        );
    }
}
```

---

## 3. API Contract Testing

Verify API contracts remain stable:

### 3.1 Contract Testing with Pact

```csharp
// Install: dotnet add package PactNet

[Collection("Pact")]
public class OrdersApiConsumerTest : IAsyncLifetime
{
    private readonly IInteractionVerifier _interactionVerifier;
    
    public OrdersApiConsumerTest()
    {
        var options = new PactVerifierOptions
        {
            ProviderVersion = "1.0.0"
        };
        
        _interactionVerifier = new PactVerifier(options);
    }
    
    [Fact]
    public async Task Verify_GetOrder_Contract()
    {
        // This verifies that the API contract matches what clients expect
        await _interactionVerifier
            .ServiceProvider("Bookstore API", new Uri("http://localhost:5000"))
            .WithHttpEndpoint(new Uri("http://localhost:9001"))
            .PublishToPactBroker("http://localhost", 
                consumerVersion: "1.0.0")
            .Verify();
    }
    
    public async Task InitializeAsync()
    {
        // Setup
        await Task.CompletedTask;
    }
    
    public async Task DisposeAsync()
    {
        // Cleanup
        await Task.CompletedTask;
    }
}

// Define expected contract
public class OrdersPactTests
{
    [Fact]
    public void GetOrder_ReturnsOrderDetails_Contract()
    {
        var pactBuilder = new PactBuilder(new PactConfig 
        { 
            SpecificationVersion = "2.0.0" 
        });
        
        pactBuilder
            .WithHttpInteractions()
            .UponReceiving("a request for order details")
            .With(new ProviderServiceRequest
            {
                Method = HttpVerb.Get,
                Path = "/api/orders/1"
            })
            .WillRespondWith(new ProviderServiceResponse
            {
                Status = 200,
                Headers = new Dictionary<string, object>
                {
                    { "Content-Type", "application/json" }
                },
                Body = new
                {
                    orderId = 1,
                    customerId = 1,
                    total = 99.99,
                    status = "Pending"
                }
            });
    }
}
```

---

## 4. Code Quality and Metrics

### 4.1 Code Coverage Analysis

```csharp
// Install: dotnet add package coverlet.collector

// Run tests with coverage:
// dotnet test /p:CollectCoverage=true

// Generate coverage report:
// dotnet test /p:CollectCoverage=true /p:CoverageFormat=lcov

// Expected coverage for enterprise code:
// - Domain Layer: >90% (business logic is critical)
// - Application Layer: >85% (orchestration)
// - Infrastructure: >75% (can be harder to test)
// - API Layer: >70% (framework code)

public class CoverageAnalysis
{
    [Fact]
    public void CoverageShouldMeetThreshold()
    {
        // Verify important domain methods are covered
        var orderType = typeof(Order);
        var methods = orderType.GetMethods(
            System.Reflection.BindingFlags.Public | 
            System.Reflection.BindingFlags.Instance
        );
        
        // All public business methods should be tested
        methods.Should().AllSatisfy(m =>
        {
            // These should all have unit tests
            var testMethod = typeof(OrderTests)
                .GetMethod(m.Name, 
                    System.Reflection.BindingFlags.Public | 
                    System.Reflection.BindingFlags.Instance
                );
            
            if (!m.Name.StartsWith("get_"))
            {
                testMethod.Should().NotBeNull(
                    because: $"Method {m.Name} should have a test"
                );
            }
        });
    }
}
```

### 4.2 Static Analysis with Roslyn

```csharp
// Install: dotnet add package Roslyn.Analyzers

// Define custom analyzer rules
public class DomainLayerAnalyzer
{
    // Rule: Domain entities should not depend on infrastructure
    // Rule: Domain methods should not throw generic Exception
    // Rule: Value objects should be immutable
    
    [Theory]
    [InlineData("Bookstore.Domain.Order", "Bookstore.Infrastructure.EFOrderRepository")]
    [InlineData("Bookstore.Domain.Customer", "Bookstore.Api.Controllers")]
    public void DomainClass_ShouldNotDependOnInfrastructure(string domainClass, string forbiddenDependency)
    {
        // Verify no forbidden dependencies exist in the codebase
        // This would typically be done by a static analyzer or code review
    }
}

// StyleCop rules for code consistency
public class CodeStyleRules
{
    // Naming conventions
    // - Classes: PascalCase
    // - Methods: PascalCase
    // - Parameters: camelCase
    // - Private fields: _camelCase
    
    // Spacing and indentation
    // - 4 spaces per indentation level
    // - Blank lines between logical sections
    
    // Organization
    // - Usings at top
    // - Namespace then class
    // - Constants, fields, properties, methods in logical order
}
```

### 4.3 SOLID Principles Verification

```csharp
public class SolidPrinciplesTests
{
    [Fact]
    public void SingleResponsibility_OrderShouldOnlyManageOrder()
    {
        // Order aggregate should only be responsible for order management
        var orderType = typeof(Order);
        var publicMethods = orderType
            .GetMethods(System.Reflection.BindingFlags.Public | 
                       System.Reflection.BindingFlags.Instance)
            .Where(m => !m.IsSpecialName);
        
        // Methods should be related to order management
        publicMethods.Should().AllSatisfy(m =>
        {
            m.Name.Should().MatchRegex("(Create|Add|Remove|Apply|Confirm|Complete|Cancel)",
                because: $"Method {m.Name} should be related to order management"
            );
        });
    }
    
    [Fact]
    public void OpenClosedPrinciple_ShouldUseInterfaces()
    {
        // Classes should depend on abstractions, not concrete implementations
        var repositoryInterface = typeof(IOrderRepository);
        repositoryInterface.Should().NotBeNull();
        
        // Implementations should be behind interface
        var implementations = typeof(EFOrderRepository)
            .GetInterfaces()
            .Should()
            .Contain(repositoryInterface);
    }
    
    [Fact]
    public void LiskovSubstitution_RepositoriesShould BeInterchangeable()
    {
        // Any repository implementation should be usable where IOrderRepository is needed
        var repositories = typeof(IOrderRepository)
            .Assembly
            .GetTypes()
            .Where(t => typeof(IOrderRepository).IsAssignableFrom(t) && 
                       !t.IsInterface)
            .ToList();
        
        repositories.Should().NotBeEmpty();
        
        // Each implementation should have the same public interface
        foreach (var repo in repositories)
        {
            repo.Should().Implement(typeof(IOrderRepository));
        }
    }
    
    [Fact]
    public void InterfaceSegregation_ClientsShouldNotDependOnUnused()
    {
        // IOrderRepository shouldn't contain methods unrelated to orders
        var methods = typeof(IOrderRepository)
            .GetMethods()
            .Where(m => !m.IsSpecialName);
        
        methods.Should().AllSatisfy(m =>
        {
            m.Name.Should().MatchRegex("(GetBy|Save|Delete|Update)",
                because: "All methods should be related to orders"
            );
        });
    }
    
    [Fact]
    public void DependencyInversion_ShouldDependOnAbstractions()
    {
        // High-level modules should depend on abstractions
        var placeOrderServiceType = typeof(PlaceOrderService);
        var constructorParams = placeOrderServiceType
            .GetConstructors()
            .First()
            .GetParameters();
        
        constructorParams.Should().AllSatisfy(p =>
        {
            p.ParameterType.Should().BeAssignableTo(typeof(object),
                because: $"Parameter {p.Name} should be an interface or abstract type"
            );
            
            // Should be an interface (starts with I)
            if (p.ParameterType.IsClass && !p.ParameterType.IsAbstract)
            {
                Assert.Fail($"Parameter {p.Name} should be an interface");
            }
        });
    }
}
```

---

## 5. Mutation Testing

Verify that tests actually catch bugs:

```csharp
// Install: dotnet add package Stryker.NET.CLI
// dotnet stryker

// Mutation testing introduces small code changes (mutations) and verifies tests catch them
// Example mutations:
// - Change > to >= in a condition
// - Remove method call
// - Change constant value
// - Negate boolean

public class MutationTestingExamples
{
    // ORIGINAL CODE:
    public class Order
    {
        public void AddItem(OrderLine item)
        {
            if (item.Quantity <= 0)  // Mutation: change to <
                throw new DomainException("Quantity must be positive");
            
            Items.Add(item);  // Mutation: remove this line
        }
    }
    
    // MUTATIONS TO CATCH:
    // 1. Change <= to < (test with quantity = 0)
    // 2. Remove Items.Add (test that item count increases)
    // 3. Change quantity check to Quantity < 1 (equivalent)
    // 4. Change to Quantity == 0 (incomplete check)
    
    [Fact]
    public void AddItem_WithZeroQuantity_ThrowsException()
    {
        var order = Order.Create(1, new List<OrderLine>());
        
        // This catches mutation: <= changed to <
        Assert.Throws<DomainException>(() =>
            order.AddItem(new OrderLine(new BookId(1), 0, new Money(50, "USD")))
        );
    }
    
    [Fact]
    public void AddItem_WithValidQuantity_ActuallyAddsItem()
    {
        var order = Order.Create(1, new List<OrderLine>());
        var initialCount = order.OrderLines.Count;
        
        order.AddItem(new OrderLine(new BookId(1), 1, new Money(50, "USD")));
        
        // This catches mutation: removing Items.Add()
        order.OrderLines.Count.Should().Be(initialCount + 1);
    }
}

// Run mutation testing: dotnet stryker
// Look for RED mutants (tests didn't catch them) and ADD TESTS
```

---

## 6. Performance Profiling

### 6.1 Memory Profiling

```csharp
public class MemoryProfilingTests
{
    [Fact]
    public void OrderCreation_ShouldNotLeakMemory()
    {
        // Create many orders and verify garbage collection
        var initialMemory = GC.GetTotalMemory(true);
        
        for (int i = 0; i < 10000; i++)
        {
            var order = Order.Create(1, new List<OrderLine>
            {
                new OrderLine(new BookId(1), 1, new Money(50, "USD"))
            });
            
            // Use order but don't hold reference
        }
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        
        var finalMemory = GC.GetTotalMemory(true);
        var increase = finalMemory - initialMemory;
        
        // Memory increase should be small (< 1MB for this operation)
        increase.Should().BeLessThan(1_000_000);
    }
    
    [Fact]
    public void QueryLargeDataset_ShouldUseReasonableMemory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<BookstoreDbContext>(options =>
            options.UseInMemoryDatabase("MemoryTest")
        );
        
        var serviceProvider = services.BuildServiceProvider();
        var dbContext = serviceProvider.GetRequiredService<BookstoreDbContext>();
        
        // Insert 100k orders
        for (int i = 1; i <= 100000; i++)
        {
            var order = Order.Create(i % 100, new List<OrderLine>
            {
                new OrderLine(new BookId(1), 1, new Money(50, "USD"))
            });
            
            dbContext.Orders.Add(order);
            
            if (i % 1000 == 0)
                dbContext.SaveChanges();
        }
        
        // Query with paging to avoid loading all at once
        var pagedOrders = dbContext.Orders
            .AsNoTracking()
            .Skip(0)
            .Take(100)
            .ToList();
        
        pagedOrders.Should().HaveCount(100);
    }
}
```

---

## 7. Scenario-Based Testing

Test complex business scenarios end-to-end:

```csharp
public class ComplexBusinessScenarioTests : IClassFixture<BookstoreWebApplicationFactory>
{
    private readonly BookstoreWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly BookstoreDbContext _db;
    
    public ComplexBusinessScenarioTests(BookstoreWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _db = factory.GetDbContext();
    }
    
    [Fact]
    public async Task HighValueCustomerOrderFlow_WithMultipleOrdersAndPayments()
    {
        // Scenario: High-value customer places multiple orders,
        // receives bulk discount, pays across orders
        
        // Arrange
        var customer = new Customer 
        { 
            Id = 1, 
            FirstName = "VIP",
            Email = new EmailAddress("vip@example.com"),
            IsLoyaltyMember = true,
            TotalSpent = 5000  // Qualifies for premium discount
        };
        
        var books = Enumerable.Range(1, 10)
            .Select(i => new Book 
            { 
                Id = i, 
                Title = $"Book {i}",
                Price = new Money(i * 10, "USD")
            })
            .ToList();
        
        _db.Customers.Add(customer);
        _db.Books.AddRange(books);
        await _db.SaveChangesAsync();
        
        var orderIds = new List<int>();
        
        // Act 1: Place first order
        var order1Request = new PlaceOrderRequest
        {
            CustomerId = 1,
            Items = books.Take(3)
                .Select((b, i) => new OrderItemRequest 
                { 
                    BookId = b.Id, 
                    Quantity = i + 1 
                })
                .ToList()
        };
        
        var order1Response = await _client
            .PostAsJsonAsync("/api/orders", order1Request);
        var order1 = await order1Response.Content
            .ReadAsAsync<PlaceOrderResponse>();
        orderIds.Add(order1.OrderId);
        
        // Act 2: Place second order
        var order2Request = new PlaceOrderRequest
        {
            CustomerId = 1,
            Items = books.Skip(3).Take(3)
                .Select(b => new OrderItemRequest 
                { 
                    BookId = b.Id, 
                    Quantity = 1 
                })
                .ToList()
        };
        
        var order2Response = await _client
            .PostAsJsonAsync("/api/orders", order2Request);
        var order2 = await order2Response.Content
            .ReadAsAsync<PlaceOrderResponse>();
        orderIds.Add(order2.OrderId);
        
        // Act 3: Customer receives loyalty discount on both
        // Act 4: Batch payment processes both orders
        
        // Assert: Verify complete scenario
        foreach (var orderId in orderIds)
        {
            var getResponse = await _client
                .GetAsync($"/api/orders/{orderId}");
            var orderDetails = await getResponse.Content
                .ReadAsAsync<OrderDetailsResponse>();
            
            orderDetails.Should().NotBeNull();
            orderDetails.Items.Should().NotBeEmpty();
        }
        
        // Assert: Customer record reflects orders
        var updatedCustomer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == 1);
        
        updatedCustomer.TotalOrders.Should().Be(2);
    }
}
```

---

## Summary

Advanced testing covers the full spectrum:

1. **Property-Based Testing**: Exhaustive input validation with shrinking
2. **Performance Testing**: Benchmarks, load tests, memory profiling
3. **API Contract Testing**: Ensure API stability across versions
4. **Code Quality**: Coverage metrics, static analysis, SOLID verification
5. **Mutation Testing**: Verify tests actually catch bugs
6. **Scenario Testing**: Complex business workflows end-to-end

**Quality metrics for enterprise code:**
- Domain layer: >90% coverage
- Application layer: >85% coverage
- No red mutants in critical code paths
- Performance benchmarks tracked over time
- API contracts verified with client tests

With unit testing (Topic 21), integration testing (Topic 22), and advanced testing (Topic 23), you now have comprehensive testing strategies for enterprise applications. This foundation ensures reliability, maintainability, and confidence in production deployments.
