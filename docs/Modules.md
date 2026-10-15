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

* **ModuleSourceTimeoutSeconds**
  Seconds any single git command against a module source may run before it is killed. Default `120`; `0` or less disables the timeout.

### Repository Requirements

* **Versions are git tags.** Every tag is a version. A repository with no tags fails to sync with an error saying so, because a Design could never reference an untagged Module.
* In the `Subdirectories` layout, a plain tag (`v1.0.0`) versions every Module in the repository. A tag of the form `<subdir>/<version>` (`network-segment/v1.2.0`) versions only that Module, so Modules can be released independently.
* **A Module root must contain `variables.tf.json` or `outputs.tf.json`.** Caster reads variables and outputs from these files in Terraform's JSON syntax. Modules written only in HCL (`variables.tf`, `outputs.tf`) are not discovered.
* An optional `caster.json` in a Module root sets `displayName` and `description`, so the Modules of one repository get distinct labels in Designer.

### When Sources Sync

Listing Modules (`GET /api/modules`, which every Designer and Modules page load sends) syncs each source first. A source whose tags have not changed since the last sync is not cloned again. If a source fails to sync, the stored Modules are still served, and `GET /api/modules/sources` reports the error for each source.

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
