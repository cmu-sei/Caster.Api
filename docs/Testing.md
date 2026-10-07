Caster.Api has an automated test suite in the `test/Caster.Api.Tests` project. This document details how the suite is built, how to run it, and what is specific to Caster. The shared parts (the harness files, the conventions, the defect documents, the scripts) are the Crucible API test standard in `agent-docs/api-testing/` of the workspace; this document points at it rather than repeating it.

# Testing

The suite is built on xUnit v3 and NSubstitute, and runs against a real PostgreSQL instance started in a container, migrated with Caster's own migrations. The harness files under `test/Caster.Api.Tests/Support/Shared/` are copied from the standard and are not edited here. Most tests are not isolated unit tests: a typical test sends an HTTP request to the application hosted in process, through the real routes, the real middleware, the real claims transformer, the real MediatR handlers and their validators, the real AutoMapper profiles and a real database, then asserts on the response and on what changed in the database. Only collaborators that leave the process are replaced. The pure unit tests (Terraform state and plan parsing, GitLab modules, file-name and interface validation, Terraform version validation, Kubernetes labels, the run queue, the workspace file-system helpers) need no database and run without Docker.

# Running the tests

```bash
dotnet test test/Caster.Api.Tests
```

Docker must be running for every test that takes a database. The suite starts and disposes its own PostgreSQL container through [Testcontainers](https://testcontainers.com/), so there is nothing to install and no local database to keep in sync. The container starts when the first test asks for a database.

A single class or test can be run with a filter (the leading dot keeps `RunRequestTests` from also matching other classes that end the same way):

```bash
dotnet test test/Caster.Api.Tests --filter "FullyQualifiedName~.WorkspaceRequestTests"
dotnet test test/Caster.Api.Tests --filter "FullyQualifiedName~.WorkspaceRequestTests.Get_returns_the_workspace_to_a_member_holding_ViewProject"
```

To see which database provider the run used, pass `-- xUnit.DiagnosticMessages=true`; the output then carries `[Caster.Api.Tests] database provider: PostgreSQL (postgres:16-alpine, real migrations)`. Never set `diagnosticMessages` in `xunit.runner.json`: the run then hangs after the last test.

# Coverage

```bash
dotnet test test/Caster.Api.Tests --collect:"XPlat Code Coverage"
```

`coverlet.runsettings` (shared by the standard) is applied automatically through `RunSettingsFilePath` in `Caster.Api.Tests.csproj`, with its collector disabled by default, so a plain run collects nothing and a coverage run cannot lose its exclusions. Caster's migrations live in the API assembly, so they are excluded by the `**/Migrations/**` file filter rather than by assembly. Coverage is a local diagnostic; CI neither collects nor gates on it.

# Build settings

- `test/Caster.Api.Tests/Directory.Build.props` (shared) turns `TreatWarningsAsErrors` on for the test project, which makes the xUnit analyzers fail the build (xUnit1051, a call that does not take the test's cancellation token; xUnit2000; xUnit2012; xUnit1026). NuGet audit and restore codes (NU1901-NU1904, NU1510, NU1701) stay warnings.
- The root `.editorconfig` raises xUnit1004, so a `[Fact(Skip = ...)]` fails the build.
- The repository has no root `Directory.Build.props`, so the application project is not built with warnings as errors, as the standard intends, and it reports warnings today: CS0168 (`Domain/Models/Resource.cs`), CS0618 (`Domain/Services/ArchiveService.cs`), CS9107 (the SignalR event handlers), CS9113 (unread constructor parameters in Projects/Create, Projects/Delete, Workspaces/Create, Workspaces/Delete, SystemRoles/Delete and Vlan DeletePartition), CA2017 and CA2200 (`FileVersionScrubService`, `PlayerSyncService`), and the restore warning NU1608 (Microsoft.CodeAnalysis 5.0.0 against Workspaces.MSBuild 4.14.0). The standard's `verify.sh` counts only the test project's own warnings and lists these under its PASS line as `note: application projects warn (not counted)`; the test project builds with no warning. Fixing them is application work.
- Caster has no central package management, so the test packages carry their versions in `Caster.Api.Tests.csproj`, pinned by the standard (`agent-docs/api-testing/test-packages.props`; `sync.sh` checks them). Implicit usings and nullable reference types are off, as in `Caster.Api`.
- The suite runs in the VSTest mode of `dotnet test`; see the standard's README for why not Microsoft.Testing.Platform.

# How the harness works

The mechanics are the standard's (README, "How the harness works"). What Caster supplies:

## Fixtures

- `Support/DatabaseFixture.cs`: an assembly fixture over the shared `PostgresTestDatabase<CasterContext>`. No `MigrationsAssembly`: Caster's migrations are in `Caster.Api` beside the context. Each test gets a database cloned from the migrated template and dropped afterwards. It also hands the host one throwaway database (`HostDatabase()`, below).
- `Support/CasterContextFactory.cs`: builds `CasterContext` with the entity event interceptor and a provider holding a substituted mediator, which `DatabaseTestBase.Mediator` exposes.
- `Support/CasterAppFactory.cs`: the run-wide `WebApplicationFactory<Program>`, one host for the whole run (the standard's default; nothing in Caster needs a per-class host).

## The host database (step 1B)

`Program.Main` always runs `InitializeDatabase()` (migrate, then apply `SeedData`), and Caster has no switch that skips it. `WebApplicationFactory` runs `Main`, so the factory points `ConnectionStrings:PostgreSQL` at a database cloned from the template for the host alone. `InitializeDatabase` resolves `CasterContext` outside any request, which the one-argument `TestDatabaseScope.ReplaceRegistration` refuses, so the factory uses the shared two-argument form: until the host has started, that resolution reaches the host database, over the host session's own services (so the seed's entity events reach no handler or recorder); after that, a context resolved outside a request throws. `CasterAppFactory.CreateHost` builds the one host under a lock, so two tests starting at once cannot build two. Requests always reach their own test's database through the `X-Test-Session` header.

## Base classes

- `Support/DatabaseTestBase.cs`: `Db`, `NewContext()`, `Seed`, `Mediator`, `Ct`, from the shared core.
- `Support/ApiTestBase.cs`: adds `Root` (an actor on the seeded Administrator role), `RootClient`, `Actor()` and `Client(actor)`.
- There is no `ServiceTestBase`: nothing in Caster needs a service built with per-test options.

## Actors

`Support/TestActor.cs` mirrors `UserClaimsService.GetPermissionClaims`:

- `WithSystemPermissions(...)` mints a system role for the actor; `WithRole(id)` and `WithAllSystemPermissions()` (the seeded Administrator) name one.
- `OnProject(project, permissions)` puts the actor on a project with a project role minted for exactly those permissions (or `roleId:` names a seeded one). A membership always has a role, and the default is the seeded Member role (ViewProject, EditProject, ImportProject), so `OnProject` refuses a call that names neither rather than inheriting it.
- `OnNewProject(permissions)` mints a project of the actor's own: the near miss "the right permission on another project".
- `OnProjectThroughNewGroup(project, permissions)` puts the actor in a new group and the group on the project, the path by which a group grants its members project permissions.
- `InGroup(group, role)` and `OnNewGroup(role)`: a group Manager holds `GroupPermission.ManageMembership` on it, a Member nothing.

`Support/TestActorTests.cs` pins each shape through the real claims service.

## What is real, and what is not

Replaced: token validation (`TestAuthHandler`), the `CasterContext` registration, and the collaborators that leave the process:

- `IHubContext<ProjectHub>` is a `HubRecorder<ProjectHub>` (`Factory.Hub<ProjectHub>().ToGroup(id)`).
- `IHttpClientFactory` is the shared `StubHttpClientFactory` over `Factory.OutboundHttp` (Player VM API, identity provider, GitLab), given the base address production's named-client registration sets for the "gitlab" client (`Terraform:GitlabApiUrl`).
- The four hosted services (`FileVersionScrubService`, `PlayerSyncService`, `ImageTagService`, `RunQueueService`) are removed. `IRunQueueService` stays registered, so a created run or apply is queued and never executed, and no Terraform binary ever runs.

`Support/TestConfiguration.cs` turns the claims cache and IdP groups off (the token `TestAuthHandler` mints carries no roles, so `UseRolesFromIdP` is left as shipped), points `Terraform:BinaryPath` at a temporary directory with one empty sub-directory per version (`0.12.29`, `1.5.7`, which is all `ProcessTerraformService.IsValidVersion` and `GetVersions` read) and `Terraform:RootWorkingDirectory` at another, and sets `Terraform:GitlabApiUrl`. The host runs as Production, so a 500's body carries the exception message as its detail and no stack trace.

The resource commands (taint, untaint, remove, refresh, import, outputs) run Terraform once past their gate. Their allowed cases seed a queued run on the workspace, so the handler answers 409 for the busy workspace, which it can only reach after the gate let the caller through.

Hubs and handlers the recorder cannot observe are driven directly: `Hubs/ProjectHubTests.cs` runs `ProjectHub`'s join methods through `HubHarness` over the real `AuthorizationService` (built by `Support/AuthorizationHarness.cs`), and `Features/Runs/EventHandlers/RunSignalRHandlerTests.cs` drives the run broadcasts, which go to two groups at once through `Clients.Groups(...)`, over a hub context the test owns.

# Adding a test

Follow the standard's CONVENTIONS.md (sections 1 and 2): derive from `ApiTestBase` to send a request, `DatabaseTestBase` to use only a database, or nothing for a pure test; seed with `TestData` mothers and `Actor()`; name the method as a sentence; pass `Ct`; re-read through `NewContext()`.

Every handler's `Authorize()` override is a gate. For each endpoint, test the caller holding exactly the project permission, the caller holding the system permission, and the near misses: another project permission on the same project, the same permission on another project (`OnNewProject`), and a neighbouring system permission. Requests are validated before they are authorized (`ValidationBehavior` is a MediatR pre-processor), so a denied test sends a body that passes validation.

A defect a test finds is characterized by a passing test of the current behaviour and described in `agent-docs/api-test-bugs/caster.api.md`, never in the test (CONVENTIONS.md section 3).

# Layout

The project mirrors `src/Caster.Api`:

- `Features/<Area>/<Area>RequestTests.cs`: the endpoints of each feature slice, plus `Features/Files/FileNameValidationTests.cs` (the validators) and `Features/Runs/EventHandlers/`.
- `Domain/Models/`, `Domain/Services/`: the pure unit tests, `RunQueueServiceTests`, `UserClaimsServiceTests`, and `Domain/Services/Terraform/ProcessTerraformServiceTests` (version validation before a process binary runs).
- `Features/Shared/Behaviors/InterfaceValidationTests.cs`: `ValidationBehavior` running the validators declared over request interfaces.
- `Data/CasterContextTests.cs`: the schema's relationships, cascades and indexes.
- `Hubs/ProjectHubTests.cs`; `Infrastructure/Authorization/`, `Infrastructure/Exceptions/`, `Infrastructure/Mapping/`, `Infrastructure/Utilities/` (`KubernetesLabelTests`).
- `Data/*.json`, `Data/terraform.tfstate`: fixtures the unit tests read.
- `Support/`: the template-derived harness, the extra `ArchiveHelper` (builds the zip archives the import endpoints read), and the harness self-tests (`DatabaseHarnessTests`, `HttpHarnessTests`, `TestActorTests`); `Support/Shared/`: the standard's shared files.

# Continuous integration

`.github/workflows/build-and-test.yml` (the standard's workflow) restores, builds and runs `dotnet test test/Caster.Api.Tests` on pull requests and pushes to `main`, then greps the log for the PostgreSQL provider banner and uploads the TRX results. Check the setup with `agent-docs/api-testing/verify.sh --app caster.api <repo>` and `sync.sh --check <repo>`.
