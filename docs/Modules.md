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
  Identifies the source. It prefixes the Path of every Module found there (`crucible-modules/network-segment`), so two sources holding a module of the same name do not collide. Renaming a source registers its Modules again under new Paths, except a change of case only, which updates the same Modules. Names are case-insensitive: a second source whose name differs from another's only in case is ignored, with a warning in the log.

* **Url** (required)
  Clone URL, used verbatim by git and in the generated `source = "git::<Url>?ref=<tag>"`. A source missing `Name` or `Url` is ignored rather than failing startup.

  > **A URL with credentials is refused.** Caster never lists, clones, or stores a source whose `Url` holds a user name (other than a bare `git@` in an ssh or scp-style URL), a password, or a token, and reports it as `Error`: *This source's URL contains credentials. Put them in the git credential store (the chart's gitcredentials) instead.* A URL that is not in one of these forms is refused the same way: *This source's URL isn't a git URL Caster can use.*

  * `https://host[:port]/path` (or `http://`), with no user name or password, query string, or fragment.
  * `ssh://[git@]host[:port]/path`, with no user name other than a bare `git`.
  * `[git@]host:path` (scp style), with no user name other than a bare `git`.
  * A `file://` URL or a local path, with no `@` and no further `://`.

  The same applies to a `Terraform:GitlabApiUrl` with a user name or password in it; the Gitlab token belongs in `Terraform:GitlabToken`.

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

* **Versions are git tags.** Every tag is a version. A repository with no tags offers no Module, because a Design could never reference an untagged Module (see [Source States](#source-states)).
* In the `Subdirectories` layout, a plain tag (`v1.0.0`) versions every Module in the repository. A tag of the form `<subdir>/<version>` (`network-segment/v1.2.0`) versions only that Module, so Modules can be released independently.
* **A Module root must contain `variables.tf.json` or `outputs.tf.json`.** Caster reads variables and outputs from these files in Terraform's JSON syntax. Modules written only in HCL (`variables.tf`, `outputs.tf`) are not discovered.
* An optional `caster.json` in a Module root sets `displayName` and `description`, so the Modules of one repository get distinct labels in Designer.

### When Sources Sync

Listing Modules (`GET /api/modules`, which every Designer and Modules page load sends) syncs each source first. A source whose tags have not changed since the last sync is not cloned again. Neither is a source found to have no Module (`Empty` or `Warning` below) while what that read cloned stays the same: its tags, including where each points, or for an untagged source its default branch's latest commit. For a repository with no commits, Caster remembers whether it has any branch; a branch other than the default pushed to it shows on a forced sync or after a restart. Each API instance remembers that read until then, a sync is forced, or the instance restarts. If a source fails to sync, the stored Modules are still served, and `GET /api/modules/sources` reports the error for each source.

To re-read one Module even when its tags have not changed (for example after a tag was moved), enter the source `Name`, or `Name/subdirectory` for one Module of a `Subdirectories` source, in **External Module ID** on *Administration > Modules* and click **Add/Update the Modules from this source**.

### Source States

`GET /api/modules/sources` reports each source's last sync on this API instance as a `state`, with a `message` for every state but `Ok`:

| `state` | When | `message` |
|---|---|---|
| `Ok` | The source synced and offers Modules. | none |
| `Empty` | No tags and no Module root, as in a newly created repository, and no Modules stored from the source. | `No Modules yet.` |
| `Empty` | Tags, but no Module root in any of them, and no Modules stored from the source. | `No Modules yet. Subdirectories layout: add a folder with a variables.tf.json or outputs.tf.json (.tf files aren't read yet), then tag the repository.` (`Root` layout: `No Modules yet. Root layout: add a variables.tf.json or outputs.tf.json at the repository root (.tf files aren't read yet), then tag the repository.`) |
| `Warning` | Module roots on the default branch, but no tags. | `Has Modules but no version tags, so none can be used. Tag the repository, e.g. v1.0.0.` |
| `Warning` | No tags any more, but Modules read earlier are still stored. | `No version tags any more; showing the N Modules stored from earlier.` |
| `Warning` | Tags, but none holds a Module root any more, while Modules read earlier are still stored. | `Its tags no longer contain Modules; showing the N Modules stored from earlier.` |
| `Warning` | No tags, and HEAD names a branch that does not exist, so the default branch cannot be searched. | `Couldn't read the repository's default branch to look for Modules.` |
| `Error` | The source could not be read: listing tags, cloning, authentication, a bad URL, or an unreadable module file. | Same as `error` |

Each source also carries a `url` for linking to it or copying it. Caster shows a source's URL only in these forms, without credentials; otherwise there is no URL (`null`). A refused source (see **Url** above) never has one:

* `https://host[:port]/path` (or `http://`), never with a user name or password, and without the query string or fragment.
* `ssh://[git@]host[:port]/path`. A user name other than a bare `git` is dropped.
* `[git@]host:path` (scp style). A user name other than a bare `git` is dropped.
* A `file://` URL or a local path, as configured, if it contains no `@` and no further `://`.

A URL whose path, query string, or fragment contains an `@` or a `://` is not shown, because that may be the end of a token the URL was mistaken for. For the legacy Gitlab source the `url` is the server's base URL in the first form, such as `https://gitlab.example.com/`, because the API URL and group id name no browsable page; `Terraform:GitlabToken` is never returned.

`Empty` and `Warning` are successful reads: they set `lastSuccess`, leave `error` null, and do not fail the Module list. `error` is set only for `Error`, where it equals `message`; it is deprecated in favor of `state` and `message`. `state` is null until this API instance has read the source; a sync that only waited for another request's sync of the same source does not count, and leaves the last state as it was.

A sync never deletes stored Modules. A Module that disappears from its source, or a source that turns `Empty`, `Warning`, or `Error`, leaves the Modules already stored from it in place.

**Not in source any more.** After each whole-source read of a git source (`Ok`, `Empty`, or `Warning`), a stored Module the read did not find gets `missingSince`, the time it was first found missing, and `inSource` turns false; both are on every Module the Modules list returns. A Module still on the default branch but no longer tagged counts as in the source. A remembered empty read (see [When Sources Sync](#when-sources-sync)) is not a new read and marks nothing. The mark clears when the Module is found again, including by importing it by name. A source whose tags match what is stored is normally not read again; but while any of its Modules is marked, that holds only if this API instance last read exactly those tags (including where each points). Otherwise the source is read once more and that read decides, so deleting the tags and pushing the same ones again clears the mark without a forced sync, while a force-moved tag that dropped a Module keeps it marked. An `Error`, a sync that waited too long for another request's, and a default branch that could not be read leave it as it was. Such a Module is still listed and usable; delete it by hand once no Design needs it. Gitlab Modules are never marked. Caster records which git source stored each Module, so a Gitlab project whose path starts with a git source's name (`terraform/vpc` beside a source named `terraform`) is never taken for one of its Modules. A Module stored before Caster recorded that is given its source on the first successful sync after an upgrade, when its Path is under the source's name (ignoring case) and either no Gitlab source is configured, or the Module has no versions, or every version points into the source's current `Url` (which a Gitlab Module's versions never do). With a Gitlab source configured, an older Module from a source whose `Url` has since changed is therefore left unclaimed, and not marked. Importing a named Module (**Add/Update the Modules from this source**) still fails with an error when the source has no tags or no such Module.

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
