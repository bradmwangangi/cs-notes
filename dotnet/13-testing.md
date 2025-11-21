# Testing

Write reliable code by testing it thoroughly.

## Unit Testing Setup

```bash
# Create test project
dotnet new xunit -n MyApp.Tests
cd MyApp.Tests
dotnet add reference ../MyApp/MyApp.csproj
```

### xUnit vs NUnit vs MSTest

xUnit is modern and recommended. All three work similarly.

## Unit Tests

Test individual methods in isolation:

```csharp
using Xunit;
using MyApp.Services;

public class CalculatorTests
{
    private readonly Calculator _calculator = new();

    [Fact]
    public void Add_WithValidNumbers_ReturnsSum()
    {
        // Arrange
        int a = 5;
        int b = 3;

        // Act
        int result = _calculator.Add(a, b);

        // Assert
        Assert.Equal(8, result);
    }

    [Fact]
    public void Subtract_WithValidNumbers_ReturnsDifference()
    {
        int result = _calculator.Subtract(10, 3);
        Assert.Equal(7, result);
    }

    [Theory]
    [InlineData(5, 3, 8)]
    [InlineData(10, 20, 30)]
    [InlineData(-5, 5, 0)]
    public void Add_WithVariousInputs_ReturnsCorrectSum(int a, int b, int expected)
    {
        int result = _calculator.Add(a, b);
        Assert.Equal(expected, result);
    }
}

public class Calculator
{
    public int Add(int a, int b) => a + b;
    public int Subtract(int a, int b) => a - b;
}
```

## Assertions

```csharp
public class AssertionExamples
{
    [Fact]
    public void AssertionExamples_DemonstrateMethods()
    {
        // Equality
        Assert.Equal(5, 5);
        Assert.NotEqual(5, 3);

        // Null
        string? text = null;
        Assert.Null(text);
        Assert.NotNull("value");

        // Boolean
        Assert.True(true);
        Assert.False(false);

        // Strings
        Assert.StartsWith("Hello", "Hello World");
        Assert.EndsWith("World", "Hello World");
        Assert.Contains("llo", "Hello");

        // Collections
        var list = new List<int> { 1, 2, 3 };
        Assert.Contains(2, list);
        Assert.DoesNotContain(5, list);
        Assert.Empty(new List<int>());
        Assert.Single(new List<int> { 1 });

        // Type checking
        object obj = "string";
        Assert.IsType<string>(obj);
        Assert.IsNotType<int>(obj);

        // Exceptions
        var ex = Assert.Throws<ArgumentException>(() => throw new ArgumentException("bad"));
        Assert.Equal("bad", ex.Message);

        // Ranges
        Assert.InRange(5, 0, 10);
        Assert.NotInRange(15, 0, 10);

        // Collections equality
        Assert.Equal(
            new List<int> { 1, 2, 3 },
            new List<int> { 1, 2, 3 }
        );
    }
}
```

## Mocking with Moq

Isolate code under test by mocking dependencies:

```bash
dotnet add package Moq
```

```csharp
using Moq;

public class UserService
{
    private readonly IUserRepository _repository;

    public UserService(IUserRepository repository) => _repository = repository;

    public async Task<User> GetUserAsync(int id)
    {
        var user = await _repository.GetUserAsync(id);
        if (user == null) throw new UserNotFoundException();
        return user;
    }

    public async Task<List<User>> GetActiveUsersAsync()
    {
        return await _repository.GetActiveUsersAsync();
    }
}

public interface IUserRepository
{
    Task<User> GetUserAsync(int id);
    Task<List<User>> GetActiveUsersAsync();
}

public class UserServiceTests
{
    [Fact]
    public async Task GetUserAsync_WithValidId_ReturnsUser()
    {
        // Arrange
        var mockRepository = new Mock<IUserRepository>();
        var user = new User { Id = 1, Name = "Alice" };

        mockRepository
            .Setup(r => r.GetUserAsync(1))
            .ReturnsAsync(user);

        var service = new UserService(mockRepository.Object);

        // Act
        var result = await service.GetUserAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Alice", result.Name);
        
        // Verify the method was called
        mockRepository.Verify(r => r.GetUserAsync(1), Times.Once);
    }

    [Fact]
    public async Task GetUserAsync_WithInvalidId_ThrowsException()
    {
        // Arrange
        var mockRepository = new Mock<IUserRepository>();
        mockRepository
            .Setup(r => r.GetUserAsync(999))
            .ReturnsAsync((User)null);

        var service = new UserService(mockRepository.Object);

        // Act & Assert
        await Assert.ThrowsAsync<UserNotFoundException>(
            () => service.GetUserAsync(999)
        );
    }

    [Fact]
    public async Task GetActiveUsersAsync_ReturnsMultipleUsers()
    {
        // Arrange
        var mockRepository = new Mock<IUserRepository>();
        var users = new List<User>
        {
            new User { Id = 1, Name = "Alice" },
            new User { Id = 2, Name = "Bob" }
        };

        mockRepository
            .Setup(r => r.GetActiveUsersAsync())
            .ReturnsAsync(users);

        var service = new UserService(mockRepository.Object);

        // Act
        var result = await service.GetActiveUsersAsync();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Alice", result[0].Name);
    }

    [Fact]
    public async Task GetUserAsync_VerifiesRepositoryCalledExactlyOnce()
    {
        var mockRepository = new Mock<IUserRepository>();
        mockRepository
            .Setup(r => r.GetUserAsync(It.IsAny<int>()))
            .ReturnsAsync(new User { Id = 1, Name = "Alice" });

        var service = new UserService(mockRepository.Object);

        await service.GetUserAsync(1);

        // Verify called exactly once with any integer
        mockRepository.Verify(r => r.GetUserAsync(It.IsAny<int>()), Times.Once);

        // Verify never called
        mockRepository.Verify(r => r.GetActiveUsersAsync(), Times.Never);
    }
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class UserNotFoundException : Exception { }
```

## Integration Tests

Test multiple components together:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Text.Json;

public class UserControllerIntegrationTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory;
    private HttpClient _client;
    private TestDatabaseContext _dbContext;

    public async Task InitializeAsync()
    {
        // Setup
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
        
        _dbContext = _factory.Services.GetRequiredService<TestDatabaseContext>();
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.Database.EnsureDeletedAsync();
        _client?.Dispose();
        _factory?.Dispose();
    }

    [Fact]
    public async Task GetUser_WithValidId_ReturnsUser()
    {
        // Arrange - seed data
        var user = new User { Id = 1, Name = "Alice", Email = "alice@example.com" };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/users/1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<User>(content);
        Assert.Equal("Alice", result.Name);
    }

    [Fact]
    public async Task CreateUser_WithValidData_ReturnsCreated()
    {
        // Arrange
        var dto = new { name = "Bob", email = "bob@example.com" };
        var json = JsonSerializer.Serialize(dto);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/users", content);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_WithValidId_ReturnsNoContent()
    {
        // Arrange
        var user = new User { Id = 1, Name = "Charlie" };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var response = await _client.DeleteAsync("/api/users/1");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await _dbContext.Users.FindAsync(1));
    }
}
```

## Test Fixtures

Share setup/teardown across tests:

```csharp
public class DatabaseFixture : IAsyncLifetime
{
    private readonly AppDbContext _dbContext;

    public DatabaseFixture()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("TestDb")
            .Options;
        _dbContext = new AppDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _dbContext.Database.EnsureCreatedAsync();
        await SeedDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.Database.EnsureDeletedAsync();
    }

    private async Task SeedDataAsync()
    {
        _dbContext.Users.Add(new User { Id = 1, Name = "Alice" });
        _dbContext.Users.Add(new User { Id = 2, Name = "Bob" });
        await _dbContext.SaveChangesAsync();
    }

    public AppDbContext GetContext() => _dbContext;
}

public class UserRepositoryTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    private readonly UserRepository _repository;

    public UserRepositoryTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _repository = new UserRepository(_fixture.GetContext());
    }

    [Fact]
    public async Task GetUserAsync_WithValidId_ReturnsUser()
    {
        var user = await _repository.GetUserAsync(1);
        Assert.NotNull(user);
        Assert.Equal("Alice", user.Name);
    }
}
```

## Test Data Builders

Create complex test objects fluently:

```csharp
public class UserBuilder
{
    private int _id = 1;
    private string _name = "Default User";
    private string _email = "user@example.com";
    private List<string> _roles = new() { "User" };

    public UserBuilder WithId(int id) { _id = id; return this; }
    public UserBuilder WithName(string name) { _name = name; return this; }
    public UserBuilder WithEmail(string email) { _email = email; return this; }
    public UserBuilder WithRole(string role) { _roles.Add(role); return this; }

    public User Build() =>
        new User { Id = _id, Name = _name, Email = _email, Roles = _roles };
}

public class UserServiceTests
{
    [Fact]
    public async Task ProcessUserAsync_WithAdminRole_GrantsPermissions()
    {
        var user = new UserBuilder()
            .WithName("Admin User")
            .WithRole("Admin")
            .Build();

        var service = new UserService();
        var hasPermission = await service.HasPermissionAsync(user, "delete");

        Assert.True(hasPermission);
    }
}
```

## Test Organization

```csharp
public class ProductServiceTests
{
    // Group related tests
    public class GetProductAsync
    {
        private readonly Mock<IProductRepository> _mockRepository;
        private readonly ProductService _service;

        public GetProductAsync()
        {
            _mockRepository = new Mock<IProductRepository>();
            _service = new ProductService(_mockRepository.Object);
        }

        [Fact]
        public async Task WithValidId_ReturnsProduct()
        {
            _mockRepository
                .Setup(r => r.GetAsync(1))
                .ReturnsAsync(new Product { Id = 1, Name = "Widget" });

            var result = await _service.GetProductAsync(1);

            Assert.NotNull(result);
        }

        [Fact]
        public async Task WithInvalidId_ThrowsNotFoundException()
        {
            _mockRepository
                .Setup(r => r.GetAsync(999))
                .ReturnsAsync((Product)null);

            await Assert.ThrowsAsync<NotFoundException>(
                () => _service.GetProductAsync(999)
            );
        }
    }

    public class CreateProductAsync
    {
        [Fact]
        public async Task WithValidData_CreatesProduct()
        {
            // Test creation logic
        }
    }
}
```

## Running Tests

```bash
# Run all tests
dotnet test

# Run specific test
dotnet test --filter "UserServiceTests.GetUserAsync_WithValidId_ReturnsUser"

# Run with verbosity
dotnet test -v detailed

# Run with coverage (requires coverlet)
dotnet add package coverlet.collector
dotnet test /p:CollectCoverage=true

# Watch mode
dotnet watch test
```

## Best Practices

```csharp
// ✓ Clear naming: MethodName_Condition_ExpectedResult
[Fact]
public void Add_WithPositiveNumbers_ReturnsSum() { }

// ✗ Poor naming
[Fact]
public void TestAdd() { }

// ✓ Arrange-Act-Assert pattern
[Fact]
public void GoodTest()
{
    var calculator = new Calculator();
    var result = calculator.Add(2, 3);
    Assert.Equal(5, result);
}

// ✓ Test one thing
[Fact]
public void GetUser_WithValidId_ReturnsUser() { }

// ✗ Testing multiple concerns
[Fact]
public void UserFlow()
{
    var user = GetUser(1);
    UpdateUser(user);
    DeleteUser(user);
    Assert.Pass();
}

// ✓ Use mocks for dependencies
var mockRepository = new Mock<IUserRepository>();

// ✗ Don't test implementation details
[Fact]
public void InternalMethod_IsCalledCorrectly() { }

// ✓ Meaningful assertions
Assert.Equal(expected, actual);

// ✗ Vague assertions
Assert.NotNull(result);
```

## Practice Exercises

1. **Calculator Tests**: Write comprehensive tests for a calculator class
2. **User Service Mock**: Mock dependencies and test business logic
3. **API Integration Tests**: Test controller endpoints end-to-end
4. **Database Tests**: Test repository methods with in-memory database
5. **Test Coverage**: Achieve 80%+ code coverage on a service

## Key Takeaways

- **Unit tests** isolate individual methods; **integration tests** test components together
- **xUnit** is modern and recommended (also consider NUnit, MSTest)
- **Moq** for mocking dependencies
- **WebApplicationFactory** for integration testing ASP.NET Core APIs
- **AAA pattern**: Arrange, Act, Assert
- **One assertion per test** (or related assertions)
- **Clear naming**: MethodName_Condition_ExpectedResult
- **Fixtures** for shared setup/teardown
- **Builders** for complex test data
- Test **behavior**, not **implementation**
