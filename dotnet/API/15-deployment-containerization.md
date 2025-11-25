# Chapter 15: Deployment & Containerization

## 15.1 Docker Fundamentals

Docker packages applications with dependencies into portable containers.

### Dockerfile for ASP.NET Core

```dockerfile
# Multi-stage build: compile and runtime
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["MyApi/MyApi.csproj", "MyApi/"]
RUN dotnet restore "MyApi/MyApi.csproj"

# Copy source and build
COPY . .
WORKDIR "/src/MyApi"
RUN dotnet build "MyApi.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "MyApi.csproj" -c Release -o /app/publish

# Runtime stage (smaller, only runtime needed)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Environment
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MyApi.dll"]
```

**Multi-stage build benefits:**
- Smaller final image (only runtime, not SDK)
- Faster deployments
- Security (no source code in production)

### Docker Compose

```yaml
version: '3.9'

services:
  api:
    build:
      context: .
      dockerfile: Dockerfile
    ports:
      - "5000:8080"
    environment:
      - ConnectionStrings__DefaultConnection=Server=db;Database=myapp;User Id=sa;Password=YourPassword123!
      - Jwt__Secret=your-secret-key-min-32-chars
    depends_on:
      - db
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 3s
      retries: 3

  db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourPassword123!
    ports:
      - "1433:1433"
    volumes:
      - sqldata:/var/opt/mssql
    healthcheck:
      test: ["CMD", "/opt/mssql-tools/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", "YourPassword123!", "-Q", "SELECT 1"]
      interval: 10s
      timeout: 3s
      retries: 3

volumes:
  sqldata:
```

**Run with:**
```bash
docker-compose up -d
docker-compose logs -f api
docker-compose down
```

---

## 15.2 Container Orchestration

### Kubernetes Basics

Kubernetes manages containerized applications at scale.

**Deployment manifest:**
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: myapi-deployment
spec:
  replicas: 3  # Run 3 instances
  selector:
    matchLabels:
      app: myapi
  template:
    metadata:
      labels:
        app: myapi
    spec:
      containers:
      - name: myapi
        image: myregistry.azurecr.io/myapi:v1.0.0
        ports:
        - containerPort: 8080
        env:
        - name: ConnectionStrings__DefaultConnection
          valueFrom:
            secretKeyRef:
              name: db-credentials
              key: connectionString
        - name: Jwt__Secret
          valueFrom:
            secretKeyRef:
              name: jwt-secret
              key: secret
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 30
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 10
```

**Service (load balancer):**
```yaml
apiVersion: v1
kind: Service
metadata:
  name: myapi-service
spec:
  type: LoadBalancer
  ports:
  - port: 80
    targetPort: 8080
  selector:
    app: myapi
```

**Deploy to Kubernetes:**
```bash
kubectl apply -f deployment.yaml
kubectl apply -f service.yaml

# Check status
kubectl get deployments
kubectl get pods
kubectl logs deployment/myapi-deployment
kubectl port-forward service/myapi-service 8080:80
```

---

## 15.3 Configuration Management

Environment-specific configuration:

### ASP.NET Core Configuration

```csharp
// Program.cs loads configuration automatically
var environment = builder.Environment.EnvironmentName;

builder.Configuration
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .AddEnvironmentVariables()
    .AddKeyVault(builder.Configuration["KeyVault:VaultUri"]);
```

**Configuration files:**

**appsettings.json** (shared):
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "Database": {
    "CommandTimeout": 30
  }
}
```

**appsettings.Production.json** (production-specific):
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  },
  "Database": {
    "CommandTimeout": 10
  }
}
```

**appsettings.Development.json** (development-specific):
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

### Azure Key Vault

Store secrets securely:

```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri("https://myvault.vault.azure.net/"),
    new DefaultAzureCredential()
);

// Access secrets
var connectionString = builder.Configuration["ConnectionStrings--DefaultConnection"];
var jwtSecret = builder.Configuration["Jwt--Secret"];
```

### Environment Variables

```bash
# Set in deployment
export ConnectionStrings__DefaultConnection="Server=db;Database=myapp;"
export Jwt__Secret="your-secret-key"
export ASPNETCORE_ENVIRONMENT=Production

# Or in Kubernetes secret
kubectl create secret generic db-credentials \
  --from-literal=connectionString="Server=db;Database=myapp;"
```

**Naming conventions:**
- Use `__` (double underscore) for nested config
- Use `:` (colon) in JSON
- Environment variables take precedence over files

---

## 15.4 Database Migrations in Production

### Automated Migrations

```csharp
// Auto-migrate on startup (development only)
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Database.Migrate();
    }
}
```

### Manual Migrations (Production)

**Generate migration script:**
```bash
dotnet ef migrations script -o migration.sql
```

**Review and apply manually:**
```sql
-- migration.sql (review before running)
BEGIN TRANSACTION;

-- Create new table
CREATE TABLE NewTable (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL
);

-- Add column to existing table
ALTER TABLE ExistingTable
ADD NewColumn INT NULL;

COMMIT;
```

**In production:**
```bash
# Create backup first
BACKUP DATABASE mydb TO DISK = 'backup.bak'

# Apply migration
sqlcmd -S server -U user -P password -d database -i migration.sql
```

### Zero-Downtime Deployments

Deploy without service interruption:

```csharp
// Migrations should be backwards compatible
// 1. Add new column (nullable)
// 2. Deploy code that reads new column
// 3. Populate new column
// 4. Make column required
// 5. Remove old column in next deployment

// Example: Rename column over multiple deployments
// Deployment 1: Add new column, copy data, keep old column
// Deployment 2: Update code to use new column
// Deployment 3: Remove old column

public class User
{
    // Old column (deprecated)
    public string FullName { get; set; }
    
    // New columns (added in deployment 1)
    public string FirstName { get; set; }
    public string LastName { get; set; }
}

// Populate in migration
migrationBuilder.Sql(@"
    UPDATE Users
    SET FirstName = LEFT(FullName, CHARINDEX(' ', FullName) - 1),
        LastName = RIGHT(FullName, LEN(FullName) - CHARINDEX(' ', FullName))
    WHERE FullName IS NOT NULL
");
```

---

## 15.5 CI/CD Pipeline

Automated testing and deployment:

### GitHub Actions Example

```yaml
name: Build and Deploy

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    
    services:
      sqlserver:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env:
          ACCEPT_EULA: Y
          SA_PASSWORD: YourPassword123!
        options: >-
          --health-cmd="/opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P YourPassword123! -Q 'SELECT 1'"
          --health-interval 10s
          --health-timeout 3s
          --health-retries 3

    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'
    
    - name: Restore
      run: dotnet restore
    
    - name: Build
      run: dotnet build --no-restore -c Release
    
    - name: Test
      run: dotnet test --no-build -c Release --logger "trx" --collect:"XPlat Code Coverage"
    
    - name: Publish Coverage
      uses: codecov/codecov-action@v3
    
    - name: Publish
      run: dotnet publish -c Release -o ./publish
    
    - name: Build Docker Image
      run: docker build -t myregistry.azurecr.io/myapi:${{ github.sha }} .
    
    - name: Login to Registry
      uses: docker/login-action@v2
      with:
        registry: myregistry.azurecr.io
        username: ${{ secrets.REGISTRY_USERNAME }}
        password: ${{ secrets.REGISTRY_PASSWORD }}
    
    - name: Push Docker Image
      run: docker push myregistry.azurecr.io/myapi:${{ github.sha }}
    
    - name: Deploy to Kubernetes
      if: github.ref == 'refs/heads/main'
      run: |
        kubectl set image deployment/myapi-deployment \
          myapi=myregistry.azurecr.io/myapi:${{ github.sha }}
```

---

## 15.6 Blue-Green Deployments

Minimize downtime during deployments:

```yaml
# Blue deployment (current, serving traffic)
apiVersion: apps/v1
kind: Deployment
metadata:
  name: myapi-blue
spec:
  replicas: 3
  selector:
    matchLabels:
      app: myapi
      version: blue
  template:
    metadata:
      labels:
        app: myapi
        version: blue
    spec:
      containers:
      - name: myapi
        image: myregistry.azurecr.io/myapi:v1.0.0

---
# Green deployment (new version, not yet serving traffic)
apiVersion: apps/v1
kind: Deployment
metadata:
  name: myapi-green
spec:
  replicas: 3
  selector:
    matchLabels:
      app: myapi
      version: green
  template:
    metadata:
      labels:
        app: myapi
        version: green
    spec:
      containers:
      - name: myapi
        image: myregistry.azurecr.io/myapi:v1.1.0

---
# Service routes to Blue initially
apiVersion: v1
kind: Service
metadata:
  name: myapi-service
spec:
  ports:
  - port: 80
    targetPort: 8080
  selector:
    app: myapi
    version: blue  # Routes to blue

---
# Switch to Green after testing
# kubectl patch service myapi-service -p '{"spec":{"selector":{"version":"green"}}}'
```

**Deployment steps:**
1. Deploy Green version (new code)
2. Run tests against Green
3. Switch traffic to Green (update selector)
4. Keep Blue running (easy rollback)
5. After stability, remove Blue

---

## 15.7 Monitoring and Logging in Production

### Centralized Logging

```csharp
// Send logs to centralized service
builder.Services.AddLogging(config =>
{
    config.ClearProviders();
    config.AddSerilog(new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.Seq(builder.Configuration["SeqUrl"])  // Seq aggregator
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "MyApi")
        .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
        .CreateLogger()
    );
});
```

### Alerting on Errors

```csharp
public class ProductionErrorMonitor
{
    public async Task MonitorAsync()
    {
        while (true)
        {
            // Check for errors in logs
            var recentErrors = await GetRecentErrorsAsync(
                TimeSpan.FromMinutes(5)
            );
            
            if (recentErrors.Count > 10)
            {
                await NotifyOncallAsync(
                    AlertSeverity.Critical,
                    $"{recentErrors.Count} errors in last 5 minutes"
                );
            }
            
            await Task.Delay(TimeSpan.FromMinutes(1));
        }
    }
}
```

---

## 15.8 Rollback Strategies

Plan for quick recovery if deployment fails:

```bash
# Container rollback
docker rollback myapi:v1.1.0 myapi:v1.0.0

# Kubernetes rollback
kubectl rollout history deployment/myapi-deployment
kubectl rollout undo deployment/myapi-deployment --to-revision=3

# Database rollback (if migrations)
RESTORE DATABASE mydb FROM DISK = 'backup.bak'
```

**Database considerations:**
- Always backup before migrations
- Keep migrations reversible (Down method)
- Test rollback procedures
- Document rollback steps

```csharp
// Reversible migration
public class AddUserStatusColumn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Status",
            table: "Users",
            type: "nvarchar(50)",
            nullable: false,
            defaultValue: "Active"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Status", table: "Users");
    }
}
```

---

## 15.9 Performance in Production

### Load Testing Before Deployment

```bash
# Use k6 for realistic load testing
k6 run loadtest.js

# Monitor metrics during test:
# - Response time (p95, p99)
# - Error rate
# - Resource usage (CPU, memory, connections)
# - Database connection pool usage
```

**Load test script (k6):**
```javascript
import http from 'k6/http';
import { check } from 'k6';

export let options = {
  stages: [
    { duration: '2m', target: 100 },  // Ramp up to 100 users
    { duration: '5m', target: 100 },  // Stay at 100
    { duration: '2m', target: 200 },  // Ramp up to 200
    { duration: '5m', target: 200 },  // Stay at 200
    { duration: '2m', target: 0 },    // Ramp down
  ],
};

export default function () {
  let res = http.get('http://localhost:5000/api/users');
  check(res, {
    'status is 200': (r) => r.status === 200,
    'response time < 200ms': (r) => r.timings.duration < 200,
  });
}
```

---

## 15.10 Deployment Checklist

**Before production deployment:**
- ✓ Code reviewed and approved
- ✓ Tests passing (unit, integration, e2e)
- ✓ Security scan passed (dependencies)
- ✓ Load testing successful
- ✓ Database migration tested and reversible
- ✓ Rollback plan documented
- ✓ Configuration validated for environment
- ✓ Secrets configured (API keys, connection strings)
- ✓ Logging and monitoring configured
- ✓ Health checks operational
- ✓ On-call team notified
- ✓ Deployment window scheduled
- ✓ Communication plan in place

---

## Summary

Docker containerizes applications for consistent deployment. Kubernetes orchestrates containers at scale. Configuration management keeps environment-specific settings secure. CI/CD pipelines automate testing and deployment. Blue-green deployments minimize downtime. Monitoring and alerting enable rapid response to issues. Rollback procedures enable quick recovery. Load testing ensures production readiness. The final chapter covers API documentation and developer experience—making your API usable and discoverable.
