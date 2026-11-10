# Infrastructure Inventory

Caster can read what is on the hypervisors it deploys to (VM templates, content library items, ISOs, networks, datastores and hosts) and list it in each Project's Inventory panel, with Terraform to copy for each object. It reads vSphere (vCenter) and Proxmox VE. The inventory is off by default. `ViewInfrastructure` holders see every connection; a project's members (`ViewProject`) see the connections assigned to their project.

## Connections

Each hypervisor is a connection under `Infrastructure:Connections`, read and cached on its own:

```json
"Infrastructure": {
  "Connections": [
    {
      "Name": "lab-vcenter",
      "Provider": "vsphere",
      "Url": "https://vcenter.example.com",
      "Username": "caster@vsphere.local",
      "Password": "...",
      "InsecureSkipVerify": false,
      "IsoLocations": [ "[iso-datastore] stock" ],
      "Datacenters": [ "DC1" ],
      "Inventory": {
        "Enabled": true,
        "RefreshMinutes": 30,
        "Resources": {
          "ContentLibraries": { "Enabled": false },
          "VmTemplates": { "Locations": [ "/DC1/vm/Templates" ], "Exclude": "-old$" },
          "Networks": { "Include": "^lab-" }
        }
      }
    }
  ]
}
```

As environment variables, the same settings are `Infrastructure__Connections__0__Name`, `Infrastructure__Connections__0__Inventory__Enabled`, `Infrastructure__Connections__0__Inventory__Resources__VmTemplates__Locations__0` and so on.

- **Name** is required and unique, ignoring case. Clients pick a connection with `?connection=<Name>`, and a request that names none uses the first connection with inventory enabled. Other settings refer to a connection by its Name, so renaming one breaks those references, which then show as missing.
- **Url, Username, Password, ApiToken.** Each connection holds one credential, which the inventory reads with. For Proxmox, `ApiToken` (`user@realm!tokenid=secret`, exactly as Proxmox prints it) is preferred over `Username` and `Password`. The credential needs to see what should be listed; for vSphere the ISO list also needs Datastore > Browse datastore.
- **IsoLocations** says where the connection's ISOs live. For vSphere it's datastore names (`iso-datastore`) or folders (`[iso-datastore] stock`), and it's required: with none, no ISOs are listed. For Proxmox it's storage names (`iso-stock`) or the start of a volid (`local:iso/stock-`), and with none every ISO storage is read.
- **Datacenters** (vSphere only) limits the read to the named datacenters. Empty reads all of them. Content libraries belong to the whole vCenter, so `Datacenters` doesn't filter them.

The API never returns a connection's url or credentials. Variables under `Infrastructure__` are never passed to Terraform runs, even with `Terraform:EnvironmentVariables:InheritAll` on; a run gets a connection's settings only through an Environment Set's connection entries.

## What is read

`Inventory:Enabled` turns the connection's inventory on, and `Inventory:RefreshMinutes` (default 30) is how often the cache is refreshed in the background. Anyone who can see the inventory can also refresh it.

`Inventory:Resources` has a setting for each kind of object: `VmTemplates`, `Isos`, `Networks`, `Datastores`, `ContentLibraries` and `Hosts`. All of them are on by default.

- **Enabled: false** skips reading that kind of object. Its endpoint reports it as not enabled, and the Inventory panel leaves it out. Data another kind needs is still read, e.g. datastores for the ISO search.
- **Locations** lists only objects in those places. Every entry ignores case.
  - vSphere: a full inventory path, in the form `govc` uses, matching that folder and everything below it. `/DC1/vm/Templates` matches `/DC1/vm/Templates/linux/ubuntu`. VM templates take a VM folder, datastores a datastore folder or datastore cluster, and hosts a cluster. Networks take a network folder, or a distributed switch's name to list its port groups.
  - Proxmox: a name. VM templates take a node or resource pool, networks a node (for bridges) or SDN zone (for vnets), datastores a storage, and hosts a node.
  - `Isos` and `ContentLibraries` have no Locations: ISOs are found in `IsoLocations`, and content libraries are filtered by name.
- **Include** and **Exclude** are regular expressions on the object's name, ignoring case. An object is listed when its name matches `Include` (or there is none) and doesn't match `Exclude`.

A setting that can't be used, such as an invalid regular expression or a location in the wrong form, marks the connection as misconfigured. It isn't read, and `GET api/inventory/connections` and every inventory endpoint say why. The API still starts.

## Projects

A connection's inventory reaches a project's Inventory panel only when the connection is assigned to the project: in Administration > Infrastructure Connections, in the project's Environment dialog, through `POST api/assignments` (several changes at once, see [Environment Sets](EnvironmentSets.md#assigning)) or `POST api/projects/{id}/inventory/connections/{name}`. Assigning needs `ManageInfrastructure`, not a project permission, so a project's managers can't give themselves a hypervisor.

* `api/projects/{id}/inventory/...` serves the same lists as `api/inventory/...`, for the project's connections only. A request that names no connection reads the project's first assigned connection, in configuration order, with inventory enabled. A project with none gets an empty result with no connection, never another connection's data.
* An assignment names the connection as configuration spelled it. A connection that leaves configuration, or is renamed, stays assigned and shows as missing.
* Assigning a connection gives its inventory, not its credentials. Runs get credentials only from [Environment Sets](EnvironmentSets.md), whose connection entries name a connection on their own.
* Assignments are not exported.

## Paths

Each vSphere object's `path` is its full inventory path, e.g. `/DC1/vm/Templates/linux/ubuntu-22.04`, `/DC1/network/Lab/lab-net` or `/DC1/host/Cluster1/esx01`, which tells apart identical folders in different datacenters. ISOs keep their datastore path, `[iso-datastore] stock/alpine.iso`. The Terraform snippets still find a template by the path below its datacenter's VM folder, which is what `vsphere_virtual_machine` takes.

## Caching

Each connection's inventory is cached in memory. When a read fails, the last good read of each kind of object is kept and marked stale. Changing any setting that changes what's listed (provider, url, credentials, `Datacenters`, `IsoLocations`, or the `Inventory` settings other than `RefreshMinutes`) empties the cache, and a read that was already running is discarded rather than shown.

Provider errors are reported and logged with the connection's password, token and token secret masked.
