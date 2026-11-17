// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.ProjectGroups;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Domain.Services.ProjectGroups;

/// <summary>What a project gets from its group: a partition when it has none, and the group's hosts.</summary>
public class ProjectGroupResolutionTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task A_project_uses_its_own_partition_then_its_groups()
    {
        var pool = TestData.Pool();
        var own = TestData.Partition(pool, "own");
        var shared = TestData.Partition(pool, "shared");
        var group = new ProjectGroup { Id = Guid.NewGuid(), Name = $"group-{Guid.NewGuid():N}", PartitionId = shared.Id };
        var withOwn = TestData.Project("With own");
        withOwn.PartitionId = own.Id;
        withOwn.ProjectGroupId = group.Id;
        var withoutOwn = TestData.Project("Without own");
        withoutOwn.ProjectGroupId = group.Id;
        var ungrouped = TestData.Project("Ungrouped");
        await Seed(pool, own, shared, group, withOwn, withoutOwn, ungrouped);

        using var db = NewContext();

        Assert.Equal(own.Id, await db.ProjectPartitionId(withOwn.Id, Ct));
        Assert.Equal(shared.Id, await db.ProjectPartitionId(withoutOwn.Id, Ct));
        Assert.Null(await db.ProjectPartitionId(ungrouped.Id, Ct));
    }

    [Fact]
    public async Task A_projects_hosts_are_its_own_and_its_groups()
    {
        var group = new ProjectGroup { Id = Guid.NewGuid(), Name = $"group-{Guid.NewGuid():N}" };
        var project = TestData.Project();
        project.ProjectGroupId = group.Id;
        var other = TestData.Project("Other");
        await Seed(group, project, other,
            new Host { Id = Guid.NewGuid(), Name = "own", MaximumMachines = 1, ProjectId = project.Id },
            new Host { Id = Guid.NewGuid(), Name = "shared", MaximumMachines = 1, ProjectGroupId = group.Id },
            new Host { Id = Guid.NewGuid(), Name = "other", MaximumMachines = 1, ProjectId = other.Id },
            new Host { Id = Guid.NewGuid(), Name = "unowned", MaximumMachines = 1 });

        using var db = NewContext();

        Assert.Equal(["own", "shared"], await db.ProjectHosts(project.Id).Select(x => x.Name).OrderBy(x => x).ToListAsync(Ct));
        Assert.Equal(["other"], await db.ProjectHosts(other.Id).Select(x => x.Name).ToListAsync(Ct));
    }
}
