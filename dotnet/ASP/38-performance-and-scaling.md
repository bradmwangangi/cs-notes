# 37. Performance & Scaling

## Overview
Performance and scaling are critical for enterprise systems handling millions of users and transactions. This involves both vertical scaling (more powerful instances) and horizontal scaling (more instances), supported by load balancing, caching, and database optimization. Understanding performance bottlenecks and having strategies to address them is essential.

---

## 1. Performance Fundamentals

### 1.1 Key Metrics

```
LATENCY (Response Time)
├─ P50 (Median): 50% of requests
├─ P95 (95th percentile): 95% of requests faster
├─ P99 (99th percentile): 99% of requests faster
└─ Goal: P95 < 200ms, P99 < 1s for APIs

THROUGHPUT
├─ Requests per second (RPS)
├─ Transactions per second (TPS)
└─ Goal: Handle peak load without degradation

ERROR RATE
├─ 5xx errors: Server errors
├─ 4xx errors: Client errors
└─ Goal: < 0.1% error rate

AVAILABILITY
├─ Uptime percentage (99%, 99.9%, 99.99%)
├─ Downtime allowed per month
└─ Goal: 99.95% or better for production

RESOURCE UTILIZATION
├─ CPU usage
├─ Memory usage
├─ Disk I/O
├─ Network bandwidth
└─ Goal: Keep utilization < 70-80%
```

### 1.2 Performance Pyramid

```
        Application
           (5%)
            ↑
     Database Query
        Optimization
          (25%)
            ↑
      Caching Strategy
          (40%)
            ↑
    Infrastructure Scaling
          (30%)
```

---

## 2. Load Testing

### 2.1 Load Testing Strategy

```csharp
// Install: dotnet add package NBomber

using NBomber.CSharp;
using NBomber.Http;

public class OrderServiceLoadTest
{
    [Fact]
    public async Task LoadTestOrderAPI()
    {
        var httpClient = new HttpClient();
        
        // Define scenario: Create orders
        var scenario = Scenario.Create("create-orders", async context =>
        {
            var request = Http.CreateRequest("POST", "https://api.example.com/orders")
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(JsonConvert.SerializeObject(new
                {
                    customerId = Random.Shared.Next(1, 10000),
                    items = new[] { new { productId = 1, quantity = 1 } },
                    total = 99.99m
                }), Encoding.UTF8, "application/json"));
            
            var response = await Http.Send(httpClient, request);
            
            // Scenario result: Success or Fail
            return response.IsSuccessStatusCode
                ? Response.Ok()
                : Response.Fail();
        })
        .WithLoadSimulations(
            // Start with 10 requests/sec
            Simulation.KeepConstant(rate: 10, during: TimeSpan.FromSeconds(10)),
            
            // Ramp up to 100 requests/sec over 30 seconds
            Simulation.RampPerSec(rate: 100, during: TimeSpan.FromSeconds(30)),
            
            // Maintain 100 requests/sec for 1 minute
            Simulation.KeepConstant(rate: 100, during: TimeSpan.FromMinutes(1)),
            
            // Ramp down
            Simulation.RampPerSec(rate: 10, during: TimeSpan.FromSeconds(30))
        );
        
        // Run load test
        var stats = await NBomberRunner.RegisterScenarios(scenario)
            .RunAsync();
        
        // Assertions
        Assert.True(stats.ScenarioStats[0].Ok.Request.Count > 0);
        Assert.True(stats.ScenarioStats[0].Fail.Request.Count == 0);
        
        // Check latency
        var latency = stats.ScenarioStats[0].Latency;
        Assert.True(latency.P95 < 500);  // P95 < 500ms
        Assert.True(latency.P99 < 1000); // P99 < 1s
    }
}

// k6 load test (alternative)
/*
import http from 'k6/http';
import { check, group } from 'k6';

export let options = {
  stages: [
    { duration: '10s', target: 10 },
    { duration: '30s', target: 100 },
    { duration: '60s', target: 100 },
    { duration: '30s', target: 10 },
  ],
  thresholds: {
    http_req_duration: ['p(95)<500', 'p(99)<1000'],
    http_req_failed: ['rate<0.1'],
  },
};

export default function () {
  group('Create Order', function () {
    const payload = JSON.stringify({
      customerId: Math.floor(Math.random() * 10000),
      items: [{ productId: 1, quantity: 1 }],
      total: 99.99,
    });

    const params = {
      headers: { 'Content-Type': 'application/json' },
    };

    const res = http.post('http://localhost:5000/api/orders', payload, params);
    check(res, { 'status is 201': (r) => r.status === 201 });
  });
}
*/
```

### 2.2 Load Test Analysis

```csharp
public class LoadTestAnalysis
{
    // Results interpretation
    
    public class LoadTestResult
    {
        public double RPS { get; set; }                    // Requests per second
        public double P50Latency { get; set; }             // Median latency (ms)
        public double P95Latency { get; set; }             // 95th percentile
        public double P99Latency { get; set; }             // 99th percentile
        public double ErrorRate { get; set; }              // % of failed requests
        public double AvgCPUUsage { get; set; }            // % CPU
        public double PeakCPUUsage { get; set; }           // Peak CPU
        public double AvgMemoryUsage { get; set; }         // MB
        public double PeakMemoryUsage { get; set; }        // MB
    }
    
    public bool IsPerformanceAcceptable(LoadTestResult result)
    {
        return result.P95Latency < 500 &&               // P95 < 500ms
               result.P99Latency < 1000 &&              // P99 < 1s
               result.ErrorRate < 0.001 &&              // < 0.1% errors
               result.PeakCPUUsage < 80 &&              // CPU < 80%
               result.PeakMemoryUsage < 8000;           // Memory < 8GB
    }
    
    // Find breaking point
    public async Task<double> FindBreakingPointAsync(Func<int, Task<LoadTestResult>> runTest)
    {
        int low = 10;      // RPS
        int high = 10000;  // RPS
        
        while (low < high)
        {
            int mid = (low + high) / 2;
            
            var result = await runTest(mid);
            
            if (IsPerformanceAcceptable(result))
            {
                low = mid + 1;  // Try higher load
            }
            else
            {
                high = mid;     // Load too high
            }
        }
        
        return low;  // Maximum RPS the system can handle
    }
}
```

---

## 3. Horizontal Scaling

### 3.1 Load Balancing

```csharp
public class LoadBalancingStrategies
{
    /*
    LOAD BALANCING ALGORITHMS
    
    1. Round Robin
       Request 1 → Server A
       Request 2 → Server B
       Request 3 → Server C
       Request 4 → Server A (cycle)
       
       ✓ Simple
       ✗ Doesn't account for server capacity
    
    2. Least Connections
       Server A: 10 connections
       Server B: 20 connections
       Server C: 5 connections
       New request → Server C (fewest)
       
       ✓ More aware of load
       ✗ Assumes all connections equal
    
    3. Least Response Time
       Server A: Avg response 100ms
       Server B: Avg response 50ms
       Server C: Avg response 150ms
       New request → Server B (fastest)
       
       ✓ Optimizes latency
       ✗ Overhead of tracking response times
    
    4. IP Hash
       Hash client IP → Consistent server
       Same client always goes to same server
       
       ✓ Session affinity
       ✗ Uneven load distribution
    
    5. Resource-Based (CPU/Memory)
       Route to server with most available resources
       
       ✓ Optimal distribution
       ✗ Requires monitoring
    */
}

// Azure Load Balancer configuration
/*
apiVersion: v1
kind: Service
metadata:
  name: order-service-lb
spec:
  type: LoadBalancer
  selector:
    app: order-service
  ports:
  - protocol: TCP
    port: 80
    targetPort: 8080
  loadBalancerSourceRanges:
  - 203.0.113.0/24  # Restrict to specific IPs
*/

// Nginx load balancer
/*
upstream order_service {
    least_conn;  # Least connections strategy
    
    server api1.example.com:8080 weight=3;  # Handle 3x more load
    server api2.example.com:8080 weight=2;
    server api3.example.com:8080 weight=1;
    
    keepalive 32;  # Connection pooling
}

server {
    listen 80;
    
    location /api/orders {
        proxy_pass http://order_service;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # Timeout settings
        proxy_connect_timeout 5s;
        proxy_send_timeout 30s;
        proxy_read_timeout 30s;
    }
}
*/
```

### 3.2 Session Affinity and Stateless Design

```csharp
// Problem: Session affinity in load-balanced systems

public class SessionManagementProblem
{
    // ❌ BAD: In-memory session state
    public class OrderService
    {
        private readonly Dictionary<string, OrderSession> _sessions;
        
        public void StartOrderSession(string sessionId, OrderContext context)
        {
            _sessions[sessionId] = new OrderSession { Context = context };
        }
    }
    
    // Problem: If user's next request goes to different server
    // That server doesn't have the session data
    // Session is lost
    
    // ✅ GOOD: Distributed session state
    public class StatefulOrderService
    {
        private readonly IDistributedCache _cache;
        
        public async Task StartOrderSessionAsync(string sessionId, OrderContext context)
        {
            // Store in Redis/distributed cache
            await _cache.SetAsync(
                $"order-session-{sessionId}",
                JsonConvert.SerializeObject(context),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                }
            );
        }
        
        public async Task<OrderContext> RetrieveOrderSessionAsync(string sessionId)
        {
            var json = await _cache.GetStringAsync($"order-session-{sessionId}");
            return JsonConvert.DeserializeObject<OrderContext>(json);
        }
    }
    
    // ✅ BEST: Stateless design
    public class StatelessOrderService
    {
        // No session state at all
        // Each request contains all needed information
        
        public async Task<int> PlaceOrderAsync(CreateOrderRequest request)
        {
            // Validate, persist, return immediately
            // No session tracking needed
            var orderId = await _repository.SaveAsync(request.ToEntity());
            return orderId;
        }
    }
}
```

---

## 4. Database Scaling

### 4.1 Database Query Optimization

```csharp
public class QueryOptimization
{
    // ❌ SLOW: N+1 query problem
    public async Task<List<OrderDetailDto>> GetOrdersWithDetailsSlowAsync()
    {
        var orders = await _context.Orders.ToListAsync();
        
        var result = new List<OrderDetailDto>();
        
        foreach (var order in orders)
        {
            // N queries: One per order!
            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Id == order.CustomerId);
            
            var items = await _context.OrderLines
                .Where(ol => ol.OrderId == order.Id)
                .ToListAsync();
            
            var shipping = await _context.Shipments
                .FirstOrDefaultAsync(s => s.OrderId == order.Id);
            
            result.Add(new OrderDetailDto
            {
                OrderId = order.Id,
                CustomerName = customer.Name,
                Items = items.Count,
                ShippingStatus = shipping?.Status
            });
        }
        
        return result;
    }
    
    // ✅ FAST: Eager loading with single query
    public async Task<List<OrderDetailDto>> GetOrdersWithDetailsFastAsync()
    {
        return await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderLines)
            .Include(o => o.Shipment)
            .Select(o => new OrderDetailDto
            {
                OrderId = o.Id,
                CustomerName = o.Customer.Name,
                Items = o.OrderLines.Count,
                ShippingStatus = o.Shipment.Status
            })
            .ToListAsync();
        
        // Single query with JOINs
    }
    
    // ✅ BEST: Use projections to select only needed columns
    public async Task<List<OrderSummaryDto>> GetOrderSummariesAsync()
    {
        return await _context.Orders
            .Select(o => new OrderSummaryDto
            {
                OrderId = o.Id,
                Total = o.Total.Amount,
                CreatedAt = o.CreatedAt
                // Only select needed columns
            })
            .ToListAsync();
        
        // Smallest dataset transfer
    }
}
```

### 4.2 Database Indexing

```csharp
public class DatabaseIndexing
{
    // Indexes speed up queries but slow down writes
    
    public class Order
    {
        [Key]
        public int Id { get; set; }
        
        [Index]  // Single-column index
        public int CustomerId { get; set; }
        
        [Index(nameof(OrderDate), nameof(Status))]  // Composite index
        public DateTime OrderDate { get; set; }
        
        public OrderStatus Status { get; set; }
        
        public decimal Total { get; set; }
    }
    
    // Index strategy
    public class IndexingStrategy
    {
        /*
        1. Index columns used in WHERE clauses
           WHERE CustomerId = 123  → Index on CustomerId
        
        2. Index columns used in JOINs
           JOIN Customer ON Order.CustomerId = Customer.Id
           → Index on CustomerId
        
        3. Create composite indexes for common filters
           WHERE OrderDate > X AND Status = 'Pending'
           → Index on (OrderDate, Status)
        
        4. Avoid over-indexing
           Too many indexes slow down writes
           Each write must update all indexes
           
        5. Monitor index usage
           Remove unused indexes
           
        6. Update statistics regularly
           Query optimizer needs stats
           
        7. Consider partitioning for large tables
           Divide table by date ranges
           Faster queries on partitions
        */
    }
    
    // Migration to add index
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_orders_customerId_status",
            table: "Orders",
            columns: new[] { "CustomerId", "Status" }
        );
        
        migrationBuilder.CreateIndex(
            name: "ix_orders_orderDate",
            table: "Orders",
            column: "OrderDate"
        );
    }
}
```

### 4.3 Read Replicas and Sharding

```csharp
public class AdvancedDatabaseScaling
{
    // Read Replicas: Distribute reads across multiple instances
    public class RepositoryWithReadReplica
    {
        private readonly ApplicationDbContext _primaryDb;
        private readonly ApplicationDbContext _replicaDb;  // Read-only
        
        public async Task<Order> GetOrderAsync(int orderId)
        {
            // Read from replica (faster, doesn't affect primary)
            return await _replicaDb.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId);
        }
        
        public async Task<int> CreateOrderAsync(Order order)
        {
            // Write to primary
            _primaryDb.Orders.Add(order);
            await _primaryDb.SaveChangesAsync();
            
            return order.Id;
        }
    }
    
    // Database Sharding: Distribute data across multiple databases
    public class ShardedOrderRepository
    {
        private readonly Dictionary<int, ApplicationDbContext> _shards;
        
        // Calculate shard for customer
        private int GetShard(int customerId)
        {
            return customerId % _shards.Count;
        }
        
        public async Task<Order> GetOrderAsync(int customerId, int orderId)
        {
            var shardId = GetShard(customerId);
            var shard = _shards[shardId];
            
            return await shard.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == customerId);
        }
        
        public async Task<int> CreateOrderAsync(Order order)
        {
            var shardId = GetShard(order.CustomerId);
            var shard = _shards[shardId];
            
            shard.Orders.Add(order);
            await shard.SaveChangesAsync();
            
            return order.Id;
        }
    }
}

// Sharding visualization
/*
CustomerId 1 → Shard 1 (Server A)
CustomerId 2 → Shard 2 (Server B)
CustomerId 3 → Shard 1 (Server A)
CustomerId 4 → Shard 2 (Server B)

Each shard stores subset of data
Queries limited to single shard
Better scalability than single database

Tradeoffs:
✓ Horizontal scaling
✓ Better performance (smaller datasets)
✗ Complex joins across shards
✗ Rebalancing when adding shards
✗ Hotspot management
*/
```

---

## 5. Caching for Performance

### 5.1 Multi-Layer Caching Strategy

```csharp
public class MultiLayerCachingStrategy
{
    // L1: Application-level cache (fastest, limited)
    // L2: Distributed cache (Redis, shared)
    // L3: Database (slowest)
    
    public class MultiLayerCacheRepository
    {
        private readonly IMemoryCache _l1Cache;
        private readonly IDistributedCache _l2Cache;
        private readonly IOrderRepository _l3Repository;
        
        public async Task<Order> GetOrderAsync(int orderId)
        {
            var cacheKey = $"order-{orderId}";
            
            // L1: Check in-memory cache (fastest)
            if (_l1Cache.TryGetValue(cacheKey, out Order cachedOrder))
            {
                return cachedOrder;
            }
            
            // L2: Check distributed cache
            var l2Cached = await _l2Cache.GetStringAsync(cacheKey);
            if (!string.IsNullOrEmpty(l2Cached))
            {
                var order = JsonConvert.DeserializeObject<Order>(l2Cached);
                
                // Populate L1 for next request
                _l1Cache.Set(cacheKey, order, TimeSpan.FromMinutes(5));
                
                return order;
            }
            
            // L3: Database query
            var dbOrder = await _l3Repository.GetByIdAsync(orderId);
            
            // Populate both caches
            _l1Cache.Set(cacheKey, dbOrder, TimeSpan.FromMinutes(5));
            await _l2Cache.SetStringAsync(
                cacheKey,
                JsonConvert.SerializeObject(dbOrder),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                }
            );
            
            return dbOrder;
        }
    }
}
```

### 5.2 Cache Warming

```csharp
public class CacheWarmingStrategy
{
    // Pre-populate cache with frequently accessed data
    
    public class StartupCacheWarming
    {
        private readonly IMemoryCache _cache;
        private readonly IOrderRepository _repository;
        private readonly ILogger<StartupCacheWarming> _logger;
        
        public async Task WarmCacheAsync()
        {
            _logger.LogInformation("Warming cache at startup");
            
            // Load frequently accessed data
            var popularProducts = await _repository.GetPopularProductsAsync(count: 100);
            foreach (var product in popularProducts)
            {
                _cache.Set($"product-{product.Id}", product, TimeSpan.FromHours(1));
            }
            
            // Load configuration
            var config = await _repository.GetSystemConfigurationAsync();
            _cache.Set("system-config", config, TimeSpan.FromDays(1));
            
            _logger.LogInformation("Cache warming completed");
        }
    }
    
    // Periodic cache refresh during off-peak hours
    public class CacheRefreshService : BackgroundService
    {
        private readonly IMemoryCache _cache;
        private readonly IOrderRepository _repository;
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Run at 2 AM daily
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                
                if (now.Hour == 2 && now.Minute == 0)
                {
                    // Refresh popular items cache
                    var popularProducts = await _repository.GetPopularProductsAsync(count: 100);
                    
                    foreach (var product in popularProducts)
                    {
                        _cache.Set($"product-{product.Id}", product, TimeSpan.FromHours(1));
                    }
                }
                
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
```

---

## 6. Auto-Scaling Configuration

### 6.1 Kubernetes Auto-Scaling

```yaml
# Horizontal Pod Autoscaler
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: order-service-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: order-service
  minReplicas: 2
  maxReplicas: 20
  metrics:
  # Scale based on CPU
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  # Scale based on memory
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
  # Scale based on custom metric
  - type: Pods
    pods:
      metric:
        name: http_requests_per_second
      target:
        type: AverageValue
        averageValue: "1000"
  behavior:
    scaleDown:
      stabilizationWindowSeconds: 300
      policies:
      - type: Percent
        value: 50
        periodSeconds: 60
    scaleUp:
      stabilizationWindowSeconds: 0
      policies:
      - type: Percent
        value: 100
        periodSeconds: 30
      - type: Pods
        value: 4
        periodSeconds: 60
      selectPolicy: Max

# Vertical Pod Autoscaler (for right-sizing)
apiVersion: autoscaling.k8s.io/v1
kind: VerticalPodAutoscaler
metadata:
  name: order-service-vpa
spec:
  targetRef:
    apiVersion: "apps/v1"
    kind: Deployment
    name: order-service
  updatePolicy:
    updateMode: "Auto"  # Can be "Off", "Initial", "Recreate", "Auto"
  resourcePolicy:
    containerPolicies:
    - containerName: order-service
      minAllowed:
        cpu: 100m
        memory: 128Mi
      maxAllowed:
        cpu: 2
        memory: 2Gi
```

### 6.2 Azure App Service Auto-Scale

```csharp
// Azure CLI for auto-scaling
/*
# Create auto-scale setting
az monitor autoscale create \
  --name order-service-autoscale \
  --resource-group production \
  --resource order-service-plan \
  --resource-type "Microsoft.Web/serverfarms" \
  --min-count 2 \
  --max-count 10 \
  --count 3

# Add scale-up rule (CPU > 80%)
az monitor autoscale rule create \
  --autoscale-name order-service-autoscale \
  --resource-group production \
  --condition "Percentage CPU > 80 avg 5m" \
  --scale out 1

# Add scale-down rule (CPU < 20%)
az monitor autoscale rule create \
  --autoscale-name order-service-autoscale \
  --resource-group production \
  --condition "Percentage CPU < 20 avg 10m" \
  --scale in 1

# View auto-scale settings
az monitor autoscale show \
  --name order-service-autoscale \
  --resource-group production
*/
```

---

## 7. CDN for Static Content

### 7.1 CDN Configuration

```csharp
public class CDNConfiguration
{
    // Azure CDN for static assets
    
    public static void ConfigureCDN(IServiceCollection services, IConfiguration config)
    {
        var cdnEndpoint = config["CDN:Endpoint"];  // e.g., https://cdn.example.com
        
        services.AddSingleton<ICDNService>(new CDNService(cdnEndpoint));
    }
}

public class CDNService : ICDNService
{
    private readonly string _cdnEndpoint;
    
    public string GetAssetUrl(string assetPath)
    {
        // Serve from CDN instead of origin server
        return $"{_cdnEndpoint}/{assetPath}";
    }
}

// In Razor views
/*
@inject ICDNService CDN

<img src="@CDN.GetAssetUrl("images/logo.png")" />
<script src="@CDN.GetAssetUrl("js/app.min.js")"></script>
<link rel="stylesheet" href="@CDN.GetAssetUrl("css/style.min.css")" />
*/

/*
CDN Benefits:
- Geographically distributed servers
- Serve content from server closest to user
- Reduce latency for static assets
- Offload bandwidth from origin
- Automatic gzip compression
- Cache invalidation support

Typical setup:
Browser → CDN (if cached) → Origin Server (if cache miss)
*/
```

### 7.2 Cache Busting

```csharp
public class CacheBusting
{
    // Problem: Browsers cache static assets
    // Solution: Include version in filename
    
    public class VersionedAssetService
    {
        private readonly IFileProvider _fileProvider;
        
        public string GetVersionedAssetUrl(string assetPath)
        {
            // Get file hash as version
            var filePath = Path.Combine("wwwroot", assetPath);
            var file = _fileProvider.GetFileInfo(filePath);
            
            if (file.Exists)
            {
                using (var stream = file.CreateReadStream())
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var hash = sha256.ComputeHash(stream);
                    var version = BitConverter.ToString(hash).Replace("-", "").Substring(0, 8);
                    
                    // Insert version before extension
                    var parts = assetPath.Split('.');
                    var withVersion = $"{string.Join(".", parts[..^1])}.{version}.{parts[^1]}";
                    
                    return withVersion;
                }
            }
            
            return assetPath;
        }
    }
}

// CSS asset
/*
Original: /css/style.css
Versioned: /css/style.a1b2c3d4.css

Browser caches versioned file indefinitely
When file changes, version changes
Browser fetches new version
*/
```

---

## Summary

Performance and scaling require multi-layered approach:

**Performance Optimization:**
1. **Query Optimization**: Reduce N+1, use projections
2. **Indexing**: Index heavily-used columns
3. **Caching**: Multi-layer strategy (in-memory, distributed)
4. **Asynchronous**: Use async/await to reduce blocking

**Scaling Strategies:**
1. **Vertical**: More powerful instances (limited)
2. **Horizontal**: More instances with load balancing
3. **Database**: Read replicas, sharding
4. **Content**: CDN for static assets

**Monitoring:**
- Track latency (P50, P95, P99)
- Monitor error rates
- Watch resource utilization
- Load test regularly
- Auto-scale based on metrics

**Key Principles:**
- Measure before optimizing (find bottlenecks)
- Cache aggressively
- Database is usually the bottleneck
- Design for stateless operation
- Distribute load horizontally
- Monitor continuously
- Test under realistic load

**Common Mistakes:**
- Premature optimization
- Inefficient database queries
- Not caching
- Over-provisioning
- Not monitoring actual usage
- Ignoring cache invalidation
- Not testing scalability

Next topics cover Real-World Systems and production-ready patterns for enterprise development.
