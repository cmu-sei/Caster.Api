# Authorization request tests

Run the full suite with Docker available:

```sh
dotnet test test/Caster.Api.Tests/Caster.Api.Tests.csproj
```

Run only the authorization cases:

```sh
dotnet test test/Caster.Api.Tests/Caster.Api.Tests.csproj --filter Category=Authorization
```

`AuthorizationDatabase` starts a disposable PostgreSQL 17.6 server using Testcontainers.
Each request test creates and removes its own database. The suite does not use the
development AppHost or its databases.

`AuthorizationTestApp` hosts the real controllers, MediatR handlers, JSON converters,
validators, mappings, authorization service and permission requirements over HTTP.
Test authentication supplies project/system claims, and Terraform version discovery is
substituted. Background workers and external event delivery are excluded.

Rejected writes are checked against a fresh database context. Successful edits also
check stored ownership, versions and workspace file selection so an ignored body field
cannot inject files into another project's Terraform inputs.
