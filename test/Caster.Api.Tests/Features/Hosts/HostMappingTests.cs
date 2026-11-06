// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Features.Hosts;
using Caster.Api.Tests.Support;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using HostEntity = Caster.Api.Domain.Models.Host;
using PartialEditHost = Caster.Api.Features.Hosts.PartialEdit.Command;

namespace Caster.Api.Tests.Features.Hosts;

/// <summary>
/// What a host is answered with and what its writes store. The partial edit has no route, so its mapping is
/// driven directly.
/// </summary>
public class HostMappingTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task Reads_and_creates_answer_every_member()
    {
        var project = TestData.Project();
        await Seed(project);

        var created = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/hosts",
            new { name = "esx", datastore = "ds1", maximumMachines = 3, enabled = true, development = true, projectId = project.Id }, Ct));
        var id = created.GetProperty("id").GetGuid();
        var one = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/hosts/{id}", Ct));
        var all = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/hosts", Ct));

        foreach (var json in new[] { created, one, Assert.Single(all, x => x.GetProperty("id").GetGuid() == id) })
        {
            Assert.Equal(("esx", "ds1", 3, true, true, project.Id), (json.GetProperty("name").GetString(), json.GetProperty("datastore").GetString(),
                json.GetProperty("maximumMachines").GetInt32(), json.GetProperty("enabled").GetBoolean(), json.GetProperty("development").GetBoolean(),
                json.GetProperty("projectId").GetGuid()));
        }
    }

    /// <summary>A PUT writes every member: what its body leaves out is cleared or zeroed.</summary>
    [Fact]
    public async Task Put_clears_what_the_body_leaves_out()
    {
        var project = TestData.Project();
        var host = TestData.Host();
        host.ProjectId = project.Id;
        host.Development = true;
        await Seed(project, host);

        await AssertStatus(System.Net.HttpStatusCode.OK, await RootClient.PutAsJsonAsync($"api/hosts/{host.Id}", new { name = "put" }, Ct));

        var stored = await ReadBack(db => db.Hosts.SingleAsync(x => x.Id == host.Id, Ct));
        Assert.Equal(("put", (string)null, 0, false, false, (Guid?)null),
            (stored.Name, stored.Datastore, stored.MaximumMachines, stored.Enabled, stored.Development, stored.ProjectId));
    }

    /// <summary>The partial edit keeps every member its command leaves null or unset, an explicitly null project included.</summary>
    [Fact]
    public void The_partial_edit_keeps_what_the_command_leaves_out()
    {
        var projectId = Guid.NewGuid();
        var unset = Host(projectId);
        var explicitNull = Host(projectId);

        ApplyPatch(new PartialEditHost { Id = unset.Id }, unset);
        ApplyPatch(new PartialEditHost { Id = explicitNull.Id, ProjectId = new Optional<Guid?>(null) }, explicitNull);

        foreach (var host in new[] { unset, explicitNull })
            Assert.Equal(("esx", "ds1", 12, true, true, (Guid?)projectId),
                (host.Name, host.Datastore, host.MaximumMachines, host.Enabled, host.Development, host.ProjectId));
    }

    [Fact]
    public void The_partial_edit_writes_what_the_command_sets()
    {
        var host = Host(Guid.NewGuid());
        var otherProject = Guid.NewGuid();

        ApplyPatch(new PartialEditHost
        {
            Id = host.Id,
            Name = "renamed",
            Datastore = "ds2",
            MaximumMachines = 3,
            Enabled = false,
            Development = false,
            ProjectId = new Optional<Guid?>(otherProject)
        }, host);

        Assert.Equal(("renamed", "ds2", 3, false, false, (Guid?)otherProject),
            (host.Name, host.Datastore, host.MaximumMachines, host.Enabled, host.Development, host.ProjectId));
    }

    private static HostEntity Host(Guid projectId) => new()
    {
        Id = Guid.NewGuid(),
        Name = "esx",
        Datastore = "ds1",
        MaximumMachines = 12,
        Enabled = true,
        Development = true,
        ProjectId = projectId
    };

    private static void ApplyPatch(PartialEditHost command, HostEntity host) => command.ApplyPatchTo(host);
}
