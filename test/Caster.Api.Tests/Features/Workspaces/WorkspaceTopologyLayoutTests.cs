// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using File = System.IO.File;
using Workspace = Caster.Api.Domain.Models.Workspace;
using WorkspaceTopology = Caster.Api.Features.Workspaces.WorkspaceTopology;

namespace Caster.Api.Tests.Features.Workspaces;

/// <summary>
/// <c>PATCH api/workspaces/{id}/topology/layout</c>: where people put the topology view's nodes and what
/// each machine is for, saved on the workspace so everyone sees one arrangement. Saving needs EditProject
/// (or EditProjects); <c>GET api/workspaces/{id}/topology</c> returns it for the nodes it draws.
/// </summary>
public class WorkspaceTopologyLayoutTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "Data", "topology");

    [Fact]
    public async Task A_member_holding_EditProject_saves_a_nodes_position_and_role_and_the_topology_returns_it()
    {
        var (project, workspace) = await SeedAppliedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject, ProjectPermission.ViewProject]).SeedAsync();
        var dc = (await GetTopology(workspace)).Nodes.First(x => x.Kind == TopologyNodeKind.Machine).Id;

        var response = await Client(actor).PatchAsJsonAsync(LayoutUrl(workspace),
            new { nodes = new Dictionary<string, object> { [dc] = new { x = 120.5, y = -40, role = "DomainController" } } }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var saved = (await GetTopology(workspace)).Layout[dc];
        Assert.Equal(120.5, saved.X);
        Assert.Equal(-40, saved.Y);
        Assert.Equal(TopologyNodeRole.DomainController, saved.Role);
    }

    [Fact]
    public async Task Saving_one_node_keeps_the_others_and_a_null_forgets_a_node()
    {
        var (_, workspace) = await SeedAppliedWorkspace();
        var ids = (await GetTopology(workspace)).Nodes.Select(x => x.Id).Take(3).ToArray();

        await Patch(workspace, new() { [ids[0]] = new { x = 1, y = 1 }, [ids[1]] = new { x = 2, y = 2 } });
        await Patch(workspace, new() { [ids[1]] = null, [ids[2]] = new { role = "Firewall" } });

        var layout = (await GetTopology(workspace)).Layout;
        Assert.Equal([ids[0], ids[2]], layout.Keys.Order());
        Assert.Equal(1, layout[ids[0]].X);
        Assert.Null(layout[ids[2]].X);
        Assert.Equal(TopologyNodeRole.Firewall, layout[ids[2]].Role);
    }

    [Fact]
    public async Task Clearing_a_nodes_position_keeps_its_role_and_clearing_everything_forgets_it()
    {
        var (_, workspace) = await SeedAppliedWorkspace();
        var ids = (await GetTopology(workspace)).Nodes.Select(x => x.Id).Take(2).ToArray();
        await Patch(workspace, new() { [ids[0]] = new { x = 5, y = 5, role = "Router" }, [ids[1]] = new { x = 6, y = 6 } });

        await Patch(workspace, new() { [ids[0]] = new { role = "Router" }, [ids[1]] = new { } });

        var layout = (await GetTopology(workspace)).Layout;
        Assert.Equal([ids[0]], layout.Keys);
        Assert.Null(layout[ids[0]].X);
        Assert.Equal(TopologyNodeRole.Router, layout[ids[0]].Role);
    }

    [Fact]
    public async Task A_node_the_topology_no_longer_draws_is_left_out_but_kept_for_when_it_returns()
    {
        var (_, workspace) = await SeedAppliedWorkspace();
        var drawn = (await GetTopology(workspace)).Nodes[0].Id;

        await Patch(workspace, new() { [drawn] = new { x = 1, y = 1 }, ["module.gone.proxmox_virtual_environment_vm.vm"] = new { x = 2, y = 2 } });

        Assert.Equal([drawn], (await GetTopology(workspace)).Layout.Keys);
        var stored = await NewContext().Workspaces.Where(x => x.Id == workspace.Id).Select(x => x.TopologyLayout).SingleAsync(Ct);
        Assert.Contains("module.gone.proxmox_virtual_environment_vm.vm", stored);
    }

    [Fact]
    public async Task Forgetting_every_node_leaves_no_layout_stored()
    {
        var (_, workspace) = await SeedAppliedWorkspace();
        var id = (await GetTopology(workspace)).Nodes[0].Id;
        await Patch(workspace, new() { [id] = new { x = 1, y = 1 } });

        await Patch(workspace, new() { [id] = null });

        Assert.Null(await NewContext().Workspaces.Where(x => x.Id == workspace.Id).Select(x => x.TopologyLayout).SingleAsync(Ct));
    }

    [Fact]
    public async Task Saving_is_forbidden_for_a_caller_holding_only_ViewProject()
    {
        var (project, workspace) = await SeedAppliedWorkspace();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).PatchAsJsonAsync(LayoutUrl(workspace),
            new { nodes = new Dictionary<string, object> { ["any"] = new { x = 1, y = 1 } } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Null(await NewContext().Workspaces.Where(x => x.Id == workspace.Id).Select(x => x.TopologyLayout).SingleAsync(Ct));
    }

    [Fact]
    public async Task An_empty_node_id_is_a_bad_request()
    {
        var (_, workspace) = await SeedAppliedWorkspace();

        var response = await RootClient.PatchAsJsonAsync(LayoutUrl(workspace),
            new { nodes = new Dictionary<string, object> { [" "] = new { x = 1, y = 1 } } }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task An_unknown_role_is_a_bad_request()
    {
        var (_, workspace) = await SeedAppliedWorkspace();

        var response = await RootClient.PatchAsJsonAsync(LayoutUrl(workspace),
            new { nodes = new Dictionary<string, object> { ["node"] = new { role = "Toaster" } } }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }

    private static string LayoutUrl(Workspace workspace) => $"api/workspaces/{workspace.Id}/topology/layout";

    private async Task Patch(Workspace workspace, Dictionary<string, object> nodes) =>
        await AssertStatus(HttpStatusCode.OK, await RootClient.PatchAsJsonAsync(LayoutUrl(workspace), new { nodes }, Ct));

    private async Task<WorkspaceTopology> GetTopology(Workspace workspace) =>
        await ReadAsync<WorkspaceTopology>(await RootClient.GetAsync($"api/workspaces/{workspace.Id}/topology", Ct));

    /// <summary>A workspace whose state holds the sample Proxmox topology, with no runs.</summary>
    private async Task<(Project Project, Workspace Workspace)> SeedAppliedWorkspace()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        workspace.State = await File.ReadAllTextAsync(Path.Combine(Data, "proxmox-modules.tfstate.json"), Ct);
        await Seed(project, directory, workspace);

        return (project, workspace);
    }
}
