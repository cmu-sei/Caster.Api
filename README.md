# Caster.Api
Caster is the primary deployment component of the Crucible framework. Caster provides a web interface that gives exercise developers a way to create, share, and manage topology configurations.

## Updating/Restarting Caster.Api
Caster.Api utilizes the Terraform binary in order execute workspace operations.  Because this binary is running inside of the Caster.Api service, restarting or stopping the Caster.Api Docker container while a Terraform operation is in progress can lead to a corrupted state.  

In order to avoid this, a System Administrator should follow these steps in the Caster UI before stopping the Caster.Api container:

- Navigate to *Administration -> Workspaces*
- Disable Workspace Operations by clicking the toggle button
- Wait until all Active Runs are completed
