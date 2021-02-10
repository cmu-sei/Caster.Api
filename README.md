# Caster.Api
Caster is the primary deployment component of the Crucible framework. Caster provides a web interface that gives exercise developers a way to create, share, and manage topology configurations.

## Updating/Restarting Caster.Api
Caster.Api utilizes the Terraform binary in order execute workspace operations.  Because this binary is running inside of the Caster.Api service, restarting or stopping the Caster.Api Docker container while a Terraform operation is in progress can lead to a corrupted state.  

In order to avoid this, a System Administrator should follow these steps in the Caster UI before stopping the Caster.Api container:

- Navigate to *Administration -> Workspaces*
- Disable Workspace Operations by clicking the toggle button
- Wait until all Active Runs are completed

## Reporting bugs and requesting features

Think you found a bug? Please report all Crucible bugs - including bugs for the individual Crucible apps - in the [cmu-sei/crucible issue tracker](https://github.com/cmu-sei/crucible/issues). 

Include as much detail as possible including steps to reproduce, specific app involved, and any error messages you may have received.

Have a good idea for a new feature? Submit all new feature requests through the [cmu-sei/crucible issue tracker](https://github.com/cmu-sei/crucible/issues). 

Include the reasons why you're requesting the new feature and how it might benefit other Crucible users.

## License

Copyright 2020 Carnegie Mellon University. See the [LICENSE.md](./LICENSE.md) files for details.
