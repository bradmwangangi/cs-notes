# 36. Cloud Platforms

## Overview
Cloud platforms provide managed infrastructure and services, eliminating server management overhead. For ASP.NET applications, Azure is the primary choice, offering deep .NET integration. Understanding cloud architecture is essential for enterprise deployments—scalability, reliability, and cost management depend on proper cloud service selection.

---

## 1. Cloud Architecture Fundamentals

### 1.1 Cloud Service Models

```
ON-PREMISES
├─ Application
├─ Middleware
├─ Runtime
├─ OS
├─ Virtualization
├─ Servers
├─ Storage
└─ Networking
(Full Control, Full Responsibility)

IAAS (Infrastructure as a Service)
├─ Application         ← Customer responsibility
├─ Middleware          ← Customer responsibility
├─ Runtime             ← Customer responsibility
├─ OS                  ← Customer responsibility
├─ Virtualization      ← Cloud provider
├─ Servers             ← Cloud provider
├─ Storage             ← Cloud provider
└─ Networking          ← Cloud provider

PAAS (Platform as a Service)
├─ Application         ← Customer responsibility
├─ Middleware          ← Cloud provider
├─ Runtime             ← Cloud provider
├─ OS                  ← Cloud provider
├─ Virtualization      ← Cloud provider
├─ Servers             ← Cloud provider
├─ Storage             ← Cloud provider
└─ Networking          ← Cloud provider

SAAS (Software as a Service)
├─ Application         ← Cloud provider
├─ Middleware          ← Cloud provider
├─ Runtime             ← Cloud provider
├─ OS                  ← Cloud provider
├─ Virtualization      ← Cloud provider
├─ Servers             ← Cloud provider
├─ Storage             ← Cloud provider
└─ Networking          ← Cloud provider
(Full Responsibility on Provider)

For ASP.NET:
- IaaS: Azure VMs (Virtual Machines)
- PaaS: Azure App Service, Azure Container Instances
- Hybrid: Kubernetes (Azure Kubernetes Service)
```

---

## 2. Azure App Service

### 2.1 App Service Fundamentals

```csharp
// App Service: Managed web hosting for ASP.NET
// Handles: Scaling, patching, monitoring, SSL

public class AppServiceConfiguration
{
    // No server management needed
    // Focus on code, Azure handles infrastructure
}

// Deployment options:
// 1. Git deployment (git push azure main)
// 2. GitHub Actions
// 3. Azure Pipelines
// 4. Docker containers
// 5. Zip deployment
```

### 2.2 App Service Configuration

```yaml
# Azure Resource Manager template

{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "appServicePlanName": {
      "type": "string",
      "defaultValue": "order-service-plan"
    },
    "webAppName": {
      "type": "string",
      "defaultValue": "order-service-prod"
    },
    "skuName": {
      "type": "string",
      "defaultValue": "P1V2",
      "allowedValues": ["B1", "B2", "B3", "S1", "S2", "S3", "P1V2", "P2V2", "P3V2"]
    }
  },
  "resources": [
    {
      "type": "Microsoft.Web/serverfarms",
      "name": "[parameters('appServicePlanName')]",
      "apiVersion": "2021-02-01",
      "location": "[resourceGroup().location]",
      "sku": {
        "name": "[parameters('skuName')]",
        "capacity": 3
      },
      "properties": {
        "reserved": false
      }
    },
    {
      "type": "Microsoft.Web/sites",
      "name": "[parameters('webAppName')]",
      "apiVersion": "2021-02-01",
      "location": "[resourceGroup().location]",
      "dependsOn": [
        "[resourceId('Microsoft.Web/serverfarms', parameters('appServicePlanName'))]"
      ],
      "properties": {
        "serverFarmId": "[resourceId('Microsoft.Web/serverfarms', parameters('appServicePlanName'))]",
        "httpsOnly": true,
        "virtualNetworkSubnetId": "[resourceId('Microsoft.Network/virtualNetworks/subnets', 'vnet', 'app-subnet')]"
      },
      "resources": [
        {
          "type": "config",
          "name": "appsettings",
          "apiVersion": "2021-02-01",
          "dependsOn": [
            "[resourceId('Microsoft.Web/sites', parameters('webAppName'))]"
          ],
          "properties": {
            "ASPNETCORE_ENVIRONMENT": "Production",
            "WEBSITE_RUN_FROM_PACKAGE": "1",
            "APPLICATIONINSIGHTS_CONNECTION_STRING": "[reference(resourceId('Microsoft.Insights/components', 'app-insights')).ConnectionString]"
          }
        },
        {
          "type": "config",
          "name": "connectionstrings",
          "apiVersion": "2021-02-01",
          "dependsOn": [
            "[resourceId('Microsoft.Web/sites', parameters('webAppName'))]"
          ],
          "properties": {
            "DefaultConnection": {
              "value": "[concat('Server=tcp:', reference(resourceId('Microsoft.Sql/servers/databases', 'sql-server', 'orderdb')).name, '.database.windows.net,1433;Initial Catalog=orderdb;')]",
              "type": "SQLAzure"
            }
          }
        }
      ]
    }
  ]
}
```

### 2.3 Scaling App Service

```csharp
public class AppServiceScaling
{
    // Vertical Scaling: Increase SKU (more powerful instance)
    // Horizontal Scaling: Increase instance count
    
    /*
    SKU Tiers (Vertical Scaling):
    
    Free        → Development only
    Shared      → Shared infrastructure
    Basic       → Single region, no auto-scale
    Standard    → Production basic
    Premium     → Production with premium features
    Isolated    → Dedicated infrastructure
    */
    
    public static void ConfigureAutoScaling(IServiceCollection services)
    {
        // App Insights monitors metrics
        // Auto-scale rules adjust instance count
        
        /*
        Auto-Scale Rules:
        
        Rule 1: Scale Up
        - Condition: CPU > 80% for 5 minutes
        - Action: +1 instance
        - Cooldown: 5 minutes
        
        Rule 2: Scale Down
        - Condition: CPU < 20% for 10 minutes
        - Action: -1 instance
        - Cooldown: 10 minutes
        
        Limits:
        - Minimum: 2 instances
        - Maximum: 10 instances
        */
    }
}

// Azure CLI for auto-scaling
/*
# Create App Service Plan with auto-scale
az appservice plan create \
  --name order-service-plan \
  --resource-group production \
  --sku P1V2 \
  --number-of-workers 3

# Create Web App
az webapp create \
  --name order-service-prod \
  --plan order-service-plan \
  --resource-group production

# Create auto-scale rule
az monitor autoscale create \
  --name order-service-autoscale \
  --resource-group production \
  --resource order-service-prod \
  --resource-type "Microsoft.Web/sites" \
  --min-count 2 \
  --max-count 10 \
  --count 3

# Add scale-up rule
az monitor autoscale rule create \
  --autoscale-name order-service-autoscale \
  --resource-group production \
  --condition "Percentage CPU > 80 avg 5m" \
  --scale out 1

# Add scale-down rule
az monitor autoscale rule create \
  --autoscale-name order-service-autoscale \
  --resource-group production \
  --condition "Percentage CPU < 20 avg 10m" \
  --scale in 1
*/
```

---

## 3. Azure SQL Database

### 3.1 SQL Database Configuration

```csharp
public class AzureSqlConfiguration
{
    public static void ConfigureDatabase(IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("DefaultConnection");
        
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(
                connectionString,
                sqlOptions =>
                {
                    // Connection pooling
                    sqlOptions.MaxBatchSize(100);
                    
                    // Retry strategy for transient failures
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelaySeconds: 30,
                        errorNumbersToAdd: null
                    );
                    
                    // Command timeout
                    sqlOptions.CommandTimeout(30);
                    
                    // Connection timeout
                }
            );
        });
    }
    
    // Connection string format
    /*
    Server=tcp:server.database.windows.net,1433;
    Initial Catalog=database;
    Persist Security Info=False;
    User ID=username;
    Password=password;
    Encrypt=True;
    TrustServerCertificate=False;
    Connection Timeout=30;
    */
}
```

### 3.2 Database Scaling

```csharp
public class DatabaseScaling
{
    // DTU (Database Transaction Unit): Bundled CPU, memory, IO
    // vCore: Virtual cores, flexible pricing
    
    /*
    DTU Tiers:
    
    Basic       → Development
    Standard    → General purpose
    Premium     → High performance
    
    vCore Tiers:
    
    General Purpose → Most workloads
    Business Critical → High performance, HA
    Hyperscale      → Very large databases (100TB+)
    */
    
    // Auto-scaling configuration
    public static void ConfigureAutoscaling()
    {
        /*
        Azure SQL enables compute auto-scaling:
        
        - Monitor CPU, memory, storage usage
        - Scale up for high demand
        - Scale down during off-peak
        - Automatic failover to replica
        
        Replica Options:
        - None: Single instance
        - One replica: Active-Active high availability
        - Multiple replicas: Read scale-out
        */
    }
}
```

### 3.3 Backup and Disaster Recovery

```csharp
public class DatabaseBackupStrategy
{
    // Automatic backups
    /*
    - Full backups: Weekly
    - Differential backups: Daily
    - Transaction log backups: Every 5-10 minutes
    
    Retention policies:
    - Basic: 7 days
    - Standard: 35 days
    - Premium: 35 days (configurable up to 35 years)
    */
    
    // Geo-replication
    public static void ConfigureGeoReplication()
    {
        /*
        Scenario: Database in West US
        
        Replica 1: East US (readable, auto-failover)
        Replica 2: Europe West (readable, manual failover)
        
        Benefits:
        - Disaster recovery
        - Read scaling across regions
        - Compliance (data residency)
        */
    }
    
    // Point-in-time restore
    public async Task RestoreDatabaseAsync(
        string databaseName,
        DateTime restorePoint)
    {
        /*
        Recover from accidental deletion or data corruption
        
        az sql db restore \
          --resource-group production \
          --server sql-server \
          --name original-db \
          --dest-name restored-db \
          --time restorePoint
        */
    }
}
```

---

## 4. Azure Service Bus

### 4.1 Service Bus Configuration

```csharp
public class AzureServiceBusConfiguration
{
    public static void ConfigureServiceBus(IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("ServiceBus");
        
        // Add Service Bus client
        services.AddAzureClients(builder =>
        {
            builder.AddServiceBusClient(connectionString);
        });
        
        // Configure topics and subscriptions
        services.AddSingleton<IServiceBusTopicManager, ServiceBusTopicManager>();
    }
}

public class ServiceBusTopicManager
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<ServiceBusTopicManager> _logger;
    
    public async Task InitializeTopicsAsync()
    {
        var admin = new ServiceBusAdministrationClient(
            _client.FullyQualifiedNamespace,
            new DefaultAzureCredential()
        );
        
        // Create topic for order events
        await CreateTopicIfNotExistsAsync(admin, "orders");
        
        // Create subscriptions
        await CreateSubscriptionIfNotExistsAsync(
            admin,
            "orders",
            "inventory-service",
            new RuleFilter("EventType = 'OrderPlaced'")
        );
        
        await CreateSubscriptionIfNotExistsAsync(
            admin,
            "orders",
            "notification-service",
            new RuleFilter("EventType IN ('OrderPlaced', 'OrderShipped')")
        );
        
        _logger.LogInformation("Service Bus topics initialized");
    }
    
    private async Task CreateTopicIfNotExistsAsync(
        ServiceBusAdministrationClient admin,
        string topicName)
    {
        try
        {
            await admin.CreateTopicAsync(new CreateTopicOptions(topicName)
            {
                MaxSizeInMegabytes = 5120,              // 5 GB
                DefaultMessageTimeToLive = TimeSpan.FromDays(14),
                DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(10),
                RequiresDuplicateDetection = true
            });
            
            _logger.LogInformation("Created topic: {Topic}", topicName);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
            _logger.LogInformation("Topic already exists: {Topic}", topicName);
        }
    }
    
    private async Task CreateSubscriptionIfNotExistsAsync(
        ServiceBusAdministrationClient admin,
        string topicName,
        string subscriptionName,
        RuleFilter filter)
    {
        try
        {
            await admin.CreateSubscriptionAsync(
                new CreateSubscriptionOptions(topicName, subscriptionName)
                {
                    DefaultMessageTimeToLive = TimeSpan.FromDays(14),
                    MaxDeliveryCount = 10,
                    DeadLetteringOnMessageExpiration = true,
                    DeadLetteringOnFilterEvaluationExceptions = true
                },
                filter
            );
            
            _logger.LogInformation(
                "Created subscription: {Topic}/{Subscription}",
                topicName,
                subscriptionName
            );
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
            _logger.LogInformation(
                "Subscription already exists: {Topic}/{Subscription}",
                topicName,
                subscriptionName
            );
        }
    }
}
```

### 4.2 Service Bus vs Event Hubs

```csharp
public class MessageServiceSelection
{
    /*
    SERVICE BUS
    - Asynchronous messaging
    - Queues and topics
    - Message ordering (sessions)
    - Max message size: 256 KB
    - Throughput: 1000s messages/sec
    
    Use for:
    - Traditional messaging
    - Complex routing
    - Message ordering
    - Transactional processing
    
    Example: Order processing with retries
    
    
    EVENT HUBS
    - Event streaming
    - Partitions for scale
    - Message size: 1 MB
    - Throughput: 1M+ events/sec
    - Consumer groups for parallel processing
    
    Use for:
    - High-volume telemetry
    - Real-time analytics
    - Event streaming
    - IoT sensors
    
    Example: Collecting application metrics
    */
    
    public async Task DemonstrateSelectionAsync()
    {
        // SERVICE BUS for order events (lower volume, need ordering)
        var sbClient = new ServiceBusClient("connection-string");
        var sender = sbClient.CreateSender("orders-topic");
        
        await sender.SendMessageAsync(new ServiceBusMessage("Order placed event"));
        
        // EVENT HUBS for application metrics (high volume)
        var ehClient = new EventHubProducerClient("connection-string");
        
        await ehClient.SendAsync(new[] {
            new EventData(Encoding.UTF8.GetBytes("CPU: 45%")),
            new EventData(Encoding.UTF8.GetBytes("Memory: 70%"))
        });
    }
}
```

---

## 5. Managed Services

### 5.1 Azure Cache for Redis

```csharp
public class AzureRedisCacheConfiguration
{
    public static void ConfigureRedis(IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("RedisCache");
        
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
            options.InstanceName = "bookstore_";
        });
    }
}

// Azure Redis configuration
/*
SKU Tiers:
- Basic (250MB - 53GB): Development/testing
- Standard (250MB - 53GB): Production
- Premium (6GB - 530GB): Persistence, clustering, high availability

Features:
- Automatic failover
- Geo-replication
- Persistence (RDB snapshots)
- Virtual Network support
- Data encryption
*/
```

### 5.2 Azure Application Insights

```csharp
public class ApplicationInsightsConfiguration
{
    public static void ConfigureApplicationInsights(IServiceCollection services, IConfiguration config)
    {
        services.AddApplicationInsightsTelemetry(config["ApplicationInsights:InstrumentationKey"]);
        
        services.AddApplicationInsightsKubernetesEnricher();
        
        services.AddSingleton<ITelemetryInitializer, CloudRoleNameTelemetryInitializer>();
    }
}

public class ApplicationInsightsUsage
{
    private readonly TelemetryClient _telemetryClient;
    
    public async Task TrackOrderProcessingAsync(int orderId)
    {
        var properties = new Dictionary<string, string>
        {
            { "OrderId", orderId.ToString() }
        };
        
        var metrics = new Dictionary<string, double>
        {
            { "ProcessingTime", 250 }
        };
        
        // Track custom event
        _telemetryClient.TrackEvent("OrderProcessed", properties, metrics);
        
        // Track request
        var startTime = DateTime.UtcNow;
        try
        {
            await ProcessOrderAsync(orderId);
            
            _telemetryClient.TrackRequest(
                "ProcessOrder",
                startTime,
                DateTime.UtcNow - startTime,
                "200",
                success: true
            );
        }
        catch (Exception ex)
        {
            _telemetryClient.TrackException(ex, properties);
            throw;
        }
    }
}
```

---

## 6. Cost Optimization

### 6.1 Cost Management

```csharp
public class AzureCostOptimization
{
    /*
    Right-size resources:
    - Monitor actual usage
    - Don't over-provision
    - Use appropriate SKU tier
    
    Cost Drivers:
    - Compute (App Service, VMs): 40-50%
    - Database (SQL): 20-30%
    - Storage: 10-20%
    - Networking: 5-10%
    - Other services: 5-10%
    */
    
    public static void OptimizeCosts()
    {
        /*
        Strategies:
        
        1. Auto-scaling
           - Don't pay for idle capacity
           - Scale up during demand
           - Scale down during off-peak
           
        2. Reserved Instances
           - Commit to 1-3 years
           - 30-50% discount
           - Good for baseline load
           
        3. Spot VMs
           - Excess capacity
           - Up to 90% discount
           - Can be evicted anytime
           
        4. Resource Groups
           - Organize by project
           - Track costs per project
           - Implement cost controls
           
        5. Tagging
           - Tag resources by cost center
           - Report costs by tag
           - Implement access controls
        */
    }
}

// Azure CLI cost monitoring
/*
# Set up cost alerts
az costmanagement alert create \
  --scope /subscriptions/{subscription-id} \
  --definition-name Budget \
  --status Active \
  --close-time 2024-12-31T23:59:59Z

# Get cost forecast
az costmanagement forecast query \
  --scope /subscriptions/{subscription-id} \
  --timeframe MonthToDate

# Export costs to storage
az costmanagement export create \
  --name monthly-export \
  --scope /subscriptions/{subscription-id} \
  --storage-account-id /subscriptions/{}/resourceGroups/{}/providers/Microsoft.Storage/storageAccounts/{}
*/
```

### 6.2 Resource Monitoring

```csharp
public class ResourceMonitoring
{
    // Monitor costs at resource level
    
    public static void ConfigureCostMonitoring(IServiceCollection services)
    {
        /*
        Monitor:
        - Storage usage trends
        - Database growth
        - Bandwidth costs
        - Compute utilization
        
        Set alerts:
        - Budget overrun
        - Anomalous usage
        - Unused resources
        */
    }
}

// Azure Portal Cost Analysis
/*
1. Navigate to Cost Management + Billing
2. Select subscription
3. View actual costs
4. Analyze by resource group
5. Create budget and alerts
6. Export for further analysis
*/
```

---

## 7. Multi-Region Deployment

### 7.1 Geographic Distribution

```yaml
# Multi-region architecture

Primary Region: East US
├─ App Service (2+ instances)
├─ SQL Database (primary)
├─ Redis Cache
├─ Service Bus
└─ Storage Account

Secondary Region: West Europe
├─ App Service (2+ instances)
├─ SQL Database (replica)
├─ Redis Cache (replica)
└─ Storage Account (geo-redundant)

Traffic Manager
├─ Route user traffic to nearest region
├─ Automatic failover if primary down
└─ Health check every 30 seconds

Benefits:
- Reduced latency (users connect to nearest region)
- Disaster recovery (automatic failover)
- Compliance (data residency)
- High availability (99.99% uptime)
```

### 7.2 Multi-Region Configuration

```csharp
public class MultiRegionConfiguration
{
    public static void ConfigureMultiRegion(IServiceCollection services, IConfiguration config)
    {
        // Primary region database
        var primaryConnection = config.GetConnectionString("PrimaryDb");
        
        // Secondary region database
        var secondaryConnection = config.GetConnectionString("SecondaryDb");
        
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            // Use read replicas for reporting
            options.UseSqlServer(
                config.GetConnectionString("ReportingDb"),
                sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                }
            );
        });
        
        // Route write operations to primary
        // Route read operations to replicas
    }
    
    public class RegionalService
    {
        private readonly ILogger<RegionalService> _logger;
        
        public async Task<Order> GetOrderAsync(int orderId)
        {
            try
            {
                // Try primary region first
                return await GetFromPrimaryAsync(orderId);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Primary region timeout, trying secondary");
                
                // Fallback to secondary
                return await GetFromSecondaryAsync(orderId);
            }
        }
    }
}
```

---

## Summary

Azure provides comprehensive managed services for ASP.NET:

**Compute:**
- App Service: Web hosting with auto-scale
- Container Instances: Containerized apps
- Kubernetes Service: Orchestration

**Data:**
- SQL Database: Relational database
- Cosmos DB: NoSQL, globally distributed
- Storage: Blob, queue, file storage

**Messaging:**
- Service Bus: Transactional messaging
- Event Hubs: Event streaming
- Event Grid: Event routing

**Monitoring:**
- Application Insights: Telemetry and monitoring
- Azure Monitor: Infrastructure monitoring
- Log Analytics: Log aggregation

**Key advantages:**
- Deep .NET integration
- Automatic scaling and failover
- Global infrastructure
- Security and compliance
- Cost-effective pricing

Best practices:
- Choose right service for workload (IaaS vs PaaS)
- Monitor costs and optimize resource allocation
- Implement multi-region for high availability
- Use managed services to reduce operational overhead
- Secure data with encryption and access controls

Next topic covers Performance & Scaling for production systems.
