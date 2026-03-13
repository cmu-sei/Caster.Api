# Caster.Api.Tests.Integration

Copyright 2026 Carnegie Mellon University. All Rights Reserved.

Integration tests for Caster API with real database and HTTP endpoints.

## Purpose

End-to-end integration testing of Caster API controllers and database operations using Testcontainers PostgreSQL and WebApplicationFactory. Tests verify API endpoints, database persistence, authentication flows, and complete request/response cycles.

## Files

### Fixtures
- **Fixtures/CasterTestContext.cs** - Custom WebApplicationFactory<Program> that configures:
  - Testcontainers PostgreSQL database for isolated test runs
  - In-memory test authentication
  - Service overrides for external dependencies
  - Database migration and seeding

### Tests
- **Tests/Controllers/HealthCheckTests.cs** - Health endpoint availability and response validation
- **Tests/Controllers/ProjectsControllerTests.cs** - Projects API endpoint tests:
  - GET /api/projects (list)
  - GET /api/projects/{id} (details)
  - POST /api/projects (create)
  - PUT /api/projects/{id} (update)
  - DELETE /api/projects/{id} (delete)
  - Authorization and permission checks

### Configuration
- **GlobalUsings.cs** - Global using directives for xUnit
- **xunit.runner.json** - xUnit test runner configuration for parallel execution

## Dependencies

- **xUnit** 2.9.3 - Test framework
- **Microsoft.AspNetCore.Mvc.Testing** 10.0.1 - WebApplicationFactory for integration testing
- **Testcontainers.PostgreSql** 4.0.0 - Docker-based PostgreSQL for isolated test database
- **AutoFixture** 4.18.1 - Test data generation
- **AutoFixture.Xunit2** 4.18.1 - AutoFixture integration with xUnit
- **FakeItEasy** 8.3.0 - Mocking library for external service dependencies
- **Shouldly** 4.2.1 - Fluent assertion library
- **Npgsql.EntityFrameworkCore.PostgreSQL** 10.0.0 - PostgreSQL EF Core provider
- **Microsoft.EntityFrameworkCore** 10.0.1 - Entity Framework Core
- **coverlet.collector** 6.0.2 - Code coverage collector
- **Caster.Api.Tests.Shared** - Shared test fixtures
- **Crucible.Common.Testing** - Shared Crucible test utilities

## How to Run

From `/mnt/data/crucible/caster/caster.api/test/Caster.Api.Tests.Integration/`:

```bash
# Run all integration tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~ProjectsControllerTests"

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"
```

## Prerequisites

- **Docker** must be running for Testcontainers to start PostgreSQL
- Tests automatically download and start PostgreSQL container
- Each test run uses an isolated database instance

## Key Patterns

### WebApplicationFactory Setup

```csharp
public class CasterTestContext : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _dbContainer;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Replace real database with test container
            services.RemoveDbContext<CasterContext>();
            services.AddDbContext<CasterContext>(options =>
                options.UseNpgsql(_dbContainer.GetConnectionString()));

            // Mock external services
            services.AddSingleton<IPlayerVmApiClient>(A.Fake<IPlayerVmApiClient>());
        });
    }
}
```

### Controller Testing

```csharp
public class ProjectsControllerTests : IClassFixture<CasterTestContext>
{
    private readonly HttpClient _client;

    [Fact]
    public async Task GetProjects_ReturnsOk()
    {
        // Arrange
        var project = fixture.Create<Project>();
        await SeedDatabase(project);

        // Act
        var response = await _client.GetAsync("/api/projects");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var projects = await response.Content.ReadAsAsync<List<ProjectDto>>();
        projects.ShouldContain(p => p.Id == project.Id);
    }
}
```

### Database Seeding

```csharp
private async Task SeedDatabase(params object[] entities)
{
    using var scope = _factory.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<CasterContext>();

    context.AddRange(entities);
    await context.SaveChangesAsync();
}
```

### Authentication Testing

```csharp
[Fact]
public async Task CreateProject_WithoutPermission_ReturnsForbidden()
{
    // Arrange
    _client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", GenerateTokenWithoutPermissions());

    // Act
    var response = await _client.PostAsJsonAsync("/api/projects", newProject);

    // Assert
    response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
}
```

## Test Isolation

- Each test class gets a fresh PostgreSQL container via `IClassFixture<CasterTestContext>`
- Database is automatically migrated before tests run
- Tests can seed data independently without affecting other tests
- Testcontainers automatically cleans up containers after tests complete

## Performance Considerations

- Container startup adds overhead (~5-10 seconds per test class)
- Tests within a class share the same container for performance
- Use `IClassFixture` for shared setup across tests in a class
- Use `ICollectionFixture` for shared setup across multiple test classes

## Testcontainers Configuration

The PostgreSQL container is configured with:
- **Image**: postgres:latest
- **Port**: Random available port (avoids conflicts)
- **Database**: Automatically created with random name
- **Credentials**: Generated for test isolation
- **Wait Strategy**: Waits for PostgreSQL to be ready before running tests

## Related Projects

- **Caster.Api.Tests.Shared** - Shared test fixtures and AutoFixture customizations
- **Caster.Api.Tests.Unit** - Unit tests with mocked dependencies
