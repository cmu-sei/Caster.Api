// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FileResponse = Caster.Api.Features.Files.File;
using FileVersionResponse = Caster.Api.Features.Files.FileVersion;
using DesignModuleResponse = Caster.Api.Features.DesignModules.DesignModule;

namespace Caster.Api.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Authorization")]
public class BodyAuthorizationTests(AuthorizationDatabase database) : IClassFixture<AuthorizationDatabase>
{
    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task Workspace_edit_authorizes_the_route_even_when_the_body_names_an_authorized_directory(string method)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        var response = await Send(app, method, $"workspaces/{app.WorkspaceB.Id}",
            new { id = app.WorkspaceA.Id, name = "stolen", directoryId = app.DirectoryA.Id });

        await AssertStatus(HttpStatusCode.Forbidden, response);
        await using var db = app.NewContext();
        var stored = await db.Workspaces.SingleAsync(w => w.Id == app.WorkspaceB.Id);
        Assert.Equal(app.DirectoryB.Id, stored.DirectoryId);
        Assert.Equal("workspace-b", stored.Name);
        Assert.Equal("sensitive-state", stored.State);
        Assert.Equal("sensitive-backup", stored.StateBackup);
        Assert.Equal(app.DirectoryB.Id, (await db.Files.SingleAsync(f => f.Id == app.FileB.Id)).DirectoryId);
    }

    [Theory]
    [InlineData("PUT", "foreign")]
    [InlineData("PATCH", "foreign")]
    [InlineData("PUT", "missing")]
    [InlineData("PATCH", "missing")]
    [InlineData("PUT", "null")]
    [InlineData("PATCH", "null")]
    [InlineData("PUT", "omitted")]
    [InlineData("PATCH", "omitted")]
    public async Task Workspace_edit_ignores_legacy_directory_fields(string method, string parent)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        var body = new Dictionary<string, object> { ["name"] = "renamed" };
        AddLegacyParent(body, "directoryId", parent, app.DirectoryB.Id);
        await AssertStatus(HttpStatusCode.OK, await Send(app, method, $"workspaces/{app.WorkspaceA.Id}", body));

        await using var db = app.NewContext();
        var stored = await db.Workspaces.SingleAsync(w => w.Id == app.WorkspaceA.Id);
        Assert.Equal("renamed", stored.Name);
        Assert.Equal(app.DirectoryA.Id, stored.DirectoryId);
        Assert.Equal("sensitive-state", stored.State);
        Assert.Equal("sensitive-backup", stored.StateBackup);
        Assert.Equal(app.DirectoryA.Id, (await db.Files.SingleAsync(f => f.Id == app.FileA.Id)).DirectoryId);
    }

    [Theory]
    [InlineData("PUT", "foreign")]
    [InlineData("PATCH", "foreign")]
    [InlineData("PUT", "missing")]
    [InlineData("PATCH", "missing")]
    [InlineData("PUT", "null")]
    [InlineData("PATCH", "null")]
    [InlineData("PUT", "omitted")]
    [InlineData("PATCH", "omitted")]
    public async Task File_edit_changes_content_without_moving_ownership_or_injecting_into_a_foreign_workspace(string method, string parent)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        var body = new Dictionary<string, object> { ["name"] = "renamed.tf", ["content"] = "changed" };
        AddLegacyParent(body, "directoryId", parent, app.DirectoryB.Id);
        AddLegacyParent(body, "workspaceId", parent, app.WorkspaceB.Id);
        int versionCount;
        await using (var before = app.NewContext())
            versionCount = await before.FileVersions.CountAsync();

        await AssertStatus(HttpStatusCode.OK, await Send(app, method, $"files/{app.FileA.Id}", body));

        await using var db = app.NewContext();
        var stored = await db.Files.SingleAsync(f => f.Id == app.FileA.Id);
        Assert.Equal("renamed.tf", stored.Name);
        Assert.Equal("changed", stored.Content);
        Assert.Equal(app.DirectoryA.Id, stored.DirectoryId);
        Assert.Equal(app.WorkspaceA.Id, stored.WorkspaceId);
        Assert.Equal(versionCount + 1, await db.FileVersions.CountAsync());
        var victimWorkspace = await db.Workspaces.SingleAsync(w => w.Id == app.WorkspaceB.Id);
        var victimDirectory = await db.Directories.SingleAsync(d => d.Id == app.DirectoryB.Id);
        var victimFiles = await db.GetWorkspaceFiles(victimWorkspace, victimDirectory);
        Assert.Equal(app.FileB.Id, Assert.Single(victimFiles).Id);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task File_edit_cannot_use_body_ownership_to_authorize_a_foreign_file(string method)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        int versionCount;
        await using (var before = app.NewContext())
            versionCount = await before.FileVersions.CountAsync();
        await AssertStatus(HttpStatusCode.Forbidden, await Send(app, method, $"files/{app.FileB.Id}",
            new { id = app.FileA.Id, name = "injected.tf", content = "injected", directoryId = app.DirectoryA.Id, workspaceId = app.WorkspaceA.Id }));

        await using var db = app.NewContext();
        var stored = await db.Files.SingleAsync(f => f.Id == app.FileB.Id);
        Assert.Equal("b.tf", stored.Name);
        Assert.Equal("b.tf", stored.Content);
        Assert.Equal(app.DirectoryB.Id, stored.DirectoryId);
        Assert.Equal(app.WorkspaceB.Id, stored.WorkspaceId);
        Assert.Equal(versionCount, await db.FileVersions.CountAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task File_create_rejects_a_workspace_in_a_different_directory_even_for_an_admin(bool sameProject, bool admin)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        if (admin) app.AsAdmin();
        var workspaceId = sameProject ? app.AlternateWorkspaceA.Id : app.WorkspaceB.Id;
        await AssertStatus(HttpStatusCode.Conflict, await app.Client.PostAsJsonAsync("api/files",
            new { name = "injected.tf", content = "injected", directoryId = app.DirectoryA.Id, workspaceId }));

        await using var db = app.NewContext();
        Assert.Equal(5, await db.Files.IgnoreQueryFilters().CountAsync());
        Assert.Equal(7, await db.FileVersions.CountAsync());
        Assert.False(await db.Files.AnyAsync(f => f.Name == "injected.tf"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task File_create_allows_matching_workspace_and_directory_or_a_directory_file(bool useWorkspace)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        var response = await app.Client.PostAsJsonAsync("api/files",
            new { name = "new.tf", content = "new", directoryId = app.DirectoryA.Id, workspaceId = useWorkspace ? (Guid?)app.WorkspaceA.Id : null });
        await AssertStatus(HttpStatusCode.Created, response);
        var created = await response.Content.ReadFromJsonAsync<FileResponse>();
        await using var db = app.NewContext();
        var stored = await db.Files.SingleAsync(f => f.Id == created.Id);
        Assert.Equal(app.DirectoryA.Id, stored.DirectoryId);
        Assert.Equal(useWorkspace ? (Guid?)app.WorkspaceA.Id : null, stored.WorkspaceId);
        Assert.Equal("new", stored.Content);
        Assert.Single(await db.FileVersions.Where(v => v.FileId == created.Id).ToArrayAsync());
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Forbidden)]
    [InlineData(true, HttpStatusCode.NotFound)]
    public async Task File_create_authorizes_the_directory_before_resolving_a_missing_workspace(bool authorizedDirectory, HttpStatusCode expected)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        await AssertStatus(expected, await app.Client.PostAsJsonAsync("api/files",
            new { name = "new.tf", directoryId = authorizedDirectory ? app.DirectoryA.Id : app.DirectoryB.Id, workspaceId = Guid.NewGuid() }));
        await using var db = app.NewContext();
        Assert.Equal(5, await db.Files.IgnoreQueryFilters().CountAsync());
    }

    [Theory]
    [InlineData("PUT", false)]
    [InlineData("PATCH", false)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    public async Task Directory_edit_rejects_foreign_parents_before_mutating_any_paths(string method, bool admin)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        if (admin) app.AsAdmin();
        await AssertStatus(HttpStatusCode.Conflict, await Send(app, method, $"directories/{app.ChildA.Id}",
            new { name = "changed", parentId = app.DirectoryB.Id }));
        await AssertDirectoryUnchanged(app);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task Directory_edit_cannot_authorize_a_foreign_target_with_a_body_parent(string method)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        await AssertStatus(HttpStatusCode.Forbidden, await Send(app, method, $"directories/{app.DirectoryB.Id}",
            new { id = app.ChildA.Id, name = "changed", parentId = app.DirectoryA.Id }));
        await using var db = app.NewContext();
        var stored = await db.Directories.SingleAsync(d => d.Id == app.DirectoryB.Id);
        Assert.Null(stored.ParentId);
        Assert.Equal(app.ProjectB.Id, stored.ProjectId);
        Assert.Equal(app.DirectoryB.Path, stored.Path);
        Assert.Equal("B", stored.Name);
        await AssertDirectoryUnchanged(app);
    }

    [Theory]
    [InlineData("PUT", false)]
    [InlineData("PATCH", false)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    public async Task Directory_edit_rejects_self_and_descendant_parents(string method, bool descendant)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        await AssertStatus(HttpStatusCode.Conflict, await Send(app, method, $"directories/{app.ChildA.Id}",
            new { name = "changed", parentId = descendant ? app.GrandchildA.Id : app.ChildA.Id }));
        await AssertDirectoryUnchanged(app);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task Directory_edit_allows_same_project_moves_and_updates_descendant_paths(string method)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        await AssertStatus(HttpStatusCode.OK, await Send(app, method, $"directories/{app.ChildA.Id}",
            new { name = "changed", parentId = app.AlternateA.Id }));
        await using var db = app.NewContext();
        var stored = await db.Directories.SingleAsync(d => d.Id == app.ChildA.Id);
        Assert.Equal(app.AlternateA.Id, stored.ParentId);
        Assert.Equal(app.ProjectA.Id, stored.ProjectId);
        Assert.Equal("changed", stored.Name);
        Assert.Equal($"{app.AlternateA.Path}{app.ChildA.Id}/", stored.Path);
        Assert.Equal($"{stored.Path}{app.GrandchildA.Id}/",
            (await db.Directories.SingleAsync(d => d.Id == app.GrandchildA.Id)).Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Directory_patch_distinguishes_omitted_parent_from_explicit_null(bool clearParent)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        var body = new Dictionary<string, object> { ["name"] = "changed" };
        if (clearParent) body["parentId"] = null;
        await AssertStatus(HttpStatusCode.OK, await Send(app, "PATCH", $"directories/{app.ChildA.Id}", body));
        await using var db = app.NewContext();
        var stored = await db.Directories.SingleAsync(d => d.Id == app.ChildA.Id);
        Assert.Equal(clearParent ? null : (Guid?)app.DirectoryA.Id, stored.ParentId);
        Assert.Equal(clearParent ? $"{app.ChildA.Id}/" : app.ChildA.Path, stored.Path);
        Assert.Equal($"{stored.Path}{app.GrandchildA.Id}/",
            (await db.Directories.SingleAsync(d => d.Id == app.GrandchildA.Id)).Path);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("omitted")]
    public async Task Design_module_edit_ignores_legacy_design_fields_and_still_updates_values(string parent)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        var body = new Dictionary<string, object>
        {
            ["name"] = "changed", ["moduleId"] = app.Module.Id, ["moduleVersion"] = "v2",
            ["values"] = new[] { new { name = "input", value = "changed" } }
        };
        AddLegacyParent(body, "designId", parent, app.DesignB.Id);
        await AssertStatus(HttpStatusCode.OK, await Send(app, "PUT", $"designModules/{app.DesignModuleA.Id}", body));
        await using var db = app.NewContext();
        var stored = await db.DesignModules.SingleAsync(m => m.Id == app.DesignModuleA.Id);
        Assert.Equal(app.DesignA.Id, stored.DesignId);
        Assert.Equal("changed", stored.Name);
        Assert.Equal("v2", stored.ModuleVersion);
        Assert.Equal("changed", Assert.Single(stored.Values).Value);
    }

    [Fact]
    public async Task Design_module_create_still_accepts_its_own_parent()
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        var response = await app.Client.PostAsJsonAsync("api/designModules",
            new { designId = app.DesignA.Id, moduleId = app.Module.Id, name = "created", moduleVersion = "v1", values = Array.Empty<object>() });
        await AssertStatus(HttpStatusCode.Created, response);
        var created = await response.Content.ReadFromJsonAsync<DesignModuleResponse>();
        await using var db = app.NewContext();
        Assert.Equal(app.DesignA.Id, (await db.DesignModules.SingleAsync(m => m.Id == created.Id)).DesignId);
    }

    [Fact]
    public async Task Design_module_edit_authorizes_the_route_before_changing_values()
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        await AssertStatus(HttpStatusCode.Forbidden, await Send(app, "PUT", $"designModules/{app.DesignModuleB.Id}",
            new
            {
                designModuleId = app.DesignModuleA.Id, designId = app.DesignA.Id, moduleId = app.Module.Id,
                name = "changed", moduleVersion = "v2", values = new[] { new { name = "input", value = "changed" } }
            }));
        await using var db = app.NewContext();
        var stored = await db.DesignModules.SingleAsync(m => m.Id == app.DesignModuleB.Id);
        Assert.Equal(app.DesignB.Id, stored.DesignId);
        Assert.Equal("victim", stored.Name);
        Assert.Equal("v1", stored.ModuleVersion);
        Assert.Equal("victim", Assert.Single(stored.Values).Value);
    }

    [Theory]
    [InlineData("reader")]
    [InlineData("none")]
    [InlineData("mixed")]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("null")]
    public async Task Tag_rejects_unauthorized_or_empty_lists_without_creating_any_versions(string scenario)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        if (scenario == "reader") app.AsProjectPermission(ProjectPermission.ViewProject);
        if (scenario == "none") app.AsProjectPermission(null);
        Guid[] ids = scenario switch
        {
            "mixed" => [app.FileA.Id, app.FileB.Id],
            "missing" => [app.FileA.Id, Guid.NewGuid()],
            "empty" => [],
            "null" => null,
            _ => [app.FileA.Id]
        };
        await AssertStatus(HttpStatusCode.Forbidden, await app.Client.PostAsJsonAsync("api/files/actions/tag",
            new { tag = "shared", fileIds = ids }));
        await using var db = app.NewContext();
        Assert.Equal(7, await db.FileVersions.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tag_requires_all_files_and_scopes_the_response_to_requested_ids(bool admin)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        if (admin) app.AsAdmin();
        // An unrelated file in A already has this tag. The foreign file in B also has it.
        var ids = admin ? new[] { app.FileA.Id, app.FileB.Id } : new[] { app.FileA.Id, app.FileA.Id };
        var response = await app.Client.PostAsJsonAsync("api/files/actions/tag", new { tag = "shared", fileIds = ids });
        await AssertStatus(HttpStatusCode.OK, response);
        var versions = await response.Content.ReadFromJsonAsync<FileVersionResponse[]>();
        Assert.Equal(ids.Distinct().OrderBy(id => id), versions.Select(v => v.FileId.Value).Distinct().OrderBy(id => id));
        Assert.All(versions, version => Assert.Null(version.Content));
        await using var db = app.NewContext();
        Assert.Equal(7 + ids.Distinct().Count(), await db.FileVersions.CountAsync());
    }

    [Fact]
    public async Task Tag_handles_multiple_authorized_files_on_one_scoped_context()
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        var response = await app.Client.PostAsJsonAsync("api/files/actions/tag",
            new { tag = "batch", fileIds = new[] { app.FileA.Id, app.AnotherFileA.Id } });
        await AssertStatus(HttpStatusCode.OK, response);
        var versions = await response.Content.ReadFromJsonAsync<FileVersionResponse[]>();
        Assert.Equal(2, versions.Length);
        await using var db = app.NewContext();
        Assert.Equal(9, await db.FileVersions.CountAsync());
    }

    [Fact]
    public async Task Tag_checks_that_every_file_exists_before_mutating_admin_requested_files()
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        app.AsAdmin();
        await AssertStatus(HttpStatusCode.NotFound, await app.Client.PostAsJsonAsync("api/files/actions/tag",
            new { tag = "shared", fileIds = new[] { app.FileA.Id, Guid.NewGuid() } }));
        await using var db = app.NewContext();
        Assert.Equal(7, await db.FileVersions.CountAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Directory_file_list_preserves_scope_when_deleted_files_and_content_are_requested(bool includeDeleted, bool includeContent)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        app.AsProjectPermission(ProjectPermission.ViewProject);
        var response = await app.Client.GetAsync(
            $"api/directories/{app.DirectoryA.Id}/files?includeDeleted={includeDeleted}&includeContent={includeContent}");
        await AssertStatus(HttpStatusCode.OK, response);
        var files = await response.Content.ReadFromJsonAsync<FileResponse[]>();
        var expected = new List<Guid> { app.FileA.Id, app.AnotherFileA.Id };
        if (includeDeleted) expected.Add(app.DeletedFileA.Id);
        Assert.Equal(expected.OrderBy(id => id), files.Select(f => f.Id).OrderBy(id => id));
        Assert.All(files, file =>
        {
            Assert.Equal(app.DirectoryA.Id, file.DirectoryId);
            Assert.Equal(includeContent ? file.Name : null, file.Content);
        });
        await using var db = app.NewContext();
        Assert.Equal(5, await db.Files.IgnoreQueryFilters().CountAsync());
        Assert.Equal(7, await db.FileVersions.CountAsync());
    }

    private static async Task AssertDirectoryUnchanged(AuthorizationTestApp app)
    {
        await using var db = app.NewContext();
        var stored = await db.Directories.SingleAsync(d => d.Id == app.ChildA.Id);
        Assert.Equal(app.DirectoryA.Id, stored.ParentId);
        Assert.Equal(app.ProjectA.Id, stored.ProjectId);
        Assert.Equal("child", stored.Name);
        Assert.Equal(app.ChildA.Path, stored.Path);
        Assert.Equal(app.GrandchildA.Path, (await db.Directories.SingleAsync(d => d.Id == app.GrandchildA.Id)).Path);
    }

    private static void AddLegacyParent(Dictionary<string, object> body, string field, string scenario, Guid foreignId)
    {
        if (scenario != "omitted")
            body[field] = scenario switch { "foreign" => foreignId, "missing" => Guid.NewGuid(), _ => null };
    }

    private static Task<HttpResponseMessage> Send(AuthorizationTestApp app, string method, string path, object body) =>
        app.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"api/{path}") { Content = JsonContent.Create(body) });

    private static async Task AssertStatus(HttpStatusCode expected, HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
