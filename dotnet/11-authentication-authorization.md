# Authentication & Authorization

Identify users (authentication) and control what they can do (authorization).

## Authentication Basics

### JWT (JSON Web Tokens)

Standard stateless authentication mechanism:

```bash
dotnet add package System.IdentityModel.Tokens.Jwt
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
```

### JWT Structure

```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.
eyJzdWIiOiIxIiwibmFtZSI6IkFsaWNlIiwiaWF0IjoxNTE2MjM5MDIyfQ.
SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c

Header . Payload . Signature
```

## JWT Implementation

### Generate JWT Token

```csharp
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

public class JwtTokenService
{
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expirationMinutes;

    public JwtTokenService(IConfiguration config)
    {
        _secretKey = config["Jwt:SecretKey"];
        _issuer = config["Jwt:Issuer"];
        _audience = config["Jwt:Audience"];
        _expirationMinutes = int.Parse(config["Jwt:ExpirationMinutes"] ?? "60");
    }

    public string GenerateToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_secretKey);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
        };

        // Add roles
        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_expirationMinutes),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
    public List<string> Roles { get; set; } = new();
}
```

### Login Endpoint

```csharp
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly JwtTokenService _tokenService;
    private readonly UserService _userService;

    public AuthController(JwtTokenService tokenService, UserService userService)
    {
        _tokenService = tokenService;
        _userService = userService;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // Find user
        var user = _userService.FindByEmail(request.Email);
        if (user == null)
            return Unauthorized(new { message = "Invalid credentials" });

        // Verify password
        if (!VerifyPassword(request.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid credentials" });

        // Generate token
        var token = _tokenService.GenerateToken(user);

        return Ok(new { token, user = new { user.Id, user.Name, user.Email } });
    }

    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest request)
    {
        if (_userService.FindByEmail(request.Email) != null)
            return BadRequest(new { message = "Email already registered" });

        var user = new User
        {
            Name = request.Name,
            Email = request.Email,
            PasswordHash = HashPassword(request.Password),
            Roles = new List<string> { "User" }
        };

        _userService.Create(user);
        var token = _tokenService.GenerateToken(user);

        return CreatedAtAction(nameof(Profile), new { token });
    }

    [Authorize]
    [HttpGet("profile")]
    public IActionResult Profile()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        var user = _userService.GetById(userId);
        return Ok(user);
    }

    private string HashPassword(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password);

    private bool VerifyPassword(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);
}

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Name, string Email, string Password);
```

### Configure JWT Authentication

```csharp
// In Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<UserService>();

var key = Encoding.ASCII.GetBytes(builder.Configuration["Jwt:SecretKey"]);

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
```

### appsettings.json Configuration

```json
{
  "Jwt": {
    "SecretKey": "your-super-secret-key-that-is-long-enough-for-256-bits",
    "Issuer": "MyApp",
    "Audience": "MyAppUsers",
    "ExpirationMinutes": 60
  }
}
```

## Authorization

### Role-Based Authorization

```csharp
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    [Authorize(Roles = "Admin")]
    [HttpGet("users")]
    public IActionResult GetAllUsers()
    {
        // Only Admin role can access
        return Ok();
    }

    [Authorize(Roles = "Admin,Moderator")]
    [HttpDelete("{id}")]
    public IActionResult DeleteUser(int id)
    {
        // Admin or Moderator can access
        return Ok();
    }
}
```

### Claims-Based Authorization

Fine-grained control with custom claims:

```csharp
// In JWT generation
var claims = new List<Claim>
{
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim("subscription_level", user.SubscriptionLevel),
    new Claim("organization_id", user.OrganizationId.ToString()),
};

// In controller
[ApiController]
[Route("api/[controller]")]
public class DataController : ControllerBase
{
    [Authorize]
    [HttpGet]
    public IActionResult GetData()
    {
        var subscriptionLevel = User.FindFirst("subscription_level")?.Value;
        
        if (subscriptionLevel == "premium")
        {
            // Premium features
        }

        return Ok();
    }
}
```

### Policy-Based Authorization

```csharp
// In Program.cs
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("MinimumAge", policy =>
        policy.Requirements.Add(new MinimumAgeRequirement(18)))
    .AddPolicy("PremiumUser", policy =>
        policy.RequireClaim("subscription_level", "premium"))
    .AddPolicy("SameOrganization", policy =>
        policy.Requirements.Add(new SameOrganizationRequirement()));

// Define requirement handler
public class MinimumAgeRequirement : IAuthorizationRequirement
{
    public int MinimumAge { get; set; }
    public MinimumAgeRequirement(int minimumAge) => MinimumAge = minimumAge;
}

public class MinimumAgeHandler : AuthorizationHandler<MinimumAgeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MinimumAgeRequirement requirement)
    {
        var ageClaim = context.User.FindFirst("age");
        if (ageClaim != null && int.TryParse(ageClaim.Value, out int age))
        {
            if (age >= requirement.MinimumAge)
                context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

// Use policy in controller
[Authorize(Policy = "MinimumAge")]
[HttpGet]
public IActionResult GetAdultContent() => Ok();
```

## Refresh Tokens

Keep users logged in without storing passwords:

```csharp
public class RefreshTokenService
{
    private readonly UserService _userService;

    public RefreshTokenService(UserService userService)
    {
        _userService = userService;
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[32];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }
    }

    public void SaveRefreshToken(int userId, string refreshToken)
    {
        var user = _userService.GetById(userId);
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        _userService.Update(user);
    }
}

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly JwtTokenService _jwtService;
    private readonly RefreshTokenService _refreshService;
    private readonly UserService _userService;

    [HttpPost("refresh")]
    public IActionResult RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var user = _userService.FindByEmail(request.Email);
        if (user == null || user.RefreshToken != request.RefreshToken)
            return Unauthorized("Invalid refresh token");

        if (user.RefreshTokenExpiry < DateTime.UtcNow)
            return Unauthorized("Refresh token expired");

        var newAccessToken = _jwtService.GenerateToken(user);
        var newRefreshToken = _refreshService.GenerateRefreshToken();

        _refreshService.SaveRefreshToken(user.Id, newRefreshToken);

        return Ok(new { accessToken = newAccessToken, refreshToken = newRefreshToken });
    }
}

public record RefreshTokenRequest(string Email, string RefreshToken);
```

## OAuth 2.0 Integration

Third-party authentication (Google, GitHub):

```csharp
builder.Services.AddAuthentication()
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
    })
    .AddGitHub(options =>
    {
        options.ClientId = builder.Configuration["Authentication:GitHub:ClientId"];
        options.ClientSecret = builder.Configuration["Authentication:GitHub:ClientSecret"];
    });

[ApiController]
[Route("api/auth")]
public class OAuthController : ControllerBase
{
    [HttpGet("login/{provider}")]
    public IActionResult Login(string provider)
    {
        var redirectUrl = Url.Action(nameof(Callback), new { returnUrl = "/" });
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, provider);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string returnUrl = "/")
    {
        var result = await HttpContext.AuthenticateAsync();
        if (!result.Succeeded) return BadRequest("External authentication failed");

        // Create or get user
        var claims = result.Principal.Claims;
        var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

        return Redirect(returnUrl);
    }
}
```

## Securing Endpoints

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProtectedController : ControllerBase
{
    // Require authentication
    [Authorize]
    [HttpGet("user-data")]
    public IActionResult GetUserData()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Ok(new { message = $"Data for user {userId}" });
    }

    // Require specific role
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public IActionResult DeleteData(int id) => Ok();

    // Require policy
    [Authorize(Policy = "PremiumUser")]
    [HttpGet("premium-content")]
    public IActionResult GetPremiumContent() => Ok();

    // Public endpoint
    [AllowAnonymous]
    [HttpGet("public")]
    public IActionResult GetPublic() => Ok();

    // No decoration = default behavior
    [HttpGet("health")]
    public IActionResult Health() => Ok("Healthy");
}
```

## Practice Exercises

1. **JWT Login**: Implement login/register with JWT token generation
2. **Role-Based**: Create different endpoints for Admin/User roles
3. **Refresh Tokens**: Implement token refresh mechanism
4. **Custom Claims**: Add custom claims and use them for authorization
5. **OAuth**: Integrate Google or GitHub login

## Key Takeaways

- **Authentication** verifies user identity; **authorization** controls access
- **JWT** tokens are stateless and contain claims
- **Refresh tokens** extend sessions without re-login
- **Roles** and **policies** control what authenticated users can do
- **Claims** enable fine-grained authorization decisions
- **Hash passwords** with bcrypt, never store plaintext
- **Secure tokens** with strong secret keys and appropriate expiration
- **OAuth 2.0** integrates third-party authentication providers
