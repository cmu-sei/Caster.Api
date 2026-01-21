# Runs

Caster uses Terraform (or a Terraform‑compatible CLI) to apply infrastructure‑as‑code (IaC) configurations. When a **Run** is created in a workspace, Caster applies the IaC configuration using the execution **mode** configured for the environment.

Caster currently supports two execution modes:

* **Process**
* **Kubernetes**

---

## Execution Modes

### Kubernetes (Recommended)

In **Kubernetes mode**, Caster creates a Kubernetes **Job** for each Run. Introduced in **Caster.Api 3.6.0**, this is the preferred execution mode when a Kubernetes cluster is available.

#### Key Benefits

* **Resumable Runs**: Runs continue even if the Caster API restarts or crashes, since the Kubernetes Jobs continue running. Caster will resume reading Job output when it restarts.
* **Improved Reliability**: Fewer concerns about in‑progress Runs during upgrades or restarts.
* **Horizontal Scaling**: Kubernetes schedules Jobs on available nodes, allowing Runs to scale with cluster capacity.

#### Configuration

Kubernetes mode is configured under:

```
Terraform__KubernetesJobs
```

These settings are defined in `appsettings.json`.

##### Settings

* **Enabled**
  Enables Kubernetes mode. When set to `true`, all Runs execute as Kubernetes Jobs.

  * If Caster is running *inside* a cluster, it uses the in‑cluster configuration and assigned role.
  * If running *outside* a cluster, it uses the local kubeconfig.

* **UseHostVolume**
  Uses a host volume for Job storage. Intended for **development only**. In production, use a PersistentVolumeClaim (PVC), as host volumes are limited to a single node.

* **HostVolumePath**
  Host path mapped into Jobs when `UseHostVolume` is enabled.

* **Namespace**
  Kubernetes namespace where Jobs are created. Caster must have permission to create Jobs here. Any referenced ConfigMaps or PVCs must also exist in this namespace.

* **Context**
  (Optional) kubeconfig context to use when creating Jobs. Defaults to the current context if not specified.

* **PersistentVolumeClaimName**
  Name of an existing PVC mounted into each Job. This same volume must also be mounted into the Caster container (or otherwise writable by Caster).

  Caster writes Terraform files to this volume before starting a Job, and the Job uses them during execution. **This volume's access mode must be ReadWriteMany to support running Jobs on multiple cluster nodes.**

* **VolumeMountPath**
  Path inside the Job where the volume is mounted.

* **VolumeMountSubPath**
  Optional subpath within the mounted volume.

* **RootWorkingDirectory**
  Root directory used by Jobs for Runs. Each Job’s working directory is set to:

  ```
  RootWorkingDirectory/<workspaceId>
  ```

* **ImageName**
  Container image used to execute Jobs.

* **ImageRegistry**
  Container registry where the image is hosted.

* **ImageTags**
  List of image tags that users can select as versions for a Workspace or Directory. These tags are assumed to exist and are not validated against the registry.

* **QueryImageTags**
  When enabled, Caster queries the registry for available image tags. If `true`, the `ImageTags` setting is ignored.

* **QueryImageTagsRegex**
  Regex filter applied to queried image tags. Useful for restricting versions (e.g., semantic versions only, excluding beta releases).

* **QueryImageTagsMinutes**
  Interval (in minutes) at which Caster re‑queries the registry for new image tags.

* **ConfigMaps**
  List of existing Kubernetes ConfigMaps to mount into Jobs.

  Each ConfigMap entry includes:

  * **Name** – Name of the ConfigMap in the cluster
  * **MountPath** – Path inside the Job where the ConfigMap is mounted

---

### Process (Legacy)

In **Process mode**, Caster executes each Run as a local child process. This remains the default for backwards compatibility, but has several limitations.

#### Limitations

* **No Run Resumption**: If Caster restarts or crashes, child processes are terminated, potentially resulting in data loss.
* **Limited Scalability**: All Runs execute in the same environment as the Caster API (same container or node).

Because of these constraints, **Kubernetes mode is strongly recommended** whenever a cluster is available.

---

## Common Terraform Settings

Settings in the main `Terraform` section of `appsettings.json` apply to **both** execution modes.

### RootWorkingDirectory

The directory where Caster writes Terraform files when a Run is created.

* Must be accessible to the executing Process or Kubernetes Job.
* In Kubernetes mode, this path must be mounted into the Job and referenced via:

```
Terraform__KubernetesJobs__RootWorkingDirectory
```

### EnvironmentVariables

Controls how environment variables are exposed to a Run.

Environment variables are injected into either:

* The local Process, or
* The Kubernetes Pod

#### Options

* **InheritAll**
  Inherits *all* environment variables from the Caster API process, including sensitive values. **Recommended to set this to `false` in production**.

* **Inherit**
  List of regular expressions used to selectively inherit environment variables when `InheritAll` is `false`.

  Useful for including provider‑specific variables (e.g., Terraform provider prefixes).

* **Direct**
  Explicitly defines environment variables injected into the Run.

  Each entry includes:

  * **Name** – Environment variable name
  * **Value** – Environment variable value

  Values defined here override any inherited variables with the same name.

---

## Migration Notes

When migrating from **Process mode** to **Kubernetes mode**, be aware that the default Terraform container image may not include all expected tools.

### Example

* `curl` is **not** installed by default
* `wget` **is** available

You may need to update provisioner scripts accordingly.

### Custom Images

If additional tools are required, configure Caster to use:

* An alternate public image (e.g., Docker Hub)
* An image hosted in a private registry
* A custom image you build and maintain
