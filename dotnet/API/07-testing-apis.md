# Chapter 7: Testing APIs

## 7.1 Testing Strategy

Testing is essential for reliable APIs. A good test strategy covers multiple levels:

**Unit Tests** - Test individual functions/methods in isolation
- Test business logic
- Mock external dependencies
- Fast execution
- Example: Testing a service method

**Integration Tests** - Test components working together
- Test API endpoints with real database
- Test data flow across layers
- Slower than unit tests
- Example: POST endpoint saves to database correctly

**End-to-End Tests** - Test full workflow
- Test from client perspective
- Test with real infrastructure
- Slowest but most realistic
- Example: Login → Create resource → Verify in database

**For APIs:**
- 70% integration tests (endpoints + database)
- 20% unit tests (business logic)
- 10% end-to-end tests (critical paths)

---

## 7.2 Unit Testing

Unit tests verify individual components in isolation.

### Testing Services

```csharp
// Using xUnit and Moq
public class UserServiceTests
{
    [Fact]
    public async Task CreateUser_WithValidRequest_ReturnsUser()
    {
        // Arrange
        var mockRepository = new Mock<IUserRepository>();
        var mockPasswordService = new Mock<IPasswordService>();
        var service = new UserService(mockRepository.Object, mockPasswordService.Object);
        
        var request = new CreateUserRequest
        {
            Email = "john@example.com",
            Name = "John Doe",
            Password = "SecurePassword123!"
        };
        
        // Act
        var result = await service.CreateUserAsync(request);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(request.Email, result.Email);
        Assert.Equal(request.Name, result.Name);
        
        // Verify repository was called
        mockRepository.Verify(
            x => x.AddAsync(It.IsAny<User>()),
            Times.Once
        );
    }
    
    [Fact]
    public async Task CreateUser_WithDuplicateEmail_ThrowsException()
    {
        // Arrange
        var mockRepository = new Mock<IUserRepository>();
        mockRepository
            .Setup(x => x.GetByEmailAsync("john@example.com"))
            .ReturnsAsync(new User { Email = "john@example.com" });
        
        var mockPasswordService = new Mock<IPasswordService>();
        var service = new UserService(mockRepository.Object, mockPasswordService.Object);
        
        var request = new CreateUserRequest
        {
            Email = "john@example.com",
            Name = "John Doe",
            Password = "SecurePassword123!"
        };
        
        // Act & Assert
        await Assert.ThrowsAsync<DuplicateEmailException>(
            () => service.CreateUserAsync(request)
        );
    }
    
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task CreateUser_WithInvalidEmail_ThrowsException(string email)
    {
        // Arrange
        var mockRepository = new Mock<IUserRepository>();
        var mockPasswordService = new Mock<IPasswordService>();
        var service = new UserService(mockRepository.Object, mockPasswordService.Object);
        
        var request = new CreateUserRequest { Email = email };
        
        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateUserAsync(request)
        );
    }
}
```

**Key concepts:**

**Arrange** - Set up test data and mocks
**Act** - Execute the code being tested
**Assert** - Verify the result

**Mock** - Fake object replacing real dependency
**Verify** - Check that mocks were called as expected

### Test Fixtures

Reuse setup code with fixtures:

```csharp
public class UserServiceFixture : IDisposable
{
    public Mock<IUserRepository> MockRepository { get; }
    public Mock<IPasswordService> MockPasswordService { get; }
    public UserService Service { get; }
    
    public UserServiceFixture()
    {
        MockRepository = new Mock<IUserRepository>();
        MockPasswordService = new Mock<IPasswordService>();
        Service = new UserService(MockRepository.Object, MockPasswordService.Object);
    }
    
    public void Dispose()
    {
        MockRepository.Reset();
        MockPasswordService.Reset();
    }
}

public class UserServiceTests : IClassFixture<UserServiceFixture>
{
    private readonly UserServiceFixture _fixture;
    
    public UserServiceTests(UserServiceFixture fixture)
    {
        _fixture = fixture;
    }
    
    [Fact]
    public async Task CreateUser_WithValidRequest_ReturnsUser()
    {
        // Arrange - use fixture's mocks
        var request = new CreateUserRequest { /* ... */ };
        
        // Act
        var result = await _fixture.Service.CreateUserAsync(request);
        
        // Assert
        Assert.NotNull(result);
    }
}
```

---

## 7.3 Integration Testing

Integration tests verify endpoints work with real database.

### Using WebApplicationFactory

```csharp
public class UserApiIntegrationTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory;
    private HttpClient _client;
    private IServiceScope _scope;
    
    // Set up test server before tests
    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace real database with test database
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    services.Remove(descriptor);
                    
                    services.AddDbContext<AppDbContext>(options =>
                    {
                        options.UseInMemoryDatabase("TestDb");
                    });
                });
            });
        
        _client = _factory.CreateClient();
        
        // Create database and seed
        _scope = _factory.Services.CreateScope();
        var context = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Database.EnsureCreated();
        
        await SeedTestDataAsync(context);
    }
    
    public async Task DisposeAsync()
    {
        _scope?.Dispose();
        _factory?.Dispose();
        _client?.Dispose();
    }
    
    // Seed test data
    private async Task SeedTestDataAsync(AppDbContext context)
    {
        context.Users.Add(new User
        {
            Id = 1,
            Email = "existing@example.com",
            Name = "Existing User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123")
        });
        
        await context.SaveChangesAsync();
    }
    
    [Fact]
    public async Task CreateUser_WithValidRequest_Returns201Created()
    {
        // Arrange
        var request = new CreateUserRequest
        {
            Email = "new@example.com",
            Name = "New User",
            Password = "SecurePassword123!"
        };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/users", request);
        
        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        
        var location = response.Headers.Location;
        Assert.NotNull(location);
        
        // Verify data in database
        var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var savedUser = await context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        Assert.NotNull(savedUser);
        Assert.Equal(request.Name, savedUser.Name);
    }
    
    [Fact]
    public async Task CreateUser_WithDuplicateEmail_Returns400BadRequest()
    {
        // Arrange
        var request = new CreateUserRequest
        {
            Email = "existing@example.com",  // Already exists
            Name = "Another User",
            Password = "SecurePassword123!"
        };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/users", request);
        
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    
    [Fact]
    public async Task GetUser_WithValidId_Returns200Ok()
    {
        // Act
        var response = await _client.GetAsync("/api/users/1");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsAsync<UserDto>();
        Assert.NotNull(content);
        Assert.Equal(1, content.Id);
        Assert.Equal("existing@example.com", content.Email);
    }
    
    [Fact]
    public async Task GetUser_WithInvalidId_Returns404NotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/users/999");
        
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

### Testing Authenticated Endpoints

```csharp
public class AuthenticatedUserApiTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory;
    private HttpClient _client;
    private string _authToken;
    
    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    services.Remove(descriptor);
                    
                    services.AddDbContext<AppDbContext>(options =>
                    {
                        options.UseInMemoryDatabase("TestDb");
                    });
                });
            });
        
        _client = _factory.CreateClient();
        
        var context = _factory.Services
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<AppDbContext>();
        context.Database.EnsureCreated();
        
        // Create test user and get token
        _authToken = await GetAuthTokenAsync("user@example.com", "password123");
    }
    
    public Task DisposeAsync() => Task.CompletedTask;
    
    private async Task<string> GetAuthTokenAsync(string email, string password)
    {
        var loginRequest = new LoginRequest { Email = email, Password = password };
        
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        var loginResponse = await response.Content.ReadAsAsync<LoginResponse>();
        
        return loginResponse.AccessToken;
    }
    
    [Fact]
    public async Task GetMyProfile_WithValidToken_Returns200Ok()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", _authToken);
        
        // Act
        var response = await _client.GetAsync("/api/users/me");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    
    [Fact]
    public async Task GetMyProfile_WithoutToken_Returns401Unauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/users/me");
        
        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    
    [Fact]
    public async Task GetMyProfile_WithInvalidToken_Returns401Unauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", "invalid-token");
        
        // Act
        var response = await _client.GetAsync("/api/users/me");
        
        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

---

## 7.4 Testing Error Scenarios

### Testing Validation Errors

```csharp
[Fact]
public async Task CreateUser_WithMissingEmail_Returns400BadRequest()
{
    // Arrange
    var request = new CreateUserRequest
    {
        Name = "John Doe",
        Password = "Password123!"
        // Email missing
    };
    
    // Act
    var response = await _client.PostAsJsonAsync("/api/users", request);
    
    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    
    var problemDetails = await response.Content.ReadAsAsync<ProblemDetails>();
    Assert.NotNull(problemDetails);
    Assert.Equal(400, problemDetails.Status);
}

[Fact]
public async Task CreateUser_WithInvalidEmail_Returns400BadRequest()
{
    // Arrange
    var request = new CreateUserRequest
    {
        Email = "not-an-email",
        Name = "John Doe",
        Password = "Password123!"
    };
    
    // Act
    var response = await _client.PostAsJsonAsync("/api/users", request);
    
    // Assert
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}
```

### Testing Business Logic Errors

```csharp
[Fact]
public async Task TransferMoney_WithInsufficientFunds_Returns422UnprocessableEntity()
{
    // Arrange
    var request = new TransferRequest
    {
        ToUserId = 2,
        Amount = 10000m  // More than available
    };
    
    _client.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", _authToken);
    
    // Act
    var response = await _client.PostAsJsonAsync("/api/transfers", request);
    
    // Assert
    Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
}
```

---

## 7.5 Testing Authorization

```csharp
[Fact]
public async Task DeleteUser_WithUserRole_Returns403Forbidden()
{
    // Arrange - login as regular user
    var userToken = await GetAuthTokenAsync("user@example.com", "password");
    _client.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", userToken);
    
    // Act
    var response = await _client.DeleteAsync("/api/users/1");
    
    // Assert - only admins can delete
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}

[Fact]
public async Task DeleteUser_WithAdminRole_Returns204NoContent()
{
    // Arrange - login as admin
    var adminToken = await GetAuthTokenAsync("admin@example.com", "password");
    _client.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", adminToken);
    
    // Act
    var response = await _client.DeleteAsync("/api/users/999");
    
    // Assert
    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
}
```

---

## 7.6 Data-Driven Tests

Use parameterized tests for multiple input/output combinations:

```csharp
public class DataDrivenUserTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory;
    private HttpClient _client;
    
    public async Task InitializeAsync()
    {
        // ... setup code
    }
    
    public Task DisposeAsync() => Task.CompletedTask;
    
    // Test multiple email formats
    [Theory]
    [InlineData("valid@example.com")]
    [InlineData("user+tag@example.co.uk")]
    [InlineData("first.last@example.com")]
    public async Task CreateUser_WithValidEmails_Succeeds(string email)
    {
        // Arrange
        var request = new CreateUserRequest
        {
            Email = email,
            Name = "Test User",
            Password = "Password123!"
        };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/users", request);
        
        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
    
    // Test multiple invalid email formats
    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    public async Task CreateUser_WithInvalidEmails_ReturnsBadRequest(string email)
    {
        // Arrange
        var request = new CreateUserRequest
        {
            Email = email,
            Name = "Test User",
            Password = "Password123!"
        };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/users", request);
        
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    
    // Test with CSV data
    [Theory]
    [MemberData(nameof(GetUserTestData))]
    public async Task CreateUser_WithTestData_Succeeds(
        string email, 
        string name, 
        bool expectedSuccess)
    {
        // Arrange
        var request = new CreateUserRequest
        {
            Email = email,
            Name = name,
            Password = "Password123!"
        };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/users", request);
        
        // Assert
        if (expectedSuccess)
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        else
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    
    public static IEnumerable<object[]> GetUserTestData() => new List<object[]>
    {
        new object[] { "valid@example.com", "John Doe", true },
        new object[] { "another@example.com", "Jane Smith", true },
        new object[] { "", "Invalid User", false },
        new object[] { "bad-email", "Another User", false }
    };
}
```

---

## 7.7 Testing Best Practices

### Isolation

Each test should be independent:

```csharp
// Bad: tests depend on execution order
[Fact]
public async Task Test1_CreateUser()
{
    await _client.PostAsJsonAsync("/api/users", user1);
}

[Fact]
public async Task Test2_GetUser_DependsOnTest1()
{
    var response = await _client.GetAsync("/api/users/1");  // Assumes Test1 ran first
}

// Good: each test sets up its own data
[Fact]
public async Task CreateUser_Succeeds()
{
    var response = await _client.PostAsJsonAsync("/api/users", user1);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
}

[Fact]
public async Task GetUser_WithExistingUser_Succeeds()
{
    // Create user first
    var createResponse = await _client.PostAsJsonAsync("/api/users", user1);
    var userId = await GetUserIdFromResponse(createResponse);
    
    // Then get it
    var response = await _client.GetAsync($"/api/users/{userId}");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

### Descriptive Names

Test names should describe what they test:

```csharp
// Good
[Fact]
public async Task CreateUser_WithValidRequest_Returns201Created()
public async Task CreateUser_WithDuplicateEmail_Returns400BadRequest()
public async Task UpdateUser_WithoutAuthentication_Returns401Unauthorized()

// Bad
[Fact]
public async Task Test1()
public async Task CreateUserTest()
public async Task TestCreateUser()
```

### Avoid Test Interdependence

```csharp
// Bad: Shared state
public class UserServiceTests
{
    private static List<User> _users = new();
    
    [Fact]
    public void Test1() => _users.Add(new User { Id = 1 });
    
    [Fact]
    public void Test2() => Assert.Single(_users);  // Depends on Test1
}

// Good: Isolated
public class UserServiceTests
{
    [Fact]
    public void Test1()
    {
        var users = new List<User>();
        users.Add(new User { Id = 1 });
        Assert.Single(users);
    }
    
    [Fact]
    public void Test2()
    {
        var users = new List<User>();
        Assert.Empty(users);
    }
}
```

---

## 7.8 Code Coverage

Measure test coverage to identify untested code:

```bash
dotnet add package coverlet.collector

# Run tests with coverage
dotnet test /p:CollectCoverage=true

# Generate report
dotnet test /p:CollectCoverage=true /p:CoverageFormat=opencover
```

**Target coverage:**
- Critical code: 100%
- Business logic: 80%+
- Overall: 70%+ (not every line needs testing)

**Don't aim for 100% coverage**—focus on testing critical paths and error scenarios.

---

## Summary

Testing APIs requires unit tests for business logic, integration tests for endpoints with databases, and end-to-end tests for critical workflows. Use WebApplicationFactory for integration testing, mock external dependencies in unit tests, and maintain test isolation. The next batch of chapters covers advanced patterns: versioning, performance optimization, and architectural patterns.
