# Environment Sets

Environment Sets keep credentials out of Terraform files. Operators give credentials to the Caster API as environment variables (from Kubernetes Secrets or similar). Admins group them, together with plain non-secret values, into **Environment Sets** and assign sets to projects. Only the sets assigned to a project are injected into that project's Terraform runs.

## Configuration

### Secret patterns

`Terraform:Secrets:Patterns` is a list of regular expressions. An API environment variable whose name matches one of them is a **secret**:

* it never reaches a run through `Terraform:EnvironmentVariables:Inherit` or `InheritAll`;
* it only reaches the projects an Environment Set gives it to.

The default is `^CASTER_SECRET_`, so it works without setup: name the variables `CASTER_SECRET_*`.

```yaml
# Kubernetes: give the API a secret from a Kubernetes Secret
env:
  - name: CASTER_SECRET_VSPHERE_PASSWORD
    valueFrom:
      secretKeyRef:
        name: caster-credentials
        key: vsphere-password
```

Patterns are case-sensitive and should be anchored (`^`). A pattern decides which API variables admins can hand to projects, so do not write one that matches the API's own settings (`ConnectionStrings__*`, `Authorization__*`, `Terraform__GitlabToken`). Blank patterns are ignored.

Secrets come from API environment variables only. Every API replica is assumed to have the same environment.

### InheritAll

`Terraform:EnvironmentVariables:InheritAll` now defaults to `false`. When `true`, every API environment variable reaches every run, including the API's own database connection string, OIDC client secret and GitLab token (secrets matching the patterns above are still held back). See [Migrating off InheritAll](#migrating-off-inheritall).

## Sets and entries

An Environment Set has a name, a description and entries. Each entry has:

* **Kind**
  * *Environment variable*: set in the run under its target name.
  * *Terraform variable*: set as `TF_VAR_<target name>`. The value can be any HCL value Terraform accepts for the variable's type; Caster does not check it, so an invalid value shows as Terraform's own error in the run.
* **Target name**: the name inside the run.
* **Value source**
  * *Secret*: the name of an API environment variable, which must match a secret pattern. Its value is read when each Terraform command starts and is never stored or shown by Caster.
  * *Plain*: a value stored in Caster. Plain values are not secret: members of every project the set is assigned to can read them.

### Rules

* Environment variable names must match `^[A-Za-z_][A-Za-z0-9_]*$`. Terraform variable names follow Terraform's identifier rules: letters, digits, `_` and `-`, not starting with a digit.
* Environment variables starting with `TF_` are refused, because they control Terraform itself (`TF_CLI_ARGS*`, `TF_DATA_DIR`, `TF_WORKSPACE`, ...). `TF_TOKEN_*` (private registry credentials) is allowed. Use the Terraform variable kind instead of `TF_VAR_*`.
* `HOME` and `PATH` are refused.
* A target name can come from only one set on a project. Assigning a set is refused if one of its target names already comes from another set on the project, and editing a set is refused if the change would cause that on any project it is assigned to.
* A secret's source name is checked against the patterns when the entry is saved, when the set is assigned, and again at every run, as the patterns can change.

### Seeding sets

`SeedData:EnvironmentSets` creates sets at startup, so a deployment can ship with its sets defined in configuration:

```json
"SeedData": {
  "EnvironmentSets": [
    {
      "Name": "vSphere",
      "Description": "Lab vCenter",
      "Entries": [
        { "Kind": "EnvironmentVariable", "TargetName": "VSPHERE_SERVER", "ValueSource": "Plain", "Value": "vcenter.example.com" },
        { "Kind": "EnvironmentVariable", "TargetName": "VSPHERE_PASSWORD", "ValueSource": "Secret", "SecretName": "CASTER_SECRET_VSPHERE_PASSWORD" }
      ]
    }
  ]
}
```

As environment variables (Helm values, `.env` files):

```text
SeedData__EnvironmentSets__0__Name=vSphere
SeedData__EnvironmentSets__0__Entries__0__Kind=EnvironmentVariable
SeedData__EnvironmentSets__0__Entries__0__TargetName=VSPHERE_PASSWORD
SeedData__EnvironmentSets__0__Entries__0__ValueSource=Secret
SeedData__EnvironmentSets__0__Entries__0__SecretName=CASTER_SECRET_VSPHERE_PASSWORD
```

* A set is added only if no set has its name (case-insensitive). An existing set is never changed, so changes made in Caster survive restarts; to apply a changed seed, delete the set and restart.
* Seeded sets are not assigned to projects. Assign them in Administration.
* Entries follow the [rules](#rules) above, `Kind` defaults to `EnvironmentVariable` and `ValueSource` to `Plain`. A secret's source may be missing at startup; the set shows as incomplete until it is set.
* Seeds name secret sources, never secret values, so they are safe in configuration that is not secret.
* A mistake in any seeded set fails startup with errors naming the set and entry (`SeedData:EnvironmentSets:0 "vSphere", Entries:1 ...`), and no set is added.

### Deleting a set

Deleting a set that is assigned to projects removes it from them. Their next runs do not get its variables. The UI lists the affected projects and asks for confirmation first.

### Export and import

Sets and assignments are never part of a project or directory export, and an import never creates them.

## Run time

For every Terraform command (init, plan, apply, import, refresh, taint, untaint, state rm, output, show), Caster builds the environment from:

1. The inherited API variables (`Inherit`, or all of them with `InheritAll`), less any secrets. In Process mode, the API's `PATH` and `HOME` are always passed, as a local process starts from an empty environment.
2. `Terraform:EnvironmentVariables:Direct`.
3. The project's Environment Sets, which override same-named variables from 1 and 2.
4. Caster's own `TF_IN_AUTOMATION` and `TF_WORKSPACE`. `TF_WORKSPACE` is set last, so nothing inherited or set can point a run at another workspace.

Sets apply to every workspace in the project, including those Alloy creates.

* **Missing secrets fail the run.** If a secret's source variable is missing, or no longer matches the patterns, the command fails before Terraform starts, with a message naming the target and the set (never the source name). Continuing would let providers fall back to other ambient credentials.
* **Masking.** Secret values of 8 characters or more are replaced with `***` in run output before it is saved or streamed to the UI, line by line for multi-line values. This catches accidental echoes (a provider error quoting a credential, debug logging); it does not stop a configuration that sets out to reveal a value, for example encoded or split.
* **Applies use current values.** An apply uses the set values as they are when the apply runs, even if the set changed after the plan.
* **Kubernetes job mode.** Values are plain environment variables in the Job spec for now, readable by anyone who can read Jobs or Pods in the jobs namespace. Keep that namespace admin-only.

## Permissions

| Permission | Allows |
|---|---|
| `ViewEnvironmentSets` | See every set: names, entries, plain values and secret source names (never secret values), the projects each set is assigned to, the available secret names and the run mode. |
| `ManageEnvironmentSets` | Create, edit and delete sets and entries, and assign sets to projects. Picking a project to assign to also needs `ViewProjects`. |
| Project `ViewProject` (or system `ViewProjects`) | See the project's sets: names, descriptions, target names, plain values, and whether each secret is available. Never secret source names or values. |

No one can see secret values through Caster. The seeded Observer role gets `ViewEnvironmentSets` with every other `View` permission.

## Security model

Environment Sets decide **which project gets which credential**. They do not keep a credential from the project that has it.

* **Project editors can always read their own project's secrets**, through their Terraform: an output, a `local-exec` provisioner, an HTTP data source. Anyone who can edit a project (holders of project `EditProject`, project `ManageProject` who can add editors, and system `EditProjects`) can reach the secrets of the sets assigned to it. Assign a set only to projects whose editors may hold its credentials.
* **Process mode gives no isolation between projects.** Terraform runs as the API's user, and can read the API's own environment (every secret, the API's own credentials) and other workspaces' folders. Policy checks on Terraform configuration (planned) reduce the risk but cannot remove it. Use Kubernetes job mode where projects must be kept apart.
* **Kubernetes job mode** isolates projects' environments. Until workspace volumes are isolated per job (planned), any job can read other workspaces' state and saved plan files, including the Terraform variable values in plan files, from the shared volume. Keep the jobs namespace admin-only.
* **Prefer provider environment variables over Terraform variables for credentials.** Terraform variable values can end up in state, outputs and saved plan files, and project members with read-only access can read state. If a Terraform variable is needed, use it only in provider blocks, and declare it `ephemeral = true` on Terraform 1.10 or later. `sensitive = true` does not keep a value out of state.
* **Kubernetes job mode currently prints Terraform variable values to pod logs**, because `terraform show -json`, which Caster runs after each plan, prints input variable values on stdout. The UI warns about this on secret Terraform variable entries.
* **Aliased providers** that need a second set of credentials must use Terraform variables, as one environment variable name can hold only one value.

## Migrating off InheritAll

`InheritAll` used to default to `true`. A deployment that relied on it now passes only what `Inherit` and `Direct` name.

1. List the variables your runs need: provider credentials (`VSPHERE_*`, `AWS_*`, `ARM_*`, `PROXMOX_VE_*`), certificates (`SSL_CERT_DIR`), git configuration for module sources (`GIT_CONFIG_*`), proxies (`HTTP_PROXY`, `HTTPS_PROXY`, `NO_PROXY`).
2. Move credentials to `CASTER_SECRET_*` variables and give them to projects with Environment Sets.
3. Add the rest to `Inherit` as anchored patterns, for example `^(SSL_CERT_DIR|HTTPS?_PROXY|NO_PROXY)$`, or set them in `Direct`.
4. Keep `InheritAll` `false`. Setting it back to `true` restores the old behaviour, except that secrets matching the patterns are still held back.
