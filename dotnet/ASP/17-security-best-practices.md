# 17. Security Best Practices

## Overview

Security is not a feature—it's a fundamental responsibility when building applications that handle user data. This topic covers essential security practices across authentication, authorization, data protection, and infrastructure.

## OWASP Top 10

The Open Web Application Security Project (OWASP) maintains the top 10 most critical security vulnerabilities:

1. **Broken Access Control** - Insufficient authorization
2. **Cryptographic Failures** - Exposed sensitive data
3. **Injection** - SQL injection, command injection
4. **Insecure Design** - Missing security requirements
5. **Security Misconfiguration** - Default credentials, exposed configs
6. **Vulnerable Outdated Components** - Unpatched dependencies
7. **Authentication Failures** - Weak authentication, session fixation
8. **Data Integrity Failures** - Unsigned/unencrypted data
9. **Logging Monitoring Failures** - Missing security monitoring
10. **SSRF** - Server-Side Request Forgery

## HTTPS/TLS

### Enforcing HTTPS

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Enforce HTTPS
builder.Services.AddHttpsRedirection(options =>
{
    options.HttpsPort = 443;
    options.RedirectStatusCode = StatusCodes.Status301MovedPermanently;
});

builder.Services.AddHsts(options =>
{
    // Strict-Transport-Security header
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;  // Include in HSTS preload list
});

var app = builder.Build();

if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

// Middleware runs in order:
// 1. HTTP request arrives
// 2. UseHttpsRedirection redirects to HTTPS
// 3. Request comes back as HTTPS
// 4. UseHsts adds security header for future requests

app.Run();

// appsettings.json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:80"
      },
      "Https": {
        "Url": "https://0.0.0.0:443",
        "Certificate": {
          "Path": "/etc/ssl/certs/certificate.pfx",
          "Password": "${CERT_PASSWORD}"
        }
      }
    }
  }
}

// Nginx reverse proxy configuration
// upstream aspnetcore {
//   server 127.0.0.1:5000;
// }
//
// server {
//   listen 443 ssl http2;
//   server_name yourdomain.com;
//   
//   ssl_certificate /etc/ssl/certs/cert.pem;
//   ssl_certificate_key /etc/ssl/private/key.pem;
//   ssl_protocols TLSv1.2 TLSv1.3;
//   ssl_ciphers HIGH:!aNULL:!MD5;
//   
//   location / {
//     proxy_pass http://aspnetcore;
//   }
// }
```

## CORS (Cross-Origin Resource Sharing)

### CORS Configuration

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        policy
            .WithOrigins("https://frontend.example.com", "https://mobile.example.com")
            .AllowAnyMethod()  // GET, POST, PUT, DELETE, etc.
            .AllowAnyHeader()
            .AllowCredentials();  // Allow cookies
    });
    
    // Development policy (permissive)
    if (builder.Environment.IsDevelopment())
    {
        options.AddPolicy("Development", policy =>
        {
            policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
    }
});

var app = builder.Build();

if (app.Environment.IsProduction())
{
    app.UseCors("AllowSpecificOrigins");  // Strict in production
}
else
{
    app.UseCors("Development");  // Permissive in development
}

app.MapControllers();
app.Run();

// Preflight request handling:
// 1. Browser makes OPTIONS request to check permissions
// 2. Server responds with CORS headers
// 3. If allowed, browser makes actual request
// 4. If denied, browser blocks request (JavaScript can't access response)
```

### CORS Best Practices

```csharp
// ✓ Whitelist specific origins
policy.WithOrigins("https://trusted-domain.com");

// ✗ Don't use AllowAnyOrigin with AllowCredentials
policy
    .AllowAnyOrigin()
    .AllowCredentials();  // INCOMPATIBLE!

// ✓ Specify allowed methods
policy.WithMethods("GET", "POST");

// ✗ Don't allow all methods for sensitive operations
policy.AllowAnyMethod();  // Could allow DELETE from browser

// ✓ Specify allowed headers
policy.WithHeaders("content-type", "authorization");

// ✗ Avoid allowing all headers for XSS prevention
policy.AllowAnyHeader();

// ✓ Set credentials explicitly if needed
policy.AllowCredentials();

// ✓ Don't expose internal headers
policy.WithExposedHeaders("X-Custom-Header");

// ✗ Never expose Authorization or Set-Cookie
```

## CSRF (Cross-Site Request Forgery) Protection

### Antiforgery Tokens

```csharp
// Program.cs
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "XSRF-TOKEN";
    options.HeaderName = "X-XSRF-TOKEN";
    options.FormFieldName = "__RequestVerificationToken";
});

var app = builder.Build();
app.UseAntiforgery();  // Validate tokens

// Controller
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    // GET returns token
    [HttpGet("csrf-token")]
    [IgnoreAntiforgeryToken]  // Don't require token for GET
    public IActionResult GetCsrfToken()
    {
        var token = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = token.RequestToken });
    }
    
    // POST requires token
    [HttpPost]
    [ValidateAntiforgeryToken]  // Validate token
    public async Task<IActionResult> CreateUser(CreateUserRequest request)
    {
        // Token validated before this runs
        return Ok();
    }
}

// Frontend
// 1. Fetch CSRF token
// const response = await fetch('/api/users/csrf-token');
// const { token } = await response.json();
//
// 2. Send with POST
// fetch('/api/users', {
//   method: 'POST',
//   headers: {
//     'Content-Type': 'application/json',
//     'X-XSRF-TOKEN': token
//   },
//   body: JSON.stringify({ name: 'John' })
// });
```

## SQL Injection Prevention

### Parameterized Queries

```csharp
// BAD: String concatenation (vulnerable to SQL injection)
string email = "john@example.com' OR '1'='1";
var query = $"SELECT * FROM users WHERE Email = '{email}'";
// Executes: SELECT * FROM users WHERE Email = 'john@example.com' OR '1'='1'
// Returns ALL users!

// GOOD: Entity Framework Core (parameterized by default)
var user = await _context.Users
    .FirstOrDefaultAsync(u => u.Email == email);
// EF Core automatically parameterizes this

// GOOD: Raw SQL with parameters
var email = "john@example.com";
var users = await _context.Users
    .FromSql($"SELECT * FROM users WHERE Email = {email}")
    .ToListAsync();
// Interpolation creates parameter, not string concat

// BAD: Raw SQL with concatenation
var query = $"SELECT * FROM users WHERE Email = '{email}'";  // NEVER!

// If you must use raw SQL:
var email = "john@example.com";
using (var command = _context.Database.GetDbConnection().CreateCommand())
{
    command.CommandText = "SELECT * FROM users WHERE Email = @email";
    command.Parameters.Add(new SqlParameter("@email", email));
    
    var users = await _context.Users.FromSqlRaw(
        "SELECT * FROM users WHERE Email = @email", 
        new SqlParameter("@email", email))
        .ToListAsync();
}
```

## XSS (Cross-Site Scripting) Prevention

### Encoding Output

```csharp
// BAD: Rendering user input without encoding
public class Comment
{
    public int Id { get; set; }
    public string Text { get; set; }
}

[HttpGet("{id}")]
public ActionResult<CommentDto> GetComment(int id)
{
    var comment = await _repository.GetCommentAsync(id);
    // If Text = "<script>alert('XSS')</script>"
    return Ok(comment);  // DANGEROUS in HTML context
}

// GOOD: ASP.NET Core encodes JSON by default
public ActionResult<CommentDto> GetComment(int id)
{
    var comment = await _repository.GetCommentAsync(id);
    // JSON response encodes: "<script>alert('XSS')</script>"
    return Ok(comment);  // Safe
}

// In HTML (if rendering server-side with Razor):
<p>@Html.Encode(comment.Text)</p>  <!-- Encoded -->
<p>@comment.Text</p>                <!-- NOT encoded (dangerous) -->
<p>@Html.Raw(comment.Text)</p>      <!-- Explicitly NOT encoded (DANGEROUS!) -->

// Configure response headers to prevent inline scripts
builder.Services.AddControllersWithViews(options =>
{
    // Content Security Policy
    options.Filters.Add(new SecurityHeadersAttribute());
});

public class SecurityHeadersAttribute : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        context.HttpContext.Response.Headers.Add(
            "X-Content-Type-Options", "nosniff");
        context.HttpContext.Response.Headers.Add(
            "X-Frame-Options", "DENY");
        context.HttpContext.Response.Headers.Add(
            "X-XSS-Protection", "1; mode=block");
        context.HttpContext.Response.Headers.Add(
            "Content-Security-Policy",
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'");
        
        await next();
    }
}
```

## Input Validation

### Validation Before Processing

```csharp
// Always validate and sanitize input
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateUser(CreateUserRequest request)
    {
        // ModelState validation from data annotations
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        // Additional business logic validation
        if (await _userService.EmailExistsAsync(request.Email))
            return BadRequest("Email already exists");
        
        // Sanitize input
        var sanitizedName = Sanitize(request.Name);
        var sanitizedBio = Sanitize(request.Bio);
        
        var user = new User
        {
            Name = sanitizedName,
            Email = request.Email.ToLower().Trim(),
            Bio = sanitizedBio
        };
        
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();
        
        return Created($"/users/{user.Id}", user);
    }
    
    // Sanitize HTML input
    private string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;
        
        // Remove HTML tags
        var htmlRemovalPattern = new Regex("<.*?>");
        var sanitized = htmlRemovalPattern.Replace(input, "");
        
        return sanitized.Trim();
    }
}

// Use FluentValidation for complex rules
public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .Must(email => !email.EndsWith("@spam.com"))
            .WithMessage("Spam email not allowed");
        
        RuleFor(x => x.Name)
            .NotEmpty()
            .Length(2, 100)
            .Must(name => !name.Any(char.IsDigit))
            .WithMessage("Name cannot contain digits");
        
        RuleFor(x => x.Age)
            .InclusiveBetween(18, 120);
    }
}
```

## Secret Management

### Secure Credential Storage

```csharp
// ✗ NEVER store secrets in code or config files
// ✗ NEVER commit secrets to version control
// ✗ NEVER log secrets

// ✓ Use environment variables for local development
var apiKey = Environment.GetEnvironmentVariable("API_KEY");

// ✓ Use User Secrets for development (local only)
// dotnet user-secrets init
// dotnet user-secrets set "Stripe:ApiKey" "sk_test_..."

builder.Configuration
    .AddUserSecrets<Program>();  // Load from user secrets

// ✓ Use Azure Key Vault for production
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{keyVaultName}.vault.azure.net/"),
    new DefaultAzureCredential());

var apiKey = builder.Configuration["Stripe:ApiKey"];

// ✓ Don't log secrets
[HttpPost("payment")]
public async Task<IActionResult> ProcessPayment(PaymentRequest request)
{
    var apiKey = _config["Stripe:ApiKey"];
    
    // ✗ BAD: Don't log API key
    // _logger.LogInformation($"Processing payment with key: {apiKey}");
    
    // ✓ GOOD: Don't include secrets in logs
    _logger.LogInformation("Processing payment for user {UserId}", userId);
    
    var result = await _paymentService.ProcessAsync(
        request.Amount, apiKey);
    
    return Ok(result);
}

// ✓ Use secrets in appsettings (development only)
{
  "Stripe": {
    "ApiKey": "${STRIPE_API_KEY}",
    "PublishableKey": "${STRIPE_PUBLISHABLE_KEY}"
  }
}

// Don't commit appsettings.Production.json:
// .gitignore
// appsettings.Production.json
```

## Authentication & Authorization Security

### Strong Password Requirements

```csharp
builder.Services.AddIdentity<User, IdentityRole<int>>(options =>
{
    options.Password.RequiredLength = 12;              // At least 12 chars
    options.Password.RequireDigit = true;              // Need 0-9
    options.Password.RequireUppercase = true;          // Need A-Z
    options.Password.RequireLowercase = true;          // Need a-z
    options.Password.RequireNonAlphanumeric = true;     // Need !@#$%
    options.Password.RequiredUniqueChars = 4;          // 4 unique chars
    
    // Lockout policy
    options.Lockout.DefaultLockoutTimeSpan = 
        TimeSpan.FromMinutes(15);                      // 15-min lockout
    options.Lockout.MaxFailedAccessAttempts = 5;       // 5 failed attempts
    
    // User requirements
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = true;       // Verify email
});
```

### Secure Session Management

```csharp
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);    // Session timeout
    options.Cookie.HttpOnly = true;                    // No JavaScript access
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // HTTPS only
    options.Cookie.SameSite = SameSiteMode.Strict;     // CSRF protection
    options.Cookie.IsEssential = true;                 // Always set cookie
});

app.UseSession();

// Check session validity
[Authorize]
[HttpGet("secure-data")]
public IActionResult GetSecureData()
{
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    
    // Verify session exists (catch session hijacking)
    if (string.IsNullOrEmpty(userId))
        return Unauthorized("Session invalid");
    
    return Ok("Secure data");
}
```

## Logging & Monitoring

### Security Logging

```csharp
// Log security events but NOT sensitive data
[HttpPost("login")]
[AllowAnonymous]
public async Task<IActionResult> Login(LoginRequest request)
{
    try
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        
        if (user == null)
        {
            // ✓ Log failed login attempt
            _logger.LogWarning(
                "Failed login attempt for email: {Email} from IP: {IpAddress}",
                HashEmail(request.Email),  // Hash for privacy
                HttpContext.Connection.RemoteIpAddress);
            
            return Unauthorized();
        }
        
        var result = await _signInManager.PasswordSignInAsync(
            user, request.Password, false, lockoutOnFailure: true);
        
        if (result.Succeeded)
        {
            // ✓ Log successful login
            _logger.LogInformation(
                "User {UserId} logged in successfully",
                user.Id);
            
            return Ok();
        }
        
        if (result.IsLockedOut)
        {
            // ✓ Log lockout event
            _logger.LogWarning(
                "User {UserId} account locked due to failed attempts",
                user.Id);
            
            return StatusCode(423, "Account locked");
        }
    }
    catch (Exception ex)
    {
        // ✗ Don't log exceptions with user input
        // _logger.LogError(ex, $"Login error: {request.Email}");
        
        // ✓ Log without sensitive data
        _logger.LogError(ex, "Login error occurred");
        
        return StatusCode(500);
    }
    
    return Unauthorized();
}

private string HashEmail(string email)
{
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(email));
    return Convert.ToBase64String(hashedBytes);
}
```

### Alert on Security Events

```csharp
// Monitor for security anomalies
public class SecurityMonitoringMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityMonitoringMiddleware> _logger;
    
    public SecurityMonitoringMiddleware(RequestDelegate next,
        ILogger<SecurityMonitoringMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value.ToLower();
        
        // Alert on suspicious patterns
        if (path.Contains("admin") && !IsAdmin(context.User))
        {
            _logger.LogWarning(
                "Unauthorized admin access attempt from {IpAddress}",
                context.Connection.RemoteIpAddress);
        }
        
        if (path.Contains("../") || path.Contains("..\\"))
        {
            _logger.LogWarning(
                "Directory traversal attempt detected: {Path}",
                path);
        }
        
        if (context.Request.Headers.Count > 100)
        {
            _logger.LogWarning(
                "Suspicious number of headers: {Count}",
                context.Request.Headers.Count);
        }
        
        await _next(context);
    }
    
    private bool IsAdmin(ClaimsPrincipal user)
    {
        return user.IsInRole("Admin");
    }
}

// Register middleware
app.UseMiddleware<SecurityMonitoringMiddleware>();
```

## Rate Limiting & DDoS Protection

### Rate Limiting Implementation

```csharp
// Install: dotnet add package AspNetCoreRateLimit

builder.Services.AddMemoryCache();
builder.Services.AddInMemoryRateLimiting();
builder.Services.Configure<IpRateLimitOptions>(
    builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
builder.Services.AddSingleton<IIpPolicyStore, MemoryIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryRateLimitCounterStore>();

var app = builder.Build();

app.UseIpRateLimiting();

// appsettings.json
{
  "IpRateLimiting": {
    "EnableEndpointRateLimiting": true,
    "StackBlockedRequests": false,
    "RealIpHeader": "X-Real-IP",
    "ClientIdHeader": "X-ClientId",
    "HttpStatusCode": 429,
    "GeneralRules": [
      {
        "Endpoint": "*",
        "Period": "1m",
        "Limit": 100
      },
      {
        "Endpoint": "/api/auth/login",
        "Period": "15m",
        "Limit": 5
      },
      {
        "Endpoint": "/api/auth/register",
        "Period": "1h",
        "Limit": 10
      }
    ]
  }
}

// Per-endpoint rate limiting
[HttpGet]
[Endpoint("GET /api/users", "GetUsers", 
    RateLimit = "100 per minute")]
public async Task<ActionResult<List<UserDto>>> GetUsers()
{
    return Ok(await _userService.GetUsersAsync());
}
```

## Dependency Updates

### Keep Dependencies Secure

```bash
# Check for vulnerable packages
dotnet list package --vulnerable

# Update to latest stable versions
dotnet outdated

# Update specific package
dotnet add package Microsoft.AspNetCore.Mvc --version 8.0.0

# Update all packages
dotnet package update
```

## Security Headers

### Comprehensive Security Headers

```csharp
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    
    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        // Prevent MIME type sniffing
        context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
        
        // Prevent clickjacking
        context.Response.Headers.Add("X-Frame-Options", "DENY");
        
        // Enable XSS protection (older browsers)
        context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
        
        // Content Security Policy
        context.Response.Headers.Add("Content-Security-Policy",
            "default-src 'self'; " +
            "script-src 'self' cdn.jsdelivr.net; " +
            "style-src 'self' 'unsafe-inline' fonts.googleapis.com; " +
            "img-src 'self' data: https:; " +
            "font-src 'self' fonts.gstatic.com; " +
            "connect-src 'self'; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'");
        
        // Referrer Policy
        context.Response.Headers.Add("Referrer-Policy",
            "strict-origin-when-cross-origin");
        
        // Feature Policy / Permissions Policy
        context.Response.Headers.Add("Permissions-Policy",
            "geolocation=(), " +
            "microphone=(), " +
            "camera=(), " +
            "payment=()");
        
        await _next(context);
    }
}

app.UseMiddleware<SecurityHeadersMiddleware>();
```

## Key Takeaways

1. **HTTPS mandatory**: Encrypt all traffic with TLS
2. **CORS whitelist**: Only allow trusted origins
3. **CSRF tokens**: Protect form submissions
4. **Parameterized queries**: Prevent SQL injection
5. **Input validation**: Whitelist, don't blacklist
6. **Output encoding**: Prevent XSS attacks
7. **Strong passwords**: Enforce complexity requirements
8. **Secure secrets**: Never hardcode credentials
9. **Logging security events**: Monitor anomalies
10. **Rate limiting**: Prevent DDoS and brute force
11. **Keep dependencies updated**: Patch vulnerabilities quickly
12. **Security headers**: Defense in depth
13. **Session security**: HttpOnly, Secure, SameSite cookies
14. **Multi-factor authentication**: Extra verification layer
15. **Least privilege**: Users/services have minimum permissions needed

## Security Checklist

- [ ] HTTPS enabled in production
- [ ] HSTS header configured
- [ ] CORS policy whitelisted
- [ ] CSRF protection enabled
- [ ] SQL injection prevention (parameterized queries)
- [ ] XSS prevention (output encoding)
- [ ] Authentication required for sensitive endpoints
- [ ] Authorization checks in place
- [ ] Password policy enforced
- [ ] Secrets stored securely (Key Vault)
- [ ] Security headers configured
- [ ] Logging monitors security events
- [ ] Rate limiting implemented
- [ ] Dependencies updated
- [ ] Error messages don't leak information
- [ ] No debug info in production
- [ ] GDPR/privacy compliance
- [ ] Security audits performed
- [ ] Incident response plan exists
- [ ] Staff trained on security

## Related Topics

- **Authentication Mechanisms** (Topic 15): User identity verification
- **Identity Management** (Topic 16): User accounts and permissions

