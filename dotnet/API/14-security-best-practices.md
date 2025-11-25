# Chapter 14: Security Best Practices

## 14.1 OWASP Top 10 for APIs

OWASP publishes the most critical security vulnerabilities. APIs face unique risks:

### 1. Broken Authentication

**Vulnerabilities:**
- Weak password policies
- Session fixation
- Credential stuffing (reused passwords)
- Missing token expiration
- Tokens in logs or error messages

**Mitigation:**
```csharp
// Strong password requirements
var passwordPolicy = new PasswordOptions
{
    RequiredLength = 12,
    RequireDigit = true,
    RequireLowercase = true,
    RequireUppercase = true,
    RequireNonAlphanumeric = true
};

// Implement account lockout
var lockoutPolicy = new LockoutOptions
{
    MaxFailedAccessAttempts = 5,
    DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5)
};

// Enforce token expiration
public class JwtService
{
    public string GenerateToken(User user)
    {
        var token = new JwtSecurityToken(
            // ...
            expires: DateTime.UtcNow.AddMinutes(15)  // Short expiration
        );
    }
}

// Never log sensitive data
_logger.LogWarning("Login failed for email {Email}", user.Email);
// NOT: "Login failed, password was {password}"
```

### 2. Broken Authorization

**Vulnerabilities:**
- Missing permission checks
- Privilege escalation
- Accessing other user's data

**Mitigation:**
```csharp
[HttpGet("{id}")]
[Authorize]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
    
    // Verify user can access this resource
    if (id != currentUserId && !User.IsInRole("Admin"))
    {
        return Forbid();  // 403
    }
    
    var user = await _userService.GetUserAsync(id);
    return Ok(user);
}

// Use policies for complex authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanDeleteUser", policy =>
        policy.Requirements.Add(new CanDeleteUserRequirement())
    );
});

public class CanDeleteUserRequirement : IAuthorizationRequirement { }

public class CanDeleteUserHandler : AuthorizationHandler<CanDeleteUserRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CanDeleteUserRequirement requirement)
    {
        if (context.User.IsInRole("Admin"))
        {
            context.Succeed(requirement);
        }
        
        return Task.CompletedTask;
    }
}
```

### 3. Injection

**Vulnerabilities:**
- SQL injection (use parameterized queries)
- NoSQL injection
- Command injection
- LDAP injection

**Mitigation:**
```csharp
// Bad: String concatenation (vulnerable to SQL injection)
var query = $"SELECT * FROM Users WHERE Email = '{email}'";

// Good: Parameterized queries
var user = await _context.Users
    .FromSqlInterpolated($"SELECT * FROM Users WHERE Email = {email}")
    .FirstOrDefaultAsync();

// Good: LINQ (compiled to parameterized queries)
var user = await _context.Users
    .FirstOrDefaultAsync(u => u.Email == email);

// Validate/sanitize input even with parameterized queries
[HttpPost]
public async Task<IActionResult> CreateUser(CreateUserRequest request)
{
    // Validate format
    if (!IsValidEmail(request.Email))
        return BadRequest("Invalid email format");
    
    // Never execute unsanitized input as commands
    // Avoid: ProcessStartInfo with unsanitized arguments
}
```

### 4. Insecure Design

**Vulnerabilities:**
- Missing security requirements in design
- No threat modeling
- No access control by default

**Mitigation:**
- Threat model your API during design
- Secure by default (deny access, require authentication)
- Security in every layer

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]  // Secure by default
public class AdminController : ControllerBase
{
    [HttpPost("users/{id}/suspend")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SuspendUser(int id)
    {
        // Only explicitly allowed users can access
    }
}
```

### 5. Broken Access Control

**Vulnerabilities:**
- Missing checks for user ownership
- Horizontal privilege escalation
- URL-based access control

**Mitigation:**
```csharp
[HttpDelete("orders/{id}")]
[Authorize]
public async Task<IActionResult> DeleteOrder(int id)
{
    var currentUserId = GetCurrentUserId();
    var order = await _orderService.GetOrderAsync(id);
    
    if (order == null)
        return NotFound();
    
    // Verify ownership (not just authorization)
    if (order.UserId != currentUserId && !User.IsInRole("Admin"))
        return Forbid();
    
    await _orderService.DeleteOrderAsync(id);
    return NoContent();
}
```

### 6. Sensitive Data Exposure

**Vulnerabilities:**
- Unencrypted data in transit
- Unencrypted data at rest
- Sensitive data in logs
- Sensitive data in responses

**Mitigation:**
```csharp
// Always use HTTPS
app.UseHttpsRedirection();

// Encrypt sensitive fields at rest
public class EncryptedProperty
{
    [Encrypted]
    public string SocialSecurityNumber { get; set; }
}

// Don't return sensitive data
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> GetUser(int id)
{
    var user = await _userService.GetUserAsync(id);
    
    // Return DTO (doesn't include PasswordHash, InternalNotes, etc.)
    return Ok(new UserDto
    {
        Id = user.Id,
        Email = user.Email,
        Name = user.Name
    });
}

// Don't log sensitive data
_logger.LogInformation("User login: {Email}", email);
// NOT: "User login with password: {Password}"

// Set security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("Strict-Transport-Security", "max-age=31536000");
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    await next();
});
```

### 7. Authentication Bypass (API-Specific)

**Vulnerabilities:**
- Missing authentication on endpoints
- Weak token validation
- Token not checked on critical operations

**Mitigation:**
```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]  // Default: all endpoints require auth
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    [AllowAnonymous]  // Explicitly allow specific endpoints
    public async Task<ActionResult<PublicUserDto>> GetPublicProfile(int id)
    {
        // This is the only unauthenticated endpoint
    }
    
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        // Always verify authorization on critical operations
    }
}
```

### 8. Software Vulnerabilities

**Vulnerabilities:**
- Unpatched dependencies
- Using outdated libraries
- Known vulnerable packages

**Mitigation:**
```bash
# Check for vulnerable dependencies
dotnet add package NuGetAuditor
nuget-auditor audit

# Keep dependencies updated
dotnet outdated
dotnet package-check

# In CI/CD pipeline
dotnet build
```

### 9. Logging and Monitoring Failures

**Vulnerabilities:**
- Not logging security events
- No detection of attacks
- No alerting on suspicious activity

**Mitigation:**
```csharp
// Log security events
_logger.LogWarning(
    "Failed login attempt {Email} from {IpAddress}",
    email,
    ipAddress);

_logger.LogError(
    "Unauthorized access attempt to {Resource} by {UserId}",
    resource,
    userId);

// Alert on suspicious patterns
public class SecurityMonitoringService
{
    public async Task CheckForAttacksAsync()
    {
        // Alert if > 10 failed logins from same IP in 5 minutes
        var failedLogins = await GetFailedLoginsAsync(
            TimeSpan.FromMinutes(5)
        );
        
        var ipAddressGroups = failedLogins
            .GroupBy(x => x.IpAddress)
            .Where(g => g.Count() > 10);
        
        foreach (var group in ipAddressGroups)
        {
            await NotifyAsync(
                AlertSeverity.High,
                $"Potential brute force attack from {group.Key}"
            );
        }
    }
}
```

### 10. Rate Limiting and Bot Protection

**Vulnerabilities:**
- No rate limiting (enables brute force, DoS)
- No bot detection
- No API throttling

**Mitigation:**
```csharp
// Rate limit login attempts
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login-limit", policy =>
    {
        policy.PermitLimit = 5;
        policy.Window = TimeSpan.FromMinutes(15);
    });
});

[HttpPost("login")]
[RequireRateLimiting("login-limit")]
public async Task<IActionResult> Login(LoginRequest request)
{
    // Limited to 5 attempts per 15 minutes per IP
}

// Bot detection
public class BotDetectionMiddleware
{
    private readonly RequestDelegate _next;
    
    public async Task InvokeAsync(HttpContext context)
    {
        var userAgent = context.Request.Headers["User-Agent"].ToString();
        
        if (IsSuspiciousBot(userAgent))
        {
            context.Response.StatusCode = 403;
            return;
        }
        
        await _next(context);
    }
    
    private bool IsSuspiciousBot(string userAgent)
    {
        return string.IsNullOrEmpty(userAgent) ||
               userAgent.Contains("bot", StringComparison.OrdinalIgnoreCase);
    }
}
```

---

## 14.2 Input Validation and Sanitization

Never trust client input:

```csharp
[HttpPost]
public async Task<IActionResult> CreateUser(CreateUserRequest request)
{
    // Validate presence
    if (string.IsNullOrWhiteSpace(request.Email))
        return BadRequest("Email required");
    
    // Validate format
    if (!IsValidEmail(request.Email))
        return BadRequest("Invalid email format");
    
    // Validate length
    if (request.Name.Length > 100)
        return BadRequest("Name too long");
    
    // Sanitize (remove dangerous characters)
    var sanitizedName = SanitizeInput(request.Name);
    
    // Validate against business rules
    var exists = await _userService.ExistsAsync(request.Email);
    if (exists)
        return BadRequest("Email already in use");
    
    // Whitelist, don't blacklist
    var allowedStatuses = new[] { "Active", "Inactive" };
    if (!allowedStatuses.Contains(request.Status))
        return BadRequest("Invalid status");
}

// Use FluentValidation for complex rules
public class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(255);
        
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(12)
            .Matches(@"[A-Z]").WithMessage("Must contain uppercase")
            .Matches(@"[a-z]").WithMessage("Must contain lowercase")
            .Matches(@"[0-9]").WithMessage("Must contain digit");
    }
}
```

---

## 14.3 Data Encryption

### Encryption in Transit (HTTPS)

```csharp
// Enforce HTTPS
app.UseHttpsRedirection();

// HSTS (HTTP Strict Transport Security)
app.UseHsts();

// Set secure connection policies
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});
```

### Encryption at Rest

```csharp
// Encrypt sensitive fields in database
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    options.UseEncryption(keyProvider: new RsaKeyProvider(publicKey));
});

public class User
{
    public int Id { get; set; }
    
    [Encrypted]  // This field is encrypted
    public string SocialSecurityNumber { get; set; }
    
    public string Email { get; set; }  // Not encrypted
}
```

---

## 14.4 Secure Password Handling

```csharp
// Hash passwords with bcrypt
public class PasswordService
{
    public string HashPassword(string plainPassword)
    {
        // bcrypt with salt
        return BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 12);
    }
    
    public bool VerifyPassword(string plainPassword, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(plainPassword, hash);
    }
}

// Never store plaintext passwords
// Never send passwords via email
// Never log passwords

// Force password reset after:
// - Breach
// - Account compromise
// - Password age > 90 days (optional, debatable)

[HttpPost("force-reset/{id}")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> ForcePasswordReset(int id)
{
    var user = await _userService.GetUserAsync(id);
    user.PasswordExpiresAt = DateTime.UtcNow;  // Force reset on next login
    await _userService.UpdateAsync(user);
    
    return Ok();
}
```

---

## 14.5 Secure Headers

Set security-related HTTP headers:

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
        // Prevent MIME-type sniffing
        context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
        
        // Prevent clickjacking
        context.Response.Headers.Add("X-Frame-Options", "DENY");
        
        // Enable XSS protection
        context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
        
        // Prevent information disclosure
        context.Response.Headers.Add("X-Powered-By", "");
        context.Response.Headers.Remove("Server");
        
        // Content Security Policy
        context.Response.Headers.Add("Content-Security-Policy",
            "default-src 'self'; script-src 'self'");
        
        // Referrer Policy
        context.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
        
        // Force HTTPS
        context.Response.Headers.Add("Strict-Transport-Security",
            "max-age=31536000; includeSubDomains; preload");
        
        await _next(context);
    }
}

app.UseMiddleware<SecurityHeadersMiddleware>();
```

---

## 14.6 API Key Management

For service-to-service authentication:

```csharp
public class ApiKeyValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IApiKeyService _apiKeyService;
    
    public ApiKeyValidationMiddleware(RequestDelegate next, IApiKeyService apiKeyService)
    {
        _next = next;
        _apiKeyService = apiKeyService;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        // Look for API key in header
        if (context.Request.Headers.TryGetValue("X-API-Key", out var apiKey))
        {
            var (valid, clientId) = await _apiKeyService.ValidateKeyAsync(apiKey);
            
            if (valid)
            {
                context.Items["ClientId"] = clientId;
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("sub", clientId) },
                    "api-key"
                ));
            }
        }
        
        await _next(context);
    }
}

// API keys should:
// - Be long and random
// - Be rotated regularly
// - Be scoped to specific resources/actions
// - Be rate-limited per key
// - Be logged when used

public class ApiKeyService
{
    public async Task<(bool Valid, string ClientId)> ValidateKeyAsync(string apiKey)
    {
        // Hash the API key (don't store plaintext)
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        
        var stored = await _context.ApiKeys
            .FirstOrDefaultAsync(k => k.Hash == hash);
        
        if (stored == null)
            return (false, null);
        
        // Check expiration
        if (stored.ExpiresAt < DateTime.UtcNow)
            return (false, null);
        
        return (true, stored.ClientId);
    }
}
```

---

## 14.7 Audit Logging

Log security-relevant events:

```csharp
public class AuditLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Action { get; set; }
    public string Resource { get; set; }
    public bool Success { get; set; }
    public string IpAddress { get; set; }
    public DateTime Timestamp { get; set; }
}

public class AuditingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AppDbContext _context;
    
    public async Task InvokeAsync(HttpContext context)
    {
        var originalBody = context.Response.Body;
        
        using (var memoryStream = new MemoryStream())
        {
            context.Response.Body = memoryStream;
            
            await _next(context);
            
            // Log audit entry
            if (ShouldAudit(context))
            {
                var userId = int.TryParse(
                    context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    out var id) ? id : 0;
                
                var auditLog = new AuditLog
                {
                    UserId = userId,
                    Action = context.Request.Method,
                    Resource = context.Request.Path,
                    Success = context.Response.StatusCode < 400,
                    IpAddress = context.Connection.RemoteIpAddress.ToString(),
                    Timestamp = DateTime.UtcNow
                };
                
                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();
            }
            
            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(originalBody);
        }
    }
    
    private bool ShouldAudit(HttpContext context)
    {
        // Audit write operations and sensitive reads
        return context.Request.Method != "GET" ||
               context.Request.Path.StartsWithSegments("/api/admin");
    }
}
```

---

## 14.8 Dependency Scanning

Keep dependencies secure:

```bash
# Scan for vulnerabilities
dotnet add package NuGetAuditor
dotnet add package Snyk.CLI

# In CI/CD
dotnet restore
dotnet list package --vulnerable
```

---

## 14.9 Security Checklist

**Before production deployment:**
- ✓ All endpoints require authentication (except public endpoints)
- ✓ Authorization checked before accessing user data
- ✓ HTTPS enforced
- ✓ Security headers set
- ✓ Rate limiting configured
- ✓ Input validation on all endpoints
- ✓ Sensitive data encrypted and never logged
- ✓ Passwords hashed with bcrypt
- ✓ SQL injection prevented (parameterized queries)
- ✓ CORS properly configured
- ✓ Dependencies scanned for vulnerabilities
- ✓ Error messages don't leak information
- ✓ Audit logging enabled
- ✓ Security testing performed
- ✓ Incident response plan in place

---

## Summary

Security is a multi-layered approach. Authentication verifies identity. Authorization checks permissions. Input validation prevents injection attacks. HTTPS protects data in transit. Encryption protects data at rest. Security headers prevent browser-based attacks. Logging and monitoring detect incidents. Rate limiting prevents abuse. The final chapters cover deployment and documentation—getting your API to production and making it usable.
