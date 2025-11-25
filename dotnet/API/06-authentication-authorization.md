# Chapter 6: Authentication & Authorization

## 6.1 Authentication Fundamentals

Authentication verifies "who are you?" Authorization verifies "what can you do?"

### JWT (JSON Web Tokens)

JWT is the industry-standard for stateless authentication in APIs.

**JWT Structure:**
```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c
```

**Three parts separated by dots:**

1. **Header** (Base64 decoded): `{"alg":"HS256","typ":"JWT"}`
   - Algorithm: HMAC SHA256
   - Type: JWT

2. **Payload** (Base64 decoded): `{"sub":"1234567890","name":"John Doe","iat":1516239022}`
   - `sub`: Subject (user ID)
   - `name`: User name
   - `iat`: Issued at (timestamp)
   - Custom claims can be added

3. **Signature**: Generated from header + payload + secret
   - Verifies token hasn't been modified
   - Only server with secret can create/verify

**JWT advantages:**
- Stateless: server doesn't store sessions
- Scalable: any server can verify without shared state
- Self-contained: user info in token
- Cross-domain: works across services

**JWT disadvantages:**
- Cannot revoke immediately (token is valid until expiration)
- Token size (larger than session cookie)
- Requires HTTPS (token in every request)

---

## 6.2 Implementing JWT Authentication

### Generating Tokens

```csharp
public interface IJwtService
{
    string GenerateToken(User user);
    ClaimsPrincipal ValidateToken(string token);
}

public class JwtService : IJwtService
{
    private readonly JwtOptions _options;
    private readonly ILogger<JwtService> _logger;
    
    public JwtService(IOptions<JwtOptions> options, ILogger<JwtService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }
    
    public string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_options.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Name),
            // Add roles
            new Claim(ClaimTypes.Role, user.Role)
        };
        
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpirationMinutes),
            signingCredentials: creds
        );
        
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    
    public ClaimsPrincipal ValidateToken(string token)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_options.Secret));
        
        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(
                token,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = true,
                    ValidIssuer = _options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = _options.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                },
                out SecurityToken validatedToken
            );
            
            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            throw;
        }
    }
}

// Configuration
public class JwtOptions
{
    public string Secret { get; set; }
    public string Issuer { get; set; }
    public string Audience { get; set; }
    public int ExpirationMinutes { get; set; }
}
```

**appsettings.json:**
```json
{
  "Jwt": {
    "Secret": "your-very-long-secret-key-at-least-32-characters-long",
    "Issuer": "your-api-name",
    "Audience": "your-api-clients",
    "ExpirationMinutes": 60
  }
}
```

### Login Endpoint

```csharp
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IJwtService _jwtService;
    private readonly ILogger<AuthController> _logger;
    
    public AuthController(
        IUserService userService, 
        IJwtService jwtService,
        ILogger<AuthController> logger)
    {
        _userService = userService;
        _jwtService = jwtService;
        _logger = logger;
    }
    
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        _logger.LogInformation("Login attempt for user {Email}", request.Email);
        
        var user = await _userService.GetUserByEmailAsync(request.Email);
        
        if (user == null || !VerifyPassword(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed for user {Email}", request.Email);
            return Unauthorized(new { error = "Invalid email or password" });
        }
        
        var token = _jwtService.GenerateToken(user);
        
        _logger.LogInformation("Login successful for user {UserId}", user.Id);
        
        return Ok(new LoginResponse
        {
            AccessToken = token,
            TokenType = "Bearer",
            ExpiresIn = 3600  // seconds
        });
    }
    
    private bool VerifyPassword(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }
}

public class LoginRequest
{
    [EmailAddress]
    public string Email { get; set; }
    
    [Required]
    public string Password { get; set; }
}

public class LoginResponse
{
    public string AccessToken { get; set; }
    public string TokenType { get; set; }
    public int ExpiresIn { get; set; }
}
```

### Configuring Authentication Middleware

```csharp
// In Program.cs
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IUserService, UserService>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>();
        var key = Encoding.UTF8.GetBytes(jwtOptions.Secret);
        
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
        
        // Handle auth failures
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsJsonAsync(new
                {
                    error = "Unauthorized",
                    message = "Authorization header missing or invalid"
                });
            },
            OnForbidden = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsJsonAsync(new
                {
                    error = "Forbidden",
                    message = "Insufficient permissions"
                });
            }
        };
    });

var app = builder.Build();

// Middleware order is critical
app.UseAuthentication();  // Must come before Authorization
app.UseAuthorization();

app.MapControllers();
app.Run();
```

---

## 6.3 Authorization

Authorization determines "what can you do?" after authentication.

### Role-Based Authorization

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]  // Require authentication
public class AdminController : ControllerBase
{
    [HttpPost("users/{id}/suspend")]
    [Authorize(Roles = "Admin")]  // Only Admin role
    public async Task<IActionResult> SuspendUser(int id)
    {
        // Only users with Admin role can access
    }
    
    [HttpPost("users/{id}/unlock")]
    [Authorize(Roles = "Admin,Moderator")]  // Multiple roles
    public async Task<IActionResult> UnlockUser(int id)
    {
        // Users with Admin OR Moderator role can access
    }
    
    [HttpGet("reports")]
    [AllowAnonymous]  // Override: allow without auth
    public IActionResult GetPublicReport()
    {
        // No authentication required
    }
}
```

### Claims-Based Authorization

Claims are custom attributes about the user:

```csharp
// When generating token, add claims
var claims = new[]
{
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Email, user.Email),
    new Claim("department", user.Department),
    new Claim("subscription_level", user.SubscriptionLevel),
    new Claim("is_verified", user.IsEmailVerified.ToString())
};

// Define policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PremiumUser", policy =>
        policy.RequireClaim("subscription_level", "premium", "enterprise"));
    
    options.AddPolicy("VerifiedEmail", policy =>
        policy.RequireClaim("is_verified", "True"));
    
    options.AddPolicy("SalesTeam", policy =>
        policy.RequireClaim("department", "sales"));
});

// Use in endpoints
[ApiController]
[Route("api/[controller]")]
public class FeaturesController : ControllerBase
{
    [HttpGet("premium-report")]
    [Authorize(Policy = "PremiumUser")]
    public IActionResult GetPremiumReport()
    {
        // Only premium users can access
    }
    
    [HttpPost("create-order")]
    [Authorize(Policy = "VerifiedEmail")]
    public IActionResult CreateOrder()
    {
        // Only verified users can create orders
    }
}
```

### Custom Authorization Handlers

```csharp
public class AgeRequirement : IAuthorizationRequirement
{
    public int MinimumAge { get; set; }
    
    public AgeRequirement(int minimumAge)
    {
        MinimumAge = minimumAge;
    }
}

public class AgeAuthorizationHandler 
    : AuthorizationHandler<AgeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AgeRequirement requirement)
    {
        var ageClaim = context.User.FindFirst("age");
        
        if (ageClaim != null && int.TryParse(ageClaim.Value, out var age))
        {
            if (age >= requirement.MinimumAge)
            {
                context.Succeed(requirement);
            }
        }
        
        return Task.CompletedTask;
    }
}

// Register policy
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Adult", policy =>
        policy.AddRequirements(new AgeRequirement(18)));
});

builder.Services.AddSingleton<IAuthorizationHandler, AgeAuthorizationHandler>();

// Use
[Authorize(Policy = "Adult")]
public IActionResult GetAdultContent() { }
```

### Accessing User Information

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    [HttpGet("me")]
    public IActionResult GetMyProfile()
    {
        // Get current user ID from token
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        var roles = User.FindAll(ClaimTypes.Role);
        
        return Ok(new { userId, email, roles });
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateResource(CreateRequest request)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
        
        // Associate resource with current user
        var resource = new Resource { UserId = userId, /* ... */ };
        
        await _service.CreateAsync(resource);
        return Created("", resource);
    }
}
```

---

## 6.4 Security Best Practices

### Password Hashing

Never store plain-text passwords. Use bcrypt:

```csharp
// Hashing password on registration
var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);
var user = new User { Email = email, PasswordHash = passwordHash };

// Verifying password on login
bool isValid = BCrypt.Net.BCrypt.Verify(providedPassword, storedHash);
```

**Don't use:**
- MD5, SHA1 (broken, fast)
- Plain hashing (no salt)
- Custom implementations

**Use:**
- bcrypt, scrypt, Argon2 (slow, salted)

### Token Storage

**Safe (for APIs):**
- Memory (in-memory)
- Secure, HttpOnly cookies
- Don't expose to JavaScript

**Unsafe:**
- localStorage (vulnerable to XSS)
- sessionStorage (vulnerable to XSS)
- Regular cookies without HttpOnly

**For mobile/SPA, prefer:**
- In-memory + refresh tokens
- HttpOnly cookie for refresh token

### HTTPS Requirement

```csharp
// Enforce HTTPS
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

app.UseHttpsRedirection();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    await next();
});
```

### Refresh Tokens

For long-lived sessions, use refresh tokens:

```csharp
public class TokenResponse
{
    public string AccessToken { get; set; }  // Short-lived (15 min)
    public string RefreshToken { get; set; }  // Long-lived (7 days)
    public int ExpiresIn { get; set; }
}

[HttpPost("refresh")]
[AllowAnonymous]
public async Task<IActionResult> RefreshToken(RefreshTokenRequest request)
{
    var principal = _jwtService.ValidateToken(request.AccessToken);
    
    var user = await _userService.GetByIdAsync(
        int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier).Value)
    );
    
    // Verify refresh token in database
    var storedToken = await _context.RefreshTokens
        .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken 
            && rt.UserId == user.Id 
            && rt.ExpiresAt > DateTime.UtcNow);
    
    if (storedToken == null)
        return Unauthorized("Invalid refresh token");
    
    // Generate new access token
    var newAccessToken = _jwtService.GenerateToken(user);
    
    return Ok(new TokenResponse
    {
        AccessToken = newAccessToken,
        RefreshToken = request.RefreshToken,
        ExpiresIn = 900  // 15 minutes
    });
}
```

Store refresh tokens in database for revocation:

```csharp
public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    
    public bool IsValid => RevokedAt == null && ExpiresAt > DateTime.UtcNow;
}

// On logout, revoke token
[HttpPost("logout")]
[Authorize]
public async Task<IActionResult> Logout()
{
    var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
    
    var token = await _context.RefreshTokens
        .FirstOrDefaultAsync(rt => rt.UserId == userId && rt.RevokedAt == null);
    
    if (token != null)
    {
        token.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }
    
    return Ok();
}
```

---

## 6.5 OAuth 2.0 and External Providers

For allowing login via Google, GitHub, etc.:

```csharp
builder.Services
    .AddAuthentication()
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Google:ClientId"];
        options.ClientSecret = builder.Configuration["Google:ClientSecret"];
    })
    .AddGitHub(options =>
    {
        options.ClientId = builder.Configuration["GitHub:ClientId"];
        options.ClientSecret = builder.Configuration["GitHub:ClientSecret"];
    });

[HttpPost("login-google")]
[AllowAnonymous]
public async Task<IActionResult> LoginWithGoogle(GoogleLoginRequest request)
{
    // Verify token with Google
    // Create/find user
    // Generate JWT token
    // Return token to client
}
```

---

## 6.6 CORS (Cross-Origin Resource Sharing)

Allow specific domains to access your API:

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin", policy =>
    {
        policy
            .WithOrigins("https://example.com", "https://app.example.com")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
    
    options.AddPolicy("AllowAnyOrigin", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

app.UseCors("AllowSpecificOrigin");

// Or per-endpoint
[HttpGet]
[EnableCors("AllowAnyOrigin")]
public IActionResult GetPublicData() { }
```

**CORS headers:**
- `Access-Control-Allow-Origin` - Allowed origins
- `Access-Control-Allow-Methods` - Allowed HTTP methods
- `Access-Control-Allow-Headers` - Allowed headers
- `Access-Control-Max-Age` - How long to cache preflight

---

## Summary

JWT tokens provide stateless authentication suitable for APIs. Configure authentication with `AddAuthentication().AddJwtBearer()`, generate tokens with claims, and enforce authorization policies. Use refresh tokens for long-lived sessions, hash passwords with bcrypt, and always require HTTPS. The next chapter covers testing—essential for reliable APIs.
