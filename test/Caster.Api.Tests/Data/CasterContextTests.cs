// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Data;

/// <summary>
/// The relationships of <c>CasterContext</c>'s project tree (projects, directories, files, workspaces) as the
/// real migrations create them: what is stored, what is read back, and what a delete takes with it.
/// </summary>
public class CasterContextTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task A_project_reads_back_with_its_directories()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        await Seed(project, directory);

        await using var context = NewContext();
        var stored = await context.Projects.Include(x => x.Directories).SingleAsync(x => x.Id == project.Id, Ct);

        Assert.Collection(stored.Directories, x => { Assert.Equal(directory.Id, x.Id); Assert.Same(stored, x.Project); });
    }

    [Fact]
    public async Task A_directory_reads_back_with_its_files_and_parent()
    {
        var project = TestData.Project();
        var parent = TestData.Directory(project, "parent");
        var child = TestData.Directory(project, "child", parent);
        var file = TestData.File(child);
        await Seed(project, parent, child, file);

        await using var context = NewContext();
        var stored = await context.Directories.Include(x => x.Files).SingleAsync(x => x.Id == child.Id, Ct);

        Assert.Equal(parent.Id, stored.ParentId);
        Assert.Collection(stored.Files, x => { Assert.Equal(file.Id, x.Id); Assert.Same(stored, x.Directory); });
        Assert.Equal($"{parent.Id}/{child.Id}/", stored.Path);
    }

    /// <summary>A deleted file stays in the table, flagged, and the query filter hides it.</summary>
    [Fact]
    public async Task A_file_marked_deleted_is_hidden_by_the_query_filter()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var file = TestData.File(directory);
        file.IsDeleted = true;
        await Seed(project, directory, file);

        await using var context = NewContext();

        Assert.False(await context.Files.AnyAsync(x => x.Id == file.Id, Ct));
        Assert.True(await context.Files.IgnoreQueryFilters().AnyAsync(x => x.Id == file.Id, Ct));
    }

    [Fact]
    public async Task Deleting_a_project_deletes_its_directories_files_and_workspaces()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var file = TestData.File(directory);
        var workspace = TestData.Workspace(directory);
        await Seed(project, directory, file, workspace);

        Db.Projects.Remove(project);
        await Db.SaveChangesAsync(Ct);

        await using var context = NewContext();
        Assert.False(await context.Directories.AnyAsync(x => x.Id == directory.Id, Ct));
        Assert.False(await context.Files.IgnoreQueryFilters().AnyAsync(x => x.Id == file.Id, Ct));
        Assert.False(await context.Workspaces.AnyAsync(x => x.Id == workspace.Id, Ct));
    }

    [Fact]
    public async Task Deleting_a_directory_deletes_its_child_directories()
    {
        var project = TestData.Project();
        var parent = TestData.Directory(project, "parent");
        var child = TestData.Directory(project, "child", parent);
        await Seed(project, parent, child);

        Db.Directories.Remove(parent);
        await Db.SaveChangesAsync(Ct);

        await using var context = NewContext();
        Assert.False(await context.Directories.AnyAsync(x => x.Id == child.Id, Ct));
    }

    /// <summary>The unique index on project memberships admits a second row for one user on one project.</summary>
    [Fact]
    public async Task A_second_membership_of_one_user_on_one_project_is_stored()
    {
        var project = TestData.Project();
        var user = TestData.User();
        await Seed(project, user, TestData.ProjectMembership(project.Id, TestData.ProjectRoles.Observer, userId: user.Id));
        Db.ProjectMemberships.Add(TestData.ProjectMembership(project.Id, TestData.ProjectRoles.Member, userId: user.Id));

        await Db.SaveChangesAsync(Ct);

        await using var context = NewContext();
        Assert.Equal(2, await context.ProjectMemberships.CountAsync(x => x.ProjectId == project.Id && x.UserId == user.Id, Ct));
    }

    [Fact]
    public async Task A_second_membership_of_one_user_in_one_group_is_refused()
    {
        var group = TestData.Group();
        var user = TestData.User();
        await Seed(group, user, TestData.GroupMembership(group.Id, user.Id));
        await using var context = NewContext();
        context.GroupMemberships.Add(TestData.GroupMembership(group.Id, user.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }
}
