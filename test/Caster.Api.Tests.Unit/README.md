# Caster.Api.Tests.Unit

Copyright 2026 Carnegie Mellon University. All Rights Reserved.

Unit tests for Caster API business logic, handlers, models, and infrastructure components.

## Purpose

Comprehensive unit test suite covering Caster's CQRS/MediatR handlers, domain models, Terraform integration, GitLab module parsing, and infrastructure utilities. Tests use FakeItEasy for mocking and Entity Framework Core InMemory provider for database operations.

## Files

### Handlers
- **Handlers/ProjectHandlerTests.cs** - CQRS handler tests for project operations (Create, Get, Update, Delete commands/queries)

### GitLab Integration
- **Gitlab/ModulesTests.cs** - GitLab module registry integration tests, parsing module metadata and version information

### Infrastructure
- **Infrastructure/ClaimsPrincipalExtensionsTests.cs** - Extension method tests for claims-based authentication and authorization
- **Infrastructure/ExceptionTests.cs** - Custom exception handling and error message tests

### Mapping
- **Mapping/MappingConfigurationTests.cs** - AutoMapper profile validation ensuring all mappings are valid and complete

### Models
- **Models/DirectoryTests.cs** - Directory entity model tests, path management, and hierarchy logic
- **Models/FileTests.cs** - File entity model tests, version tracking, and content management
- **Models/RunTests.cs** - Run entity model tests for Terraform execution tracking
- **Models/SystemRoleTests.cs** - SystemRole entity tests for permission management
- **Models/VlanTests.cs** - VLAN entity tests for network configuration
- **Models/WorkspaceTests.cs** - Workspace entity model tests, Terraform workspace state management

### Terraform
- **Terraform/PlanOutputTests.cs** - Terraform plan JSON output parsing and resource change detection
- **Terraform/StateTests.cs** - Terraform state file parsing, resource tracking, and output extraction

### Configuration
- **GlobalUsings.cs** - Global using directives for Caster domain models (Directory, File)
- **xunit.runner.json** - xUnit test runner configuration

### Test Data
- **Data/terraform.tfstate** - Sample Terraform state file for state parsing tests
- **Data/plan.json** - Sample Terraform plan JSON for plan output tests
- **Data/gitlab-modules.json** - Sample GitLab module registry response for module tests

## Dependencies

- **xUnit** 2.9.3 - Test framework
- **FakeItEasy** 8.3.0 - Mocking library (migrated from NSubstitute)
- **Microsoft.EntityFrameworkCore.InMemory** 10.0.1 - In-memory database for EF Core tests
- **Microsoft.NET.Test.Sdk** 18.0.1 - Test SDK
- **coverlet.collector** 6.0.2 - Code coverage collector
- **Caster.Api.Tests.Shared** - Shared test fixtures

## How to Run

From `/mnt/data/crucible/caster/caster.api/test/Caster.Api.Tests.Unit/`:

```bash
# Run all unit tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~ProjectHandlerTests"

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"
```

## Key Patterns

### CQRS/MediatR Testing
Caster uses the CQRS pattern with MediatR handlers instead of traditional services:

```csharp
// Example handler test structure
public class GetProjectHandler_Tests
{
    [Fact]
    public async Task Handle_ValidId_ReturnsProject()
    {
        // Arrange: Create handler with mocked dependencies
        var context = A.Fake<CasterContext>();
        var mapper = A.Fake<IMapper>();
        var handler = new GetProjectHandler(context, mapper);

        // Act: Execute handler
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert: Verify behavior
        Assert.NotNull(result);
    }
}
```

### Model Testing
Domain model tests verify business logic, validation, and computed properties:

```csharp
[Fact]
public void Directory_SetPath_CalculatesCorrectPath()
{
    var directory = new Directory { Name = "test", Parent = rootDir };
    directory.SetPath();
    Assert.Equal("/root/test", directory.Path);
}
```

### Terraform Integration Testing
Tests parse real Terraform JSON output to ensure compatibility:

```csharp
[Fact]
public void ParsePlanOutput_ValidJson_ExtractsResourceChanges()
{
    var json = File.ReadAllText("Data/plan.json");
    var plan = TerraformPlanParser.Parse(json);
    Assert.Equal(5, plan.ResourceChanges.Count);
}
```

### AutoMapper Validation
Ensures all AutoMapper profiles are correctly configured:

```csharp
[Fact]
public void AutoMapper_Configuration_IsValid()
{
    var config = new MapperConfiguration(cfg =>
        cfg.AddMaps(typeof(Startup).Assembly));
    config.AssertConfigurationIsValid();
}
```

## Test Coverage

This is the most comprehensive unit test suite in the Crucible framework, with 135+ tests covering:

- CQRS handlers for all major entities
- Domain model business logic
- Terraform state and plan parsing
- GitLab module registry integration
- Permission and authorization extensions
- AutoMapper configuration validation

## Migration Notes

- **Migrated from NSubstitute to FakeItEasy** for consistency with other Crucible projects
- Uses Entity Framework Core InMemory provider for database-dependent tests
- Test data files (terraform.tfstate, plan.json, gitlab-modules.json) are copied to output directory for test execution

## Related Projects

- **Caster.Api.Tests.Shared** - Shared test fixtures and AutoFixture customizations
- **Caster.Api.Tests.Integration** - Integration tests with real database and API endpoints
