# 22. Integration Testing

## Overview
Integration tests validate multiple components working together. In enterprise ASP.NET applications, they test the full request-response pipeline, database interactions, and cross-layer communication without deploying to a server.

---

## 1. Integration Testing Fundamentals

### 1.1 Testing Scope

Integration tests sit between unit tests and end-to-end tests:

```
Unit Tests          Integration Tests       E2E Tests
┌─────────────┐     ┌──────────────────┐    ┌──────────────┐
│ Individual  │     │ Multiple         │    │ Entire       │
│ classes     │     │ components       │    │ application  │
│ In isolation│     │ Real DB/APIs     │    │ + external   │
│ Fast        │     │ Moderate speed   │    │ systems      │
│ Many tests  │     │ Fewer tests      │    │ Few tests    │
└─────────────┘     └──────────────────┘    └──────────────┘
```

### 1.2 Project Setup

```csharp
// Bookstore.Api.IntegrationTests.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <TargetFramework>net8.0</TargetFramework>
  <IsTestProject>true</IsTestProject>
  
  <ItemGroup>
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.0" />
    <PackageReference Include="Testcontainers" Version="3.4.0" />
    <PackageReference Include="Testcontainers.MsSql" Version="3.4.0" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>
  
  <ItemGroup>
    <ProjectReference Include="..\Bookstore.Api\Bookstore.Api.csproj" />
  </ItemGroup>
</Project>
```

---

## 2. WebApplicationFactory for Testing

`WebApplicationFactory` creates an in-memory test server running your actual application.

### 2.1 Basic Setup

```csharp
// Base test class
public class IntegrationTestBase : IAsyncLifetime
{
    protected readonly HttpClient HttpClient;
    protected readonly WebApplicationFactory<Program> WebFactory;
    protected readonly BookstoreDbContext DbContext;
    
    public IntegrationTestBase()
    {
        WebFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove the normal database service
                    var descriptor = services.SingleOrDefault(d => 
                        d.ServiceType == typeof(DbContextOptions<BookstoreDbContext>));
                    
                    if (descriptor != null)
                        services.Remove(descriptor);
                    
                    // Add in-memory database for testing
                    services.AddDbContext<BookstoreDbContext>(options =>
                        options.UseInMemoryDatabase("BookstoreTest")
                    );
                });
            });
        
        HttpClient = WebFactory.CreateClient();
        
        // Get the database context for seeding data
        var scope = WebFactory.Services.CreateScope();
        DbContext = scope.ServiceProvider.GetRequiredService<BookstoreDbContext>();
    }
    
    // IAsyncLifetime implementation
    public async Task InitializeAsync()
    {
        await DbContext.Database.EnsureCreatedAsync();
    }
    
    public async Task DisposeAsync()
    {
        await WebFactory.DisposeAsync();
        DbContext.Dispose();
    }
}

// Usage
public class OrdersEndpointTests : IntegrationTestBase
{
    [Fact]
    public async Task PostOrder_WithValidRequest_Returns201Created()
    {
        // Arrange
        var request = new PlaceOrderRequest
        {
            CustomerId = 1,
            Items = new List<OrderItemRequest>
            {
                new OrderItemRequest { BookId = 1, Quantity = 2 }
            }
        };
        
        // Act
        var response = await HttpClient.PostAsJsonAsync("/api/orders", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var content = await response.Content.ReadAsAsync<PlaceOrderResponse>();
        content.OrderId.Should().BeGreaterThan(0);
        content.Status.Should().Be("Success");
    }
}
```

### 2.2 Custom WebApplicationFactory

Create a specialized factory for consistent test configuration:

```csharp
public class BookstoreWebApplicationFactory : WebApplicationFactory<Program>
{
    private string _connectionString = 
        "Server=(localdb)\\mssqllocaldb;Database=BookstoreTest;Trusted_Connection=true;";
    
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove normal database
            var descriptor = services.SingleOrDefault(d => 
                d.ServiceType == typeof(DbContextOptions<BookstoreDbContext>));
            
            if (descriptor != null)
                services.Remove(descriptor);
            
            // Use test database
            services.AddDbContext<BookstoreDbContext>(options =>
                options.UseSqlServer(_connectionString)
            );
            
            // Override email service to prevent actual emails
            var emailDescriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(IEmailService));
            if (emailDescriptor != null)
                services.Remove(emailDescriptor);
            
            services.AddScoped<IEmailService, MockEmailService>();
            
            // Build service provider and create database
            var sp = services.BuildServiceProvider();
            
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BookstoreDbContext>();
                db.Database.EnsureDeleted();
                db.Database.EnsureCreated();
            }
        });
        
        builder.UseEnvironment("Testing");
    }
    
    public BookstoreDbContext GetDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<BookstoreDbContext>();
    }
}

// Mock email service for testing
public class MockEmailService : IEmailService
{
    public List<SentEmail> SentEmails { get; } = new();
    
    public async Task SendAsync(string to, string subject, string body)
    {
        SentEmails.Add(new SentEmail 
        { 
            To = to, 
            Subject = subject, 
            Body = body,
            SentAt = DateTime.UtcNow
        });
        
        await Task.CompletedTask;
    }
}

public class SentEmail
{
    public string To { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }
    public DateTime SentAt { get; set; }
}
```

---

## 3. Testing Data Access Layer

### 3.1 Database Testing with Real Database

```csharp
public class OrderRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly BookstoreDbContext _dbContext;
    private readonly IOrderRepository _repository;
    
    public OrderRepositoryIntegrationTests()
    {
        var services = new ServiceCollection();
        
        services.AddDbContext<BookstoreDbContext>(options =>
            options.UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=BookstoreTest;Trusted_Connection=true;"
            )
        );
        
        services.AddScoped<IOrderRepository, EFOrderRepository>();
        
        var serviceProvider = services.BuildServiceProvider();
        _dbContext = serviceProvider.GetRequiredService<BookstoreDbContext>();
        _repository = serviceProvider.GetRequiredService<IOrderRepository>();
    }
    
    public async Task InitializeAsync()
    {
        // Clean and create fresh database for each test
        await _dbContext.Database.EnsureDeletedAsync();
        await _dbContext.Database.EnsureCreatedAsync();
    }
    
    public async Task DisposeAsync()
    {
        await _dbContext.Database.EnsureDeletedAsync();
        _dbContext.Dispose();
    }
    
    [Fact]
    public async Task SaveAsync_WithNewOrder_PersistsToDatabase()
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 2, new Money(29.99m, "USD"))
        });
        
        // Act
        await _repository.SaveAsync(order);
        
        // Assert
        var savedOrder = await _repository.GetByIdAsync(order.Id);
        savedOrder.Should().NotBeNull();
        savedOrder.CustomerId.Should().Be(1);
        savedOrder.OrderLines.Should().HaveCount(1);
    }
    
    [Fact]
    public async Task GetByIdAsync_WithNonexistentId_ReturnsNull()
    {
        // Act
        var order = await _repository.GetByIdAsync(999);
        
        // Assert
        order.Should().BeNull();
    }
    
    [Fact]
    public async Task UpdateAsync_WithModifiedOrder_PersistsChanges()
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 1, new Money(100, "USD"))
        });
        
        await _repository.SaveAsync(order);
        
        // Act
        order.AddItem(new BookId(2), 2, new Money(50, "USD"));
        await _repository.UpdateAsync(order);
        
        // Assert
        var updatedOrder = await _repository.GetByIdAsync(order.Id);
        updatedOrder.OrderLines.Should().HaveCount(2);
        updatedOrder.Total.Amount.Should().Be(200);
    }
    
    [Fact]
    public async Task DeleteAsync_WithExistingOrder_RemovesFromDatabase()
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 1, new Money(100, "USD"))
        });
        
        await _repository.SaveAsync(order);
        var orderId = order.Id;
        
        // Act
        await _repository.DeleteAsync(orderId);
        
        // Assert
        var deletedOrder = await _repository.GetByIdAsync(orderId);
        deletedOrder.Should().BeNull();
    }
}
```

### 3.2 Testing with Testcontainers

For real database testing with SQL Server:

```csharp
public class OrderRepositoryWithTestcontainersTests : IAsyncLifetime
{
    private MsSqlContainer _mssqlContainer;
    private BookstoreDbContext _dbContext;
    private IOrderRepository _repository;
    
    public async Task InitializeAsync()
    {
        // Start SQL Server container
        _mssqlContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("SA_PASSWORD", "Test@1234")
            .WithCleanUp(true)
            .Build();
        
        await _mssqlContainer.StartAsync();
        
        // Create DbContext with container connection string
        var connectionString = _mssqlContainer.GetConnectionString();
        
        var services = new ServiceCollection();
        services.AddDbContext<BookstoreDbContext>(options =>
            options.UseSqlServer(connectionString)
        );
        services.AddScoped<IOrderRepository, EFOrderRepository>();
        
        var serviceProvider = services.BuildServiceProvider();
        _dbContext = serviceProvider.GetRequiredService<BookstoreDbContext>();
        _repository = serviceProvider.GetRequiredService<IOrderRepository>();
        
        // Create schema
        await _dbContext.Database.EnsureCreatedAsync();
    }
    
    public async Task DisposeAsync()
    {
        if (_dbContext != null)
        {
            await _dbContext.Database.EnsureDeletedAsync();
            _dbContext.Dispose();
        }
        
        if (_mssqlContainer != null)
        {
            await _mssqlContainer.StopAsync();
            await _mssqlContainer.DisposeAsync();
        }
    }
    
    [Fact]
    public async Task SaveAsync_WithRealDatabase_PersistsCorrectly()
    {
        // Arrange
        var order = Order.Create(1, new List<OrderLine>
        {
            new OrderLine(new BookId(1), 3, new Money(19.99m, "USD"))
        });
        
        // Act
        await _repository.SaveAsync(order);
        
        // Assert - Query directly from database
        var ordersInDb = await _dbContext.Orders.ToListAsync();
        ordersInDb.Should().HaveCount(1);
        ordersInDb[0].CustomerId.Should().Be(1);
    }
}
```

---

## 4. Testing Complete Request-Response Pipeline

### 4.1 API Endpoint Testing

```csharp
public class OrdersApiIntegrationTests : IClassFixture<BookstoreWebApplicationFactory>
{
    private readonly BookstoreWebApplicationFactory _factory;
    private readonly HttpClient _httpClient;
    private readonly BookstoreDbContext _dbContext;
    
    public OrdersApiIntegrationTests(BookstoreWebApplicationFactory factory)
    {
        _factory = factory;
        _httpClient = factory.CreateClient();
        _dbContext = factory.GetDbContext();
    }
    
    private async Task SeedDataAsync()
    {
        var customer = new Customer 
        { 
            Id = 1, 
            FirstName = "John",
            Email = new EmailAddress("john@example.com")
        };
        
        var book = new Book 
        { 
            Id = 1, 
            Title = "Test Book",
            Author = "Test Author",
            Price = new Money(29.99m, "USD")
        };
        
        _dbContext.Customers.Add(customer);
        _dbContext.Books.Add(book);
        
        await _dbContext.SaveChangesAsync();
    }
    
    [Fact]
    public async Task PlaceOrder_WithValidData_ReturnsSuccessfulResponse()
    {
        // Arrange
        await SeedDataAsync();
        
        var request = new PlaceOrderRequest
        {
            CustomerId = 1,
            Items = new List<OrderItemRequest>
            {
                new OrderItemRequest { BookId = 1, Quantity = 2 }
            }
        };
        
        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/orders", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        
        var result = await response.Content.ReadAsAsync<PlaceOrderResponse>();
        result.Should().NotBeNull();
        result.OrderId.Should().BeGreaterThan(0);
        result.Total.Should().Be(59.98m);
        result.Status.Should().Be("Success");
    }
    
    [Fact]
    public async Task PlaceOrder_WithInvalidCustomer_ReturnsBadRequest()
    {
        // Arrange - Don't seed customer
        var request = new PlaceOrderRequest
        {
            CustomerId = 999,
            Items = new List<OrderItemRequest>
            {
                new OrderItemRequest { BookId = 1, Quantity = 1 }
            }
        };
        
        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/orders", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var error = await response.Content.ReadAsAsync<ErrorResponse>();
        error.Message.Should().Contain("Customer");
    }
    
    [Fact]
    public async Task PlaceOrder_WithMissingItems_ReturnsBadRequest()
    {
        // Arrange
        await SeedDataAsync();
        
        var request = new PlaceOrderRequest
        {
            CustomerId = 1,
            Items = new List<OrderItemRequest>()  // Empty
        };
        
        // Act
        var response = await _httpClient.PostAsJsonAsync("/api/orders", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    
    [Fact]
    public async Task GetOrder_WithExistingOrder_ReturnsOrderDetails()
    {
        // Arrange
        await SeedDataAsync();
        
        // Create order through API
        var createRequest = new PlaceOrderRequest
        {
            CustomerId = 1,
            Items = new List<OrderItemRequest>
            {
                new OrderItemRequest { BookId = 1, Quantity = 1 }
            }
        };
        
        var createResponse = await _httpClient
            .PostAsJsonAsync("/api/orders", createRequest);
        var createdOrder = await createResponse.Content
            .ReadAsAsync<PlaceOrderResponse>();
        
        // Act
        var getResponse = await _httpClient
            .GetAsync($"/api/orders/{createdOrder.OrderId}");
        
        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var orderDetails = await getResponse.Content
            .ReadAsAsync<OrderDetailsResponse>();
        orderDetails.OrderId.Should().Be(createdOrder.OrderId);
        orderDetails.Total.Should().Be(29.99m);
    }
    
    [Fact]
    public async Task GetOrder_WithNonexistentId_ReturnsNotFound()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/orders/999");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

### 4.2 Testing Multiple Related Operations

```csharp
public class OrderWorkflowIntegrationTests : IClassFixture<BookstoreWebApplicationFactory>
{
    private readonly BookstoreWebApplicationFactory _factory;
    private readonly HttpClient _httpClient;
    private readonly BookstoreDbContext _dbContext;
    
    public OrderWorkflowIntegrationTests(BookstoreWebApplicationFactory factory)
    {
        _factory = factory;
        _httpClient = factory.CreateClient();
        _dbContext = factory.GetDbContext();
    }
    
    [Fact]
    public async Task CompleteOrderWorkflow_CreateConfirmAndPay_SuccessfullyProcesses()
    {
        // Arrange - Seed initial data
        var customer = new Customer 
        { 
            Id = 1, 
            FirstName = "John",
            Email = new EmailAddress("john@example.com")
        };
        
        var book1 = new Book 
        { 
            Id = 1, 
            Title = "Book 1",
            Price = new Money(29.99m, "USD")
        };
        
        var book2 = new Book 
        { 
            Id = 2, 
            Title = "Book 2",
            Price = new Money(39.99m, "USD")
        };
        
        _dbContext.Customers.Add(customer);
        _dbContext.Books.AddRange(book1, book2);
        await _dbContext.SaveChangesAsync();
        
        // ACT 1: Place order with multiple items
        var placeOrderRequest = new PlaceOrderRequest
        {
            CustomerId = 1,
            Items = new List<OrderItemRequest>
            {
                new OrderItemRequest { BookId = 1, Quantity = 2 },
                new OrderItemRequest { BookId = 2, Quantity = 1 }
            }
        };
        
        var placeResponse = await _httpClient
            .PostAsJsonAsync("/api/orders", placeOrderRequest);
        placeResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var orderId = (await placeResponse.Content
            .ReadAsAsync<PlaceOrderResponse>()).OrderId;
        
        // ASSERT 1: Order created successfully
        var getResponse = await _httpClient
            .GetAsync($"/api/orders/{orderId}");
        var orderDetails = await getResponse.Content
            .ReadAsAsync<OrderDetailsResponse>();
        
        orderDetails.Total.Should().Be(99.97m);
        orderDetails.Items.Should().HaveCount(2);
        
        // ACT 2: Process payment
        var paymentRequest = new ProcessPaymentRequest
        {
            OrderId = orderId,
            CardToken = "tok_test_123",
            Amount = 99.97m
        };
        
        var paymentResponse = await _httpClient
            .PostAsJsonAsync("/api/orders/payment", paymentRequest);
        paymentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // ASSERT 2: Order status updated
        var updatedResponse = await _httpClient
            .GetAsync($"/api/orders/{orderId}");
        var updatedOrder = await updatedResponse.Content
            .ReadAsAsync<OrderDetailsResponse>();
        
        updatedOrder.Status.Should().Be("Paid");
        
        // ASSERT 3: Confirmation email was queued
        // (assuming MockEmailService is used)
        var emailService = _factory.Services
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<IEmailService>();
        
        if (emailService is MockEmailService mockEmailService)
        {
            mockEmailService.SentEmails
                .Should()
                .ContainSingle(e => e.To == "john@example.com");
        }
    }
}
```

---

## 5. Testing Authentication and Authorization

### 5.1 Testing Protected Endpoints

```csharp
public class AuthorizedEndpointTests : IClassFixture<BookstoreWebApplicationFactory>
{
    private readonly HttpClient _httpClient;
    private readonly BookstoreWebApplicationFactory _factory;
    
    public AuthorizedEndpointTests(BookstoreWebApplicationFactory factory)
    {
        _factory = factory;
        _httpClient = factory.CreateClient();
    }
    
    [Fact]
    public async Task ProtectedEndpoint_WithoutAuthorization_ReturnsForbidden()
    {
        // Act
        var response = await _httpClient.GetAsync("/api/orders/admin");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
    
    [Fact]
    public async Task ProtectedEndpoint_WithValidToken_ReturnsSuccess()
    {
        // Arrange
        var token = GenerateTestToken("user@example.com", new[] { "user" });
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);
        
        // Act
        var response = await _httpClient.GetAsync("/api/orders");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
    
    private string GenerateTestToken(string userId, string[] roles)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes("test_secret_key_at_least_32_characters_long");
        
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userId)
        };
        
        claims.AddRange(roles.Select(role => 
            new Claim(ClaimTypes.Role, role)
        ));
        
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature
            )
        };
        
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
```

---

## 6. Best Practices for Integration Tests

| Practice | Implementation |
|----------|-----------------|
| **Isolated databases** | Fresh database per test or test class |
| **Seed data builders** | Reusable data creation helpers |
| **Async operations** | Use IAsyncLifetime for setup/teardown |
| **Mock external services** | Override to prevent real API calls |
| **Test multiple scenarios** | Happy path + error paths |
| **Assert business impact** | Verify database state changed correctly |
| **Keep tests maintainable** | Extract common setup to fixtures |

---

## 7. Common Integration Test Patterns

### 7.1 Testing Event Publishing

```csharp
public class DomainEventPublishingIntegrationTests : 
    IClassFixture<BookstoreWebApplicationFactory>
{
    private readonly BookstoreWebApplicationFactory _factory;
    private readonly HttpClient _httpClient;
    private readonly BookstoreDbContext _dbContext;
    
    public DomainEventPublishingIntegrationTests(
        BookstoreWebApplicationFactory factory)
    {
        _factory = factory;
        _httpClient = factory.CreateClient();
        _dbContext = factory.GetDbContext();
    }
    
    [Fact]
    public async Task PlaceOrder_PublishesDomainEvent()
    {
        // Arrange
        var customer = new Customer 
        { 
            Id = 1, 
            Email = new EmailAddress("test@example.com")
        };
        
        var book = new Book 
        { 
            Id = 1, 
            Price = new Money(50, "USD")
        };
        
        _dbContext.Customers.Add(customer);
        _dbContext.Books.Add(book);
        await _dbContext.SaveChangesAsync();
        
        var request = new PlaceOrderRequest
        {
            CustomerId = 1,
            Items = new List<OrderItemRequest>
            {
                new OrderItemRequest { BookId = 1, Quantity = 1 }
            }
        };
        
        // Act
        var response = await _httpClient
            .PostAsJsonAsync("/api/orders", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        
        // Get email service and verify event was handled
        var scope = _factory.Services.CreateScope();
        var emailService = scope.ServiceProvider
            .GetRequiredService<IEmailService>();
        
        if (emailService is MockEmailService mockService)
        {
            mockService.SentEmails
                .Should()
                .ContainSingle(e => e.To == "test@example.com");
        }
    }
}
```

### 7.2 Testing Database Constraints

```csharp
public class DatabaseConstraintTests : IAsyncLifetime
{
    private BookstoreDbContext _dbContext;
    
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<BookstoreDbContext>(options =>
            options.UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=BookstoreTest;Trusted_Connection=true;"
            )
        );
        
        var serviceProvider = services.BuildServiceProvider();
        _dbContext = serviceProvider.GetRequiredService<BookstoreDbContext>();
        
        await _dbContext.Database.EnsureDeletedAsync();
        await _dbContext.Database.EnsureCreatedAsync();
    }
    
    public async Task DisposeAsync()
    {
        await _dbContext.Database.EnsureDeletedAsync();
        _dbContext.Dispose();
    }
    
    [Fact]
    public async Task Order_WithoutCustomer_ViolatesForeignKeyConstraint()
    {
        // Arrange
        var order = new Order 
        { 
            Id = 1, 
            CustomerId = 999  // Non-existent customer
        };
        
        _dbContext.Orders.Add(order);
        
        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await _dbContext.SaveChangesAsync()
        );
    }
}
```

---

## Summary

Integration tests validate component interactions:

1. **WebApplicationFactory**: In-memory testing server
2. **Real databases**: In-memory or Testcontainers
3. **Full pipeline**: Request through response
4. **Seeded data**: Realistic test scenarios
5. **Mock externals**: Prevent side effects
6. **Business verification**: Correct state changes

Integration tests bridge unit tests and end-to-end tests, catching issues that unit tests miss while remaining fast and deterministic.

Next topic covers Advanced Testing techniques including property-based testing and performance testing.
