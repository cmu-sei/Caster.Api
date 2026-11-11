# Deployment Capacity

Before a run is applied, Caster checks its size: against the limits set for its workspace, and against the room left on the hypervisors. A run over a limit is never applied. A run the hypervisors may not have room for is applied once someone acknowledges the warning, or not at all, depending on `Capacity:RoomCheck`.

## Settings

```json
"Capacity": {
  "MaxMachines": null,
  "MaxVcpu": null,
  "MaxMemoryGiB": null,
  "MaxDiskGiB": null,
  "CpuOvercommitRatio": 4,
  "ReservePercent": 10,
  "RoomCheck": "Warn"
}
```

| Setting | Default | Meaning |
| --- | --- | --- |
| `MaxMachines` | none | The most machines one workspace may have. |
| `MaxVcpu` | none | The most vCPUs one workspace's machines may have between them. |
| `MaxMemoryGiB` | none | The most memory, in GiB, one workspace's machines may have between them. |
| `MaxDiskGiB` | none | The most disk, in GiB, one workspace's machines may have between them. |
| `CpuOvercommitRatio` | 4 | vCPUs a host may give its VMs for each core it has. |
| `ReservePercent` | 10 | Percent of each host's memory and each datastore's free space never counted as room. |
| `RoomCheck` | `Warn` | `Warn`: a run without room is applied once acknowledged. `Block`: it is not applied. |

As environment variables: `Capacity__MaxVcpu=200`, `Capacity__RoomCheck=Block`.

## Limits

Each project and each workspace can set the same four limits. The limit in force for a workspace is the lowest of its own, its project's and the global one; one that none of them sets is no limit. Only a user with the `EditProjects` system permission can set them, with `PUT api/projects/{id}/limits` and `PUT api/workspaces/{id}/limits`; a project's own editors cannot. `GET api/workspaces/{id}/limits` and `GET api/projects/{id}/limits` give the limits in force, and where each is set.

Limits are on what the workspace has after the run: the machines it keeps, those it changes and those it creates. A run is refused only for a limit it takes the workspace further over, so shrinking or destroying a workspace that is over a lowered limit still works.

## Room

The room comes from the infrastructure inventory (see [Inventory.md](Inventory.md)): the hosts and datastores of every connection with inventory enabled, as last read. With no inventory, room is not checked.

- Free vCPU on a host is its cores times `CpuOvercommitRatio`, less the vCPUs its running VMs are given. A host that does not report what its VMs are given has its current CPU load times its cores taken off instead, and the check says so.
- Free memory on a host is its memory less `ReservePercent`, less the memory in use.
- Free disk on a datastore is its free space less `ReservePercent`. Proxmox storages that hold no VM or container disks are left out, as are vSphere datastores that aren't accessible. A Proxmox storage local to each node (`local-lvm`) is one datastore, with the free space of every node added up.
- An offline, disconnected or powered off host has no room.

Only what a run adds needs room: every machine it creates, and what it gives a machine it changes beyond what that had. What it frees is not counted.

A machine the plan puts on a host is counted against that host: a Proxmox `node_name` or `target_node`, or a vSphere `host_system_id`. A machine on no host, or on one the inventory does not list, is counted against every online host of its provider between them, less what the placed machines take. Disks are counted the same way by datastore: a Proxmox `datastore_id` or `storage`, or a vSphere disk's `datastore_id`, else the machine's.

## Machines

The machines and their sizes are read from `terraform show -json` of the plan, when the plan is made, and saved on it. Where the plan leaves out an attribute that has a provider default, the default is used.

| Resource | vCPU | Memory | Disks | Host |
| --- | --- | --- | --- | --- |
| `proxmox_virtual_environment_vm` (bpg) | `cpu.cores` x `cpu.sockets` | `memory.dedicated` | each `disk` `size` on its `datastore_id` | `node_name` |
| `proxmox_virtual_environment_cloned_vm`, `_vm2` (bpg) | `cpu.cores` x `cpu.sockets` | `memory.size` | each `disk` by slot | `node_name` |
| `proxmox_virtual_environment_container` (bpg) | `cpu.cores` | `memory.dedicated` | `disk.size`, sized `mount_point`s | `node_name` |
| `proxmox_vm_qemu` (Telmate 2 and 3) | `vcpus`, else cores x sockets | `memory` | `disk` list, or the `disks` tree | `target_node` |
| `proxmox_lxc` (Telmate) | `cores` | `memory` | `rootfs`, `mountpoint`s | `target_node` |
| `vsphere_virtual_machine` | `num_cpus` | `memory` | each `disk` `size` | `host_system_id` |
| `aws_instance`, `azurerm_*virtual_machine` | | | | |

Cloud machines count against `MaxMachines` only. A clone that does not size its disks takes them from its template, which the plan does not know, so they are not counted, and the check says which machines are not wholly sized. Runs planned before this check existed are not checked. Player records (`crucible_player_virtual_machine`) are not machines.

## Apply

`GET api/runs/{id}/capacity` gives a run's check as it stands now: what the run adds, what the workspace has before and after, the limits in force, a line for each host and datastore with what is needed and what is free, and notes on what was not checked. `POST api/runs/{id}/actions/apply` makes the same check: over a limit, or without room in `Block` mode, it answers 409 with the reason; without room in `Warn` mode, it answers 409 unless called with `?acknowledgeCapacityWarning=true`.

## AI drafts

An AI design or Terraform draft is shown the room on each host of the connections it is grounded in (free vCPU and memory, worked out as above) and the limits a workspace in its project is held to: the lower of the project's and the global ones. Its topology plan is checked as a run of its machines would be, each sized from the plan or its template; the plan places machines on no host, so they are counted against all of their hypervisor's hosts and datastores between them. Room and limit problems are warnings on the draft, and the check is on the plan's totals as `capacity`.

Saving a draft (`POST api/directories/{id}/designs/ai-draft/create`, `POST api/directories/{id}/files/ai-draft/apply`) with its `topology` is refused with a 409 when the machines go over a limit. Room is not checked when saving, since it can change before a run; the check before apply makes it then.
