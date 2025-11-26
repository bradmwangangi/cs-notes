# 35. CI/CD Pipelines

## Overview
CI/CD pipelines automate building, testing, and deploying code changes. This eliminates manual error, enables frequent deployments, and provides quick feedback. Enterprise systems require sophisticated pipelines with multiple environments, quality gates, and sophisticated deployment coordination.

---

## 1. CI/CD Fundamentals

### 1.1 Pipeline Stages

```
Code Push
  ↓
[CONTINUOUS INTEGRATION]
  ├─ Checkout code
  ├─ Restore dependencies
  ├─ Build
  ├─ Run unit tests
  ├─ Run integration tests
  ├─ Code analysis (SonarQube)
  ├─ Security scanning
  └─ Artifact creation
        ↓
   [ARTIFACT REPOSITORY]
   (Docker, NuGet, etc.)
        ↓
[CONTINUOUS DEPLOYMENT]
  ├─ Deploy to staging
  ├─ Run smoke tests
  ├─ Approve manually
  └─ Deploy to production
        ↓
[MONITORING]
  ├─ Health checks
  ├─ Error rates
  ├─ Performance metrics
  └─ Automated rollback if needed
```

### 1.2 Pipeline Characteristics

```
Speed:        Fast feedback (5-15 minutes)
Reliability:  Consistent, reproducible builds
Safety:       Quality gates prevent bad code
Visibility:   Real-time status, clear failures
Reversible:   Easy rollback of bad deployments
```

---

## 2. GitHub Actions Pipeline

### 2.1 Basic Build Pipeline

```yaml
# .github/workflows/build.yml

name: Build and Test

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main, develop]

jobs:
  build:
    runs-on: ubuntu-latest
    
    services:
      postgres:
        image: postgres:15
        env:
          POSTGRES_PASSWORD: postgres
          POSTGRES_DB: testdb
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
        ports:
          - 5432:5432
    
    steps:
      # Step 1: Checkout code
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0  # Full history for versioning
      
      # Step 2: Setup .NET
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      # Step 3: Cache dependencies
      - uses: actions/cache@v3
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/packages.lock.json') }}
          restore-keys: |
            ${{ runner.os }}-nuget-
      
      # Step 4: Restore dependencies
      - name: Restore dependencies
        run: dotnet restore
      
      # Step 5: Build
      - name: Build
        run: dotnet build --configuration Release --no-restore
      
      # Step 6: Run unit tests
      - name: Run unit tests
        run: dotnet test UnitTests/ --configuration Release --no-build --logger "trx;LogFileName=test-results.trx"
      
      # Step 7: Run integration tests
      - name: Run integration tests
        run: dotnet test IntegrationTests/ --configuration Release --no-build
        env:
          ConnectionStrings__DefaultConnection: "Host=localhost;Username=postgres;Password=postgres;Database=testdb"
      
      # Step 8: Publish test results
      - name: Publish test results
        uses: EnricoMi/publish-unit-test-result-action@v2
        if: always()
        with:
          files: '**/test-results.trx'
          check_name: Unit Test Results
      
      # Step 9: Code coverage
      - name: Generate code coverage
        run: dotnet test --configuration Release --collect:"XPlat Code Coverage" --logger "trx"
      
      # Step 10: Upload coverage to Codecov
      - name: Upload coverage to Codecov
        uses: codecov/codecov-action@v3
        with:
          files: ./coverage.xml
          fail_ci_if_error: false
```

### 2.2 Quality Gates

```yaml
# .github/workflows/quality.yml

name: Code Quality

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main, develop]

jobs:
  code-quality:
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      # SonarQube analysis
      - name: Install SonarCloud scanner
        run: dotnet tool install --global dotnet-sonarscanner
      
      - name: SonarCloud analysis
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
        run: |
          dotnet sonarscanner begin \
            /k:"bookstore_order-service" \
            /o:"company-org" \
            /d:sonar.host.url="https://sonarcloud.io" \
            /d:sonar.login="${{ env.SONAR_TOKEN }}"
          
          dotnet build --configuration Release
          
          dotnet sonarscanner end /d:sonar.login="${{ env.SONAR_TOKEN }}"
      
      # StyleCop analysis
      - name: Run StyleCop
        run: dotnet build --no-restore /p:StyleCopTreatErrorsAsWarnings=false
      
      # Security scanning
      - name: Run security scan
        run: dotnet list package --vulnerable
      
      # Check code coverage threshold
      - name: Check coverage threshold
        run: |
          # Fail if coverage < 75%
          coverage=$(grep -oP 'LineCoverage>\K[^<]+' coverage.xml)
          if (( $(echo "$coverage < 75" | bc -l) )); then
            echo "Coverage $coverage% is below 75% threshold"
            exit 1
          fi
```

### 2.3 Build and Push Docker Image

```yaml
# .github/workflows/docker.yml

name: Build and Push Docker Image

on:
  push:
    branches: [main]
    tags: [v*]

jobs:
  docker:
    runs-on: ubuntu-latest
    
    permissions:
      contents: read
      packages: write
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v2
      
      # Generate version from git tag or commit
      - name: Generate version
        id: version
        run: |
          if [[ $GITHUB_REF == refs/tags/* ]]; then
            VERSION=${GITHUB_REF#refs/tags/}
          else
            VERSION=latest
          fi
          echo "version=$VERSION" >> $GITHUB_OUTPUT
      
      - name: Log in to Docker Hub
        uses: docker/login-action@v2
        with:
          username: ${{ secrets.DOCKER_USERNAME }}
          password: ${{ secrets.DOCKER_PASSWORD }}
      
      - name: Log in to GitHub Container Registry
        uses: docker/login-action@v2
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      
      - name: Build and push Docker image
        uses: docker/build-push-action@v4
        with:
          context: .
          push: true
          tags: |
            bookstore/order-service:${{ steps.version.outputs.version }}
            bookstore/order-service:latest
            ghcr.io/${{ github.repository }}:${{ steps.version.outputs.version }}
          cache-from: type=registry,ref=bookstore/order-service:buildcache
          cache-to: type=registry,ref=bookstore/order-service:buildcache,mode=max
          build-args: |
            VERSION=${{ steps.version.outputs.version }}
            BUILD_DATE=$(date -u +'%Y-%m-%dT%H:%M:%SZ')
            VCS_REF=${{ github.sha }}
```

---

## 3. Azure Pipelines

### 3.1 Multi-Stage Pipeline

```yaml
# azure-pipelines.yml

trigger:
  branches:
    include:
    - main
    - develop
  paths:
    exclude:
    - README.md
    - docs/*

pr:
  branches:
    include:
    - main
    - develop

variables:
  buildConfiguration: 'Release'
  dotnetVersion: '8.0.x'
  dockerImageName: 'bookstore/order-service'

stages:

# Stage 1: Build and Test
- stage: Build
  displayName: 'Build and Test'
  
  jobs:
  - job: BuildJob
    displayName: 'Build'
    pool:
      vmImage: 'ubuntu-latest'
    
    steps:
    - task: UseDotNet@2
      displayName: 'Install .NET'
      inputs:
        packageType: 'sdk'
        version: $(dotnetVersion)
    
    - task: DotNetCoreCLI@2
      displayName: 'Restore'
      inputs:
        command: 'restore'
        projects: '**/*.csproj'
    
    - task: DotNetCoreCLI@2
      displayName: 'Build'
      inputs:
        command: 'build'
        arguments: '--configuration $(buildConfiguration)'
    
    - task: DotNetCoreCLI@2
      displayName: 'Run unit tests'
      inputs:
        command: 'test'
        arguments: '--configuration $(buildConfiguration) --logger trx --collect:"XPlat Code Coverage"'
        publishTestResults: true
    
    - task: PublishCodeCoverageResults@1
      displayName: 'Publish code coverage'
      inputs:
        codeCoverageTool: Cobertura
        summaryFileLocation: '$(Agent.TempDirectory)/**/*coverage.cobertura.xml'

  - job: SecurityScan
    displayName: 'Security Scanning'
    pool:
      vmImage: 'ubuntu-latest'
    
    steps:
    - task: UseDotNet@2
      inputs:
        packageType: 'sdk'
        version: $(dotnetVersion)
    
    - task: DotNetCoreCLI@2
      displayName: 'Check vulnerable packages'
      inputs:
        command: 'custom'
        custom: 'list'
        arguments: 'package --vulnerable'
    
    - task: WhiteSource@21
      displayName: 'WhiteSource scan'
      inputs:
        cwd: '.'

# Stage 2: Deploy to Staging
- stage: DeployStaging
  displayName: 'Deploy to Staging'
  dependsOn: Build
  condition: succeeded()
  
  jobs:
  - job: Deploy
    pool:
      vmImage: 'ubuntu-latest'
    
    steps:
    - task: AzureWebApp@1
      displayName: 'Deploy to Azure App Service'
      inputs:
        azureSubscription: 'Production'
        appType: 'webAppLinux'
        appName: 'order-service-staging'
        package: '$(Pipeline.Workspace)/drop'
        runtimeStack: 'DOTNETCORE|8.0'
    
    - task: AzureAppServiceManage@0
      displayName: 'Start application'
      inputs:
        azureSubscription: 'Production'
        action: 'Start Azure App Service'
        appName: 'order-service-staging'

  - job: SmokeTest
    dependsOn: Deploy
    pool:
      vmImage: 'ubuntu-latest'
    
    steps:
    - task: UseDotNet@2
      inputs:
        packageType: 'sdk'
        version: $(dotnetVersion)
    
    - task: DotNetCoreCLI@2
      displayName: 'Run smoke tests'
      inputs:
        command: 'test'
        arguments: '--configuration $(buildConfiguration) SmokeTests/'

# Stage 3: Deploy to Production (Manual Approval)
- stage: DeployProduction
  displayName: 'Deploy to Production'
  dependsOn: DeployStaging
  condition: succeeded()
  
  jobs:
  - job: waitForValidation
    displayName: 'Wait for manual approval'
    pool: server
    timeoutInMinutes: 1440  # 24 hours
    
    steps:
    - task: ManualValidation@0
      timeoutInMinutes: 1440
      inputs:
        notifyUsers: 'devops@company.com'
        instructions: 'Review staging deployment and approve for production'

  - job: DeployProd
    displayName: 'Deploy to Production'
    dependsOn: waitForValidation
    pool:
      vmImage: 'ubuntu-latest'
    
    steps:
    - task: AzureWebApp@1
      displayName: 'Deploy to Production'
      inputs:
        azureSubscription: 'Production'
        appType: 'webAppLinux'
        appName: 'order-service-prod'
        package: '$(Pipeline.Workspace)/drop'
        runtimeStack: 'DOTNETCORE|8.0'
    
    - task: AzureMonitorAlerts@0
      displayName: 'Monitor deployment'
      inputs:
        azureSubscription: 'Production'
        resourceGroupName: 'production'
        resourceName: 'order-service-prod'
        alertRule: 'HighErrorRate'
```

---

## 4. Pipeline Optimization

### 4.1 Caching and Parallelization

```yaml
# .github/workflows/optimized.yml

name: Optimized Pipeline

on:
  push:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        dotnet-version: ['8.0.x']
    
    steps:
      - uses: actions/checkout@v4
      
      # Cache NuGet packages
      - uses: actions/cache@v3
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/packages.lock.json') }}
          restore-keys: ${{ runner.os }}-nuget-
      
      # Cache Docker layers
      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v2
      
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: ${{ matrix.dotnet-version }}
      
      - run: dotnet restore
      - run: dotnet build --configuration Release --no-restore

  test-unit:
    needs: build
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      - run: dotnet restore
      - run: dotnet test UnitTests/ --configuration Release

  test-integration:
    needs: build
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
    
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      - run: dotnet restore
      - run: dotnet test IntegrationTests/ --configuration Release

  # Parallel jobs for different concerns
  analyze-code:
    needs: build
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      - run: dotnet tool install --global dotnet-format
      - run: dotnet format --verify-no-changes --verbosity diagnostic

  security-scan:
    needs: build
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      - run: dotnet restore
      - run: dotnet list package --vulnerable
```

### 4.2 Artifact Management

```yaml
# .github/workflows/artifact.yml

name: Build Artifacts

on:
  push:
    branches: [main]

jobs:
  build-publish:
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      # Version from git tag or semantic versioning
      - name: Calculate version
        id: versioning
        run: |
          VERSION=$(git describe --tags --always)
          echo "version=$VERSION" >> $GITHUB_OUTPUT
      
      # Build and package
      - name: Build
        run: dotnet build --configuration Release
      
      - name: Publish
        run: dotnet publish -c Release -o release
      
      # Create NuGet package
      - name: Create NuGet package
        run: dotnet pack --configuration Release --output artifacts
      
      # Upload as artifacts
      - name: Upload build artifacts
        uses: actions/upload-artifact@v3
        with:
          name: build-${{ steps.versioning.outputs.version }}
          path: release/
          retention-days: 30
      
      # Publish to NuGet
      - name: Publish to NuGet
        run: |
          dotnet nuget push artifacts/*.nupkg \
            --api-key ${{ secrets.NUGET_API_KEY }} \
            --source https://api.nuget.org/v3/index.json \
            --skip-duplicate
      
      # Create GitHub Release
      - name: Create GitHub Release
        uses: softprops/action-gh-release@v1
        if: startsWith(github.ref, 'refs/tags/')
        with:
          files: artifacts/*.nupkg
          generate_release_notes: true
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

---

## 5. Advanced Pipeline Patterns

### 5.1 Matrix Builds

```yaml
# Test against multiple frameworks
name: Matrix Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ${{ matrix.os }}
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
        dotnet-version: ['7.0.x', '8.0.x']
        exclude:
          # Don't test macOS with net7 (unsupported combo)
          - os: macos-latest
            dotnet-version: '7.0.x'
    
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: ${{ matrix.dotnet-version }}
      
      - run: dotnet restore
      - run: dotnet test --configuration Release
```

### 5.2 Notifications and Alerts

```yaml
# .github/workflows/notifications.yml

name: Deployment Notifications

on:
  workflow_run:
    workflows: [Deploy to Production]
    types: [completed]

jobs:
  notify:
    runs-on: ubuntu-latest
    
    steps:
      - name: Notify Slack on success
        if: ${{ github.event.workflow_run.conclusion == 'success' }}
        uses: slackapi/slack-github-action@v1
        with:
          payload: |
            {
              "text": "✅ Deployment successful",
              "blocks": [
                {
                  "type": "section",
                  "text": {
                    "type": "mrkdwn",
                    "text": "*Deployment Successful*\nRepository: ${{ github.repository }}\nRef: ${{ github.event.workflow_run.head_branch }}\nCommit: ${{ github.event.workflow_run.head_commit.id }}"
                  }
                }
              ]
            }
        env:
          SLACK_WEBHOOK_URL: ${{ secrets.SLACK_WEBHOOK_URL }}
      
      - name: Notify Slack on failure
        if: ${{ github.event.workflow_run.conclusion == 'failure' }}
        uses: slackapi/slack-github-action@v1
        with:
          payload: |
            {
              "text": "❌ Deployment failed",
              "blocks": [
                {
                  "type": "section",
                  "text": {
                    "type": "mrkdwn",
                    "text": "*Deployment Failed*\nRepository: ${{ github.repository }}\nRef: ${{ github.event.workflow_run.head_branch }}\nRun URL: ${{ github.event.workflow_run.html_url }}"
                  }
                }
              ]
            }
        env:
          SLACK_WEBHOOK_URL: ${{ secrets.SLACK_WEBHOOK_URL }}
      
      - name: Create incident on failure
        if: ${{ github.event.workflow_run.conclusion == 'failure' }}
        uses: PagerDuty/create-incident-action@v2
        with:
          routing-key: ${{ secrets.PAGERDUTY_ROUTING_KEY }}
          dedup-key: deployment-failure-${{ github.run_id }}
          event-action: trigger
          severity: error
          event-source: github-actions
          summary: "Deployment failed for ${{ github.repository }}"
          details: "See ${{ github.event.workflow_run.html_url }}"
```

---

## 6. Pipeline Security

### 6.1 Secrets Management

```csharp
// Access secrets in code
public class SecretConfiguration
{
    public static void ConfigureSecrets(IConfigurationBuilder config)
    {
        // Secrets from Azure Key Vault
        var keyVaultEndpoint = new Uri(Environment.GetEnvironmentVariable("KEY_VAULT_URL"));
        
        config.AddAzureKeyVault(
            keyVaultEndpoint,
            new DefaultAzureCredential()
        );
        
        // Secrets from GitHub
        var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");
        var apiKey = Environment.GetEnvironmentVariable("API_KEY");
    }
}
```

```yaml
# GitHub Secrets in Actions
- name: Deploy with secrets
  env:
    DB_PASSWORD: ${{ secrets.DB_PASSWORD }}
    API_KEY: ${{ secrets.API_KEY }}
    DOCKER_PASSWORD: ${{ secrets.DOCKER_PASSWORD }}
  run: |
    # Secrets automatically masked in logs
    echo "Deploying with credentials..."
    dotnet run
```

### 6.2 Protected Branches and Reviews

```yaml
# Branch protection rules (via settings or API)
- Require pull request reviews before merging
- Require status checks to pass before merging
- Require code review from code owners
- Require up-to-date branches before merging
- Require signed commits
- Auto-delete head branches

# CODEOWNERS file
docs/* @technical-writers
src/OrderService/* @order-team
src/PaymentService/* @payment-team
src/Infrastructure/* @platform-team

# .github/pull_request_template.md
## Description
Describe the changes here

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update

## Testing
- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Tested in staging

## Checklist
- [ ] Code follows style guidelines
- [ ] No new warnings generated
- [ ] Database migrations included
- [ ] Documentation updated
```

---

## 7. Monitoring Pipeline Health

### 7.1 Pipeline Metrics

```csharp
public class PipelineMetrics
{
    // Track pipeline performance
    
    public string PipelineId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    
    public int TotalTests { get; set; }
    public int PassedTests { get; set; }
    public int FailedTests { get; set; }
    
    public decimal PassRate => TotalTests > 0 
        ? (decimal)PassedTests / TotalTests 
        : 0;
    
    public decimal CodeCoveragePercentage { get; set; }
    
    public int SecurityIssuesFound { get; set; }
    
    public PipelineStatus Status { get; set; }
    
    public List<string> FailedStages { get; set; }
}

public enum PipelineStatus
{
    Success,
    PartialFailure,
    Failed,
    Cancelled
}

// Analyze pipeline metrics
public class PipelineAnalyzer
{
    public bool IsPipelineHealthy(PipelineMetrics metrics)
    {
        return metrics.Status == PipelineStatus.Success &&
               metrics.PassRate >= 0.95m &&  // 95% pass rate
               metrics.CodeCoveragePercentage >= 0.75m &&  // 75% coverage
               metrics.SecurityIssuesFound == 0;
    }
    
    public TimeSpan GetAveragePipelineTime(List<PipelineMetrics> runs)
    {
        return TimeSpan.FromMilliseconds(
            runs.Average(m => m.Duration.TotalMilliseconds)
        );
    }
}
```

### 7.2 Pipeline Dashboards

```yaml
# Monitor pipeline health over time

Dashboard: CI/CD Health
├─ Build Success Rate (should be > 95%)
├─ Average Build Time (trend analysis)
├─ Test Coverage (track improvement)
├─ Security Issues Found (0 high/critical)
├─ Deployment Frequency (times per week)
├─ Lead Time for Changes (how fast from commit to prod)
├─ Mean Time to Recovery (how fast to recover from issues)
└─ Change Failure Rate (% of changes that cause issues)
```

---

## Summary

CI/CD pipelines provide:
- **Automation**: Build, test, deploy without manual intervention
- **Consistency**: Same process every time
- **Speed**: Deploy multiple times daily
- **Quality**: Automatic testing and analysis
- **Visibility**: Clear status of every change
- **Safety**: Multiple stages before production

Key components:
1. **Build Stage**: Compile, restore dependencies
2. **Test Stage**: Unit, integration, smoke tests
3. **Quality Gate**: Code analysis, security scanning, coverage
4. **Artifact**: Package and store build output
5. **Staging**: Deploy to staging environment
6. **Approval**: Manual review and approval
7. **Production**: Deploy to production
8. **Monitor**: Health checks and rollback if needed

Best practices:
- Run tests in parallel for speed
- Cache dependencies to reduce build time
- Fail fast on critical checks
- Require code review and quality gates
- Automate everything possible
- Monitor pipeline metrics
- Secure secrets with environment variables
- Notify team on failures
- Keep pipelines simple and maintainable

Next topic covers Cloud Platforms for enterprise deployment.
