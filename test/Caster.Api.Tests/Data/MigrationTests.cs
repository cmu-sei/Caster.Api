// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Data;

/// <summary>The migrations match the model, as <c>dotnet ef migrations has-pending-model-changes</c> checks.</summary>
public class MigrationTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public void The_model_has_no_changes_without_a_migration() =>
        Assert.False(Db.Database.HasPendingModelChanges());

    [Fact]
    public async Task The_capacity_columns_exist_and_hold_null()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        workspace.MaxDiskGiB = 500;
        await Seed(project, directory, workspace);

        await using var context = NewContext();
        var stored = await context.Workspaces.Include(x => x.Directory).ThenInclude(x => x.Project).SingleAsync(x => x.Id == workspace.Id, Ct);
        Assert.Equal(500, stored.MaxDiskGiB);
        Assert.Null(stored.MaxMachines);
        Assert.Null(stored.Directory.Project.MaxVcpu);
        Assert.Contains("20261111051320_Add_Deployment_Capacity", context.Database.GetAppliedMigrations().ToList());
    }
}
