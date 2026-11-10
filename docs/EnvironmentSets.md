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
  * *Connection*: a field of an [infrastructure connection](Inventory.md), read from configuration when each Terraform command starts. The entry names the connection and the field; creating or editing one also needs `ManageInfrastructure`.

### Connection fields

The fields come from Caster's connection settings, not from any Terraform provider, so the same field serves every provider that wants that form. A field is available only when its setting is set, it applies to the connection's provider, and it is unambiguous; otherwise the entry shows as unavailable, with the reason, and runs that use it fail.

| Field | Secret | vSphere | Proxmox |
|---|---|---|---|
| `Url` | no | `Url` as configured | `Url` as configured, e.g. `https://pve:8006` |
| `Host` | no | host, and port if the `Url` names one, without scheme or path | same |
| `ApiUrl` | no | n/a | `Url` + `/api2/json`, never doubled |
| `Insecure` | no | `true`/`false` from `InsecureSkipVerify` | same |
| `Username` | no | `Username` | `Username` (`user@realm`) |
| `Password` | yes | `Password` | `Password` |
| `ApiToken` | yes | n/a | the whole `user@realm!id=secret` |
| `ApiTokenId` | yes | n/a | the token before its first `=` |
| `ApiTokenSecret` | yes | n/a | the token after its first `=` |
| `IsoStorage` | no | the datastore of the only `IsoLocations` entry | the storage of the only `IsoLocations` entry |
| `IsoFolder` | no | the folder of the only `IsoLocations` entry, empty for none | n/a |
| `Datacenter` | no | the only `Datacenters` entry | n/a |

A value the list doesn't cover can be a Plain entry. Connections are referred to by name, so renaming one makes its entries unavailable until they are pointed at the new name.

**Create set from connection** (`POST api/environment-sets/from-connection`) makes a set holding a provider's environment variables for one connection: `bpg/proxmox` (`PROXMOX_VE_*`), `Telmate/proxmox` (`PM_*`) and `hashicorp/vsphere` (`VSPHERE_*`). The Proxmox presets use the token when the connection has one, and its username and password otherwise.

### Rules

* Environment variable names must match `^[A-Za-z_][A-Za-z0-9_]*$`. Terraform variable names follow Terraform's identifier rules: letters, digits, `_` and `-`, not starting with a digit.
* Environment variables starting with `TF_` are refused, because they control Terraform itself (`TF_CLI_ARGS*`, `TF_DATA_DIR`, `TF_WORKSPACE`, ...). `TF_TOKEN_*` (private registry credentials) is allowed. Use the Terraform variable kind instead of `TF_VAR_*`.
* `HOME` and `PATH` are refused.
* A project gets its own sets and its [project group](ProjectGroups.md)'s; a set given both ways reaches it once.
* A target name can come from only one set on a project. Assigning a set is refused if one of its target names already comes from another set on the project, and editing a set is refused if the change would cause that on any project it is assigned to.
* A secret's source name is checked against the patterns when the entry is saved, when the set is assigned, and again at every run, as the patterns can change.
* **A secret can't be a Terraform variable**: neither a Secret entry nor a connection credential. Terraform saves variable values in saved plans and state, so provider credentials go in the provider's environment variables, which every preset uses. This is checked on save, in copies and multi-edits, when seeding, and again at run time.
* `GIT_CONFIG_COUNT`, `GIT_CONFIG_KEY_n` and `GIT_CONFIG_VALUE_n` are refused: a set's `GIT_CONFIG_KEY_0` would replace Caster's own git configuration for module sources, and another count would break its numbering.

### Assigning

* A set is assigned to projects on its page in Administration > Environment Sets, and a project's sets and inventory connections can be added and removed together in the project's Environment dialog.
* **Assignments** (`POST api/assignments`) adds and removes sets and [inventory connections](Inventory.md) on projects in one request: each change names a project and either a set or a connection. The changes are checked against the final state and saved together, or not at all, so a set can be swapped for one giving the same names. Adding what is already assigned, or removing what isn't, changes nothing. Set changes need `ManageEnvironmentSets`, connection changes `ManageInfrastructure`.
* `POST` and `DELETE api/projects/{id}/environment-sets/{setId}` assign or remove one set.

### Copying and editing several sets

* **Copy** (`POST api/environment-sets/{id}/copy`) makes "*name* (copy)" with every entry. Secret entries copy the secret's name, never a value. The copy is assigned to no project. Copying a set with connection entries also needs `ManageInfrastructure`.
* **Multi-edit** (`POST api/environment-sets/batch-edit`) changes the same entry, by kind and target name, in several sets at once: update it where it exists, add it where it doesn't, or delete it. Everything is checked against the whole final state, every set on every project the edited sets reach, and saved together or not at all.
* Entry edits, assignments and multi-edits take one PostgreSQL advisory lock around reading, checking and saving, so two API instances can't each pass a check the other's change breaks.

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

* **Missing values fail the run.** If a secret's source variable is missing or no longer matches the patterns, or a connection or its field isn't available, the command fails before Terraform starts, with a message naming the target and the set (never a secret's source name). Continuing would let providers fall back to other ambient credentials.
* **Masking.** Secret values (Secret entries and connection credentials) are replaced with `***` in run output before it is saved or streamed to the UI, each separately, line by line for multi-line values. This catches accidental echoes (a provider error quoting a credential, debug logging); it does not stop a configuration that sets out to reveal a value, for example encoded or split.
* **Short secrets fail the run.** Masking replaces every occurrence of a value, so a short one (`1`, `true`, `lab`) would also replace ordinary text and corrupt the JSON Caster reads from Terraform's output. A secret shorter than `Terraform:Secrets:MinimumMaskedLength` (default 8) fails the run, and the admin pages warn about it. Lowering the minimum risks that corruption.
* **Which commands get which entries.** A plan, import or refresh gets every entry. An apply runs the saved plan, which already holds the Terraform variables, so it gets the environment-variable entries only, read again when the apply starts; an unavailable Terraform-variable source doesn't block it. Init, show, output and state commands get the environment-variable entries only.
* **The run names its sets.** A plan or apply's output starts with one line naming the sets it used and the target names each supplied, never values or secret source names. Run output is saved, so this is the record of what a run was given.
* **Kubernetes job mode, after an API restart.** The API reattaches to a job that is still running, and masks the values the job's spec carries as well as today's, so a credential rotated mid-run is still masked.
* **Kubernetes job mode.** Values are plain environment variables in the Job spec for now, readable by anyone who can read Jobs or Pods in the jobs namespace. Keep that namespace admin-only.

## Permissions

| Permission | Allows |
|---|---|
| `ViewEnvironmentSets` | See every set: names, entries, plain values, secret source names and connection names (never secret values), the projects each set is assigned to, the available secret names and the run mode. |
| `ManageEnvironmentSets` | Everything `ViewEnvironmentSets` allows. Create, edit, copy, multi-edit and delete sets and entries, list the connections' fields and presets (never values), and assign sets to projects. Picking a project to assign to also needs `ViewProjects`. |
| `ManageEnvironmentSets` + `ManageInfrastructure` | Create or edit connection entries, copy a set that has them, and create a set from a connection's preset. |
| Project `ViewProject` (or system `ViewProjects`) | See the project's sets: names, descriptions, target names, plain values, which connection a non-secret field comes from, and whether each secret is available. Never secret source names or values. |

No one can see secret values through Caster. The seeded Observer role gets `ViewEnvironmentSets` with every other `View` permission.

## Security model

Environment Sets decide **which project gets which credential**. They do not keep a credential from the project that has it.

* **Project editors can always read their own project's secrets**, through their Terraform: an output, a `local-exec` provisioner, an HTTP data source. Anyone who can edit a project (holders of project `EditProject`, project `ManageProject` who can add editors, and system `EditProjects`) can reach the secrets of the sets assigned to it. Assign a set only to projects whose editors may hold its credentials.
* **Process mode gives no isolation between projects.** Terraform runs as the API's user, and can read the API's own environment (every secret, the API's own credentials) and other workspaces' folders. Policy checks on Terraform configuration (planned) reduce the risk but cannot remove it. Use Kubernetes job mode where projects must be kept apart.
* **Kubernetes job mode** isolates projects' environments. Until workspace volumes are isolated per job (planned), any job can read other workspaces' state and saved plan files, including the Terraform variable values in plan files, from the shared volume. Keep the jobs namespace admin-only.
* **Credentials are provider environment variables.** Terraform variable values end up in state, outputs and saved plan files, which project members with read-only access can read, so a secret can't be a Terraform variable.
* **Kubernetes job mode currently prints Terraform variable values to pod logs**, because `terraform show -json`, which Caster runs after each plan, prints input variable values on stdout. No secret can be a Terraform variable, so these are plain values.
* **Aliased providers** that need a second set of credentials can't get them from a set for now, as one environment variable name holds one value and a secret can't be a Terraform variable.

## Migrating off InheritAll

`InheritAll` used to default to `true`. A deployment that relied on it now passes only what `Inherit` and `Direct` name.

1. List the variables your runs need: provider credentials (`VSPHERE_*`, `AWS_*`, `ARM_*`, `PROXMOX_VE_*`), certificates (`SSL_CERT_DIR`), git configuration for module sources (`GIT_CONFIG_*`), proxies (`HTTP_PROXY`, `HTTPS_PROXY`, `NO_PROXY`).
2. Move credentials to `CASTER_SECRET_*` variables and give them to projects with Environment Sets.
3. Add the rest to `Inherit` as anchored patterns, for example `^(SSL_CERT_DIR|HTTPS?_PROXY|NO_PROXY)$`, or set them in `Direct`.
4. Keep `InheritAll` `false`. Setting it back to `true` restores the old behaviour, except that secrets matching the patterns are still held back.
