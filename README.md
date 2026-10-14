# Caster.Api Readme

Caster is the primary deployment component of the Crucible framework. Caster provides a web interface that gives exercise developers a way to create, share, and manage topology configurations.

## Updating/Restarting Caster.Api
By default Caster.Api runs Terraform in Process mode (`Terraform:KubernetesJobs:Enabled` is `false` in `appsettings.json`): it executes workspace operations by running the Terraform binary as a child process.  Because this binary is running inside of the Caster.Api service, restarting or stopping the Caster.Api container while a Terraform operation is in progress can lead to a corrupted state.

In order to avoid this, a user with the `ManageWorkspaces` system permission should follow these steps in the Caster UI before stopping the Caster.Api container:

- Navigate to *Administration > Workspaces*
- Disable Workspace Operations by clicking the toggle button
- Wait until all Active Runs are completed

The toggle is held in memory and resets when Caster.Api restarts.

In Kubernetes mode each run executes as a Kubernetes Job, which continues while Caster.Api restarts. See [docs/Runs.md](docs/Runs.md) for both execution modes and their settings.

## Testing

The tests are in `test/Caster.Api.Tests`:

```bash
dotnet test test/Caster.Api.Tests
```

Docker must be running: the tests that use a database start a PostgreSQL container through [Testcontainers](https://testcontainers.com/). See [docs/Testing.md](docs/Testing.md) for how the suite is built and how to add a test.

## Reporting bugs and requesting features

Think you found a bug? Please report all Crucible bugs - including bugs for the individual Crucible apps - in the [cmu-sei/crucible issue tracker](https://github.com/cmu-sei/crucible/issues). 

Include as much detail as possible including steps to reproduce, specific app involved, and any error messages you may have received.

Have a good idea for a new feature? Submit all new feature requests through the [cmu-sei/crucible issue tracker](https://github.com/cmu-sei/crucible/issues). 

Include the reasons why you're requesting the new feature and how it might benefit other Crucible users.

## License

Copyright 2021 Carnegie Mellon University. See the [LICENSE.md](./LICENSE.md) file for details.
