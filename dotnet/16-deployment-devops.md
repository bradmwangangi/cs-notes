# Deployment & DevOps

Deploy applications reliably to production.

## Docker

Containerize your application:

### Dockerfile

```dockerfile
# Multi-stage build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["MyApp.csproj", "."]
RUN dotnet restore "MyApp.csproj"

# Copy source and build
COPY . .
RUN dotnet build "MyApp.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "MyApp.csproj" -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "MyApp.dll"]
```

### Docker Compose

```yaml
version: '3.8'
services:
  web:
    build: .
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__DefaultConnection=Server=postgres;User Id=postgres;Password=password;Database=mydb
    depends_on:
      - postgres
    networks:
      - mynetwork

  postgres:
    image: postgres:15
    environment:
      - POSTGRES_PASSWORD=password
      - POSTGRES_DB=mydb
    volumes:
      - postgres_data:/var/lib/postgresql/data
    ports:
      - "5432:5432"
    networks:
      - mynetwork

networks:
  mynetwork:
    driver: bridge

volumes:
  postgres_data:
```

### Build and Run

```bash
# Build image
docker build -t myapp:1.0 .

# Run container
docker run -p 8080:8080 -e ASPNETCORE_ENVIRONMENT=Production myapp:1.0

# Docker Compose
docker-compose up -d
docker-compose logs -f web
docker-compose down
```

## Publishing

### Self-Contained Executable

```bash
# Publish for specific platform (executable doesn't need .NET installed)
dotnet publish -c Release -r win-x64 -o ./publish
dotnet publish -c Release -r linux-x64 -o ./publish

# Run
./MyApp.exe  # Windows
./MyApp      # Linux
```

### Framework-Dependent Deployment

```bash
# Smaller, requires .NET runtime
dotnet publish -c Release -o ./publish

# Run (requires dotnet installed)
dotnet MyApp.dll
```

## CI/CD with GitHub Actions

```yaml
# .github/workflows/ci.yml
name: CI/CD

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    
    services:
      postgres:
        image: postgres:15
        env:
          POSTGRES_PASSWORD: postgres
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
        ports:
          - 5432:5432

    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build
      run: dotnet build --configuration Release --no-restore
    
    - name: Run tests
      run: dotnet test --configuration Release --no-build --verbosity normal
      env:
        ConnectionStrings__DefaultConnection: "Server=localhost;Database=testdb;User Id=postgres;Password=postgres;"
    
    - name: Publish
      run: dotnet publish -c Release -o ./publish
    
    - name: Upload artifact
      uses: actions/upload-artifact@v3
      with:
        name: app-release
        path: publish/

  deploy-to-staging:
    needs: build-and-test
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/develop'
    
    steps:
    - name: Download artifact
      uses: actions/download-artifact@v3
      with:
        name: app-release
    
    - name: Deploy to staging
      env:
        DEPLOY_KEY: ${{ secrets.STAGING_DEPLOY_KEY }}
        DEPLOY_HOST: ${{ secrets.STAGING_HOST }}
      run: |
        mkdir -p ~/.ssh
        echo "$DEPLOY_KEY" > ~/.ssh/deploy_key
        chmod 600 ~/.ssh/deploy_key
        ssh -i ~/.ssh/deploy_key user@$DEPLOY_HOST "bash /home/user/deploy-staging.sh"

  deploy-to-production:
    needs: build-and-test
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/main' && github.event_name == 'push'
    
    steps:
    - name: Deploy to Azure
      uses: azure/webapps-deploy@v2
      with:
        app-name: 'myapp-prod'
        publish-profile: ${{ secrets.AZURE_PUBLISH_PROFILE }}
        package: ${{ github.workspace }}
```

## Azure Deployment

```bash
# Install Azure CLI
# https://learn.microsoft.com/en-us/cli/azure/install-azure-cli

# Login
az login

# Create resource group
az group create --name myapp-rg --location eastus

# Create App Service plan
az appservice plan create \
  --name myapp-plan \
  --resource-group myapp-rg \
  --sku FREE

# Create Web App
az webapp create \
  --name myapp-prod \
  --resource-group myapp-rg \
  --plan myapp-plan \
  --runtime "DOTNETCORE:8.0"

# Deploy from local
dotnet publish -c Release
cd bin/Release/net8.0/publish
zip -r ../../app.zip .
az webapp deployment source config-zip \
  --resource-group myapp-rg \
  --name myapp-prod \
  --src ../../app.zip

# Set configuration
az webapp config appsettings set \
  --name myapp-prod \
  --resource-group myapp-rg \
  --settings \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__DefaultConnection=$DB_CONNECTION

# View logs
az webapp log tail --name myapp-prod --resource-group myapp-rg
```

## Database Migrations in Production

```csharp
// In Program.cs
public static void Main(string[] args)
{
    var app = CreateHostBuilder(args).Build();
    
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.Migrate();  // Auto-apply migrations
    }
    
    app.Run();
}
```

## Environment Variables

```bash
# .env file (local, not committed)
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=Server=...;
Jwt__SecretKey=...
Jwt__Issuer=myapp
Jwt__Audience=myappusers

# Load in docker-compose
docker-compose --env-file .env up
```

## Health Checks

```csharp
// In Program.cs
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>()
    .AddUrlGroup(new Uri("https://example.com"), name: "ExternalAPI");

var app = builder.Build();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = reg => reg.Name.Contains("live")
});
```

## Logging & Monitoring

```csharp
// Serilog with Application Insights
builder.Host.UseSerilog((context, configuration) =>
    configuration
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.ApplicationInsights(
            telemetryClient,
            TelemetryConverter.Traces)
);

// Application Insights setup
builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();
app.UseApplicationInsightsRequestTelemetry();
```

## Graceful Shutdown

```csharp
// Shut down gracefully
var app = builder.Build();

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

lifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine("Application is shutting down...");
    // Complete in-flight requests, close connections
});

app.Run();
```

## Load Testing

```bash
# Apache Bench
ab -n 10000 -c 100 http://localhost:8080/

# wrk
wrk -t4 -c100 -d30s http://localhost:8080/
```

## Reverse Proxy Setup (Nginx)

```nginx
upstream dotnet_backend {
    server localhost:5000;
    server localhost:5001;
}

server {
    listen 80;
    server_name example.com;
    
    client_max_body_size 100M;

    location / {
        proxy_pass http://dotnet_backend;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # Health check endpoint
    location /health {
        access_log off;
        proxy_pass http://dotnet_backend;
    }
}
```

## Secrets Management

```bash
# Never commit secrets

# Option 1: Environment variables
export ConnectionString="..."
export JwtSecret="..."

# Option 2: Azure Key Vault
az keyvault secret set --vault-name myapp-kv --name ConnectionString --value "..."
az keyvault secret show --vault-name myapp-kv --name ConnectionString

# Access in code
var keyVaultUrl = new Uri("https://myapp-kv.vault.azure.net/");
var credential = new DefaultAzureCredential();
var client = new SecretClient(keyVaultUrl, credential);
KeyVaultSecret secret = await client.GetSecretAsync("ConnectionString");
```

## Monitoring & Alerting

```csharp
// Application Insights custom metrics
var telemetryClient = new TelemetryClient();

public class OrderService
{
    private readonly TelemetryClient _telemetryClient;

    public async Task ProcessOrderAsync(Order order)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Process order
            stopwatch.Stop();
            
            _telemetryClient.TrackEvent("OrderProcessed", 
                new Dictionary<string, string> 
                { 
                    { "OrderId", order.Id.ToString() }
                },
                new Dictionary<string, double>
                {
                    { "Duration", stopwatch.ElapsedMilliseconds }
                }
            );
        }
        catch (Exception ex)
        {
            _telemetryClient.TrackException(ex);
            throw;
        }
    }
}
```

## Rollback Strategy

```bash
# Keep previous releases
/releases/
  ├── v1.0.0/
  ├── v1.1.0/  (current)
  └── v1.0.1/

# Symlink to current
/app -> /releases/v1.1.0

# Rollback: change symlink
ln -snf /releases/v1.0.1 /app
systemctl restart dotnet-app
```

## Blue-Green Deployment

```yaml
# Keep two production environments
# Blue: current production
# Green: new version being deployed

# 1. Deploy to green
# 2. Test green
# 3. Switch traffic from blue to green
# 4. Keep blue as rollback point
```

## Practice Exercises

1. **Docker**: Create Dockerfile and docker-compose for your app
2. **GitHub Actions**: Set up CI pipeline with tests
3. **Azure Deploy**: Deploy to Azure App Service
4. **Health Checks**: Add health check endpoints
5. **Monitoring**: Set up logging and Application Insights

## Key Takeaways

- **Docker** containerizes applications for consistent deployment
- **GitHub Actions** automates build, test, deploy pipelines
- **Azure**, AWS, or other clouds for scalable hosting
- **Migrations** should run automatically on startup
- **Health checks** enable monitoring and load balancer detection
- **Graceful shutdown** handles in-flight requests
- **Secrets** via environment variables or Key Vault (never in code)
- **Blue-green** or canary deployments reduce risk
- **Logs** and **monitoring** provide production visibility
