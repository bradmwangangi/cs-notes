# 15. Authentication Mechanisms

## Overview

Authentication is the process of verifying **who** a user is. Different mechanisms exist for different scenarios: web applications, APIs, mobile apps, and distributed systems. Understanding each mechanism and when to use it is critical for building secure systems.

## Authentication vs Authorization

```
Authentication: WHO are you?
- Credentials verification
- User identity confirmation
- Examples: Login, API key, JWT token

Authorization: WHAT can you do?
- Permission checking
- Access control
- Examples: Admin role, delete permission

Authentication must happen BEFORE Authorization
```

## Cookie-Based Authentication

Traditional approach for server-rendered web applications.

### How It Works

```
1. User submits credentials
2. Server validates credentials
3. Server creates session (in-memory or database)
4. Session ID stored in HttpOnly cookie
5. Cookie sent with every request
6. Server looks up session by ID
7. If valid session, request proceeds
```

### Implementation

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;  // Reset expiration on each request
        options.Cookie.HttpOnly = true;    // Can't access from JavaScript
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // HTTPS only
        options.Cookie.SameSite = SameSiteMode.Strict;  // CSRF protection
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();

// Login endpoint
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        // Validate credentials
        var user = await _userService.AuthenticateAsync(
            request.Email, request.Password);
        
        if (user == null)
            return Unauthorized("Invalid credentials");
        
        // Create claims for identity
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Name)
        };
        
        // Add roles as claims
        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }
        
        var claimsIdentity = new ClaimsIdentity(
            claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = request.RememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
        };
        
        // Create authentication ticket and sign in
        var principal = new ClaimsPrincipal(claimsIdentity);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            authProperties);
        
        return Ok(new { message = "Login successful" });
    }
    
    // Logout endpoint
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
        
        return Ok(new { message = "Logout successful" });
    }
}

public class LoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; }
    
    [Required]
    public string Password { get; set; }
    
    public bool RememberMe { get; set; }
}

// Protected endpoint usage
[Authorize]
[HttpGet("profile")]
public async Task<ActionResult<UserDto>> GetProfile()
{
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var user = await _userService.GetUserAsync(int.Parse(userId));
    return Ok(user);
}

// Role-based authorization
[Authorize(Roles = "Admin")]
[HttpDelete("{id}")]
public async Task<IActionResult> DeleteUser(int id)
{
    await _userService.DeleteUserAsync(id);
    return NoContent();
}
```

### Cookie Security Considerations

```csharp
// HttpOnly: Can't access from JavaScript (prevents XSS attacks)
// Secure: Only sent over HTTPS (prevents man-in-the-middle)
// SameSite: Restricts when cookie is sent (prevents CSRF)

options.Cookie.HttpOnly = true;           // ✓ Recommended
options.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // ✓ Recommended
options.Cookie.SameSite = SameSiteMode.Strict;  // ✓ Recommended

// Sliding expiration: Extend timeout on activity
options.SlidingExpiration = true;  // ✓ Good UX

// Session data in-memory (lose on app restart) or database
// For production: Use distributed cache (Redis)
```

## JWT (JSON Web Tokens)

Modern, stateless authentication for APIs and distributed systems.

### How JWT Works

```
JWT = Header.Payload.Signature

Header: {"alg": "HS256", "typ": "JWT"}
Payload: {"userId": 1, "email": "john@example.com", "role": "admin", "exp": 1735689600}
Signature: HMACSHA256(base64(header) + base64(payload), "secret-key")

Complete JWT: 
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.
eyJ1c2VySWQiOjEsImVtYWlsIjoiam9obkBlYGFtcGxlLmNvbSJ9.
SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c

Flow:
1. User sends credentials
2. Server validates
3. Server creates JWT with claims
4. Server returns JWT to client
5. Client stores JWT (localStorage, memory)
6. Client sends JWT in Authorization header: "Bearer <token>"
7. Server validates signature and expiration
8. Request proceeds if valid
```

### Implementation

```csharp
// Install: dotnet add package System.IdentityModel.Tokens.Jwt

// Program.cs
var builder = WebApplication.CreateBuilder(args);

var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"];
var validIssuer = jwtSettings["ValidIssuer"];
var validAudience = jwtSettings["ValidAudience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(secretKey)),
            
            ValidateIssuer = true,
            ValidIssuer = validIssuer,
            
            ValidateAudience = true,
            ValidAudience = validAudience,
            
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,  // Don't allow clock skew
            
            RequireExpirationTime = true
        };
        
        // Handle auth failures
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                
                var response = new
                {
                    status = 401,
                    message = "Unauthorized - Invalid or missing token"
                };
                
                return context.Response.WriteAsJsonAsync(response);
            },
            OnForbidden = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                
                var response = new
                {
                    status = 403,
                    message = "Forbidden - Insufficient permissions"
                };
                
                return context.Response.WriteAsJsonAsync(response);
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

// appsettings.json
{
  "JwtSettings": {
    "SecretKey": "your-very-long-secret-key-at-least-32-characters",
    "ValidIssuer": "your-app-name",
    "ValidAudience": "your-app-users",
    "ExpiryMinutes": 60
  }
}

// ITokenService.cs
public interface ITokenService
{
    string GenerateToken(User user);
    ClaimsPrincipal ValidateToken(string token);
}

// TokenService.cs
public class TokenService : ITokenService
{
    private readonly IConfiguration _config;
    
    public TokenService(IConfiguration config)
    {
        _config = config;
    }
    
    public string GenerateToken(User user)
    {
        var jwtSettings = _config.GetSection("JwtSettings");
        var secretKey = jwtSettings["SecretKey"];
        var validIssuer = jwtSettings["ValidIssuer"];
        var validAudience = jwtSettings["ValidAudience"];
        var expiryMinutes = int.Parse(jwtSettings["ExpiryMinutes"]);
        
        // Create claims
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim("custom_claim", "custom_value")
        };
        
        // Add roles
        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }
        
        // Create signing key
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(secretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        // Create token
        var token = new JwtSecurityToken(
            issuer: validIssuer,
            audience: validAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: creds);
        
        var tokenHandler = new JwtSecurityTokenHandler();
        return tokenHandler.WriteToken(token);
    }
    
    public ClaimsPrincipal ValidateToken(string token)
    {
        var jwtSettings = _config.GetSection("JwtSettings");
        var secretKey = jwtSettings["SecretKey"];
        
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(secretKey);
        
        try
        {
            var principal = tokenHandler.ValidateToken(token,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                },
                out SecurityToken validatedToken);
            
            return principal;
        }
        catch
        {
            return null;
        }
    }
}

// AuthController.cs
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ITokenService _tokenService;
    
    public AuthController(IUserService userService, ITokenService tokenService)
    {
        _userService = userService;
        _tokenService = tokenService;
    }
    
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        // Validate credentials
        var user = await _userService.AuthenticateAsync(
            request.Email, request.Password);
        
        if (user == null)
            return Unauthorized("Invalid credentials");
        
        // Generate JWT
        var token = _tokenService.GenerateToken(user);
        
        return Ok(new
        {
            token,
            user = new
            {
                user.Id,
                user.Name,
                user.Email
            }
        });
    }
    
    [HttpPost("refresh")]
    [Authorize]
    public async Task<IActionResult> RefreshToken()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var user = await _userService.GetUserAsync(int.Parse(userId));
        
        if (user == null)
            return Unauthorized();
        
        var newToken = _tokenService.GenerateToken(user);
        
        return Ok(new { token = newToken });
    }
}

// Protected endpoint
[Authorize]
[HttpGet("profile")]
public async Task<ActionResult<UserDto>> GetProfile()
{
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var user = await _userService.GetUserAsync(int.Parse(userId));
    return Ok(user);
}
```

### JWT Best Practices

```csharp
// ✓ Use HS256 or RS256 for signing
// ✗ Don't use HS512 or weak algorithms

// ✓ Include expiration claim
// ✗ Don't create tokens that never expire

// ✓ Short-lived tokens (15-60 minutes)
// ✗ Don't create tokens that last days

// ✓ Use refresh tokens for long-lived sessions
// ✗ Don't use single long-lived token

// ✓ Store secret securely (Azure Key Vault, environment variable)
// ✗ Don't hardcode secret in code

// ✓ Include minimal claims needed
// ✗ Don't include entire user object

// ✓ Validate signature on every request
// ✗ Don't skip signature validation
```

## Refresh Tokens

Extended authentication without re-entering credentials.

```csharp
// RefreshToken model
public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; }
    public DateTime ExpiryDate { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; }
    
    public User User { get; set; }
}

// Token service with refresh
public class TokenService : ITokenService
{
    private readonly IConfiguration _config;
    private readonly ApplicationDbContext _context;
    
    public TokenService(IConfiguration config, ApplicationDbContext context)
    {
        _config = config;
        _context = context;
    }
    
    // Generate both access and refresh tokens
    public async Task<TokenResponse> GenerateTokenPairAsync(User user)
    {
        var accessToken = GenerateAccessToken(user);
        var refreshToken = await GenerateRefreshTokenAsync(user);
        
        return new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = 3600  // 1 hour in seconds
        };
    }
    
    private string GenerateAccessToken(User user)
    {
        // Short-lived JWT (1 hour)
        var jwtSettings = _config.GetSection("JwtSettings");
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email)
        };
        
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        var token = new JwtSecurityToken(
            issuer: jwtSettings["ValidIssuer"],
            audience: jwtSettings["ValidAudience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: creds);
        
        var tokenHandler = new JwtSecurityTokenHandler();
        return tokenHandler.WriteToken(token);
    }
    
    private async Task<string> GenerateRefreshTokenAsync(User user)
    {
        // Long-lived token (7 days)
        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = GenerateRandomToken(),
            ExpiryDate = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        
        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();
        
        return refreshToken.Token;
    }
    
    public async Task<TokenResponse> RefreshAccessTokenAsync(string refreshToken)
    {
        var token = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);
        
        if (token == null || token.IsRevoked || token.ExpiryDate < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Invalid refresh token");
        
        var newAccessToken = GenerateAccessToken(token.User);
        var newRefreshToken = await GenerateRefreshTokenAsync(token.User);
        
        // Revoke old refresh token
        token.IsRevoked = true;
        await _context.SaveChangesAsync();
        
        return new TokenResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            ExpiresIn = 3600
        };
    }
    
    public async Task RevokeTokenAsync(string refreshToken)
    {
        var token = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);
        
        if (token != null)
        {
            token.IsRevoked = true;
            await _context.SaveChangesAsync();
        }
    }
    
    private string GenerateRandomToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }
}

public class TokenResponse
{
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
}

// Refresh endpoint
[HttpPost("refresh-token")]
[AllowAnonymous]
public async Task<IActionResult> RefreshToken(RefreshTokenRequest request)
{
    try
    {
        var response = await _tokenService.RefreshAccessTokenAsync(
            request.RefreshToken);
        
        return Ok(response);
    }
    catch (UnauthorizedAccessException)
    {
        return Unauthorized("Invalid or expired refresh token");
    }
}

// Logout (revoke refresh token)
[HttpPost("logout")]
[Authorize]
public async Task<IActionResult> Logout(LogoutRequest request)
{
    await _tokenService.RevokeTokenAsync(request.RefreshToken);
    return Ok("Logout successful");
}
```

## OAuth 2.0 & OpenID Connect

Third-party authentication and authorization.

### OAuth 2.0 Flow

```
1. User clicks "Login with Google"
2. App redirects to Google authorization endpoint
3. User logs in to Google (if not already)
4. Google asks user to grant permissions
5. User grants permission
6. Google redirects back to app with authorization code
7. App exchanges authorization code for access token
8. App uses access token to call Google API
9. App creates session for user
```

### ASP.NET Core Implementation

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie()
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
    options.Scope.Add("profile");
    options.Scope.Add("email");
    
    // Custom user info endpoint
    options.Events = new OAuthEvents
    {
        OnCreatingTicket = async context =>
        {
            // Handle user creation/update
            var userEmail = context.Principal.FindFirst(ClaimTypes.Email)?.Value;
            var userName = context.Principal.FindFirst(ClaimTypes.Name)?.Value;
            
            // Find or create user in database
            var user = await _userService.FindOrCreateAsync(userEmail, userName);
            
            // Add custom claims
            context.Principal.FindFirst(ClaimTypes.NameIdentifier)?
                .Value = user.Id.ToString();
        }
    };
})
.AddMicrosoftAccount(options =>
{
    options.ClientId = builder.Configuration["Authentication:Microsoft:ClientId"];
    options.ClientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"];
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// OAuth challenge endpoint
[HttpGet("login/{provider}")]
[AllowAnonymous]
public IActionResult Login(string provider)
{
    var redirectUrl = Url.Action(nameof(LoginCallback), "Auth");
    var properties = new AuthenticationProperties
    {
        RedirectUri = redirectUrl
    };
    
    return Challenge(properties, provider);
}

// OAuth callback endpoint
[HttpGet("login-callback")]
[AllowAnonymous]
public async Task<IActionResult> LoginCallback()
{
    var result = await HttpContext.AuthenticateAsync(
        CookieAuthenticationDefaults.AuthenticationScheme);
    
    if (!result.Succeeded)
        return Redirect("/login?error=authentication_failed");
    
    // User authenticated, redirect to app
    return Redirect("/dashboard");
}

app.MapControllers();
app.Run();

// appsettings.json
{
  "Authentication": {
    "Google": {
      "ClientId": "your-google-client-id",
      "ClientSecret": "your-google-client-secret"
    },
    "Microsoft": {
      "ClientId": "your-microsoft-client-id",
      "ClientSecret": "your-microsoft-client-secret"
    }
  }
}
```

## Multi-Factor Authentication (MFA)

Adding additional verification layer beyond password.

```csharp
// MFA types:
// - TOTP (Time-based One-Time Password): Google Authenticator, Authy
// - SMS: Text message code
// - Email: Code sent to email
// - Backup codes: Generated codes for account recovery

// Install: dotnet add package Google.Authenticator

public class User
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
    
    public bool TwoFactorEnabled { get; set; }
    public string TwoFactorSecretKey { get; set; }  // TOTP secret
    public List<string> BackupCodes { get; set; } = new();
}

// Enable TOTP for user
[HttpPost("mfa/enable")]
[Authorize]
public async Task<IActionResult> EnableMfa()
{
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var user = await _userService.GetUserAsync(int.Parse(userId));
    
    // Generate TOTP secret
    var tg = new TwoFactorAuthenticator();
    var secret = tg.GenerateSetupCode(
        issuer: "YourApp",
        accountTitle: user.Email,
        accountSecret: Guid.NewGuid().ToByteArray(),
        qrCodeWidth: 300);
    
    return Ok(new
    {
        qrCode = secret.QrCodeAsImageUrl,
        manualKey = secret.ManualEntryKey
    });
}

// Verify TOTP code and enable MFA
[HttpPost("mfa/verify")]
[Authorize]
public async Task<IActionResult> VerifyMfa(VerifyMfaRequest request)
{
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var user = await _userService.GetUserAsync(int.Parse(userId));
    
    var tg = new TwoFactorAuthenticator();
    var isValid = tg.ValidateTwoFactorPin(
        secretKey: request.SecretKey,
        twoFactorCodeFromClient: request.Code);
    
    if (!isValid)
        return BadRequest("Invalid code");
    
    // Enable MFA
    user.TwoFactorEnabled = true;
    user.TwoFactorSecretKey = request.SecretKey;
    
    // Generate backup codes
    user.BackupCodes = GenerateBackupCodes(10);
    
    await _userService.UpdateAsync(user);
    
    return Ok(new
    {
        message = "MFA enabled",
        backupCodes = user.BackupCodes  // Show once!
    });
}

// Login with MFA
[HttpPost("login-mfa")]
[AllowAnonymous]
public async Task<IActionResult> LoginMfa(LoginMfaRequest request)
{
    var user = await _userService.AuthenticateAsync(
        request.Email, request.Password);
    
    if (user == null)
        return Unauthorized("Invalid credentials");
    
    if (!user.TwoFactorEnabled)
    {
        // No MFA, complete login
        var token = _tokenService.GenerateToken(user);
        return Ok(new { token });
    }
    
    // Verify TOTP code
    var tg = new TwoFactorAuthenticator();
    var isValid = tg.ValidateTwoFactorPin(
        user.TwoFactorSecretKey,
        request.TwoFactorCode);
    
    // Or check backup code
    if (!isValid && user.BackupCodes.Contains(request.TwoFactorCode))
    {
        isValid = true;
        user.BackupCodes.Remove(request.TwoFactorCode);
        await _userService.UpdateAsync(user);
    }
    
    if (!isValid)
        return Unauthorized("Invalid MFA code");
    
    var token = _tokenService.GenerateToken(user);
    return Ok(new { token });
}

private List<string> GenerateBackupCodes(int count)
{
    var codes = new List<string>();
    for (int i = 0; i < count; i++)
    {
        var code = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
        codes.Add(code);
    }
    return codes;
}
```

## Key Takeaways

1. **Cookie authentication**: Stateful, good for web apps, requires session storage
2. **JWT authentication**: Stateless, good for APIs, self-contained claims
3. **Refresh tokens**: Extend sessions without re-authentication
4. **OAuth 2.0**: Delegate authentication to third parties
5. **MFA**: Additional security layer beyond passwords
6. **Always validate signatures**: Never trust tokens without verification
7. **Use HTTPS**: Encrypt tokens in transit
8. **Short token lifetime**: Reduce exposure of compromised tokens
9. **Secure storage**: Store secrets in Key Vault, not code
10. **Bearer tokens**: Standard for API authentication

## Related Topics

- **Identity Management** (Topic 16): User management, roles, claims
- **Security Best Practices** (Topic 17): Password hashing, CORS, etc.

