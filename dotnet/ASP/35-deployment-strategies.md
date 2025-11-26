# 34. Deployment Strategies

## Overview
Deployment strategies determine how applications reach production and how traffic transitions from old versions to new. The choice affects risk, speed, and ability to roll back. Enterprise systems require zero-downtime deployments with careful coordination.

---

## 1. Deployment Fundamentals

### 1.1 Deployment Goals

```
RELIABILITY    → Minimize downtime, ensure rollback capability
SAFETY        → Test changes before full deployment
SPEED         → Deploy frequently, reduce deployment duration
VISIBILITY    → Monitor health during deployment
REVERSIBILITY → Quick rollback if issues detected

Tradeoff Matrix:
Speed ←→ Safety
Blue/Green: Safe, slower
Canary:     Balanced
Rolling:    Fast, less safe
```

### 1.2 Infrastructure Readiness

```csharp
// Health check endpoint (required for all deployment strategies)
[ApiController]
[Route("/health")]
public class HealthCheckController : ControllerBase
{
    private readonly IHealthCheckService _healthCheck;
    
    [HttpGet("live")]
    public async Task<IActionResult> Liveness()
    {
        // Is application process running?
        return Ok(new { status = "alive" });
    }
    
    [HttpGet("ready")]
    public async Task<IActionResult> Readiness()
    {
        // Can application handle requests?
        var dbHealthy = await _healthCheck.IsDatabaseHealthyAsync();
        var cacheHealthy = await _healthCheck.IsCacheHealthyAsync();
        var dependenciesHealthy = await _healthCheck.AreExternalDependenciesHealthyAsync();
        
        if (dbHealthy && cacheHealthy && dependenciesHealthy)
        {
            return Ok(new { status = "ready" });
        }
        else
        {
            return StatusCode(503, new 
            { 
                status = "not_ready",
                details = new
                {
                    database = dbHealthy,
                    cache = cacheHealthy,
                    dependencies = dependenciesHealthy
                }
            });
        }
    }
    
    [HttpGet("startup")]
    public async Task<IActionResult> Startup()
    {
        // Has application completed startup tasks?
        var startupComplete = await _healthCheck.IsStartupCompleteAsync();
        
        return startupComplete 
            ? Ok(new { status = "started" })
            : StatusCode(503, new { status = "starting" });
    }
}

// Startup configuration
public void ConfigureHealthChecks(IServiceCollection services)
{
    services.AddHealthChecks()
        .AddDbContextCheck<ApplicationDbContext>()
        .AddCheck<RedisHealthCheck>("redis")
        .AddCheck<ExternalServiceHealthCheck>("external-services");
}
```

---

## 2. Blue-Green Deployment

### 2.1 Blue-Green Strategy

```
BLUE (Current Production)        GREEN (New Version)
┌──────────────────────┐        ┌──────────────────────┐
│ v1.0.0 (Active)      │        │ v1.1.0 (Standby)     │
│ - All production      │        │ - Fully tested       │
│   traffic            │        │ - Warmed up          │
│ - Full load          │        │ - Ready to switch    │
└──────────────────────┘        └──────────────────────┘
        │                                 │
        └─────────────┬───────────────────┘
                      │
                   Switch
                      │
        ┌─────────────┴───────────────────┐
        │                                 │
    GREEN (Now Active)              BLUE (Standby)
    v1.1.0                          v1.0.0
    
    If issues → Switch back to BLUE immediately
```

### 2.2 Blue-Green Implementation

```csharp
public class BlueGreenDeploymentService
{
    private readonly ILoadBalancer _loadBalancer;
    private readonly IHealthCheckService _healthCheck;
    private readonly ILogger<BlueGreenDeploymentService> _logger;
    
    public async Task SwitchToGreenAsync()
    {
        // Phase 1: Verify green health
        _logger.LogInformation("Checking green environment health");
        
        if (!await _healthCheck.IsEnvironmentHealthyAsync("green"))
        {
            throw new DeploymentException("Green environment not healthy");
        }
        
        // Phase 2: Warm up green (optional)
        _logger.LogInformation("Warming up green environment");
        await _healthCheck.WarmUpAsync("green");
        
        // Phase 3: Switch traffic
        _logger.LogInformation("Switching traffic from blue to green");
        
        try
        {
            await _loadBalancer.SetActiveTargetAsync("green");
            _logger.LogInformation("Successfully switched to green");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch to green, rolling back");
            
            // Immediate rollback
            await _loadBalancer.SetActiveTargetAsync("blue");
            throw;
        }
        
        // Phase 4: Monitor green
        _logger.LogInformation("Monitoring green environment");
        
        var monitorTask = MonitorEnvironmentAsync("green", TimeSpan.FromMinutes(5));
        if (!await monitorTask)
        {
            _logger.LogError("Green environment unhealthy, rolling back");
            
            await _loadBalancer.SetActiveTargetAsync("blue");
            throw new DeploymentException("Green environment failed health check");
        }
        
        // Phase 5: Blue becomes standby
        _logger.LogInformation("Blue is now standby, ready for next deployment");
    }
    
    private async Task<bool> MonitorEnvironmentAsync(string environment, TimeSpan duration)
    {
        var endTime = DateTime.UtcNow.Add(duration);
        
        while (DateTime.UtcNow < endTime)
        {
            var healthy = await _healthCheck.IsEnvironmentHealthyAsync(environment);
            
            if (!healthy)
            {
                return false;
            }
            
            var errorRate = await GetErrorRateAsync(environment);
            if (errorRate > 0.05m)  // 5% error threshold
            {
                _logger.LogWarning(
                    "High error rate in {Environment}: {ErrorRate:P}",
                    environment,
                    errorRate
                );
                
                return false;
            }
            
            await Task.Delay(TimeSpan.FromSeconds(30));
        }
        
        return true;
    }
}

// Advantages:
// - Instant rollback (just switch traffic)
// - Full testing before switch
// - Zero downtime
// - Complete environment isolation

// Disadvantages:
// - Double infrastructure cost
// - Database schema migration complexity
// - More to monitor
```

### 2.3 Blue-Green with Database Migration

```csharp
public class BlueGreenWithMigrationService
{
    private readonly IDatabase _database;
    private readonly ILoadBalancer _loadBalancer;
    
    public async Task SwitchWithDatabaseMigrationAsync()
    {
        // Strategy: Shared database, versioned code
        
        // Phase 1: Deploy new code to green (read old schema)
        await DeployAsync("green", "v1.1.0");
        
        // Phase 2: New schema changes must be backward compatible
        // - Add columns with defaults
        // - Mark old columns as deprecated
        // - Deploy code that reads new, writes both
        
        await _database.MigrateAsync(new AddNewColumnsUsingDefaults());
        
        // Phase 3: Code update: Read from new columns
        await DeployAsync("green", "v1.1.1-with-new-reads");
        
        // Phase 4: Test thoroughly
        var testsPassed = await RunIntegrationTestsAsync("green");
        if (!testsPassed) throw new Exception("Tests failed");
        
        // Phase 5: Switch traffic
        await _loadBalancer.SetActiveTargetAsync("green");
        
        // Phase 6: Cleanup old columns (in next deployment)
        // This is deferred to avoid rollback issues
    }
}

// Database migration pattern:
/*
v1.0.0 (Blue)
├─ Reads: column_a
└─ Writes: column_a

v1.1.0 Migration
├─ Add column_b with DEFAULT value
└─ Column_a still exists

v1.1.0 (Green)
├─ Reads: column_b
├─ Writes: column_b
└─ Writes: column_a (for rollback)

v1.1.0 (Deployment succeeds)
├─ Green is new Blue
└─ Blue is standby (still using column_a)

If rollback needed:
├─ Switch to Blue
└─ Blue still writes column_a, so no data loss

v1.2.0 (Remove column_a)
├─ Only after Blue never uses it
└─ Wait at least 1 deployment cycle
*/
```

---

## 3. Rolling Deployment

### 3.1 Rolling Deployment Strategy

```
Instance 1: v1.0.0 → v1.1.0 (1/4)
Instance 2: v1.0.0 → v1.1.0 (2/4)  
Instance 3: v1.0.0 → v1.1.0 (3/4)
Instance 4: v1.0.0 → v1.1.0 (4/4)

Timeline:
┌─────────────┬──────────────┬──────────────┬──────────────┐
│ Inst 1: Old │ Inst 1: New  │              │              │
│ Inst 2: Old │ Inst 2: Old  │ Inst 2: New  │              │
│ Inst 3: Old │ Inst 3: Old  │ Inst 3: Old  │ Inst 3: New  │
│ Inst 4: Old │ Inst 4: Old  │ Inst 4: Old  │ Inst 4: New  │
└─────────────┴──────────────┴──────────────┴──────────────┘

At any moment: Mix of old and new versions running
Require: Backward/forward compatibility
```

### 3.2 Rolling Deployment Implementation

```csharp
public class RollingDeploymentOrchestrator
{
    private readonly IKubernetesClient _kubernetes;
    private readonly IHealthCheckService _healthCheck;
    private readonly ILogger<RollingDeploymentOrchestrator> _logger;
    
    public async Task DeployAsync(
        string deployment,
        string newImage,
        int maxUnavailable = 1,
        int maxSurge = 1)
    {
        _logger.LogInformation(
            "Starting rolling deployment: {Deployment}, Image: {Image}",
            deployment,
            newImage
        );
        
        // Kubernetes handles rolling deployment automatically
        var rolloutStrategy = new RollingUpdate
        {
            MaxUnavailable = maxUnavailable,     // Keep at least (replicas - 1) running
            MaxSurge = maxSurge                  // Allow at most (replicas + 1) for surge
        };
        
        await _kubernetes.UpdateDeploymentAsync(new DeploymentUpdate
        {
            Name = deployment,
            NewImage = newImage,
            Strategy = rolloutStrategy
        });
        
        // Monitor rollout
        var watches = _kubernetes.WatchRolloutAsync(deployment);
        
        await foreach (var status in watches)
        {
            _logger.LogInformation(
                "Rollout status: {Ready}/{Desired}, Updated: {Updated}",
                status.ReadyReplicas,
                status.DesiredReplicas,
                status.UpdatedReplicas
            );
            
            // Check health at each step
            if (!await _healthCheck.IsDeploymentHealthyAsync(deployment))
            {
                _logger.LogError("Deployment unhealthy during rollout, rolling back");
                await _kubernetes.RollbackDeploymentAsync(deployment);
                throw new DeploymentException("Rolling update failed health check");
            }
        }
        
        _logger.LogInformation("Rolling deployment completed successfully");
    }
}

// Kubernetes YAML
/*
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
spec:
  replicas: 4
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxUnavailable: 1      # Always keep 3+ instances
      maxSurge: 1            # Allow up to 5 instances during upgrade
  selector:
    matchLabels:
      app: order-service
  template:
    metadata:
      labels:
        app: order-service
    spec:
      containers:
      - name: order-service
        image: bookstore/order-service:1.1.0
        lifecycle:
          preStop:
            exec:
              command: ["/bin/sh", "-c", "sleep 15"]  # Graceful shutdown delay
        livenessProbe:
          httpGet:
            path: /health/live
            port: 80
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 80
          initialDelaySeconds: 5
          periodSeconds: 5
*/

// Advantages:
// - Gradual rollout reduces risk
// - No double infrastructure
// - Cost efficient
// - Automatic rollback support

// Disadvantages:
// - Temporary mix of versions (compatibility risk)
// - Slower full deployment
// - Harder to rollback once further updates deployed
```

### 3.3 Backward Compatibility During Rolling Deployment

```csharp
// API versioning for rolling deployments

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IMediator _mediator;
    
    // v1 endpoint: Still supported for old instances
    [HttpPost("v1")]
    public async Task<IActionResult> CreateOrderV1(CreateOrderRequestV1 request)
    {
        // Parse v1 format, convert to internal representation
        var command = MapV1ToCommand(request);
        
        var result = await _mediator.Send(command);
        
        return CreatedAtAction(nameof(GetOrderV1), new { id = result.Id }, result);
    }
    
    // v2 endpoint: New instances use this
    [HttpPost("v2")]
    public async Task<IActionResult> CreateOrderV2(CreateOrderRequestV2 request)
    {
        var command = MapV2ToCommand(request);
        
        var result = await _mediator.Send(command);
        
        return CreatedAtAction(nameof(GetOrderV2), new { id = result.Id }, result);
    }
    
    // Intelligent routing: Client unaware of version
    [HttpPost]
    public async Task<IActionResult> CreateOrder(
        [FromHeader(Name = "X-API-Version")] string apiVersion,
        [FromBody] dynamic request)
    {
        return apiVersion switch
        {
            "1.0" => await CreateOrderV1(JsonConvert.DeserializeObject<CreateOrderRequestV1>(
                JsonConvert.SerializeObject(request)
            )),
            "2.0" => await CreateOrderV2(JsonConvert.DeserializeObject<CreateOrderRequestV2>(
                JsonConvert.SerializeObject(request)
            )),
            _ => await CreateOrderV2(JsonConvert.DeserializeObject<CreateOrderRequestV2>(
                JsonConvert.SerializeObject(request)
            ))
        };
    }
}

// Old instance (v1.0.0):
// - Sends CreateOrderRequestV1
// - New instance handles with v1 path
// - Works fine

// New instance (v1.1.0):
// - Sends CreateOrderRequestV2
// - New instance handles with v2 path
// - Works fine
```

---

## 4. Canary Deployment

### 4.1 Canary Deployment Strategy

```
PRODUCTION (Old: v1.0.0)
├─ 99% traffic → Stable version
└─ 1% traffic → New version (Canary)
        ↓
    Monitor metrics
        ↓
    Issues detected? → Rollback immediately
    Metrics good? → Gradually increase traffic
        ↓
PRODUCTION (New: v1.1.0)
├─ 95% traffic → v1.1.0
└─ 5% traffic → v1.1.0 (monitor phase)
        ↓
    All traffic to v1.1.0
```

### 4.2 Canary Implementation

```csharp
public class CanaryDeploymentService
{
    private readonly ILoadBalancer _loadBalancer;
    private readonly IMetricsService _metrics;
    private readonly ILogger<CanaryDeploymentService> _logger;
    
    public async Task DeployCanaryAsync(
        string serviceName,
        string newImage,
        decimal startTrafficPercentage = 0.01m)  // Start with 1%
    {
        _logger.LogInformation(
            "Starting canary deployment for {Service} with image {Image}",
            serviceName,
            newImage
        );
        
        // Phase 1: Deploy canary version
        await DeployCanaryInstanceAsync(serviceName, newImage);
        
        // Phase 2: Shift small percentage of traffic
        var currentTraffic = startTrafficPercentage;
        
        while (currentTraffic < 1.0m)
        {
            _logger.LogInformation(
                "Canary traffic: {Traffic:P}",
                currentTraffic
            );
            
            // Route traffic to canary
            await _loadBalancer.SetCanaryTrafficAsync(
                serviceName,
                currentTraffic
            );
            
            // Monitor canary health
            var canaryHealthy = await MonitorCanaryAsync(
                serviceName,
                TimeSpan.FromMinutes(5)
            );
            
            if (!canaryHealthy)
            {
                _logger.LogError("Canary unhealthy, rolling back");
                await _loadBalancer.SetCanaryTrafficAsync(serviceName, 0);
                await DeleteCanaryAsync(serviceName);
                throw new DeploymentException("Canary deployment failed");
            }
            
            // Gradually increase traffic
            currentTraffic = Math.Min(currentTraffic * 2, 1.0m);
        }
        
        // Phase 3: Canary is now production
        _logger.LogInformation("Canary deployment completed successfully");
        await PromoteCanaryToProductionAsync(serviceName);
    }
    
    private async Task<bool> MonitorCanaryAsync(
        string serviceName,
        TimeSpan duration)
    {
        var endTime = DateTime.UtcNow.Add(duration);
        
        while (DateTime.UtcNow < endTime)
        {
            var metrics = await _metrics.GetMetricsAsync(
                serviceName,
                TimeSpan.FromMinutes(1)
            );
            
            // Check error rate
            if (metrics.ErrorRate > 0.01m)  // 1% threshold
            {
                _logger.LogWarning(
                    "Canary error rate high: {ErrorRate:P}",
                    metrics.ErrorRate
                );
                
                return false;
            }
            
            // Check latency (p95)
            if (metrics.LatencyP95 > TimeSpan.FromSeconds(2))
            {
                _logger.LogWarning(
                    "Canary latency high: {Latency}ms",
                    metrics.LatencyP95.TotalMilliseconds
                );
                
                return false;
            }
            
            // Check exception rate
            if (metrics.ExceptionRate > 0.001m)  // 0.1% threshold
            {
                return false;
            }
            
            await Task.Delay(TimeSpan.FromSeconds(30));
        }
        
        return true;
    }
}

// Advantages:
// - Real production traffic test
// - Catch issues before full rollout
// - Automatic rollback capability
// - Minimal blast radius (1% affected)
// - Cost efficient (no double infrastructure)

// Disadvantages:
// - Complex to implement
// - Requires sophisticated monitoring
// - Longer deployment time
// - Network complexity (traffic splitting)
```

### 4.3 Canary with Feature Flags

```csharp
// Alternative: Use feature flags instead of traffic percentage

public class FeatureFlagCanary
{
    private readonly IFeatureFlagService _featureFlags;
    private readonly IMetricsService _metrics;
    
    public async Task DeployWithFeatureFlagAsync(string serviceName, string newImage)
    {
        // Deploy new code everywhere
        await DeployAsync(serviceName, newImage);
        
        // But new features are disabled initially
        await _featureFlags.SetFeatureFlagAsync("new-order-processing", false);
        
        // Phase 1: Enable for 1% of users
        await _featureFlags.SetFeatureFlagRolloutAsync(
            "new-order-processing",
            percentage: 0.01m,
            userIdHash: true  // Consistent per user
        );
        
        // Monitor metrics
        if (await MetricsHealthy())
        {
            // Phase 2: Enable for 10% of users
            await _featureFlags.SetFeatureFlagRolloutAsync(
                "new-order-processing",
                percentage: 0.10m
            );
        }
        
        // Continue rolling out...
        // Eventually 100%
    }
}
```

---

## 5. Deployment Orchestration

### 5.1 Infrastructure as Code

```yaml
# Terraform: Infrastructure provisioning
resource "aws_autoscaling_group" "order_service" {
  name                = "order-service-asg"
  max_size            = 10
  min_size            = 2
  desired_capacity    = 4
  availability_zones  = ["us-east-1a", "us-east-1b"]
  health_check_type   = "ELB"
  health_check_grace_period = 60
  
  launch_template {
    id      = aws_launch_template.order_service.id
    version = "$Latest"
  }
  
  tag {
    key                 = "Name"
    value               = "order-service"
    propagate_at_launch = true
  }
}

resource "aws_lb" "order_service" {
  name               = "order-service-nlb"
  internal           = false
  load_balancer_type = "network"  # Network LB for high throughput
  
  enable_deletion_protection = true
  
  tags = {
    Name = "order-service"
  }
}
```

### 5.2 CI/CD Pipeline

```yaml
# GitHub Actions: Automated deployment pipeline
name: Deploy to Production

on:
  push:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v3
      
      - name: Build
        run: dotnet build --configuration Release
      
      - name: Test
        run: dotnet test --configuration Release
      
      - name: Build Docker Image
        run: docker build -t bookstore/order-service:${{ github.sha }} .
      
      - name: Push to Registry
        run: |
          docker login -u ${{ secrets.DOCKER_USER }} -p ${{ secrets.DOCKER_PASS }}
          docker push bookstore/order-service:${{ github.sha }}
  
  deploy-staging:
    needs: build
    runs-on: ubuntu-latest
    
    steps:
      - name: Deploy to Staging
        run: |
          kubectl set image deployment/order-service \
            order-service=bookstore/order-service:${{ github.sha }} \
            -n staging
      
      - name: Wait for Rollout
        run: |
          kubectl rollout status deployment/order-service \
            -n staging --timeout=5m
      
      - name: Run Integration Tests
        run: |
          dotnet test IntegrationTests.csproj \
            --environment staging
  
  deploy-production-canary:
    needs: deploy-staging
    runs-on: ubuntu-latest
    
    steps:
      - name: Deploy Canary
        run: |
          kubectl set image deployment/order-service-canary \
            order-service=bookstore/order-service:${{ github.sha }} \
            -n production
      
      - name: Monitor Canary Metrics
        run: |
          ./scripts/monitor-canary.sh 300  # Monitor for 5 minutes
      
      - name: Promote to Production
        if: success()
        run: |
          kubectl set image deployment/order-service \
            order-service=bookstore/order-service:${{ github.sha }} \
            -n production
```

---

## 6. Rollback Strategies

### 6.1 Automatic Rollback

```csharp
public class AutomaticRollbackService
{
    private readonly IDeploymentService _deploymentService;
    private readonly IHealthCheckService _healthCheck;
    private readonly IMetricsService _metrics;
    private readonly ILogger<AutomaticRollbackService> _logger;
    
    public async Task<bool> MonitorAndRollbackIfNeededAsync(
        string deployment,
        string previousVersion,
        TimeSpan monitorDuration)
    {
        var endTime = DateTime.UtcNow.Add(monitorDuration);
        var issueDetectedAt = (DateTime?)null;
        
        while (DateTime.UtcNow < endTime)
        {
            var health = await _healthCheck.GetHealthAsync(deployment);
            var metrics = await _metrics.GetMetricsAsync(deployment);
            
            // Check health
            if (health.Status != HealthStatus.Healthy)
            {
                if (issueDetectedAt == null)
                {
                    issueDetectedAt = DateTime.UtcNow;
                }
                
                // Grace period: Wait 30 seconds in case it's transient
                if (DateTime.UtcNow - issueDetectedAt > TimeSpan.FromSeconds(30))
                {
                    _logger.LogError(
                        "Health check failed for {Deployment}, initiating rollback",
                        deployment
                    );
                    
                    await RollbackAsync(deployment, previousVersion);
                    return false;
                }
            }
            else
            {
                issueDetectedAt = null;  // Reset grace period
            }
            
            // Check error rate
            if (metrics.ErrorRate > 0.05m)  // 5%
            {
                _logger.LogError(
                    "High error rate {ErrorRate:P} for {Deployment}, rolling back",
                    metrics.ErrorRate,
                    deployment
                );
                
                await RollbackAsync(deployment, previousVersion);
                return false;
            }
            
            // Check latency spike
            if (metrics.LatencyP99 > TimeSpan.FromSeconds(10))
            {
                _logger.LogError(
                    "High latency {Latency}ms for {Deployment}, rolling back",
                    metrics.LatencyP99.TotalMilliseconds,
                    deployment
                );
                
                await RollbackAsync(deployment, previousVersion);
                return false;
            }
            
            await Task.Delay(TimeSpan.FromSeconds(30));
        }
        
        _logger.LogInformation(
            "Deployment {Deployment} monitored successfully for {Duration}",
            deployment,
            monitorDuration
        );
        
        return true;
    }
    
    private async Task RollbackAsync(string deployment, string previousVersion)
    {
        _logger.LogInformation(
            "Rolling back {Deployment} to version {Version}",
            deployment,
            previousVersion
        );
        
        try
        {
            await _deploymentService.RollbackAsync(deployment, previousVersion);
            
            // Verify rollback successful
            await Task.Delay(TimeSpan.FromSeconds(30));
            
            if (!await _healthCheck.IsHealthyAsync(deployment))
            {
                throw new RollbackFailedException(
                    $"Rollback failed for {deployment}: Health check failed"
                );
            }
            
            _logger.LogInformation(
                "Rollback completed successfully for {Deployment}",
                deployment
            );
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "Failed to rollback {Deployment}, manual intervention required",
                deployment
            );
            
            throw;
        }
    }
}
```

### 6.2 Database Rollback Considerations

```csharp
// Forward-only migrations: Never rollback database schema

public class DatabaseMigrationPolicy
{
    // ✅ GOOD: Can rollback
    public static void AddNewOptionalColumn(IMigrationBuilder m)
    {
        // New column with default - old code doesn't write it, doesn't crash
        m.AddColumn<string>("new_field", defaultValue: "");
    }
    
    // ✅ GOOD: Can rollback
    public static void MarkColumnAsDeprecated(IMigrationBuilder m)
    {
        // Rename old column, keep duplicate for compatibility
        // Old code writes to both, new code reads from new
    }
    
    // ❌ BAD: Cannot easily rollback
    public static void RemoveColumn(IMigrationBuilder m)
    {
        // Data loss! Can't rollback
        // Must do only after no instances use the column
    }
    
    // Migration strategy:
    // 1. Deploy v1.0.0 (writes column_a)
    // 2. Migrate: Add column_b with default
    // 3. Deploy v1.1.0 (reads/writes column_b, writes column_a for safety)
    // 4. Deploy v1.2.0 (reads column_b only)
    // 5. After week: Remove column_a (no way to rollback, but safe)
}
```

---

## Summary

Deployment strategies balance **speed, safety, and cost**:

| Strategy | Speed | Safety | Cost | Rollback |
|----------|-------|--------|------|----------|
| Blue-Green | Slow | Highest | High | Instant |
| Rolling | Fast | Medium | Low | Complex |
| Canary | Medium | Very High | Low | Instant |
| Feature Flag | Fast | High | Low | Instant |

Best practices:
1. **Always health check** before switching traffic
2. **Monitor deployment** automatically
3. **Automate rollback** on metric degradation
4. **Version everything** (code, database, config)
5. **Test deployment** in staging first
6. **Plan database migrations** for forward-only changes
7. **Implement feature flags** for safe rollout control
8. **Keep previous version** running for quick rollback

Kubernetes handles much deployment complexity automatically with liveness/readiness probes and rolling updates.

Next topic covers CI/CD Pipelines for automated deployment.
