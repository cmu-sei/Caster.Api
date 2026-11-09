# Project Groups

A project group is a set of projects that share memberships, Environment Sets, inventory connections, a VLAN partition and hosts. What a group carries adds to what each of its projects has of its own. A project is in at most one group, and groups don't nest.

## What a group gives its projects

* **Memberships.** A user or user group with a project role in the group has that role in every project of the group. A project's own memberships and its group's are merged into one set of permissions per project.
* **Environment Sets.** Every project in the group gets the group's sets as well as its own; a set assigned both ways is given once. A target name can still come from only one set on a project, so a set that would clash with a project's own, or a project moving into a group whose sets clash with its own, is refused, as is a set that clashes with the group's other sets.
* **Inventory connections.** Every project in the group sees the group's connections' inventory as well as its own.
* **Partition.** A project acquires VLANs from its own partition, then its group's, then the default.
* **Hosts.** Dynamic host selection picks from the project's own hosts and its group's. A host belongs to a project or a group, never both; moving it is one edit. A workspace keeps the host it was placed on after the host stops being eligible, so destroy still works.

The project's views mark what comes through the group, e.g. "via group Range A".

## Who can do what

| Operation | Needs |
|---|---|
| List groups | `ViewProjects` or `ManageProjectGroups` sees every group; others see the groups they're members of |
| Get a group | `ViewProjects`, `ManageProjectGroups`, or membership of the group |
| Create, edit and delete groups | `ManageProjectGroups` |
| List a group's memberships | `ViewProjects`, `ManageProjectGroups`, or managing the group |
| Add, edit and remove a group's memberships | `ManageProjectGroups`, or managing the group |
| Add a project to a group | managing the group, and `ManageProject` on the project (or `ManageProjects`) |
| Remove a project from a group | `ManageProjectGroups`, or managing the group. A project's own managers can't. |
| Move a project between groups | the rights to remove it from the old group and add it to the new one |
| Create a project in a group | `CreateProjects`, and managing the group |
| Give a group Environment Sets, connections, a partition or hosts | `ManageEnvironmentSets`, `ManageInfrastructure`, `ManageVLANs` or `ManageHosts` |

**Managing a group** means holding `ManageProjectGroups`, or a membership of the group whose role has `AllPermissions` (the seeded Manager role) or `ManageProject`. Group managers can grant any role in their group, the manager role included, as user-group managers can, so a manager can pass the group on.

**`ManageProjectGroups` is a powerful permission.** A holder can make themselves a manager of any group, and so reach every grouped project and the credentials its Environment Sets give. Anyone who can edit a project's Terraform can read those credentials. Grant it as carefully as `ManageEnvironmentSets`. Requiring `ManageProject` to add a project keeps a group manager from pulling in a project they don't already manage.

## When changes take effect

Permissions are cached per user for `ClaimsTransformation:CacheExpirationSeconds` (60 by default) when `ClaimsTransformation:EnableCaching` is on, the default. A change to a group's memberships or projects reaches a user when their cached permissions expire, or sooner when a new token arrives. Open project pages are told about changes to the sets they get.

Deleting a group leaves its projects and hosts, ungrouped, and removes its memberships and what it carried from them. Groups and their links are never part of a project export.
