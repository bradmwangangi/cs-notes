# 16. Identity Management

## Overview

Identity Management is the infrastructure for managing user identities, including user creation, password management, roles, claims, and permissions. ASP.NET Core Identity provides a complete solution for these concerns.

## ASP.NET Core Identity

### What is ASP.NET Core Identity?

ASP.NET Core Identity is a membership system that:
- Creates and manages user accounts
- Stores passwords securely (hashed)
- Manages roles and claims
- Implements policies for passwords, lockouts
- Supports external logins (OAuth, OIDC)

### Installation & Setup

```csharp
// Install packages
// dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore
// dotnet add package Microsoft.AspNetCore.Identity.UI
// dotnet add package Microsoft.AspNetCore.Identity.Stores

// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add DbContext with Identity
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Add Identity
builder.Services.AddIdentity<User, IdentityRole<int>>(options =>
{
    // Password requirements
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    
    // User requirements
    options.User.RequireUniqueEmail = true;
    options.User.AllowedUserNameCharacters = 
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
    
    // Lockout policy
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
    
    // Signin requirements
    options.SignIn.RequireConfirmedEmail = true;
    options.SignIn.RequireConfirmedPhoneNumber = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();  // For password reset, email confirmation

// Authentication & Authorization
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
})
.AddIdentityCookies();

builder.Services.AddAuthorization();

// Inject Identity services
builder.Services.AddScoped<UserManager<User>>();
builder.Services.AddScoped<RoleManager<IdentityRole<int>>>();
builder.Services.AddScoped<SignInManager<User>>();

var app = builder.Build();

// Apply migrations automatically (development only)
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();
        db.Database.Migrate();
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();

// DbContext with Identity
public class ApplicationDbContext : IdentityDbContext<User, IdentityRole<int>, int>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
    
    public DbSet<Order> Orders { get; set; }
    
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        
        // Custom configurations
    }
}

// User model
public class User : IdentityUser<int>
{
    // Identity provides: Id, UserName, Email, PasswordHash, etc.
    
    // Custom properties
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string ProfilePictureUrl { get; set; }
    
    // Navigation
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
```

### User Registration

```csharp
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IEmailService _emailService;
    
    public AuthController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IEmailService emailService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _emailService = emailService;
    }
    
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        // Check if email already exists
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
            return BadRequest("Email already registered");
        
        // Create user
        var user = new User
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            EmailConfirmed = false,
            LockoutEnabled = true
        };
        
        // Create with password (automatically hashed)
        var result = await _userManager.CreateAsync(user, request.Password);
        
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description);
            return BadRequest(new { errors });
        }
        
        // Send email confirmation
        var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var callbackUrl = $"https://yourapp.com/confirm-email?userId={user.Id}&code={Uri.EscapeDataString(code)}";
        
        await _emailService.SendEmailAsync(
            to: user.Email,
            subject: "Confirm your email",
            htmlContent: $"Click <a href='{callbackUrl}'>here</a> to confirm email");
        
        return Ok(new
        {
            message = "User registered. Check email to confirm.",
            userId = user.Id
        });
    }
    
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(
        int userId, string code)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return NotFound("User not found");
        
        var result = await _userManager.ConfirmEmailAsync(user, code);
        if (!result.Succeeded)
            return BadRequest("Invalid confirmation code");
        
        return Ok("Email confirmed successfully");
    }
    
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
            return Unauthorized("Invalid credentials");
        
        // Check if email is confirmed
        if (!user.EmailConfirmed)
            return Unauthorized("Email not confirmed");
        
        // Check if locked out
        if (await _userManager.IsLockedOutAsync(user))
            return StatusCode(StatusCodes.Status423Locked,
                "Account locked due to too many failed attempts");
        
        // Sign in
        var result = await _signInManager.PasswordSignInAsync(
            user, request.Password, request.RememberMe, 
            lockoutOnFailure: true);
        
        if (result.Succeeded)
        {
            return Ok(new
            {
                message = "Login successful",
                userId = user.Id,
                email = user.Email
            });
        }
        
        if (result.IsLockedOut)
        {
            return StatusCode(StatusCodes.Status423Locked,
                "Account locked");
        }
        
        return Unauthorized("Invalid password");
    }
    
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Ok("Logout successful");
    }
}

public class RegisterRequest
{
    [Required]
    public string FirstName { get; set; }
    
    [Required]
    public string LastName { get; set; }
    
    [Required]
    [EmailAddress]
    public string Email { get; set; }
    
    [Required]
    [StringLength(100, MinimumLength = 12)]
    public string Password { get; set; }
    
    [Required]
    [Compare(nameof(Password))]
    public string ConfirmPassword { get; set; }
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
```

## Roles & Claims

### Role-Based Authorization

```csharp
// Create roles
[HttpPost("roles")]
[Authorize(Roles = "SuperAdmin")]
public async Task<IActionResult> CreateRole(CreateRoleRequest request)
{
    var role = new IdentityRole<int>
    {
        Name = request.RoleName,
        NormalizedName = request.RoleName.ToUpper()
    };
    
    var result = await _roleManager.CreateAsync(role);
    if (!result.Succeeded)
        return BadRequest(result.Errors);
    
    return Ok("Role created");
}

// Assign role to user
[HttpPost("users/{userId}/roles")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> AssignRole(int userId, AssignRoleRequest request)
{
    var user = await _userManager.FindByIdAsync(userId.ToString());
    if (user == null)
        return NotFound();
    
    // Check if role exists
    var roleExists = await _roleManager.RoleExistsAsync(request.RoleName);
    if (!roleExists)
        return BadRequest("Role does not exist");
    
    var result = await _userManager.AddToRoleAsync(user, request.RoleName);
    if (!result.Succeeded)
        return BadRequest(result.Errors);
    
    return Ok($"User assigned to {request.RoleName} role");
}

// Remove role from user
[HttpDelete("users/{userId}/roles/{roleName}")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> RemoveRole(int userId, string roleName)
{
    var user = await _userManager.FindByIdAsync(userId.ToString());
    if (user == null)
        return NotFound();
    
    var result = await _userManager.RemoveFromRoleAsync(user, roleName);
    if (!result.Succeeded)
        return BadRequest(result.Errors);
    
    return Ok($"User removed from {roleName} role");
}

// Get user roles
[HttpGet("users/{userId}/roles")]
[Authorize]
public async Task<IActionResult> GetUserRoles(int userId)
{
    var user = await _userManager.FindByIdAsync(userId.ToString());
    if (user == null)
        return NotFound();
    
    var roles = await _userManager.GetRolesAsync(user);
    return Ok(roles);
}

// Use roles in endpoints
[Authorize(Roles = "Admin,SuperAdmin")]
[HttpDelete("users/{id}")]
public async Task<IActionResult> DeleteUser(int id)
{
    // Only Admin or SuperAdmin can delete
    var user = await _userManager.FindByIdAsync(id.ToString());
    if (user == null)
        return NotFound();
    
    var result = await _userManager.DeleteAsync(user);
    if (!result.Succeeded)
        return BadRequest(result.Errors);
    
    return NoContent();
}
```

### Claims-Based Authorization

```csharp
// Add claims to user
[HttpPost("users/{userId}/claims")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> AddClaim(int userId, AddClaimRequest request)
{
    var user = await _userManager.FindByIdAsync(userId.ToString());
    if (user == null)
        return NotFound();
    
    var claim = new Claim(request.ClaimType, request.ClaimValue);
    var result = await _userManager.AddClaimAsync(user, claim);
    
    if (!result.Succeeded)
        return BadRequest(result.Errors);
    
    return Ok("Claim added");
}

// Get user claims
[HttpGet("users/{userId}/claims")]
[Authorize]
public async Task<IActionResult> GetUserClaims(int userId)
{
    var user = await _userManager.FindByIdAsync(userId.ToString());
    if (user == null)
        return NotFound();
    
    var claims = await _userManager.GetClaimsAsync(user);
    return Ok(claims);
}

// Configure claim-based policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MustBeAdmin", policy =>
        policy.RequireClaim("IsAdmin", "true"));
    
    options.AddPolicy("MustBeVerified", policy =>
        policy.RequireClaim("EmailVerified", "true"));
    
    options.AddPolicy("DepartmentManager", policy =>
        policy.RequireClaim("Department", "Sales", "Marketing", "Engineering"));
});

// Use claim-based policies
[Authorize(Policy = "MustBeAdmin")]
[HttpGet("admin-only")]
public IActionResult AdminOnly()
{
    return Ok("Admin content");
}

[Authorize(Policy = "DepartmentManager")]
[HttpGet("department-data")]
public IActionResult DepartmentData()
{
    return Ok("Department data");
}
```

## Password Management

### Password Reset

```csharp
// Request password reset
[HttpPost("forgot-password")]
[AllowAnonymous]
public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
{
    var user = await _userManager.FindByEmailAsync(request.Email);
    
    if (user == null)
    {
        // Don't reveal if email exists
        return Ok("If email exists, reset link has been sent");
    }
    
    // Generate password reset token
    var code = await _userManager.GeneratePasswordResetTokenAsync(user);
    
    // Send reset link via email
    var resetUrl = $"https://yourapp.com/reset-password?userId={user.Id}&code={Uri.EscapeDataString(code)}";
    
    await _emailService.SendEmailAsync(
        to: user.Email,
        subject: "Reset your password",
        htmlContent: $"Click <a href='{resetUrl}'>here</a> to reset your password");
    
    return Ok("Password reset link sent");
}

// Reset password
[HttpPost("reset-password")]
[AllowAnonymous]
public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
{
    var user = await _userManager.FindByIdAsync(request.UserId.ToString());
    if (user == null)
        return BadRequest("Invalid user");
    
    var result = await _userManager.ResetPasswordAsync(
        user, request.Code, request.NewPassword);
    
    if (!result.Succeeded)
        return BadRequest(result.Errors);
    
    return Ok("Password reset successfully");
}

public class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; }
}

public class ResetPasswordRequest
{
    [Required]
    public int UserId { get; set; }
    
    [Required]
    public string Code { get; set; }
    
    [Required]
    [StringLength(100, MinimumLength = 12)]
    public string NewPassword { get; set; }
    
    [Required]
    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; }
}
```

### Change Password (Authenticated User)

```csharp
[HttpPost("change-password")]
[Authorize]
public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
{
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var user = await _userManager.FindByIdAsync(userId);
    
    if (user == null)
        return Unauthorized();
    
    // Verify current password
    var result = await _userManager.ChangePasswordAsync(
        user, request.CurrentPassword, request.NewPassword);
    
    if (!result.Succeeded)
    {
        var errors = result.Errors.Select(e => e.Description);
        return BadRequest(new { errors });
    }
    
    return Ok("Password changed successfully");
}

public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; }
    
    [Required]
    [StringLength(100, MinimumLength = 12)]
    public string NewPassword { get; set; }
    
    [Required]
    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; }
}
```

## Password Hashing

### How ASP.NET Core Hashes Passwords

```csharp
// ASP.NET Core Identity uses PBKDF2 by default
// Formula: hash = PBKDF2(password, salt, iterations)
// Default iterations: 10,000 (can be increased)

// When user registers:
var user = new User { UserName = "john" };
var result = await _userManager.CreateAsync(user, "MyPassword123!");
// Password is:
// 1. Salted (random bytes added)
// 2. Hashed using PBKDF2 with 10,000+ iterations
// 3. Stored securely (original password never stored)

// When user logs in:
var result = await _signInManager.PasswordSignInAsync(
    "john@example.com", "MyPassword123!", rememberMe: false, lockoutOnFailure: true);
// 1. Retrieved stored hash from database
// 2. Hash provided password with stored salt
// 3. Compare hashes (constant-time comparison to prevent timing attacks)
// 4. If match, authentication succeeds

// Never create your own hash!
// Bad: user.PasswordHash = SHA256.ComputeHash(password);
// Good: Let UserManager.CreateAsync handle it
```

### Password Hasher Customization

```csharp
// Custom password hasher for additional requirements
public class EnhancedPasswordHasher : IPasswordHasher<User>
{
    private readonly PasswordHasher<User> _defaultHasher = 
        new PasswordHasher<User>();
    
    public string HashPassword(User user, string password)
    {
        // Add validation
        if (password.Length < 12)
            throw new ArgumentException("Password too short");
        
        // Use default hasher with upgraded iterations
        var hashed = _defaultHasher.HashPassword(user, password);
        
        // Could add additional logic here
        return hashed;
    }
    
    public PasswordVerificationResult VerifyHashedPassword(
        User user, string hash, string providedPassword)
    {
        return _defaultHasher.VerifyHashedPassword(user, hash, providedPassword);
    }
}

// Register custom hasher
builder.Services.AddScoped<IPasswordHasher<User>, EnhancedPasswordHasher>();
```

## User Management

### Profile Management

```csharp
[ApiController]
[Route("api/users")]
public class UserController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    
    public UserController(UserManager<User> userManager)
    {
        _userManager = userManager;
    }
    
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserProfileDto>> GetProfile()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var user = await _userManager.FindByIdAsync(userId);
        
        if (user == null)
            return NotFound();
        
        return Ok(new UserProfileDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            ProfilePictureUrl = user.ProfilePictureUrl
        });
    }
    
    [HttpPut("me")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var user = await _userManager.FindByIdAsync(userId);
        
        if (user == null)
            return NotFound();
        
        user.FirstName = request.FirstName ?? user.FirstName;
        user.LastName = request.LastName ?? user.LastName;
        user.PhoneNumber = request.PhoneNumber ?? user.PhoneNumber;
        user.ProfilePictureUrl = request.ProfilePictureUrl ?? user.ProfilePictureUrl;
        
        var result = await _userManager.UpdateAsync(user);
        
        if (!result.Succeeded)
            return BadRequest(result.Errors);
        
        return Ok("Profile updated");
    }
    
    [HttpDelete("me")]
    [Authorize]
    public async Task<IActionResult> DeleteAccount(DeleteAccountRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var user = await _userManager.FindByIdAsync(userId);
        
        if (user == null)
            return NotFound();
        
        // Verify password before deletion
        var passwordValid = await _userManager.CheckPasswordAsync(
            user, request.Password);
        
        if (!passwordValid)
            return Unauthorized("Invalid password");
        
        var result = await _userManager.DeleteAsync(user);
        
        if (!result.Succeeded)
            return BadRequest(result.Errors);
        
        return Ok("Account deleted");
    }
}

public class UpdateProfileRequest
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string PhoneNumber { get; set; }
    public string ProfilePictureUrl { get; set; }
}

public class DeleteAccountRequest
{
    [Required]
    public string Password { get; set; }
}
```

## Key Takeaways

1. **ASP.NET Core Identity**: Complete membership system
2. **Automatic password hashing**: Never store plaintext passwords
3. **Roles**: Group permissions for users
4. **Claims**: Granular permission attributes
5. **Email confirmation**: Verify user owns email
6. **Lockout policy**: Prevent brute force attacks
7. **Password reset tokens**: Time-limited, single-use
8. **UserManager**: Central service for user operations
9. **RoleManager**: Central service for role operations
10. **SignInManager**: Handle login/logout and security checks

## Related Topics

- **Authentication Mechanisms** (Topic 15): How users prove identity
- **Security Best Practices** (Topic 17): Securing the entire system

