// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Capacity;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Infrastructure.Serialization;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Features.Capacity;

/// <summary>
/// The size check before apply, over HTTP: a run over a limit is refused, one the hypervisors may not have
/// room for is refused in Block mode and needs acknowledging in Warn mode; and the limits only an admin sets.
/// </summary>
public class CapacityRequestTests(DatabaseFixture fixture, CapacityAppFactory factory)
    : ApiTestBase(fixture, factory), IClassFixture<CapacityAppFactory>
{
    private const long GiB = 1024L * 1024 * 1024;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        factory.Options = new CapacityOptions();
        factory.Room = Room(freeVcpu: 64, freeMemoryGiB: 256);
    }

    private static HypervisorRoom Room(int freeVcpu, long freeMemoryGiB)
    {
        var room = new HypervisorRoom
        {
            Hosts = [new HostRoom { Connection = "lab", Hypervisor = "proxmox", Id = "pve1", Name = "pve1", Online = true, FreeVcpu = freeVcpu, FreeMemoryMiB = freeMemoryGiB * 1024 }],
        };
        room.HostsRead.Add("proxmox");
        return room;
    }

    /// <summary>A planned run that creates one 4 vCPU, 8 GiB machine with a 40 GiB disk on pve1.</summary>
    private async Task<(Project Project, Workspace Workspace, Run Run)> SeedPlannedRun(int? projectMaxVcpu = null, int? workspaceMaxMachines = null)
    {
        var project = TestData.Project();
        project.MaxVcpu = projectMaxVcpu;
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        workspace.MaxMachines = workspaceMaxMachines;
        var run = TestData.Run(workspace, RunStatus.Planned);
        var plan = TestData.Plan(run);
        plan.CapacityDemand = JsonSerializer.Serialize(new DeploymentDemand
        {
            Machines =
            [
                new PlannedMachine
                {
                    Address = "proxmox_virtual_environment_vm.web",
                    Type = "proxmox_virtual_environment_vm",
                    Hypervisor = "proxmox",
                    Action = "create",
                    Host = "pve1",
                    After = new MachineSize { Vcpu = 4, MemoryMiB = 8192, Disks = [new PlannedDisk { Datastore = "local-lvm", SizeGiB = 40 }] },
                },
            ],
        }, DefaultJsonSettings.Settings);
        await Seed(project, directory, workspace, run, plan);

        return (project, workspace, run);
    }

    private Task<System.Net.Http.HttpResponseMessage> Apply(Run run, bool acknowledge = false) =>
        RootClient.PostAsync($"api/runs/{run.Id}/actions/apply{(acknowledge ? "?acknowledgeCapacityWarning=true" : "")}", null, Ct);

    private async Task<bool> Applied(Run run)
    {
        await using var context = NewContext();
        return await context.Applies.AnyAsync(x => x.RunId == run.Id, Ct);
    }

    [Fact]
    public async Task Apply_within_the_limits_and_the_room_is_queued()
    {
        var (_, _, run) = await SeedPlannedRun();

        await AssertStatus(HttpStatusCode.OK, await Apply(run));
        Assert.True(await Applied(run));
    }

    [Fact]
    public async Task Apply_over_a_project_limit_is_refused_naming_the_limit_and_totals()
    {
        var (_, _, run) = await SeedPlannedRun(projectMaxVcpu: 2);

        var problem = await AssertProblem(HttpStatusCode.Conflict, await Apply(run, acknowledge: true));

        Assert.Contains("4 vCPUs, over its limit of 2 vCPUs (set on the project)", problem.Title);
        Assert.False(await Applied(run));
    }

    [Fact]
    public async Task Apply_over_a_global_limit_is_refused()
    {
        factory.Options = new CapacityOptions { MaxMemoryGiB = 4 };
        var (_, _, run) = await SeedPlannedRun();

        var problem = await AssertProblem(HttpStatusCode.Conflict, await Apply(run));

        Assert.Contains("set in the Capacity settings", problem.Title);
    }

    [Fact]
    public async Task The_lowest_limit_wins_over_a_higher_global_one()
    {
        factory.Options = new CapacityOptions { MaxMachines = 10 };
        var (_, _, run) = await SeedPlannedRun(workspaceMaxMachines: 0);

        var problem = await AssertProblem(HttpStatusCode.Conflict, await Apply(run));

        Assert.Contains("(set on the workspace)", problem.Title);
    }

    [Fact]
    public async Task Apply_without_room_in_warn_mode_needs_acknowledging()
    {
        factory.Room = Room(freeVcpu: 2, freeMemoryGiB: 256);
        var (_, _, run) = await SeedPlannedRun();

        var problem = await AssertProblem(HttpStatusCode.Conflict, await Apply(run));
        Assert.Contains("pve1 needs 4 vCPUs", problem.Title);
        Assert.Contains("Acknowledge the warning", problem.Title);
        Assert.False(await Applied(run));

        await AssertStatus(HttpStatusCode.OK, await Apply(run, acknowledge: true));
        Assert.True(await Applied(run));
    }

    [Fact]
    public async Task Apply_without_room_in_block_mode_is_refused_even_when_acknowledged()
    {
        factory.Options = new CapacityOptions { RoomCheck = RoomCheckMode.Block };
        factory.Room = Room(freeVcpu: 64, freeMemoryGiB: 4);
        var (_, _, run) = await SeedPlannedRun();

        var problem = await AssertProblem(HttpStatusCode.Conflict, await Apply(run, acknowledge: true));

        Assert.StartsWith("This run cannot be applied.", problem.Title);
        Assert.False(await Applied(run));
    }

    [Fact]
    public async Task A_run_planned_before_the_check_existed_is_applied()
    {
        factory.Options = new CapacityOptions { MaxMachines = 0 };
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        var run = TestData.Run(workspace, RunStatus.Planned);
        await Seed(project, directory, workspace, run, TestData.Plan(run));

        await AssertStatus(HttpStatusCode.OK, await Apply(run));
    }

    [Fact]
    public async Task Get_capacity_shows_the_totals_limits_and_host_lines()
    {
        factory.Room = Room(freeVcpu: 2, freeMemoryGiB: 256);
        var (project, _, run) = await SeedPlannedRun(projectMaxVcpu: 16);
        var viewer = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var check = await ReadAsync<CapacityCheck>(await Client(viewer).GetAsync($"api/runs/{run.Id}/capacity", Ct));

        Assert.Equal(CapacityCheckStatus.Warning, check.Status);
        Assert.True(check.RequiresAcknowledgement);
        Assert.Equal(1, check.Requested.Machines);
        Assert.Equal(4, check.Requested.Vcpu);
        Assert.Equal(16, check.Limits.MaxVcpu.Value);
        Assert.Equal(LimitSource.Project, check.Limits.MaxVcpu.Source);
        var host = Assert.Single(check.Hosts);
        Assert.Equal(("pve1", 4, 2, false), (host.Host, host.NeedVcpu, host.FreeVcpu, host.Fits));
    }

    [Fact]
    public async Task Get_capacity_is_forbidden_on_another_project()
    {
        var (_, _, run) = await SeedPlannedRun();
        var stranger = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(stranger).GetAsync($"api/runs/{run.Id}/capacity", Ct));
    }
}
