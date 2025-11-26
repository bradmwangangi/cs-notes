# 41. Production Readiness

## Overview
Production readiness encompasses all the practices, tools, and processes required to safely operate enterprise systems. A production-ready system must be observable, secure, recoverable, and configurable without redeployment. This topic covers the operational excellence required to run systems reliably at scale.

---

## 1. Feature Flags and Configuration

### 1.1 Feature Flag Management

```csharp
// Feature flags decouple code deployment from feature release

namespace OrderManagement.FeatureManagement
{
    // Feature flag service
    public interface IFeatureFlagService
    {
        Task<bool> IsEnabledAsync(string featureName, int? userId = null);
        Task<T> GetVariantAsync<T>(string featureName, T defaultValue);
        Task<FeatureFlagStats> GetStatsAsync(string featureName);
    }
    
    public class FeatureFlagService : IFeatureFlagService
    {
        private readonly IFeatureFlagStore _store;
        private readonly ILogger<FeatureFlagService> _logger;
        
        public async Task<bool> IsEnabledAsync(string featureName, int? userId = null)
        {
            var flag = await _store.GetAsync(featureName);
            
            if (flag == null)
            {
                _logger.LogWarning("Feature flag not found: {FeatureName}", featureName);
                return false;  // Default to off
            }
            
            if (!flag.IsEnabled)
                return false;
            
            // Check rollout percentage
            if (flag.RolloutPercentage < 100)
            {
                if (userId == null)
                    return false;
                
                // Consistent hashing: same user always gets same result
                var hash = Math.Abs(HashCode.Combine(userId, featureName));
                var percentage = hash % 100;
                
                return percentage < flag.RolloutPercentage;
            }
            
            return true;
        }
        
        public async Task<T> GetVariantAsync<T>(string featureName, T defaultValue)
        {
            var flag = await _store.GetAsync(featureName);
            
            if (flag?.Variant == null)
                return defaultValue;
            
            return JsonConvert.DeserializeObject<T>(flag.Variant);
        }
        
        public async Task<FeatureFlagStats> GetStatsAsync(string featureName)
        {
            return await _store.GetStatsAsync(featureName);
        }
    }
    
    // Feature flag states
    public class FeatureFlag
    {
        public string Name { get; set; }
        public bool IsEnabled { get; set; }
        public int RolloutPercentage { get; set; }  // 0-100
        public string Variant { get; set; }  // JSON for A/B testing
        public DateTime CreatedAt { get; set; }
        public DateTime LastModifiedAt { get; set; }
        public string Description { get; set; }
    }
    
    // Usage in code
    public class OrderService
    {
        private readonly IFeatureFlagService _flagService;
        private readonly IOrderRepository _repository;
        
        public async Task<OrderDto> GetOrderAsync(int orderId, int userId)
        {
            var order = await _repository.GetByIdAsync(orderId);
            
            // Feature: Enhanced order details
            var useEnhancedDetails = await _flagService.IsEnabledAsync(
                "enhanced-order-details",
                userId
            );
            
            if (useEnhancedDetails)
            {
                return await BuildEnhancedOrderDto(order);
            }
            
            return BuildSimpleOrderDto(order);
        }
        
        public async Task<int> CreateOrderAsync(CreateOrderRequest request)
        {
            // Feature: New order processing pipeline
            var useNewPipeline = await _flagService.IsEnabledAsync(
                "new-order-pipeline"
            );
            
            if (useNewPipeline)
            {
                return await _newPipeline.ProcessAsync(request);
            }
            
            return await _legacyPipeline.ProcessAsync(request);
        }
    }
    
    // Deployment scenarios with flags
    public class DeploymentScenarios
    {
        /*
        SCENARIO 1: Gradual Rollout
        Day 1: Enable for 10% of users
        Day 2: Enable for 25% of users
        Day 3: Enable for 50% of users
        Day 4: Enable for 100% of users
        
        SCENARIO 2: Canary Release
        Enable for users in specific region first
        Monitor metrics for 24 hours
        If metrics good, enable for all regions
        
        SCENARIO 3: A/B Testing
        Variant A: New UI design
        Variant B: Current UI design
        Show 50% of users each variant
        Measure conversion/engagement
        
        SCENARIO 4: Kill Switch
        Feature causes issues in production
        Disable immediately without redeployment
        Users see old behavior
        Team debugs and fixes
        Re-enable when ready
        */
    }
}
```

### 1.2 Configuration Management

```csharp
// Configuration without hardcoding or environment variables

namespace OrderManagement.Configuration
{
    // Hierarchical configuration with overrides
    public interface IConfigurationService
    {
        T GetSetting<T>(string key, T defaultValue = default);
        Task<T> GetSettingAsync<T>(string key, T defaultValue = default);
        void SetSetting<T>(string key, T value);
    }
    
    public class ConfigurationService : IConfigurationService
    {
        private readonly IConfiguration _config;
        private readonly IConfigurationStore _store;  // Database, Redis, etc.
        private readonly IMemoryCache _cache;
        
        public async Task<T> GetSettingAsync<T>(string key, T defaultValue = default)
        {
            // L1: Check in-memory cache
            if (_cache.TryGetValue($"config-{key}", out T cached))
                return cached;
            
            // L2: Check configuration store (database, Redis)
            var stored = await _store.GetAsync(key);
            if (stored != null)
            {
                var value = JsonConvert.DeserializeObject<T>(stored);
                _cache.Set($"config-{key}", value, TimeSpan.FromMinutes(5));
                return value;
            }
            
            // L3: Check appsettings.json (immutable after startup)
            var configValue = _config.GetValue<T>(key);
            if (configValue != null)
                return configValue;
            
            return defaultValue;
        }
        
        public void SetSetting<T>(string key, T value)
        {
            // Store in database/Redis
            _store.SetAsync(key, JsonConvert.SerializeObject(value)).Wait();
            
            // Invalidate cache
            _cache.Remove($"config-{key}");
            
            // Publish event for other instances to refresh
            _eventBus.PublishAsync(new ConfigurationChangedEvent { Key = key });
        }
    }
    
    // Configuration examples
    public class OrderManagementConfiguration
    {
        // Feature toggles
        public bool EnableNewOrderProcessing { get; set; }
        public bool EnablePaymentOptimization { get; set; }
        
        // Service settings
        public int MaxOrderItemCount { get; set; } = 100;
        public decimal MaxOrderTotal { get; set; } = 999999.99m;
        public int OrderTimeoutMinutes { get; set; } = 30;
        
        // Notification settings
        public bool SendOrderConfirmationEmail { get; set; } = true;
        public int NotificationRetryCount { get; set; } = 3;
        
        // Rate limiting
        public int MaxOrdersPerMinute { get; set; } = 100;
        public int MaxOrdersPerHour { get; set; } = 10000;
    }
    
    // Usage
    public class OrderService
    {
        private readonly IConfigurationService _config;
        
        public async Task<int> CreateOrderAsync(CreateOrderRequest request)
        {
            // Load configuration dynamically
            var maxItems = await _config.GetSettingAsync("MaxOrderItemCount", 100);
            var enableNewPipeline = await _config.GetSettingAsync("EnableNewOrderProcessing", false);
            
            if (request.Items.Count > maxItems)
                throw new ValidationException($"Too many items (max {maxItems})");
            
            if (enableNewPipeline)
                return await _newPipeline.ProcessAsync(request);
            
            return await _legacyPipeline.ProcessAsync(request);
        }
    }
}
```

---

## 2. Secrets Management

### 2.1 Azure Key Vault Integration

```csharp
// Never commit secrets to source control

namespace OrderManagement.Security
{
    public class SecretManagementSetup
    {
        public static void ConfigureSecrets(IConfigurationBuilder config, IHostEnvironment env)
        {
            var builtConfig = config.Build();
            
            if (env.IsProduction())
            {
                // Production: Use Azure Key Vault
                var keyVaultUrl = builtConfig["KeyVault:Url"];
                
                config.AddAzureKeyVault(
                    new Uri(keyVaultUrl),
                    new DefaultAzureCredential()  // Uses managed identity in Azure
                );
            }
            else if (env.IsStaging())
            {
                // Staging: Use development Key Vault
                var keyVaultUrl = builtConfig["KeyVault:StagingUrl"];
                
                config.AddAzureKeyVault(
                    new Uri(keyVaultUrl),
                    new DefaultAzureCredential()
                );
            }
            else
            {
                // Development: Use user secrets (local machine only)
                config.AddUserSecrets<Program>();
            }
        }
    }
    
    // Secrets to store in Key Vault
    public class SecretsConfiguration
    {
        public string DatabaseConnectionString { get; set; }
        public string PaymentGatewayApiKey { get; set; }
        public string JwtSigningKey { get; set; }
        public string CacheConnectionString { get; set; }
        public string ServiceBusConnectionString { get; set; }
        public string SendGridApiKey { get; set; }
        public string AwsAccessKey { get; set; }
        public string AwsSecretKey { get; set; }
        public string EncryptionKey { get; set; }
    }
    
    // Usage
    public class DatabaseConfiguration
    {
        public static void ConfigureDatabase(
            IServiceCollection services,
            IConfiguration config)
        {
            // Connection string loaded from Key Vault
            var connectionString = config.GetConnectionString("DefaultConnection");
            
            services.AddDbContext<OrderDbContext>(options =>
                options.UseSqlServer(connectionString)
            );
        }
    }
    
    // Audit logging of secret access
    public class SecretAccessAudit
    {
        private readonly ILogger<SecretAccessAudit> _logger;
        
        public string GetSecretWithAudit(string secretName)
        {
            _logger.LogInformation(
                "Secret accessed: {SecretName}, User: {User}, Time: {Time}",
                secretName,
                Thread.CurrentPrincipal?.Identity?.Name ?? "Unknown",
                DateTime.UtcNow
            );
            
            // Return secret from Key Vault
            return GetSecret(secretName);
        }
    }
}
```

### 2.2 Secret Rotation

```csharp
// Rotate secrets without downtime

namespace OrderManagement.Security
{
    public class SecretRotationService
    {
        private readonly IKeyVaultClient _keyVault;
        private readonly ILogger<SecretRotationService> _logger;
        
        // Run monthly to rotate database password
        public async Task RotateDatabasePasswordAsync()
        {
            _logger.LogInformation("Starting database password rotation");
            
            try
            {
                // Step 1: Generate new password
                var newPassword = GenerateSecurePassword();
                
                // Step 2: Update database password
                await UpdateDatabasePasswordAsync(newPassword);
                
                // Step 3: Store new password in Key Vault
                await _keyVault.SetSecretAsync(
                    "db-password-staging",
                    newPassword
                );
                
                // Step 4: Update connection string with new password
                await _keyVault.SetSecretAsync(
                    "DefaultConnection",
                    BuildConnectionString(newPassword)
                );
                
                // Step 5: Notify clients to reload configuration
                await NotifyApplicationsAsync("secret-rotated");
                
                _logger.LogInformation("Database password rotated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database password rotation failed");
                // Alert operations team
                throw;
            }
        }
        
        // Rotate API keys (quarterly)
        public async Task RotateApiKeysAsync()
        {
            var services = new[] { "Stripe", "PayPal", "SendGrid" };
            
            foreach (var service in services)
            {
                var oldKey = await _keyVault.GetSecretAsync($"{service}-api-key");
                var newKey = await GenerateNewApiKeyAsync(service);
                
                // Dual-key strategy: Keep both old and new for transition period
                await _keyVault.SetSecretAsync($"{service}-api-key", newKey);
                await _keyVault.SetSecretAsync($"{service}-api-key-old", oldKey);
                
                _logger.LogInformation("{Service} API key rotated", service);
            }
            
            // After 1 week, remove old keys
            // (ensure all clients switched to new key)
        }
        
        private string GenerateSecurePassword()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*";
            var random = new Random();
            var password = new string(
                Enumerable.Range(0, 32)
                    .Select(_ => chars[random.Next(chars.Length)])
                    .ToArray()
            );
            
            return password;
        }
    }
}
```

---

## 3. Backup and Disaster Recovery

### 3.1 Backup Strategy

```csharp
// Comprehensive backup strategy for all data

namespace OrderManagement.DisasterRecovery
{
    public class BackupStrategy
    {
        /*
        BACKUP HIERARCHY
        
        Database (Primary)
        ├─ Automatic backups (daily)
        │  └─ Retention: 35 days
        ├─ Transaction logs (every 5 min)
        │  └─ Point-in-time restore
        └─ Geo-replication (secondary region)
           └─ Automatic failover
        
        Storage Account (Blobs, Files)
        ├─ Snapshots (daily)
        │  └─ Retention: 30 days
        └─ Geo-redundant replication
           └─ Read-access from secondary region
        
        Configuration & Secrets
        ├─ Version history in Key Vault
        ├─ Backed up daily to storage
        └─ Encrypted at rest and in transit
        
        RTO: Recovery Time Objective
        └─ Maximum acceptable downtime: 1 hour
        
        RPO: Recovery Point Objective
        └─ Maximum acceptable data loss: 15 minutes
        */
    }
    
    public class DatabaseBackupConfiguration
    {
        public static void ConfigureAutoBackup(IServiceCollection services)
        {
            services.AddScoped<IBackupService, DatabaseBackupService>();
        }
    }
    
    public interface IBackupService
    {
        Task<BackupResult> CreateBackupAsync(string databaseName);
        Task<List<BackupInfo>> ListBackupsAsync(string databaseName);
        Task<bool> RestoreFromBackupAsync(string databaseName, string backupId);
        Task<bool> VerifyBackupAsync(string backupId);
    }
    
    public class DatabaseBackupService : IBackupService
    {
        private readonly SqlManagementClient _sqlClient;
        private readonly ILogger<DatabaseBackupService> _logger;
        
        public async Task<BackupResult> CreateBackupAsync(string databaseName)
        {
            _logger.LogInformation("Creating backup for database: {DatabaseName}", databaseName);
            
            try
            {
                // Azure SQL automatically creates backups
                // This example shows verification
                
                var backupInfo = new BackupInfo
                {
                    DatabaseName = databaseName,
                    BackupId = Guid.NewGuid().ToString(),
                    CreatedAt = DateTime.UtcNow,
                    SizeBytes = await GetDatabaseSizeAsync(databaseName),
                    Status = BackupStatus.Completed
                };
                
                // Store backup metadata
                await _sqlClient.RecordBackupAsync(backupInfo);
                
                return new BackupResult { Success = true, BackupId = backupInfo.BackupId };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup creation failed");
                return new BackupResult { Success = false, ErrorMessage = ex.Message };
            }
        }
        
        public async Task<bool> VerifyBackupAsync(string backupId)
        {
            _logger.LogInformation("Verifying backup: {BackupId}", backupId);
            
            try
            {
                // Attempt restore to test database to verify integrity
                var testDb = $"test-restore-{backupId}";
                
                // This would restore to a test database and verify
                // Implementation depends on backup technology
                
                _logger.LogInformation("Backup verification completed");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup verification failed");
                return false;
            }
        }
    }
    
    // Backup info model
    public class BackupInfo
    {
        public string DatabaseName { get; set; }
        public string BackupId { get; set; }
        public DateTime CreatedAt { get; set; }
        public long SizeBytes { get; set; }
        public BackupStatus Status { get; set; }
        public string Location { get; set; }  // Primary or secondary region
    }
    
    public enum BackupStatus
    {
        InProgress,
        Completed,
        Failed,
        Verified
    }
}
```

### 3.2 Disaster Recovery Plan

```csharp
// Tested disaster recovery procedures

namespace OrderManagement.DisasterRecovery
{
    public class DisasterRecoveryPlan
    {
        /*
        SCENARIO 1: Database Unavailable
        
        Detection:
        └─ Health checks fail for 2+ minutes
        
        Response:
        ├─ Failover to secondary region (automatic)
        ├─ Redirect traffic to secondary
        ├─ Notify team via PagerDuty
        ├─ Post status in Slack
        └─ Begin monitoring for full recovery
        
        Recovery:
        ├─ Investigate root cause
        ├─ Restore health to primary
        ├─ Failback to primary when ready
        └─ Post-mortem analysis
        
        ---
        
        SCENARIO 2: Corruption / Bad Deployment
        
        Detection:
        └─ Automated tests fail, data inconsistency detected
        
        Response:
        ├─ Rollback to previous version (1 minute)
        ├─ Restore database from backup (5-10 minutes)
        ├─ Verify data integrity
        ├─ Resume normal operations
        └─ Investigate what went wrong
        
        ---
        
        SCENARIO 3: Ransomware / Data Breach
        
        Detection:
        └─ Unusual activity detected, data access audits
        
        Response:
        ├─ Isolate affected systems (air-gap)
        ├─ Preserve evidence
        ├─ Activate backup/recovery (offline backups)
        ├─ Notify customers and authorities
        ├─ Restore from clean backup
        └─ Incident investigation
        */
    }
    
    public class DisasterRecoveryWorkflow
    {
        private readonly IBackupService _backupService;
        private readonly ILogger<DisasterRecoveryWorkflow> _logger;
        
        // Automated failover
        public async Task<bool> FailoverToSecondaryAsync()
        {
            _logger.LogCritical("INITIATING FAILOVER TO SECONDARY REGION");
            
            try
            {
                // 1. Check secondary region is healthy
                var secondaryHealthy = await VerifySecondaryHealthAsync();
                if (!secondaryHealthy)
                {
                    _logger.LogError("Secondary region not healthy, failover aborted");
                    return false;
                }
                
                // 2. Update DNS/load balancer to point to secondary
                await UpdateDnsAsync("secondary-region");
                
                // 3. Notify all services
                await NotifyServicesAsync("failover-initiated");
                
                // 4. Begin monitoring secondary
                _logger.LogCritical("Failover to secondary completed");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failover failed - MANUAL INTERVENTION REQUIRED");
                return false;
            }
        }
        
        // Database point-in-time restore
        public async Task<bool> RestoreToPointInTimeAsync(DateTime targetTime)
        {
            _logger.LogWarning(
                "Restoring database to point in time: {TargetTime}",
                targetTime
            );
            
            try
            {
                // Create new database from backup at target time
                var newDbName = $"restored-{Guid.NewGuid().ToString().Substring(0, 8)}";
                
                // This brings up a new database with old data
                // Old application servers point to restored database
                // New servers still run on original (to compare)
                
                await VerifyRestoredDataAsync(newDbName);
                
                _logger.LogInformation("Database restored successfully");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore failed");
                return false;
            }
        }
        
        // Test disaster recovery (monthly)
        public async Task<bool> TestDisasterRecoveryAsync()
        {
            _logger.LogInformation("Starting DR test");
            
            try
            {
                // 1. Create test environment
                var testDb = await CreateTestEnvironmentAsync();
                
                // 2. Restore from backup
                await RestoreDatabaseAsync(testDb);
                
                // 3. Run smoke tests
                var testsPass = await RunSmokeTestsAsync(testDb);
                
                if (!testsPass)
                {
                    _logger.LogError("DR test failed - smoke tests did not pass");
                    return false;
                }
                
                // 4. Cleanup
                await CleanupTestEnvironmentAsync(testDb);
                
                _logger.LogInformation("DR test completed successfully");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DR test failed");
                return false;
            }
        }
    }
}
```

---

## 4. Security Hardening

### 4.1 API Security

```csharp
// Secure API endpoints against common attacks

namespace OrderManagement.Security
{
    public class ApiSecurityConfiguration
    {
        public static void ConfigureSecurity(IApplicationBuilder app)
        {
            // HTTPS only
            app.UseHttpsRedirection();
            
            // HSTS: Force HTTPS for future requests
            app.UseHsts();
            
            // X-Frame-Options: Prevent clickjacking
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Add("X-Frame-Options", "DENY");
                await next();
            });
            
            // X-Content-Type-Options: Prevent MIME sniffing
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
                await next();
            });
            
            // Content-Security-Policy: Prevent XSS
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Add(
                    "Content-Security-Policy",
                    "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'"
                );
                await next();
            });
        }
    }
    
    // Rate limiting
    public class RateLimitConfiguration
    {
        public static void ConfigureRateLimiting(IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                
                // Per-IP rate limit: 100 requests per minute
                options.AddFixedWindowLimiter(
                    policyName: "ip-limit",
                    configureOptions: limitOptions =>
                    {
                        limitOptions.Window = TimeSpan.FromMinutes(1);
                        limitOptions.PermitLimit = 100;
                        limitOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                        limitOptions.QueueLimit = 2;
                    }
                );
                
                // Per-user rate limit: 1000 requests per hour
                options.AddSlidingWindowLimiter(
                    policyName: "user-limit",
                    configureOptions: limitOptions =>
                    {
                        limitOptions.Window = TimeSpan.FromHours(1);
                        limitOptions.SegmentsPerWindow = 10;
                        limitOptions.PermitLimit = 1000;
                        limitOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                        limitOptions.QueueLimit = 5;
                    }
                );
            });
        }
    }
    
    [ApiController]
    [Route("api/orders")]
    public class SecureOrdersController : ControllerBase
    {
        [HttpPost]
        [RequireRateLimit("ip-limit")]  // Rate limit per IP
        public async Task<ActionResult<int>> CreateOrder(
            [FromBody] CreateOrderRequest request)
        {
            // Validate input
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            
            // Sanitize input
            request.CustomerName = SanitizeInput(request.CustomerName);
            
            // Only allow user to create orders for themselves
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.Parse(userId) != request.CustomerId)
                return Forbid();
            
            return Ok();
        }
        
        private string SanitizeInput(string input)
        {
            // Remove dangerous characters
            return System.Web.HttpUtility.HtmlEncode(input);
        }
    }
}
```

### 4.2 Data Protection

```csharp
// Encrypt sensitive data at rest and in transit

namespace OrderManagement.Security
{
    public class DataProtectionConfiguration
    {
        public static void ConfigureDataProtection(IServiceCollection services)
        {
            // DPAPI (Data Protection API)
            services.AddDataProtection()
                // Store keys in Azure Key Vault (not local filesystem)
                .PersistKeysToAzureBlobStorage(
                    new Uri("https://storage.blob.core.windows.net/keys"),
                    new DefaultAzureCredential()
                )
                // Protect keys with Azure Key Vault
                .ProtectKeysWithAzureKeyVault(
                    new Uri("https://keyvault.azure.net/keys/dataprotection"),
                    new DefaultAzureCredential()
                );
        }
    }
    
    // Encrypt sensitive fields
    public class SensitiveDataEncryption
    {
        private readonly IDataProtector _protector;
        
        public SensitiveDataEncryption(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("OrderManagement.Payment");
        }
        
        public string EncryptCardNumber(string cardNumber)
        {
            return _protector.Protect(cardNumber);
        }
        
        public string DecryptCardNumber(string encrypted)
        {
            return _protector.Unprotect(encrypted);
        }
    }
    
    // Database field encryption (at application level)
    public class EncryptedOrder
    {
        public int Id { get; set; }
        
        // Sensitive field encrypted before storage
        [EncryptedProperty]
        public string PaymentCardToken { get; set; }
        
        [EncryptedProperty]
        public string CustomerSsn { get; set; }
    }
    
    // Custom encryption attribute
    [AttributeUsage(AttributeTargets.Property)]
    public class EncryptedPropertyAttribute : Attribute { }
}
```

---

## 5. Monitoring and Observability

### 5.1 Health Checks and Probes

```csharp
// Kubernetes-style health checks

namespace OrderManagement.Health
{
    public class HealthCheckConfiguration
    {
        public static void ConfigureHealthChecks(IServiceCollection services)
        {
            services.AddHealthChecks()
                // Database connectivity
                .AddDbContextCheck<OrderDbContext>(
                    name: "database",
                    tags: new[] { "critical" }
                )
                // Redis connectivity
                .AddStackExchangeRedisCheck(
                    "localhost:6379",
                    name: "cache",
                    tags: new[] { "non-critical" }
                )
                // External service connectivity
                .AddCheck<PaymentGatewayHealthCheck>(
                    name: "payment-gateway",
                    tags: new[] { "external" }
                )
                // Application startup complete
                .AddCheck<StartupCompleteHealthCheck>(
                    name: "startup",
                    tags: new[] { "startup" }
                );
        }
    }
    
    [ApiController]
    [Route("health")]
    public class HealthController : ControllerBase
    {
        private readonly HealthCheckService _healthCheckService;
        
        /// <summary>
        /// Liveness probe: Is the application process running?
        /// Used by orchestrators to restart crashed containers
        /// Should respond quickly (no external dependencies)
        /// </summary>
        [HttpGet("live")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Live()
        {
            var result = await _healthCheckService.CheckHealthAsync(
                new HealthCheckFilter { Tags = new[] { "liveness" } }
            );
            
            return result.Status == HealthStatus.Healthy
                ? Ok("Live")
                : StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        
        /// <summary>
        /// Readiness probe: Is the application ready to handle traffic?
        /// Used by load balancers to route traffic
        /// Checks all dependencies (database, cache, etc.)
        /// </summary>
        [HttpGet("ready")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Ready()
        {
            var result = await _healthCheckService.CheckHealthAsync(
                new HealthCheckFilter { Tags = new[] { "critical" } }
            );
            
            if (result.Status != HealthStatus.Healthy)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new { status = result.Status, details = result.Entries }
                );
            }
            
            return Ok(new { status = "Ready" });
        }
        
        /// <summary>
        /// Startup probe: Has the application completed initialization?
        /// Used before readiness probe to give app time to start
        /// </summary>
        [HttpGet("startup")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Startup()
        {
            var result = await _healthCheckService.CheckHealthAsync(
                new HealthCheckFilter { Tags = new[] { "startup" } }
            );
            
            return result.Status == HealthStatus.Healthy
                ? Ok("Started")
                : StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
    
    // Custom health check
    public class PaymentGatewayHealthCheck : IHealthCheck
    {
        private readonly IPaymentService _paymentService;
        
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Quick connectivity check (not a real transaction)
                var isHealthy = await _paymentService.PingAsync();
                
                return isHealthy
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy("Payment gateway not responding");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy(
                    "Payment gateway check failed",
                    exception: ex
                );
            }
        }
    }
}
```

### 5.2 Structured Logging for Observability

```csharp
// Structured logs enable searching and aggregation

namespace OrderManagement.Logging
{
    public class StructuredLoggingSetup
    {
        public static void ConfigureLogging(ILoggingBuilder logging, IConfiguration config)
        {
            logging.ClearProviders();
            
            logging.AddSerilog(new LoggerConfiguration()
                // Write to console
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                // Write to files
                .WriteTo.File(
                    path: "logs/application-.txt",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                // Write to Application Insights
                .WriteTo.ApplicationInsights(
                    new TelemetryClient(),
                    TelemetryConverter.Traces
                )
                // Enrich logs with context
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "OrderManagement")
                .MinimumLevel.Debug()
                .Build()
            );
        }
    }
    
    // Structured logging in code
    public class OrderService
    {
        private readonly ILogger<OrderService> _logger;
        
        public async Task<int> CreateOrderAsync(CreateOrderRequest request)
        {
            using (_logger.BeginScope(new { UserId = request.CustomerId }))
            {
                _logger.LogInformation(
                    "Creating order. Items: {ItemCount}, Total: {Total}",
                    request.Items.Count,
                    request.Total
                );
                
                try
                {
                    var orderId = await _repository.SaveAsync(request.ToEntity());
                    
                    _logger.LogInformation(
                        "Order created successfully. OrderId: {OrderId}",
                        orderId
                    );
                    
                    return orderId;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to create order. Items: {ItemCount}",
                        request.Items.Count
                    );
                    
                    throw;
                }
            }
        }
    }
    
    // Query logs easily
    /*
    Kusto Query Language (Application Insights):
    
    // Find all failed orders
    traces
    | where message startswith "Failed to create order"
    | where severityLevel == 2  // Error
    
    // Performance analysis
    traces
    | where message contains "Order created successfully"
    | extend duration = todouble(customDimensions.DurationMs)
    | summarize avg(duration), max(duration) by bin(timestamp, 5m)
    
    // Error trends
    traces
    | where severityLevel >= 2  // Error or worse
    | summarize count() by tostring(customDimensions.ErrorType)
    */
}
```

---

## Summary

Production readiness requires comprehensive preparation:

**Configuration Management:**
- Feature flags for safe releases
- Externalized configuration
- No hardcoded values
- Runtime updates without deployment

**Secrets Management:**
- Never commit secrets
- Use Key Vault for storage
- Automatic rotation
- Audit trail

**Backup & Recovery:**
- Automated daily backups
- Point-in-time restore capability
- Tested disaster recovery
- RTO < 1 hour, RPO < 15 minutes

**Security Hardening:**
- HTTPS only with HSTS
- Input validation and sanitization
- Rate limiting
- Data encryption at rest
- PII protection

**Health & Observability:**
- Liveness, readiness, startup probes
- Structured logging
- Health checks for all dependencies
- Metric collection and alerting

**Key Metrics:**
- Mean time to recovery (< 1 hour)
- Backup success rate (100%)
- Security scan pass rate (100%)
- Log data retention (2+ years)
- Configuration change frequency

**Best Practices:**
- Test disaster recovery monthly
- Rotate secrets quarterly
- Update security patches weekly
- Review and analyze logs daily
- Automate everything possible
- Have runbooks for common issues
- Document everything

**Common Mistakes:**
- Hardcoded configuration
- Missing health checks
- Untested disaster recovery
- Insecure secret storage
- No audit logging
- Insufficient backup retention
- Manual operational procedures

This concludes the enterprise fundamentals. The final topic covers real-world case studies and advanced scenarios.
