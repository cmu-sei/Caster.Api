// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Features.EnvironmentSets.EventHandlers;
using Caster.Api.Hubs;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Features.Assignments;

/// <summary>
/// <c>POST api/assignments</c>: Environment Sets and inventory connections added to and removed from
/// projects and project groups in one request, checked against the final state and saved together. Set changes need
/// ManageEnvironmentSets, connection changes ManageInfrastructure.
/// </summary>
public class AssignmentRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string Assignments = "api/assignments";

    [Fact]
    public async Task Edit_adds_and_removes_sets_and_connections_together()
    {
        var project = TestData.Project();
        var kept = TestData.EnvironmentSet("kept");
        var removed = TestData.EnvironmentSet("removed");
        var added = TestData.EnvironmentSet("added");
        await Seed(
            project, kept, removed, added,
            TestData.ProjectEnvironmentSet(project, kept),
            TestData.ProjectEnvironmentSet(project, removed),
            Connection(project, TestConfiguration.VsphereConnection));

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[]
            {
                new { operation = "Add", projectId = project.Id, environmentSetId = added.Id },
                new { operation = "Remove", projectId = project.Id, environmentSetId = removed.Id },
                new { operation = "Add", projectId = project.Id, connectionName = TestConfiguration.ProxmoxConnection.ToUpperInvariant() },
                new { operation = "Remove", projectId = project.Id, connectionName = TestConfiguration.VsphereConnection }
            }
        }, Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        var sets = await ReadBack(db => db.ProjectEnvironmentSets.Where(x => x.ProjectId == project.Id).Select(x => x.EnvironmentSetId).ToListAsync(Ct));
        Assert.Equal(new[] { added.Id, kept.Id }.Order(), sets.Order());
        var connection = Assert.Single(await ReadBack(db => db.ProjectInventoryConnections.Where(x => x.ProjectId == project.Id).ToListAsync(Ct)));
        Assert.Equal(TestConfiguration.ProxmoxConnection, connection.ConnectionName);
        Assert.Contains(Factory.Hub<ProjectHub>().ToGroup(project.Id), x => x.Method == EnvironmentSetHubMethods.ProjectEnvironmentSetsUpdated);
    }

    /// <summary>Swapping one set for another that gives the same names passes: only the final state is checked.</summary>
    [Fact]
    public async Task Edit_swaps_a_set_for_one_giving_the_same_names()
    {
        var project = TestData.Project();
        var east = TestData.EnvironmentSet("east");
        var west = TestData.EnvironmentSet("west");
        await Seed(
            project, east, west,
            TestData.PlainEntry(east, "REGION", "east"),
            TestData.PlainEntry(west, "REGION", "west"),
            TestData.ProjectEnvironmentSet(project, east));

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[]
            {
                new { operation = "Remove", projectId = project.Id, environmentSetId = east.Id },
                new { operation = "Add", projectId = project.Id, environmentSetId = west.Id }
            }
        }, Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Equal(west.Id, Assert.Single(await ReadBack(db => db.ProjectEnvironmentSets.Where(x => x.ProjectId == project.Id).ToListAsync(Ct))).EnvironmentSetId);
    }

    [Fact]
    public async Task Edit_refuses_two_added_sets_giving_the_same_name_and_changes_nothing()
    {
        var project = TestData.Project();
        var first = TestData.EnvironmentSet("first");
        var second = TestData.EnvironmentSet("second");
        await Seed(project, first, second, TestData.PlainEntry(first, "REGION", "east"), TestData.PlainEntry(second, "REGION", "west"));

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[]
            {
                new { operation = "Add", projectId = project.Id, environmentSetId = first.Id },
                new { operation = "Add", projectId = project.Id, environmentSetId = second.Id },
                new { operation = "Add", projectId = project.Id, connectionName = TestConfiguration.ProxmoxConnection }
            }
        }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.False(await ReadBack(db => db.ProjectEnvironmentSets.AnyAsync(x => x.ProjectId == project.Id, Ct)));
        Assert.False(await ReadBack(db => db.ProjectInventoryConnections.AnyAsync(x => x.ProjectId == project.Id, Ct)));
    }

    [Fact]
    public async Task Edit_refuses_an_added_set_giving_a_name_a_kept_set_gives()
    {
        var project = TestData.Project();
        var kept = TestData.EnvironmentSet("kept");
        var added = TestData.EnvironmentSet("added");
        await Seed(
            project, kept, added,
            TestData.PlainEntry(kept, "REGION", "east"),
            TestData.PlainEntry(added, "REGION", "west"),
            TestData.ProjectEnvironmentSet(project, kept));

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[] { new { operation = "Add", projectId = project.Id, environmentSetId = added.Id } }
        }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    [Fact]
    public async Task Edit_assigns_one_set_to_several_projects()
    {
        var a = TestData.Project("a");
        var b = TestData.Project("b");
        var set = TestData.EnvironmentSet();
        await Seed(a, b, set);

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[]
            {
                new { operation = "Add", projectId = a.Id, environmentSetId = set.Id },
                new { operation = "Add", projectId = b.Id, environmentSetId = set.Id }
            }
        }, Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Equal(2, await ReadBack(db => db.ProjectEnvironmentSets.CountAsync(x => x.EnvironmentSetId == set.Id, Ct)));
    }

    /// <summary>Adding what is assigned, or removing what isn't, is not an error: the request says the final state.</summary>
    [Fact]
    public async Task Edit_changes_nothing_for_an_add_already_made_or_a_remove_of_what_is_not_assigned()
    {
        var project = TestData.Project();
        var assigned = TestData.EnvironmentSet("assigned");
        var other = TestData.EnvironmentSet("other");
        await Seed(project, assigned, other, TestData.ProjectEnvironmentSet(project, assigned));

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[]
            {
                new { operation = "Add", projectId = project.Id, environmentSetId = assigned.Id },
                new { operation = "Remove", projectId = project.Id, environmentSetId = other.Id },
                new { operation = "Remove", projectId = project.Id, connectionName = "not-configured" }
            }
        }, Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Equal(assigned.Id, Assert.Single(await ReadBack(db => db.ProjectEnvironmentSets.Where(x => x.ProjectId == project.Id).ToListAsync(Ct))).EnvironmentSetId);
    }

    [Fact]
    public async Task Edit_refuses_a_set_whose_secret_source_no_longer_matches_the_patterns()
    {
        var project = TestData.Project();
        var set = TestData.EnvironmentSet();
        await Seed(project, set, TestData.SecretEntry(set, "TOKEN", "ConnectionStrings__PostgreSQL"));

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[] { new { operation = "Add", projectId = project.Id, environmentSetId = set.Id } }
        }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    [Fact]
    public async Task Edit_answers_404_for_a_connection_that_is_not_configured_and_changes_nothing()
    {
        var project = TestData.Project();
        var set = TestData.EnvironmentSet();
        await Seed(project, set);

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[]
            {
                new { operation = "Add", projectId = project.Id, environmentSetId = set.Id },
                new { operation = "Add", projectId = project.Id, connectionName = "not-configured" }
            }
        }, Ct);

        await AssertProblem(HttpStatusCode.NotFound, response);
        Assert.False(await ReadBack(db => db.ProjectEnvironmentSets.AnyAsync(x => x.ProjectId == project.Id, Ct)));
    }

    [Fact]
    public async Task Edit_answers_404_for_a_project_that_does_not_exist()
    {
        var set = TestData.EnvironmentSet();
        await Seed(set);

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[] { new { operation = "Add", projectId = Guid.NewGuid(), environmentSetId = set.Id } }
        }, Ct);

        await AssertProblem(HttpStatusCode.NotFound, response);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Edit_refuses_a_change_naming_both_or_neither_a_set_and_a_connection(bool withSet, bool withConnection)
    {
        var project = TestData.Project();
        var set = TestData.EnvironmentSet();
        await Seed(project, set);

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[]
            {
                new
                {
                    operation = "Add",
                    projectId = project.Id,
                    environmentSetId = withSet ? set.Id : (Guid?)null,
                    connectionName = withConnection ? TestConfiguration.ProxmoxConnection : null
                }
            }
        }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task Edit_refuses_a_set_both_added_and_removed()
    {
        var project = TestData.Project();
        var set = TestData.EnvironmentSet();
        await Seed(project, set);

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[]
            {
                new { operation = "Add", projectId = project.Id, environmentSetId = set.Id },
                new { operation = "Remove", projectId = project.Id, environmentSetId = set.Id }
            }
        }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task Edit_refuses_an_empty_list()
    {
        await AssertProblem(HttpStatusCode.BadRequest, await RootClient.PostAsJsonAsync(Assignments, new { changes = Array.Empty<object>() }, Ct));
    }

    /// <summary>Each kind of change needs its own permission, and a project permission gives neither.</summary>
    [Fact]
    public async Task Edit_needs_ManageEnvironmentSets_for_sets_and_ManageInfrastructure_for_connections()
    {
        var project = TestData.Project();
        var set = TestData.EnvironmentSet();
        await Seed(project, set);
        var sets = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();
        var infrastructure = await Actor().WithSystemPermissions(SystemPermission.ManageInfrastructure).SeedAsync();
        var projectManager = await Actor().OnProject(project, [ProjectPermission.ManageProject]).SeedAsync();
        var setChange = new { operation = "Add", projectId = project.Id, environmentSetId = (Guid?)set.Id, connectionName = (string)null };
        var connectionChange = new { operation = "Add", projectId = project.Id, environmentSetId = (Guid?)null, connectionName = TestConfiguration.ProxmoxConnection };

        await AssertProblem(HttpStatusCode.Forbidden, await Client(sets).PostAsJsonAsync(Assignments, new { changes = new[] { setChange, connectionChange } }, Ct));
        await AssertProblem(HttpStatusCode.Forbidden, await Client(infrastructure).PostAsJsonAsync(Assignments, new { changes = new[] { setChange } }, Ct));
        await AssertProblem(HttpStatusCode.Forbidden, await Client(projectManager).PostAsJsonAsync(Assignments, new { changes = new[] { setChange } }, Ct));
        Assert.False(await ReadBack(db => db.ProjectEnvironmentSets.AnyAsync(x => x.ProjectId == project.Id, Ct)));

        await AssertStatus(HttpStatusCode.NoContent, await Client(sets).PostAsJsonAsync(Assignments, new { changes = new[] { setChange } }, Ct));
        await AssertStatus(HttpStatusCode.NoContent, await Client(infrastructure).PostAsJsonAsync(Assignments, new { changes = new[] { connectionChange } }, Ct));
    }

    // ---- project groups -------------------------------------------------------------------------

    [Fact]
    public async Task Edit_adds_and_removes_a_group_s_sets_and_connections()
    {
        var group = Group();
        var removed = TestData.EnvironmentSet("removed");
        var added = TestData.EnvironmentSet("added");
        await Seed(
            group, removed, added,
            new ProjectGroupEnvironmentSet { Id = Guid.NewGuid(), ProjectGroupId = group.Id, EnvironmentSetId = removed.Id },
            GroupConnection(group, TestConfiguration.VsphereConnection));

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[]
            {
                new { operation = "Add", projectGroupId = group.Id, environmentSetId = added.Id },
                new { operation = "Remove", projectGroupId = group.Id, environmentSetId = removed.Id },
                new { operation = "Add", projectGroupId = group.Id, connectionName = TestConfiguration.ProxmoxConnection },
                new { operation = "Remove", projectGroupId = group.Id, connectionName = TestConfiguration.VsphereConnection }
            }
        }, Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Equal(added.Id, Assert.Single(await ReadBack(db => db.ProjectGroupEnvironmentSets.Where(x => x.ProjectGroupId == group.Id).ToListAsync(Ct))).EnvironmentSetId);
        Assert.Equal(TestConfiguration.ProxmoxConnection,
            Assert.Single(await ReadBack(db => db.ProjectGroupInventoryConnections.Where(x => x.ProjectGroupId == group.Id).ToListAsync(Ct))).ConnectionName);
    }

    /// <summary>A group's set reaches its projects, so it is checked against each project's own sets.</summary>
    [Fact]
    public async Task Edit_refuses_a_group_set_giving_a_name_a_project_in_the_group_gets_directly()
    {
        var group = Group();
        var project = TestData.Project();
        project.ProjectGroupId = group.Id;
        var direct = TestData.EnvironmentSet("direct");
        var shared = TestData.EnvironmentSet("shared");
        await Seed(
            group, project, direct, shared,
            TestData.PlainEntry(direct, "REGION", "east"),
            TestData.PlainEntry(shared, "REGION", "west"),
            TestData.ProjectEnvironmentSet(project, direct));

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[] { new { operation = "Add", projectGroupId = group.Id, environmentSetId = shared.Id } }
        }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.False(await ReadBack(db => db.ProjectGroupEnvironmentSets.AnyAsync(x => x.ProjectGroupId == group.Id, Ct)));
    }

    /// <summary>Taking the project's own set off in the same request makes room for the group's.</summary>
    [Fact]
    public async Task Edit_moves_a_set_from_a_project_to_its_group_in_one_request()
    {
        var group = Group();
        var project = TestData.Project();
        project.ProjectGroupId = group.Id;
        var direct = TestData.EnvironmentSet("direct");
        var shared = TestData.EnvironmentSet("shared");
        await Seed(
            group, project, direct, shared,
            TestData.PlainEntry(direct, "REGION", "east"),
            TestData.PlainEntry(shared, "REGION", "west"),
            TestData.ProjectEnvironmentSet(project, direct));

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[]
            {
                new { operation = "Remove", projectId = (Guid?)project.Id, projectGroupId = (Guid?)null, environmentSetId = direct.Id },
                new { operation = "Add", projectId = (Guid?)null, projectGroupId = (Guid?)group.Id, environmentSetId = shared.Id }
            }
        }, Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.False(await ReadBack(db => db.ProjectEnvironmentSets.AnyAsync(x => x.ProjectId == project.Id, Ct)));
        Assert.True(await ReadBack(db => db.ProjectGroupEnvironmentSets.AnyAsync(x => x.ProjectGroupId == group.Id && x.EnvironmentSetId == shared.Id, Ct)));
    }

    [Fact]
    public async Task Edit_refuses_a_change_naming_both_a_project_and_a_group()
    {
        var group = Group();
        var project = TestData.Project();
        var set = TestData.EnvironmentSet();
        await Seed(group, project, set);

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[] { new { operation = "Add", projectId = project.Id, projectGroupId = group.Id, environmentSetId = set.Id } }
        }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task Edit_answers_404_for_a_group_that_does_not_exist()
    {
        var set = TestData.EnvironmentSet();
        await Seed(set);

        var response = await RootClient.PostAsJsonAsync(Assignments, new
        {
            changes = new object[] { new { operation = "Add", projectGroupId = Guid.NewGuid(), environmentSetId = set.Id } }
        }, Ct);

        await AssertProblem(HttpStatusCode.NotFound, response);
    }

    /// <summary>Group assignments are admin-only too: managing the group gives neither.</summary>
    [Fact]
    public async Task Edit_on_a_group_needs_the_system_permissions_not_a_group_role()
    {
        var group = Group();
        var set = TestData.EnvironmentSet();
        await Seed(group, set);
        var sets = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();
        var groupManager = await Actor().WithSystemPermissions(SystemPermission.ManageProjectGroups).SeedAsync();
        var change = new { operation = "Add", projectGroupId = group.Id, environmentSetId = set.Id };

        await AssertProblem(HttpStatusCode.Forbidden, await Client(groupManager).PostAsJsonAsync(Assignments, new { changes = new[] { change } }, Ct));
        await AssertStatus(HttpStatusCode.NoContent, await Client(sets).PostAsJsonAsync(Assignments, new { changes = new[] { change } }, Ct));
    }

    private static ProjectGroup Group() => new() { Id = Guid.NewGuid(), Name = $"group-{Guid.NewGuid():N}" };

    private static ProjectGroupInventoryConnection GroupConnection(ProjectGroup group, string name) => new()
    {
        Id = Guid.NewGuid(),
        ProjectGroupId = group.Id,
        ConnectionName = name,
        NormalizedConnectionName = ProjectInventoryConnection.Normalize(name)
    };

    /// <summary>A blank connection name on a set change is no connection: it needs no ManageInfrastructure and adds no connection.</summary>
    [Fact]
    public async Task Edit_treats_a_blank_connection_name_on_a_set_change_as_none()
    {
        var project = TestData.Project();
        var set = TestData.EnvironmentSet();
        await Seed(project, set);
        var sets = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(sets).PostAsJsonAsync(Assignments, new
        {
            changes = new[] { new { operation = "Add", projectId = project.Id, environmentSetId = set.Id, connectionName = "" } }
        }, Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.True(await ReadBack(db => db.ProjectEnvironmentSets.AnyAsync(x => x.ProjectId == project.Id && x.EnvironmentSetId == set.Id, Ct)));
        Assert.False(await ReadBack(db => db.ProjectInventoryConnections.AnyAsync(x => x.ProjectId == project.Id, Ct)));
    }

    private static ProjectInventoryConnection Connection(Project project, string name) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = project.Id,
        ConnectionName = name,
        NormalizedConnectionName = ProjectInventoryConnection.Normalize(name)
    };
}
