Caster.Api has an automated test suite in the `test/Caster.Api.Tests` project. This document details how the suite is built, how to run it, and what is specific to Caster. The shared parts (the harness files, the conventions, the defect documents, the scripts) are the Crucible API test standard in `agent-docs/api-testing/` of the workspace; this document points at it rather than repeating it.

# Testing

The suite is built on xUnit v3 and NSubstitute, and runs against a real PostgreSQL instance started in a container, migrated with Caster's own migrations. The harness files under `test/Caster.Api.Tests/Support/Shared/` are copied from the standard and are not edited here. Most tests are not isolated unit tests: a typical test sends an HTTP request to the application hosted in process, through the real routes, the real middleware, the real claims transformer, the real MediatR handlers and their validators, the real AutoMapper profiles and a real database, then asserts on the response and on what changed in the database. Only collaborators that leave the process are replaced. The pure unit tests (Terraform state and plan parsing, directory paths and import names, GitLab modules, module version order, required values and snippets, archive round trips, file, directory and workspace name validation and interface validation, Terraform version validation, the permission handlers and requirements, the import result mapping, Kubernetes labels, module default conversion, git tag parsing and the module source dispatcher, the run queue, the workspace file-system helpers) need no database and run without Docker. The git module source tests (`Domain/Services/Modules/GitModuleRepositoryProviderTests`, `GitModuleSourceEndToEndTests`) store what they discover in the test's database, and the end-to-end ones run the real `git` binary against a `file://` repository they build, so they need `git` on the path, as the Caster image has.

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
- The repository has no root `Directory.Build.props`, so the application project is not built with warnings as errors, as the standard intends, and it reports warnings today: CS0168 (`Domain/Models/Resource.cs`), CS9107 (the SignalR event handlers), CS9113 (unread constructor parameters in Projects/Create, Projects/Delete, Workspaces/Create, Workspaces/Delete, SystemRoles/Delete and Vlan DeletePartition), CA2017 and CA2200 (`FileVersionScrubService`, `PlayerSyncService`), and the restore warning NU1608 (Microsoft.CodeAnalysis 5.0.0 against Workspaces.MSBuild 4.14.0). The standard's `verify.sh` counts only the test project's own warnings and lists these under its PASS line as `note: application projects warn (not counted)`; the test project builds with no warning. Fixing them is application work.
- Caster has no central package management, so the test packages carry their versions in `Caster.Api.Tests.csproj`, pinned by the standard (`agent-docs/api-testing/test-packages.props`; `sync.sh` checks them). Implicit usings and nullable reference types are off, as in `Caster.Api`.
- The suite runs in the VSTest mode of `dotnet test`; see the standard's README for why not Microsoft.Testing.Platform.

# How the harness works

The mechanics are the standard's (README, "How the harness works"). What Caster supplies:

## Fixtures

- `Support/DatabaseFixture.cs`: an assembly fixture over the shared `PostgresTestDatabase<CasterContext>`. No `MigrationsAssembly`: Caster's migrations are in `Caster.Api` beside the context. Each test gets a database cloned from the migrated template and dropped afterwards. It also hands the host one throwaway database (`HostDatabase()`, below).
- `Support/CasterContextFactory.cs`: builds `CasterContext` with the entity event interceptor and a provider holding a substituted mediator, which `DatabaseTestBase.Mediator` exposes.
- `Support/CasterAppFactory.cs`: the run-wide `WebApplicationFactory<Program>`, one host for the whole run (the standard's default). It configures no module source, as shipped, so `GET api/modules` never syncs against the shared outbound stub.
- `Support/GitlabSourceAppFactory.cs`: the same host with the legacy GitLab module source configured (`Terraform:GitlabGroupId`), over a host database of its own, for the one class whose routes need a configured source (`Features/Modules/ModuleSourceRequestTests`, an `IClassFixture`). It leaves the shared Terraform directories to the run-wide host.

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
- `OnProjectThroughNewGroup(project, permissions)` puts the actor in a new group and the group on the project, the path by which a group grants its members project permissions; `OnProjectThroughNewGroup(project, roleId)` gives that membership an existing role (the seeded Manager, which grants every project permission).
- `InGroup(group, role)` and `OnNewGroup(role)`: a group Manager holds `GroupPermission.ManageMembership` on it, a Member nothing.

`Support/TestActorTests.cs` pins each shape through the real claims service.

## What is real, and what is not

Replaced: token validation (`TestAuthHandler`), the `CasterContext` registration, and the collaborators that leave the process:

- `IHubContext<ProjectHub>` is a `HubRecorder<ProjectHub>` (`Factory.Hub<ProjectHub>().ToGroup(id)`).
- `IHttpClientFactory` is the shared `StubHttpClientFactory` over `Factory.OutboundHttp` (Player VM API, identity provider, GitLab), given the base address production's named-client registration sets for the "gitlab" client (`Terraform:GitlabApiUrl`).
- The four hosted services (`FileVersionScrubService`, `PlayerSyncService`, `ImageTagService`, `RunQueueService`) are removed. `IRunQueueService` stays registered, so a created run or apply is queued and never executed, and no Terraform binary ever runs.

`Support/TestConfiguration.cs` turns the claims cache off (the token `TestAuthHandler` mints carries no roles and no groups, so `UseRolesFromIdP` and `UseGroupsFromIdP` have nothing to read and are left as shipped), points `Terraform:BinaryPath` at a temporary directory with one empty sub-directory per version (`0.12.29`, `1.5.7`, which is all `ProcessTerraformService.IsValidVersion` and `GetVersions` read) and `Terraform:RootWorkingDirectory` at another, and sets `Terraform:GitlabApiUrl`. The host runs as Production, so a 500's body carries the exception message as its detail and no stack trace.

The resource commands (taint, untaint, remove, refresh, import, outputs) run Terraform once past their gate. Their allowed cases seed a queued run on the workspace, so the handler answers 409 for the busy workspace, which it can only reach after the gate let the caller through.

Hubs and handlers the recorder cannot observe are driven directly: `Hubs/ProjectHubTests.cs` runs `ProjectHub`'s join methods through `HubHarness` over the real `AuthorizationService` (built by `Support/AuthorizationHarness.cs`); `Hubs/ProjectHubConnectionTests.cs` proves the hub's own `[Authorize]` over a real SignalR connection (an actor connects over WebSockets through `Factory.Server.CreateWebSocketClient()` with its `X-Test-User`, `X-Test-Name` and `X-Test-Session` headers and joins a project; a negotiate with no identity is a 401, and one whose token lacks the caster scope a 403), with `Microsoft.AspNetCore.SignalR.Client` at the version the standard pins. `ProjectHub` names no scheme, so the factory needs no Bearer recipe. And `Features/Runs/EventHandlers/RunSignalRHandlerTests.cs` drives the run broadcasts, which go to two groups at once through `Clients.Groups(...)`, over a hub context the test owns.

Variable request tests also read saved rows through `GetWorkspaceFiles`, checking the generated design filename and Terraform declaration content. System-role and group-membership request tests cover numeric enum inputs as well as named values. Directory create tests send client-chosen identifiers and read the stored row through a cold context. Background-worker tests run the real queue dispatch and MediatR handler against their own PostgreSQL database; Player sync startup and shutdown run in a child process that the parent always terminates and joins, keeping its removal worker out of the suite process. Child failure messages and output are retained for diagnostics.

The Player sync child runner disables color so ANSI reset sequences cannot prefix its exact completion marker. The child writes and flushes that marker only after its assertions finish, then exits explicitly to stop its isolated threads. The parent decides from the marker, not the runner's shutdown or exit code, with a cancellable two-minute deadline for child startup and scheduling. It always terminates any child still alive and joins the process and both output readers. To exercise the GitHub runner environment on a two-core Linux host, run `taskset -c 0,1 env -u NO_COLOR dotnet test test/Caster.Api.Tests --filter 'FullyQualifiedName~.PlayerSyncServiceTests'`.

The removal-worker probe exempts the startup thread only for its first synchronous scope lookup. Later scopes are removal work even when an async continuation resumes on that same pool thread; their signals, not thread identity or sleeps, establish the before-stop and after-stop observations.

# Adding a test

Follow the standard's CONVENTIONS.md (sections 1 and 2): derive from `ApiTestBase` to send a request, `DatabaseTestBase` to use only a database, or nothing for a pure test; seed with `TestData` mothers and `Actor()`; name the method as a sentence; pass `Ct`; re-read through `NewContext()`.

Every handler's `Authorize()` override is a gate, and each authorization call in it, in a hub or in a service (`ICasterAuthorizationService.Authorize`, `CanLock`) is a call site of its own. For every call site:

- each permission value it reads has an allowed test whose caller holds exactly that value, never `Root`: a system permission through `WithSystemPermissions`, a project permission through `OnProject` (or `OnProjectThroughNewGroup`), a group permission through `InGroup(group, GroupMembershipRole.Manager)`;
- each tier of a multi-tier gate (system, project, group) has a near miss of its own: a caller holding a neighbouring permission of that tier (another system permission, another project permission on the same project), or the same value on another project (`OnNewProject`) or group (`OnNewGroup`, a Manager of a group of its own), refused by that site. A caller that an earlier gate on the route already refuses is not a near miss of a later one.

Requests are validated before they are authorized (`ValidationBehavior` is a MediatR pre-processor), so a denied test sends a body that passes validation.

The default authorization policy (`Infrastructure/Extensions/AuthorizationPolicyExtension.cs`: an authenticated user whose token carries every scope in `Authorization:AuthorizationScope`) applies to every controller, to `ProjectHub` and to the Prometheus endpoint (`MapPrometheusScrapingEndpoint().RequireAuthorization()`); `Infrastructure/Extensions/AuthorizationPolicyExtensionTests.cs` sends `X-Test-Scope` without the caster scope to a controller route and to `/metrics`, and `ProjectHubConnectionTests` to the hub's negotiate.

A file write also asks `FileCommandHandler.CanLock` (LockFiles) whether the caller may pass an administrative lock, and an import asks `ImportService.CanLock` per file; their allowed cases and near misses act on an administratively locked file, and the near misses assert the 409's own title.

A defect a test finds is characterized by a passing test of the current behaviour and described in `agent-docs/api-test-bugs/caster.api.md`, never in the test (CONVENTIONS.md section 3).

# Layout

The project mirrors `src/Caster.Api`:

- `Features/<Area>/*RequestTests.cs`: the endpoints of each feature slice (`Features/Projects/` has `ProjectRequestTests` and `ProjectMembershipRequestTests`), plus the validators constructed directly (`Features/Files/FileNameValidationTests.cs`, `Features/Directories/DirectoryNameValidationTests.cs`, `Features/Workspaces/WorkspaceReservedNameValidationTests.cs`), `Features/DesignModules/DesignModuleValidationTests.cs` (the completeness check Create, Edit and AddOrUpdateValues run, over the test's database), `Features/Modules/GetAllModulesTests.cs` (the module listing handler over a module source the test substitutes: a failed or unconfigured sync, cancellation, and the forced sync of a design's listing for the callers its gate admits, none of which the run-wide host can show, as it configures no module source), `Features/Directories/GetAllDirectoriesTests.cs` (the directory listing for a member of a group the token names, through the real claims service, which the hosted test token cannot carry) and `Features/Runs/EventHandlers/`. The tests of what an edit body may name against what its route authorizes (workspaces, files, directories, design modules, tags) seed `Support/TwoProjectGraph`, two projects of the same shape.
- `Domain/Models/`, `Domain/Services/`: the pure unit tests (among them `Domain/Models/Modules/` with `GitlabModuleTests` and `ModuleVersion*Tests`, the version order, required values and snippets), `RunQueueServiceTests`, `ArchiveRoundTripTests` (what an export carries and an import reads back) and `Domain/Services/Terraform/ProcessTerraformServiceTests` (version validation before a process binary runs); `Domain/Services/Modules/` (`GitTagParserTests`, `ModuleRepositoryDispatcherTests`, and, over the test's database, `GitModuleRepositoryProviderTests` over the `Support/FakeGitCommandRunner` and `GitModuleSourceEndToEndTests` over real git, both in the `GitModuleTempRoot` collection because they share one temporary clone directory); and, over the test's database, `UserClaimsServiceTests` and `GitlabRepositoryServiceTokenTests` (the GitLab token header, on the real client registration).
- `Features/Shared/Behaviors/InterfaceValidationTests.cs`: `ValidationBehavior` running the validators declared over request interfaces.
- `Data/CasterContextTests.cs`: the schema's relationships, cascades and indexes; `Data/WorkspaceQueriesTests.cs`: the module version each design module is written into a workspace with.
- `Hubs/ProjectHubTests.cs` (the join methods through `HubHarness`), `Hubs/ProjectHubConnectionTests.cs` (the real connection); `Infrastructure/Authorization/` (`AuthorizationServiceTests` over the test's database, `PermissionHandlerTests`, `PermissionRequirementTests`: the requirement handlers over null, empty and non-empty permission lists), `Infrastructure/Exceptions/`, `Infrastructure/Extensions/` (the default policy's scope), `Infrastructure/Mapping/` (`MappingConfigurationTests`, `ImportResultMappingTests`), `Infrastructure/Serialization/` (`NumberToStringConverterTests`), `Infrastructure/Utilities/` (`KubernetesLabelTests`).
- `Data/*.json`, `Data/terraform.tfstate`: fixtures the tests read (`terraform.tfstate` also by `ResourceRequestTests`). `Data/test.tf` is read by no test.
- `AssemblyFixtures.cs`: registers `DatabaseFixture` and `CasterAppFactory` as assembly fixtures.
- `Support/`: the template-derived harness, the extras `ArchiveHelper` (builds the zip archives the import endpoints read), `FakeGitCommandRunner` (stands in for the git binary: a clone copies a prepared tree), `GitModuleTempRoot` (the collection the git module source tests share) and the seed-graph helper `TwoProjectGraph`, and the harness self-tests (`DatabaseHarnessTests`, `HttpHarnessTests`, `TestActorTests`); `Support/Shared/`: the standard's shared files.

# Continuous integration

`.github/workflows/build-and-test.yml` (the standard's workflow) restores, builds and runs `dotnet test test/Caster.Api.Tests` on pull requests and pushes to `main`, then greps the log for the PostgreSQL provider banner and uploads the TRX results. Check the setup with `agent-docs/api-testing/verify.sh --app caster.api <repo>` and `sync.sh --check <repo>`. `verify.sh` includes the gates check (`check-repo.js gates`), whose allow-list for this app is `agent-docs/api-test-gaps/caster.api.txt`, outside the repo.
