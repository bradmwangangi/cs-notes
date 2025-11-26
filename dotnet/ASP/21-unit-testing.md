# 21. Unit Testing

## Overview
Unit testing validates the smallest testable units of code in isolation. In an enterprise system built with layered architecture and DDD, unit tests primarily focus on domain logic, with minimal mocking, testing business rules thoroughly.

---

## 1. Unit Testing Fundamentals

### 1.1 Testing Frameworks and Setup

**xUnit** is the preferred framework for .NET enterprise applications (used by Microsoft and the community).

```csharp
// Project file setup
<Project Sdk="Microsoft.NET.Sdk">
  <TargetFramework>net8.0</TargetFramework>
  <IsTestProject>true</IsTestProject>
  
  <ItemGroup>
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>
  
  <ItemGroup>
    <ProjectReference Include="..\Bookstore.Domain\Bookstore.Domain.csproj" />
    <ProjectReference Include="..\Bookstore.Application\Bookstore.Application.csproj" />
  </ItemGroup>
</Project>
```

### 1.2 Arrange-Act-Assert Pattern

The standard pattern for unit tests:

```csharp
public class OrderTests
{
    [Fact]
    public void AddItem_WithValidInput_IncreaseItemCount()
    {
        // ARRANGE: Set up test data and preconditions
        var order = new Order { Id = 1, CustomerId = 1 };
        var bookId = new BookId(100);
        var quantity = 5;
        var price = new Money(29.99m, "USD");
        
        // ACT: Execute the method being tested
        order.AddItem(bookId, quantity, price);
        
        // ASSERT: Verify the results
        Assert.Single(order.OrderLines);
        Assert.Equal(quantity, order.OrderLines[0].Quantity);
        Assert.Equal(price, order.OrderLines[0].UnitPrice);
    }
}
```

### 1.3 Test Organization

Organize tests by the component being tested:

```
Bookstore.Domain.Tests/
├── Entities/
│   ├── OrderTests.cs
│   ├── CustomerTests.cs
│   ├── BookTests.cs
├── ValueObjects/
│   ├── MoneyTests.cs
│   ├── EmailAddressTests.cs
│   ├── BookIdTests.cs
├── Aggregates/
│   ├── OrderAggregateTests.cs
└── Events/
    └── DomainEventTests.cs

Bookstore.Application.Tests/
├── Services/
│   ├── PlaceOrderServiceTests.cs
│   ├── ProcessPaymentServiceTests.cs
└── EventHandlers/
    └── SendOrderConfirmationEmailHandlerTests.cs
```

---

## 2. Testing Domain Layer

Domain tests have minimal external dependencies since domain logic is pure.

### 2.1 Testing Value Objects

```csharp
public class MoneyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(999999.99)]
    public void Constructor_WithValidAmount_CreatesSuccessfully(decimal amount)
    {
        // Act
        var money = new Money(amount, "USD");
        
        // Assert
        Assert.Equal(amount, money.Amount);
        Assert.Equal("USD", money.Currency);
    }
    
    [Fact]
    public void Constructor_WithNegativeAmount_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            new Money(-10, "USD")
        );
    }
    
    [Theory]
    [InlineData("USD", 100, "USD", 50)]
    [InlineData("EUR", 25, "EUR", 25)]
    public void Addition_WithSameCurrency_ReturnsCorrectSum(
        string currency1, decimal amount1, string currency2, decimal amount2)
    {
        // Arrange
        var money1 = new Money(amount1, currency1);
        var money2 = new Money(amount2, currency2);
        
        // Act
        var result = money1 + money2;
        
        // Assert
        Assert.Equal(amount1 + amount2, result.Amount);
        Assert.Equal(currency1, result.Currency);
    }
    
    [Fact]
    public void Addition_WithDifferentCurrency_ThrowsException()
    {
        // Arrange
        var usd = new Money(100, "USD");
        var eur = new Money(100, "EUR");
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => usd + eur);
    }
    
    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(100, 99, false)]
    public void Equality_WithSameCurrency_ComparesCorrectly(
        decimal amount1, decimal amount2, bool shouldBeEqual)
    {
        // Arrange
        var money1 = new Money(amount1, "USD");
        var money2 = new Money(amount2, "USD");
        
        // Act & Assert
        if (shouldBeEqual)
        {
            Assert.Equal(money1, money2);
            Assert.True(money1 == money2);
        }
        else
        {
            Assert.NotEqual(money1, money2);
            Assert.True(money1 != money2);
        }
    }
    
    [Fact]
    public void GetHashCode_WithEqualValues_ReturnsSameHashCode()
    {
        // Arrange
        var money1 = new Money(100, "USD");
        var money2 = new Money(100, "USD");
        
        // Act & Assert
        Assert.Equal(money1.GetHashCode(), money2.GetHashCode());
    }
}

public class EmailAddressTests
{
    [Theory]
    [InlineData("valid@example.com")]
    [InlineData("user.name@domain.co.uk")]
    [InlineData("test+tag@example.com")]
    public void Constructor_WithValidEmail_CreatesSuccessfully(string email)
    {
        // Act
        var emailAddress = new EmailAddress(email);
        
        // Assert
        Assert.Equal(email, emailAddress.Value);
    }
    
    [Theory]
    [InlineData("notanemail")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("")]
    public void Constructor_WithInvalidEmail_ThrowsException(string email)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            new EmailAddress(email)
        );
    }
}
```

### 2.2 Testing Entities and Aggregates

```csharp
public class OrderTests
{
    [Fact]
    public void Create_WithValidCustomerAndItems_CreatesOrderWithPendingStatus()
    {
        // Arrange
        var customerId = 1;
        var lines = new List<OrderLine>
        {
            new OrderLine(new BookId(1), 2, new Money(29.99m, "USD")),
            new OrderLine(new BookId(2), 1, new Money(39.99m, "USD"))
        };
        
        // Act
        var order = Order.Create(customerId, lines);
        
        // Assert
        Assert.Equal(customerId, order.CustomerId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.NotEmpty(order.OrderLines);
        Assert.Equal(2, order.OrderLines.Count);
    }
    
    [Fact]
    public void Create_WithEmptyItems_ThrowsDomainException()
    {
        // Arrange
        var customerId = 1;
        var emptyLines = new List<OrderLine>();
        
        // Act & Assert
        Assert.Throws<DomainException>(() => 
            Order.Create(customerId, emptyLines)
        );
    }
    
    [Fact]
    public void AddItem_WithValidInput_AddsItemAndRecalculatesTotal()
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 1, new Money(100, "USD"))
        });
        
        var initialTotal = order.Total;
        
        // Act
        order.AddItem(new BookId(2), 2, new Money(50, "USD"));
        
        // Assert
        Assert.Equal(2, order.OrderLines.Count);
        Assert.Equal(new Money(200, "USD"), order.Total);
        Assert.NotEqual(initialTotal, order.Total);
    }
    
    [Fact]
    public void AddItem_WithZeroQuantity_ThrowsDomainException()
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 1, new Money(100, "USD"))
        });
        
        // Act & Assert
        Assert.Throws<DomainException>(() => 
            order.AddItem(new BookId(2), 0, new Money(50, "USD"))
        );
    }
    
    [Fact]
    public void RemoveItem_WithExistingItem_RemovesAndRecalculatesTotal()
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 1, new Money(100, "USD")),
            new OrderLine(new BookId(2), 1, new Money(50, "USD"))
        });
        
        // Act
        order.RemoveItem(new BookId(1));
        
        // Assert
        Assert.Single(order.OrderLines);
        Assert.Equal(new Money(50, "USD"), order.Total);
    }
    
    [Fact]
    public void RemoveItem_WithNonexistentItem_DoesNothing()
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 1, new Money(100, "USD"))
        });
        
        // Act
        order.RemoveItem(new BookId(999));
        
        // Assert
        Assert.Single(order.OrderLines);
    }
    
    [Fact]
    public void ConfirmOrder_WithValidOrder_ChangesStatusAndRaisesEvent()
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 1, new Money(100, "USD"))
        });
        
        // Act
        order.Confirm();
        
        // Assert
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.NotEmpty(order.GetDomainEvents());
        
        var confirmEvent = order.GetDomainEvents()
            .OfType<OrderConfirmedEvent>()
            .FirstOrDefault();
        Assert.NotNull(confirmEvent);
        Assert.Equal(order.Id, confirmEvent.OrderId);
    }
    
    [Fact]
    public void ConfirmOrder_WhenNotPending_ThrowsDomainException()
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 1, new Money(100, "USD"))
        });
        order.Confirm();  // Already confirmed
        
        // Act & Assert
        Assert.Throws<DomainException>(() => order.Confirm());
    }
    
    [Fact]
    public void ApplyPayment_WithValidPayment_UpdatesStatusAndRaisesEvent()
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 1, new Money(100, "USD"))
        });
        order.Confirm();
        
        var payment = new Payment
        {
            TransactionId = "TXN123",
            Amount = new Money(100, "USD")
        };
        
        // Act
        order.ApplyPayment(payment);
        
        // Assert
        Assert.Equal(OrderStatus.Paid, order.Status);
        
        var paymentEvent = order.GetDomainEvents()
            .OfType<PaymentProcessedEvent>()
            .FirstOrDefault();
        Assert.NotNull(paymentEvent);
        Assert.Equal("TXN123", paymentEvent.TransactionId);
    }
}
```

### 2.3 Testing Complex Business Logic

```csharp
public class OrderPricingServiceTests
{
    private readonly OrderPricingService _service;
    
    public OrderPricingServiceTests()
    {
        _service = new OrderPricingService();
    }
    
    [Theory]
    [InlineData(100, 0)]      // No discount for $100
    [InlineData(500, 25)]     // 5% discount for $500
    [InlineData(1000, 100)]   // 10% discount for $1000
    [InlineData(5000, 750)]   // 15% discount for $5000
    public void CalculateDiscount_WithDifferentOrderAmounts_AppliesCorrectDiscount(
        decimal orderAmount, decimal expectedDiscount)
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 1, new Money(orderAmount, "USD"))
        });
        
        var customer = new Customer { Id = 1 };
        
        // Act
        var discount = _service.CalculateDiscount(order, customer);
        
        // Assert
        Assert.Equal(expectedDiscount, discount.Amount);
    }
    
    [Theory]
    [InlineData("CA", 0.0725)]
    [InlineData("NY", 0.04)]
    [InlineData("WA", 0.065)]
    public void CalculateTax_WithDifferentStates_AppliesCorrectRate(
        string state, decimal expectedRate)
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 1, new Money(100, "USD"))
        });
        
        var address = new Address("123 Main", "City", "12345", state);
        
        // Act
        var tax = _service.CalculateTax(order, address);
        
        // Assert
        var expectedTax = 100 * expectedRate;
        Assert.Equal(expectedTax, tax.Amount, 2);  // 2 decimal places
    }
    
    [Fact]
    public void CalculateDiscount_ForLoyalCustomer_AppliesAdditionalDiscount()
    {
        // Arrange
        var loyalCustomer = new Customer { Id = 1, IsLoyaltyMember = true };
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 1, new Money(1000, "USD"))
        });
        
        // Act
        var discount = _service.CalculateDiscount(order, loyalCustomer);
        
        // Assert
        // Loyalty members get an additional 5% off
        Assert.Equal(new Money(150, "USD"), discount);  // 10% + 5% loyalty = 15%
    }
}
```

### 2.4 Testing Domain Exceptions

```csharp
public class DomainValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Customer_Create_WithEmptyName_ThrowsDomainException(string name)
    {
        // Act & Assert
        Assert.Throws<DomainException>(() => 
            Customer.Create(name, "john@example.com")
        );
    }
    
    [Fact]
    public void Order_AddItem_WithNegativeQuantity_ThrowsDomainException()
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 1, new Money(100, "USD"))
        });
        
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() => 
            order.AddItem(new BookId(2), -5, new Money(50, "USD"))
        );
        
        Assert.Contains("positive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

---

## 3. Testing Application Layer

Application layer tests need more mocking, but focus on orchestration logic.

### 3.1 Testing Application Services

```csharp
public class PlaceOrderServiceTests
{
    private readonly Mock<IOrderRepository> _mockOrderRepository;
    private readonly Mock<IBookRepository> _mockBookRepository;
    private readonly Mock<ICustomerRepository> _mockCustomerRepository;
    private readonly Mock<IDomainEventPublisher> _mockEventPublisher;
    private readonly PlaceOrderService _service;
    
    public PlaceOrderServiceTests()
    {
        _mockOrderRepository = new Mock<IOrderRepository>();
        _mockBookRepository = new Mock<IBookRepository>();
        _mockCustomerRepository = new Mock<ICustomerRepository>();
        _mockEventPublisher = new Mock<IDomainEventPublisher>();
        
        _service = new PlaceOrderService(
            _mockOrderRepository.Object,
            _mockBookRepository.Object,
            _mockCustomerRepository.Object,
            _mockEventPublisher.Object
        );
    }
    
    [Fact]
    public async Task Execute_WithValidRequest_CreatesAndSavesOrder()
    {
        // Arrange
        var customerId = 1;
        var request = new PlaceOrderRequest
        {
            CustomerId = customerId,
            Items = new List<OrderItemRequest>
            {
                new OrderItemRequest { BookId = 1, Quantity = 2 }
            }
        };
        
        var customer = new Customer { Id = customerId };
        var book = new Book { Id = 1, Price = new Money(29.99m, "USD") };
        
        _mockCustomerRepository
            .Setup(r => r.GetByIdAsync(customerId))
            .ReturnsAsync(customer);
        
        _mockBookRepository
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(book);
        
        // Act
        var response = await _service.ExecuteAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Equal("Success", response.Status);
        
        // Verify repository was called to save
        _mockOrderRepository.Verify(
            r => r.SaveAsync(It.IsAny<Order>()),
            Times.Once
        );
        
        // Verify events were published
        _mockEventPublisher.Verify(
            p => p.PublishAsync(It.IsAny<IEnumerable<DomainEvent>>()),
            Times.Once
        );
    }
    
    [Fact]
    public async Task Execute_WithNonexistentCustomer_ThrowsException()
    {
        // Arrange
        var request = new PlaceOrderRequest
        {
            CustomerId = 999,
            Items = new List<OrderItemRequest>
            {
                new OrderItemRequest { BookId = 1, Quantity = 1 }
            }
        };
        
        _mockCustomerRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Customer)null);
        
        // Act & Assert
        await Assert.ThrowsAsync<ApplicationException>(() => 
            _service.ExecuteAsync(request)
        );
    }
    
    [Fact]
    public async Task Execute_WithInvalidQuantity_ThrowsException()
    {
        // Arrange
        var customerId = 1;
        var request = new PlaceOrderRequest
        {
            CustomerId = customerId,
            Items = new List<OrderItemRequest>
            {
                new OrderItemRequest { BookId = 1, Quantity = 0 }  // Invalid
            }
        };
        
        var customer = new Customer { Id = customerId };
        var book = new Book { Id = 1, Price = new Money(29.99m, "USD") };
        
        _mockCustomerRepository
            .Setup(r => r.GetByIdAsync(customerId))
            .ReturnsAsync(customer);
        
        _mockBookRepository
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(book);
        
        // Act & Assert
        await Assert.ThrowsAsync<ApplicationException>(() => 
            _service.ExecuteAsync(request)
        );
    }
    
    [Fact]
    public async Task Execute_WithRepositoryFailure_PropagatesException()
    {
        // Arrange
        var request = new PlaceOrderRequest
        {
            CustomerId = 1,
            Items = new List<OrderItemRequest>
            {
                new OrderItemRequest { BookId = 1, Quantity = 1 }
            }
        };
        
        var customer = new Customer { Id = 1 };
        var book = new Book { Id = 1, Price = new Money(29.99m, "USD") };
        
        _mockCustomerRepository
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(customer);
        
        _mockBookRepository
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(book);
        
        _mockOrderRepository
            .Setup(r => r.SaveAsync(It.IsAny<Order>()))
            .ThrowsAsync(new RepositoryException("Database error"));
        
        // Act & Assert
        await Assert.ThrowsAsync<RepositoryException>(() => 
            _service.ExecuteAsync(request)
        );
    }
}
```

### 3.2 Testing Event Handlers

```csharp
public class SendOrderConfirmationEmailHandlerTests
{
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<ICustomerRepository> _mockCustomerRepository;
    private readonly SendOrderConfirmationEmailHandler _handler;
    
    public SendOrderConfirmationEmailHandlerTests()
    {
        _mockEmailService = new Mock<IEmailService>();
        _mockCustomerRepository = new Mock<ICustomerRepository>();
        
        _handler = new SendOrderConfirmationEmailHandler(
            _mockEmailService.Object,
            _mockCustomerRepository.Object
        );
    }
    
    [Fact]
    public async Task Handle_WithValidEvent_SendsConfirmationEmail()
    {
        // Arrange
        var @event = new OrderPlacedEvent(
            orderId: 123,
            customerId: 1,
            totalAmount: 99.99m,
            items: new List<OrderItemDetail>
            {
                new OrderItemDetail { BookId = 1, Quantity = 1, Price = 99.99m }
            }
        );
        
        var customer = new Customer 
        { 
            Id = 1, 
            Email = new EmailAddress("john@example.com") 
        };
        
        _mockCustomerRepository
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(customer);
        
        // Act
        await _handler.HandleAsync(@event);
        
        // Assert
        _mockEmailService.Verify(
            s => s.SendAsync(
                It.Is<string>(email => email == "john@example.com"),
                It.IsAny<string>(),
                It.IsAny<string>()
            ),
            Times.Once
        );
    }
    
    [Fact]
    public async Task Handle_WithNonexistentCustomer_DoesNotSendEmail()
    {
        // Arrange
        var @event = new OrderPlacedEvent(
            orderId: 123,
            customerId: 999,
            totalAmount: 99.99m,
            items: new List<OrderItemDetail>()
        );
        
        _mockCustomerRepository
            .Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((Customer)null);
        
        // Act
        await _handler.HandleAsync(@event);
        
        // Assert
        _mockEmailService.Verify(
            s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
    }
}
```

---

## 4. Using FluentAssertions

FluentAssertions provides more readable assertions:

```csharp
public class OrderTestsWithFluentAssertions
{
    [Fact]
    public void Order_Create_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 2, new Money(50, "USD"))
        });
        
        // Assert - Much more readable
        order.Should().NotBeNull();
        order.Status.Should().Be(OrderStatus.Pending);
        order.CustomerId.Should().Be(1);
        order.OrderLines.Should().HaveCount(1);
        order.OrderLines.First().Quantity.Should().Be(2);
        order.Total.Amount.Should().Be(100);
        order.Total.Currency.Should().Be("USD");
    }
    
    [Fact]
    public void Order_AddMultipleItems_ShouldCalculateCorrectTotal()
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>());
        
        // Act
        order.AddItem(new BookId(1), 2, new Money(25, "USD"));
        order.AddItem(new BookId(2), 3, new Money(15, "USD"));
        
        // Assert
        order.OrderLines.Should()
            .HaveCount(2)
            .And.AllSatisfy(line => line.Quantity.Should().BeGreaterThan(0));
        
        order.Total.Amount.Should().Be(95);
    }
    
    [Fact]
    public async Task PlaceOrderService_WithInvalidCustomer_ShouldThrowException()
    {
        // Arrange
        var mockRepo = new Mock<ICustomerRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Customer)null);
        
        var service = new PlaceOrderService(
            mockRepo.Object, 
            null, null, null
        );
        
        // Act
        var action = () => service.ExecuteAsync(new PlaceOrderRequest());
        
        // Assert
        await action.Should()
            .ThrowAsync<ApplicationException>()
            .WithMessage("*Customer*");
    }
}
```

---

## 5. Test Naming Conventions

Follow clear naming patterns:

```csharp
public class OrderTestsNamingConventions
{
    // Pattern: MethodName_Condition_ExpectedResult
    
    [Fact]
    public void AddItem_WithValidQuantity_IncreasesItemCount()
    {
        // ...
    }
    
    [Fact]
    public void AddItem_WithZeroQuantity_ThrowsDomainException()
    {
        // ...
    }
    
    [Fact]
    public void ConfirmOrder_WhenAlreadyConfirmed_ThrowsDomainException()
    {
        // ...
    }
    
    [Fact]
    public void CalculateTotal_WithMultipleLines_ReturnsSumOfAllSubtotals()
    {
        // ...
    }
}
```

---

## 6. Test Data Builders and Fixtures

Create reusable test data:

```csharp
// Builder pattern for complex test objects
public class OrderBuilder
{
    private int _customerId = 1;
    private List<OrderLine> _lines = new();
    
    public OrderBuilder WithCustomer(int customerId)
    {
        _customerId = customerId;
        return this;
    }
    
    public OrderBuilder AddItem(int bookId, int quantity, decimal price)
    {
        _lines.Add(new OrderLine(
            new BookId(bookId),
            quantity,
            new Money(price, "USD")
        ));
        return this;
    }
    
    public Order Build()
    {
        if (!_lines.Any())
            _lines.Add(new OrderLine(new BookId(1), 1, new Money(10, "USD")));
        
        return Order.Create(_customerId, _lines);
    }
}

// Usage
public class OrderTests
{
    [Fact]
    public void Order_WithMultipleItems_CalculatesCorrectTotal()
    {
        // Arrange
        var order = new OrderBuilder()
            .WithCustomer(1)
            .AddItem(bookId: 1, quantity: 2, price: 50)
            .AddItem(bookId: 2, quantity: 1, price: 30)
            .Build();
        
        // Act & Assert
        order.Total.Amount.Should().Be(130);
    }
    
    [Fact]
    public void Order_WithDefaultValues_StillValid()
    {
        // Arrange
        var order = new OrderBuilder().Build();
        
        // Act & Assert
        order.Should().NotBeNull();
        order.OrderLines.Should().NotBeEmpty();
    }
}

// Fixtures for shared setup
public class OrderTestFixture
{
    public Customer DefaultCustomer { get; } = new Customer { Id = 1 };
    public Book DefaultBook { get; } = new Book 
    { 
        Id = 1, 
        Title = "Test Book",
        Price = new Money(29.99m, "USD") 
    };
    public List<Book> SampleBooks { get; } = new()
    {
        new Book { Id = 1, Title = "Book 1", Price = new Money(10, "USD") },
        new Book { Id = 2, Title = "Book 2", Price = new Money(20, "USD") },
        new Book { Id = 3, Title = "Book 3", Price = new Money(30, "USD") }
    };
}

public class OrderTestsWithFixture : IClassFixture<OrderTestFixture>
{
    private readonly OrderTestFixture _fixture;
    
    public OrderTestsWithFixture(OrderTestFixture fixture)
    {
        _fixture = fixture;
    }
    
    [Fact]
    public void Order_WithDefaultBook_CalculatesTotalCorrectly()
    {
        // Arrange
        var order = Order.Create(_fixture.DefaultCustomer.Id, 
            new List<OrderLine>
            {
                new OrderLine(
                    new BookId(_fixture.DefaultBook.Id),
                    2,
                    _fixture.DefaultBook.Price
                )
            }
        );
        
        // Act & Assert
        order.Total.Amount.Should().Be(59.98m);
    }
}
```

---

## 7. Best Practices for Unit Tests

| Practice | Benefit |
|----------|---------|
| **One assertion per test** | Clear what failed, easier to understand |
| **Descriptive names** | Test serves as documentation |
| **Isolated tests** | Tests don't depend on each other |
| **Minimal mocking** | Tests focus on real logic |
| **Setup/Teardown** | Consistent test state |
| **Fast execution** | Quick feedback loop |
| **No hard-coded paths** | Tests work on any machine |

---

## 8. Common Anti-Patterns

| Anti-Pattern | Problem | Solution |
|--------------|---------|----------|
| **Testing implementation** | Tests break when refactoring | Test behavior, not implementation |
| **Over-mocking** | Tests become brittle | Mock only external dependencies |
| **Test interdependence** | Order matters, hard to debug | Each test must be independent |
| **Too many assertions** | Unclear what failed | One logical assertion per test |
| **Ignoring test names** | Tests don't document behavior | Use descriptive, clear names |
| **Slow tests** | Feedback loop slows development | Keep unit tests fast (< 1 second) |

---

## Summary

Unit tests form the foundation of quality assurance:

1. **Domain layer**: Pure logic with minimal mocks
2. **Application layer**: Mock repositories and external services
3. **Arrange-Act-Assert**: Clear test structure
4. **xUnit + Moq + FluentAssertions**: Standard .NET tools
5. **Builders and Fixtures**: Reusable test data
6. **Clear naming**: Tests document behavior

Next topic covers Integration Testing, which validates components working together.
