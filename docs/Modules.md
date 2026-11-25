# Modules

A **Module** is a Terraform module that Designer offers as a tile. Caster discovers Modules from configured sources, stores each one with its versions, variables, and outputs, and generates a `module` block that points Terraform back at the source.

Caster can read Modules from:

* **Git module sources**: any repository git can clone, listed under `Terraform:ModuleSources`.
* **GitLab** (legacy): every project in one GitLab group, read through the GitLab API. Set `Terraform:GitlabApiUrl`, `Terraform:GitlabToken`, and `Terraform:GitlabGroupId`.

The two are independent. Configure either, both, or neither; existing GitLab-only deployments need no changes.

---

## Git Module Sources

Caster reads git sources with git itself, so no server API is involved and the host can be anything git can clone (GitLab, GitHub, Gitea, Forgejo, Bitbucket, or a `file://` path).

```json
"Terraform": {
  "ModuleSources": [
    {
      "Name": "crucible-modules",
      "Description": "Crucible vSphere module catalog",
      "Provider": "Git",
      "Url": "https://git.example.com/your-org/terraform-modules.git",
      "Layout": "Subdirectories"
    }
  ],
  "ModuleSourceTimeoutSeconds": 120
}
```

As environment variables, which is how the Helm chart sets them:

```
Terraform__ModuleSources__0__Name=crucible-modules
Terraform__ModuleSources__0__Url=https://git.example.com/your-org/terraform-modules.git
Terraform__ModuleSources__0__Layout=Subdirectories
```

### Settings

* **Name** (required)
  Identifies the source. It prefixes the Path of every Module found there (`crucible-modules/network-segment`), so two sources holding a module of the same name do not collide. Renaming a source registers its Modules again under new Paths.

* **Url** (required)
  Clone URL, used verbatim by git and in the generated `source = "git::<Url>?ref=<tag>"`. A source missing `Name` or `Url` is ignored rather than failing startup.

* **Description**
  Shown in Designer for Modules that do not provide their own description.

* **Provider**
  `Git` (default).

* **Layout**
  * `Root` (default): the repository root is one Module.
  * `Subdirectories`: every immediate subdirectory is a Module, addressed as `git::<Url>//<subdir>?ref=<tag>`. Deeper directories are not searched, and directories starting with `.` are skipped.

* **Shared**
  `false` (default). `true` makes every project able to use this source's Modules whatever its Module Scopes; see [Module Scopes](#module-scopes).

* **ModuleSourceTimeoutSeconds**
  Seconds any single git command against a module source may run before it is killed. Default `120`; `0` or less disables the timeout.

### Repository Requirements

* **Versions are git tags.** Every tag is a version. A repository with no tags fails to sync with an error saying so, because a Design could never reference an untagged Module.
* In the `Subdirectories` layout, a plain tag (`v1.0.0`) versions every Module in the repository. A tag of the form `<subdir>/<version>` (`network-segment/v1.2.0`) versions only that Module, so Modules can be released independently.
* **A Module root must contain `variables.tf.json` or `outputs.tf.json`.** Caster reads variables and outputs from these files in Terraform's JSON syntax. Modules written only in HCL (`variables.tf`, `outputs.tf`) are not discovered.
* An optional `caster.json` in a Module root sets `displayName` and `description`, so the Modules of one repository get distinct labels in Designer.

### When Sources Sync

Listing Modules (`GET /api/modules`, which every Designer and Modules page load sends) syncs each source first. A source whose tags have not changed since the last sync is not cloned again. If a source fails to sync, the stored Modules are still served, and `GET /api/modules/sources` reports the error for each source. The API log records a source's failure in full once, again when the error changes, and as a reminder after 15 minutes, then after 30 minutes, 1 hour, and so on up to every 4 hours while it keeps failing the same way; it logs a line when the source syncs again.

To re-read one Module even when its tags have not changed (for example after a tag was moved), enter the source `Name`, or `Name/subdirectory` for one Module of a `Subdirectories` source, in **External Module ID** on *Administration > Modules* and click **Add/Update the Modules from this source**.

### Credentials

`file://` and public https URLs need no credentials. Private https sources authenticate through git's own configuration, which must be available in two places:

1. **The Caster.Api container**, where discovery runs `git ls-remote` and `git clone`.
2. **Wherever Terraform runs**, because `terraform init` fetches each Module from its `git::` address. In Process mode that is the same container. In Kubernetes mode it is the Job pod; see [Runs: Git Credentials for Module Sources](Runs.md#git-credentials-for-module-sources).

With the Helm chart (caster 1.10.1 or later), set the `gitcredentials` value. It covers both places. Outside the chart, give git a credential helper scoped to the module host, for example through `GIT_CONFIG_SYSTEM` or the `GIT_CONFIG_COUNT`/`GIT_CONFIG_KEY_n`/`GIT_CONFIG_VALUE_n` environment variables:

```
GIT_CONFIG_COUNT=1
GIT_CONFIG_KEY_0=credential.https://git.example.com.helper
GIT_CONFIG_VALUE_0=!f() { test "$1" = get && echo username=caster && echo password=$CASTER_GIT_TOKEN; }; f
CASTER_GIT_TOKEN=<token with read access to the repositories>
```

The helper only answers `get`, so git never tries to save credentials back, and it is only consulted for the host it names.

## Module Scopes

By default every project can use every Module. A **Module Scope** limits that: it is a named list of module sources that admins assign to projects and [project groups](ProjectGroups.md). Scopes live in the database, not in API settings, and are never part of a project export or import.

### One source per vendor, plus a stock source

The intended layout is one repository per content vendor, each configured as its own module source, plus one **shared** repository of stock modules anybody can use. A project may then use its own vendor's source (or sources) and the stock source, and nothing else.

```json
"Terraform": {
  "ModuleSources": [
    { "Name": "cmu",   "Url": "https://git.example.com/cmu/terraform-modules.git",   "Layout": "Subdirectories" },
    { "Name": "sei",   "Url": "https://git.example.com/sei/terraform-modules.git",   "Layout": "Subdirectories" },
    { "Name": "stock", "Url": "https://git.example.com/stock/terraform-modules.git", "Layout": "Subdirectories", "Shared": true }
  ],
  "ModuleScopes": { "RestrictUnassigned": true }
}
```

Create a scope `cmu` listing the path `cmu` and assign it to the cmu projects (or to a project group holding them), and a scope `sei` listing `sei` the same way. A cmu project then sees the Modules of `cmu` and `stock`, an sei project those of `sei` and `stock`, and a project with no scope only `stock`.

### Settings

* `Terraform:ModuleSources:<n>:Shared` (default `false`; environment variable `Terraform__ModuleSources__<n>__Shared`). A shared source is available to every project whatever its scopes, and cannot be restricted: a scope that does not list it still gets it, and nothing removes it. The Modules administration page reports each source's `shared` value (`GET api/modules/sources`). Source names are compared case insensitively, as elsewhere. The legacy Gitlab group cannot be shared.
* `Terraform:ModuleScopes:RestrictUnassigned` (default `false`; environment variable `Terraform__ModuleScopes__RestrictUnassigned`). With the default, a project with no scope, directly or through its project group, can use every Module, so existing deployments are unchanged. When `true`, such a project can use only the shared sources. Turn it on once the vendor projects are scoped. It is named for the case it governs (projects that have nothing assigned) and sits under `ModuleScopes` with the rest of this feature, next to the per-source `Shared` flag.

A project's effective access is every shared source plus the paths of the scopes assigned to the project and to its group:

| Scope assigned (project or group) | `RestrictUnassigned` | A project can use |
| --- | --- | --- |
| none | `false` | every Module |
| none | `true` | the shared sources only |
| yes | either | the shared sources and the scope paths (all scopes united) |

The Modules admin page (`ViewModules`, no project) always shows every Module. Without `projectId`, a member's access is the union of their projects' access: every Module if any of their projects can use every Module, else the united paths and the shared sources.

### Paths

Each entry of a scope is a Module Path: a source's name (`cmu`) or, as before, a folder of one (`cmu/exercise-x`). An entry allows the Modules at exactly that Path and those below it, so `cmu` allows `cmu` and `cmu/exercise-x` but not `cmu2`. A path may contain any character except wildcards (`*`, `?`), backslashes and control characters, so names such as `_cmu` or `crucible-modules` work; empty parts, `.` and `..` are refused. Paths are matched exactly, including case, as the Modules page shows them. Folders only exist for sources whose layout makes one Module per folder (see the source `Layout`), and only as deep as they are discovered. Scoping by source is the intended use; folder paths still work.

### What is enforced

* The Designer, the file editor's module insert and every Module lookup the API serves for a project only offer the Modules the project may use. This is enforced by the API: `GET api/modules?projectId=` lists a project's Modules (a `projectId` that names no project is `404`), and without `projectId` a member gets what their own projects together allow. Adding a Module outside the project's access to a Design (or switching a Design's Module to one) is refused.
* A Design that already uses a Module outside the project's access keeps working: the Module can still be looked up by that project, and plans and applies are unaffected. The Designer marks it as outside the project's Module Scopes.
* The snippet the file editor inserts (`POST api/modules/snippet`) is scoped the same way. With `projectId` it needs `ViewProject` on that project (or `ViewProjects`) instead of `ViewModules`, so a project author does not need `ViewModules`, which would show every Module. A Module outside the project's access answers `404`, as for a missing version, and a Design that already uses it does not open it for a new snippet. A holder of `ViewModules` who names a project is held to its access too. Without `projectId` the endpoint is unchanged: `ViewModules`, any Module.

### What scopes do not restrict

Module Scopes limit what the catalog offers and what the API serves: the Designer, the file editor's module insert, the snippet endpoint, `GET api/modules` and adding a Module to a Design. They are not an access boundary on the module repositories.

* Terraform at run time can still clone any configured source. Every plan and apply uses the same git credentials (the shared credential store or `CASTER_GIT_TOKEN`), whatever the project.
* A file author can type any module source into a `.tf` file, for example `source = "git::https://git.example.com/sei/terraform-modules.git//foo?ref=v1"`, since the repository URL appears in every snippet. A project that is scoped away from a source can still pull it that way.

A deployment that needs real isolation between vendors must use separate git credentials or separate deployments.

Scopes are created, changed and deleted with `ManageModules` and read with `ViewModules` or `ManageModules`. Assigning a scope to a project or group uses `POST api/assignments` (`moduleScopeId`) and also needs `ManageModules`, so a project's own admins cannot widen the list. A project's members read what applies to them with `GET api/projects/{id}/module-scopes`: `restricted`, `restrictedBecauseUnassigned` (restricted only because nothing is assigned and `RestrictUnassigned` is on), the united `paths`, the `sharedSources`, each path with the scopes and groups it comes from (`pathOrigins`), and the `scopes` themselves.

Deleting a scope, or deleting a project group or moving a project out of it, can leave a project with no scope, which can then use every Module again (or only the shared sources, with `RestrictUnassigned`).

