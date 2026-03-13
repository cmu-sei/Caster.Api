# Caster.Api.Tests.Shared

Copyright 2026 Carnegie Mellon University. All Rights Reserved.

Shared test fixtures and utilities for Caster API test projects.

## Purpose

Provides common AutoFixture customizations and test data for both unit and integration tests. Prevents code duplication across test projects and ensures consistent test data generation for Caster's extensive entity model. Used with TUnit test framework.

## Files

- **Fixtures/CasterCustomization.cs** - AutoFixture customization that configures entity factory rules for all Caster domain entities. Prevents circular reference issues and ensures entities are created with valid default values for testing. Covers:
  - Project, Directory, File, FileVersion
  - Workspace, Run, Plan, Apply
  - Design, DesignModule, Module, ModuleVersion
  - Variable, Host, HostMachine
  - Vlan, Pool, Partition
  - User, Group, GroupMembership, ProjectMembership
  - SystemRole, ProjectRole
  - ContainerImage, RemovedResource

## Dependencies

- **Caster.Api** - Main API project (domain entities)
- **Crucible.Common.Testing** - Shared Crucible test utilities
- **AutoFixture** - Test data generation (via Crucible.Common.Testing)

## Usage

Reference this project from unit and integration test projects:

```xml
<ProjectReference Include="..\Caster.Api.Tests.Shared\Caster.Api.Tests.Shared.csproj" />
```

Apply the customization in tests:

```csharp
var fixture = new Fixture();
fixture.Customize(new CasterCustomization());
var project = fixture.Create<Project>();
```

## Key Patterns

- **Circular Reference Prevention** - Uses `OmitOnRecursionBehavior` to break circular dependencies in entity relationships
- **Navigation Property Exclusion** - Entity collections and navigation properties are excluded via `.Without()` to prevent complex object graph creation
- **Default Value Configuration** - Collections initialized to empty arrays/lists where needed
- **Path Management** - Directory entities automatically call `SetPath()` after creation

## Notes

Caster has the most complex entity model in the Crucible framework, including infrastructure-as-code concepts (Projects, Directories, Files, Workspaces), execution tracking (Runs, Plans, Applies), module management (Designs, Modules), and networking (VLANs, Pools, Partitions). The customization ensures all entities can be created independently for focused unit testing.
