// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Inventory;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Features.Inventory;

/// <summary>
/// <c>api/projects/{id}/inventory</c>: a project's members see the inventory of the connections assigned
/// to it, and only those; assigning one needs ManageInfrastructure. The connections are
/// <see cref="TestConfiguration"/>'s.
/// </summary>
public class ProjectInventoryRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private static string Inventory(Guid projectId) => $"api/projects/{projectId}/inventory";

    private static ProjectInventoryConnection Assignment(Project project, string name) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = project.Id,
        ConnectionName = name,
        NormalizedConnectionName = ProjectInventoryConnection.Normalize(name)
    };

    [Fact]
    public async Task Assign_needs_ManageInfrastructure_not_a_project_permission()
    {
        var project = TestData.Project();
        await Seed(project);
        var projectManager = await Actor().OnProject(project, [ProjectPermission.ManageProject]).SeedAsync();
        var infrastructure = await Actor().WithSystemPermissions(SystemPermission.ManageInfrastructure).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden,
            await Client(projectManager).PostAsync($"{Inventory(project.Id)}/connections/{TestConfiguration.ProxmoxConnection}", null, Ct));

        var response = await Client(infrastructure).PostAsync($"{Inventory(project.Id)}/connections/{TestConfiguration.ProxmoxConnection}", null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var connection = Assert.Single(await ReadAsync<InventoryConnection[]>(response));
        Assert.Equal(TestConfiguration.ProxmoxConnection, connection.Name);
        Assert.Equal(InventoryConnectionStatus.Available, connection.Status);
    }

    [Fact]
    public async Task Assign_stores_the_configured_spelling_once_however_it_is_named()
    {
        var project = TestData.Project();
        await Seed(project);

        await AssertStatus(HttpStatusCode.OK, await RootClient.PostAsync($"{Inventory(project.Id)}/connections/LAB-PVE", null, Ct));
        await AssertStatus(HttpStatusCode.OK, await RootClient.PostAsync($"{Inventory(project.Id)}/connections/lab-pve", null, Ct));

        var stored = await ReadBack(db => db.ProjectInventoryConnections.Where(x => x.ProjectId == project.Id).ToListAsync(Ct));
        Assert.Equal([TestConfiguration.ProxmoxConnection], stored.Select(x => x.ConnectionName));
    }

    [Fact]
    public async Task Assign_refuses_a_connection_that_is_not_configured()
    {
        var project = TestData.Project();
        await Seed(project);

        await AssertProblem(HttpStatusCode.NotFound, await RootClient.PostAsync($"{Inventory(project.Id)}/connections/nowhere", null, Ct));
    }

    [Fact]
    public async Task A_member_reads_an_assigned_connection_and_its_inventory()
    {
        var project = TestData.Project();
        await Seed(project, Assignment(project, TestConfiguration.VsphereConnection));
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var connections = await ReadAsync<InventoryConnection[]>(await Client(member).GetAsync($"{Inventory(project.Id)}/connections", Ct));
        var templates = await ReadAsync<InventoryResult>(await Client(member).GetAsync($"{Inventory(project.Id)}/vm-templates", Ct));

        Assert.Equal([TestConfiguration.VsphereConnection], connections.Select(x => x.Name));
        Assert.Equal(TestConfiguration.VsphereConnection, templates.Connection);
    }

    [Fact]
    public async Task A_member_cannot_read_a_connection_the_project_was_not_given()
    {
        var project = TestData.Project();
        await Seed(project, Assignment(project, TestConfiguration.VsphereConnection));
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(member).GetAsync($"{Inventory(project.Id)}/isos?connection={TestConfiguration.ProxmoxConnection}", Ct);

        await AssertProblem(HttpStatusCode.NotFound, response);
    }

    /// <summary>The service's global default is never used for a project.</summary>
    [Fact]
    public async Task A_project_without_a_connection_lists_nothing()
    {
        var project = TestData.Project();
        await Seed(project);
        var member = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var result = await ReadAsync<InventoryResult>(await Client(member).GetAsync($"{Inventory(project.Id)}/networks", Ct));

        Assert.Null(result.Connection);
        Assert.False(result.Available);
        Assert.Empty(result.Items);
        Assert.Equal(ProjectInventoryConnections.NoConnection, result.Error);
    }

    [Fact]
    public async Task Another_projects_member_is_forbidden()
    {
        var project = TestData.Project();
        var other = TestData.Project("Other");
        await Seed(project, other, Assignment(project, TestConfiguration.VsphereConnection));
        var outsider = await Actor().OnProject(other, [ProjectPermission.ViewProject]).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(outsider).GetAsync($"{Inventory(project.Id)}/vm-templates", Ct));
    }

    [Fact]
    public async Task ViewInfrastructure_reads_any_connection_through_a_project()
    {
        var project = TestData.Project();
        await Seed(project);
        var admin = await Actor().WithSystemPermissions(SystemPermission.ViewInfrastructure).SeedAsync();

        var result = await ReadAsync<InventoryResult>(
            await Client(admin).GetAsync($"{Inventory(project.Id)}/hosts?connection={TestConfiguration.ProxmoxConnection}", Ct));

        Assert.Equal(TestConfiguration.ProxmoxConnection, result.Connection);
    }

    [Fact]
    public async Task A_connection_that_left_configuration_stays_assigned_as_missing_and_can_be_unassigned()
    {
        var project = TestData.Project();
        await Seed(project, Assignment(project, "renamed-away"));

        var connection = Assert.Single(await ReadAsync<InventoryConnection[]>(await RootClient.GetAsync($"{Inventory(project.Id)}/connections", Ct)));
        Assert.Equal(InventoryConnectionStatus.Missing, connection.Status);

        await AssertStatus(HttpStatusCode.OK, await RootClient.DeleteAsync($"{Inventory(project.Id)}/connections/Renamed-Away", Ct));
        Assert.False(await ReadBack(db => db.ProjectInventoryConnections.AnyAsync(x => x.ProjectId == project.Id, Ct)));
    }

    [Fact]
    public async Task The_system_connection_list_names_the_projects_each_is_assigned_to()
    {
        var project = TestData.Project("Range A");
        await Seed(project, Assignment(project, TestConfiguration.ProxmoxConnection));
        var admin = await Actor().WithSystemPermissions(SystemPermission.ViewInfrastructure).SeedAsync();

        var connections = await ReadAsync<InventoryConnection[]>(await Client(admin).GetAsync("api/inventory/connections", Ct));

        Assert.Contains(connections.Single(x => x.Name == TestConfiguration.ProxmoxConnection).Projects, x => x.ProjectId == project.Id);
    }
}
