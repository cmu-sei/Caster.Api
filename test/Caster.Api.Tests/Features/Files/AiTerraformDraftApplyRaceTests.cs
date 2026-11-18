// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Files;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;

namespace Caster.Api.Tests.Features.Files;

/// <summary>
/// Applying a reviewed Terraform draft loads the Directory's files and then takes each replaced file's lock. A file changed
/// or deleted between the two must stop the whole apply, with nothing saved. The lock service's hook (see
/// <see cref="DeletingLockAppFactory"/>) is what reaches that window: it is asked for the lock after the files are loaded.
/// </summary>
public class AiTerraformDraftApplyRaceTests(DatabaseFixture fixture, DeletingLockAppFactory factory)
    : ApiTestBase(fixture, factory), IClassFixture<DeletingLockAppFactory>
{
    private DeletingLockAppFactory Hooked => (DeletingLockAppFactory)Factory;

    private async Task<(Project Project, Directory Directory, File Main)> Seed()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var main = TestData.File(directory, "main.tf", "# what the draft saw\n");
        await Seed(project, directory, main);
        return (project, directory, main);
    }

    private Task<System.Net.Http.HttpResponseMessage> Apply(TestActor actor, Directory directory, File main) =>
        Client(actor).PostAsJsonAsync($"api/directories/{directory.Id}/files/ai-draft/apply", new
        {
            files = new object[]
            {
                new { name = "new.tf", content = "# new\n" },
                new { name = "main.tf", content = "# drafted\n", existingFileId = main.Id, existingContentHash = AiTerraformDraftBuilder.ContentHash(main.Content) }
            }
        }, Ct);

    /// <summary>Caster's own delete only sets IsDeleted, which a reload does not detach; the apply must still stop.</summary>
    [Fact]
    public async Task Apply_saves_nothing_when_a_replaced_file_is_soft_deleted_after_the_files_were_loaded()
    {
        var (project, directory, main) = await Seed();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        Hooked.Locks.BeforeFileLock = id =>
        {
            using var other = NewContext();
            var file = other.Files.Single(f => f.Id == id);
            file.IsDeleted = true;
            other.SaveChanges();
        };

        try
        {
            var response = await Apply(actor, directory, main);

            await AssertProblem(HttpStatusCode.Conflict, response);
        }
        finally
        {
            Hooked.Locks.BeforeFileLock = null;
        }

        await using var db = NewContext();
        var stored = await db.Files.IgnoreQueryFilters().SingleAsync(f => f.Id == main.Id, Ct);
        Assert.Equal(("# what the draft saw\n", true), (stored.Content, stored.IsDeleted));
        Assert.False(await db.Files.AnyAsync(f => f.DirectoryId == directory.Id && f.Name == "new.tf", Ct));
        Assert.Equal(0, await db.FileVersions.CountAsync(v => v.FileId == main.Id, Ct));
    }

    /// <summary>A save that lands between the load and the lock is seen, so the stale copy is not written over it.</summary>
    [Fact]
    public async Task Apply_saves_nothing_when_a_replaced_file_is_saved_after_the_files_were_loaded()
    {
        var (project, directory, main) = await Seed();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        Hooked.Locks.BeforeFileLock = id =>
        {
            using var other = NewContext();
            other.Files.Single(f => f.Id == id).Content = "# saved by someone else\n";
            other.SaveChanges();
        };

        try
        {
            var response = await Apply(actor, directory, main);

            await AssertProblem(HttpStatusCode.Conflict, response);
        }
        finally
        {
            Hooked.Locks.BeforeFileLock = null;
        }

        await using var db = NewContext();
        Assert.Equal("# saved by someone else\n", (await db.Files.SingleAsync(f => f.Id == main.Id, Ct)).Content);
        Assert.False(await db.Files.AnyAsync(f => f.DirectoryId == directory.Id && f.Name == "new.tf", Ct));
    }
}
