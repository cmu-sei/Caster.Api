// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Features.Projects;

/// <summary>What projects, their memberships, groups and group memberships are answered with, and what their writes store.</summary>
public class ProjectMappingTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task A_project_is_answered_with_every_member()
    {
        var pool = TestData.Pool();
        var partition = TestData.Partition(pool);
        var project = TestData.Project();
        project.Description = "described";
        project.PartitionId = partition.Id;
        await Seed(pool, partition, project);

        var one = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/projects/{project.Id}", Ct));
        var all = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/projects", Ct));

        foreach (var json in new[] { one, Assert.Single(all, x => x.GetProperty("id").GetGuid() == project.Id) })
        {
            Assert.Equal((project.Name, "described", partition.Id), (json.GetProperty("name").GetString(), json.GetProperty("description").GetString(),
                json.GetProperty("partitionId").GetGuid()));
            Assert.Equal(TestData.DefaultDateCreated, json.GetProperty("dateCreated").GetDateTime().ToUniversalTime());
        }
    }

    /// <summary>A create keeps the id the body sends, or is given one.</summary>
    [Fact]
    public async Task A_create_keeps_the_body_id_or_is_given_one()
    {
        var id = Guid.NewGuid();

        var withId = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/projects", new { id, name = "with id", description = "d" }, Ct));
        var withoutId = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/projects", new { name = "without id" }, Ct));

        Assert.Equal((id, "with id", "d"), (withId.GetProperty("id").GetGuid(), withId.GetProperty("name").GetString(), withId.GetProperty("description").GetString()));
        Assert.NotEqual(Guid.Empty, withoutId.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, withoutId.GetProperty("description").ValueKind);
        Assert.Equal("d", (await ReadBack(db => db.Projects.SingleAsync(x => x.Id == id, Ct))).Description);
    }

    /// <summary>A PUT writes every member: one without a description clears it.</summary>
    [Fact]
    public async Task Put_without_a_description_clears_it()
    {
        var project = TestData.Project();
        project.Description = "before";
        await Seed(project);

        var json = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync($"api/projects/{project.Id}", new { name = "after" }, Ct));

        Assert.Equal(JsonValueKind.Null, json.GetProperty("description").ValueKind);
        var stored = await ReadBack(db => db.Projects.SingleAsync(x => x.Id == project.Id, Ct));
        Assert.Equal(("after", (string)null), (stored.Name, stored.Description));
    }

    [Fact]
    public async Task Memberships_are_answered_with_every_member()
    {
        var project = TestData.Project();
        var user = TestData.User();
        var group = TestData.Group();
        await Seed(project, user, group);

        var created = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync($"api/projects/{project.Id}/memberships", new { userId = user.Id }, Ct));
        var id = created.GetProperty("id").GetGuid();
        var edited = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync("api/projects/memberships", new { id, roleId = TestData.ProjectRoles.Observer }, Ct));
        var one = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/projects/memberships/{id}", Ct));
        var list = await ReadAsync<JsonElement[]>(await RootClient.GetAsync($"api/projects/{project.Id}/memberships", Ct));
        var groupMembership = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync($"api/projects/{project.Id}/memberships", new { groupId = group.Id }, Ct));

        Assert.Equal((project.Id, user.Id, JsonValueKind.Null, TestData.ProjectRoles.Member), (created.GetProperty("projectId").GetGuid(), created.GetProperty("userId").GetGuid(),
            created.GetProperty("groupId").ValueKind, created.GetProperty("roleId").GetGuid()));
        foreach (var json in new[] { edited, one, Assert.Single(list, x => x.GetProperty("id").GetGuid() == id) })
            Assert.Equal((id, user.Id, TestData.ProjectRoles.Observer), (json.GetProperty("id").GetGuid(), json.GetProperty("userId").GetGuid(), json.GetProperty("roleId").GetGuid()));
        Assert.Equal((JsonValueKind.Null, group.Id), (groupMembership.GetProperty("userId").ValueKind, groupMembership.GetProperty("groupId").GetGuid()));
    }

    [Fact]
    public async Task Groups_and_their_memberships_are_answered_with_every_member()
    {
        var user = TestData.User();
        await Seed(user);

        var created = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/groups", new { name = $"group-{Guid.NewGuid():N}" }, Ct));
        var groupId = created.GetProperty("id").GetGuid();
        var put = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync("api/groups", new { id = groupId, name = $"renamed-{groupId:N}" }, Ct));
        var membership = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync($"api/groups/{groupId}/memberships", new { userId = user.Id }, Ct));
        var membershipId = membership.GetProperty("id").GetGuid();
        var editedMembership = await ReadAsync<JsonElement>(await RootClient.PutAsJsonAsync($"api/groups/memberships/{membershipId}", new { role = "Manager" }, Ct));
        var memberships = await ReadAsync<JsonElement[]>(await RootClient.GetAsync($"api/groups/{groupId}/memberships", Ct));
        var one = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/groups/{groupId}", Ct));

        Assert.Equal($"renamed-{groupId:N}", put.GetProperty("name").GetString());
        Assert.Equal($"renamed-{groupId:N}", one.GetProperty("name").GetString());
        Assert.Equal((groupId, user.Id, "Member"), (membership.GetProperty("groupId").GetGuid(), membership.GetProperty("userId").GetGuid(), membership.GetProperty("role").GetString()));
        Assert.Equal("Manager", editedMembership.GetProperty("role").GetString());
        Assert.Equal("Manager", Assert.Single(memberships).GetProperty("role").GetString());
        Assert.Equal(GroupMembershipRole.Manager, (await ReadBack(db => db.GroupMemberships.SingleAsync(x => x.Id == membershipId, Ct))).Role);
    }
}
