// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using DirectoryResponse = Caster.Api.Features.Directories.Directory;
using FileVersionResponse = Caster.Api.Features.Files.FileVersion;
using ModuleResponse = Caster.Api.Features.Modules.Module;

namespace Caster.Api.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Authorization")]
public class ReadAuthorizationTests(AuthorizationDatabase database) : IClassFixture<AuthorizationDatabase>
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task File_history_is_available_in_the_authorized_project_including_deleted_files(bool deleted, bool systemAccess)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        if (systemAccess)
            app.AsSystemPermissions(SystemPermission.ViewProjects);
        else
            app.AsProjectPermission(ProjectPermission.ViewProject);
        var file = deleted ? app.DeletedFileA : app.FileA;
        await using var db = app.NewContext();
        var versionId = await db.FileVersions.Where(v => v.FileId == file.Id).Select(v => v.Id).SingleAsync();

        var listResponse = await app.Client.GetAsync($"api/files/{file.Id}/versions");
        await AssertStatus(HttpStatusCode.OK, listResponse);
        var version = Assert.Single(await listResponse.Content.ReadFromJsonAsync<FileVersionResponse[]>());
        Assert.Equal(versionId, version.Id);
        Assert.Equal(file.Id, version.FileId);
        Assert.Null(version.Content);

        var response = await app.Client.GetAsync($"api/files/versions/{versionId}");
        await AssertStatus(HttpStatusCode.OK, response);
        var history = await response.Content.ReadFromJsonAsync<FileVersionResponse>();
        Assert.Equal(file.Id, history.FileId);
        Assert.Equal(file.Content, history.Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task File_history_in_another_project_is_denied_including_deleted_files(bool deleted)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        app.AsProjectPermission(ProjectPermission.ViewProject);
        var file = deleted ? app.DeletedFileB : app.FileB;
        await using var db = app.NewContext();
        var versionIds = await db.FileVersions.Where(v => v.FileId == file.Id).Select(v => v.Id).ToArrayAsync();

        await AssertStatus(HttpStatusCode.Forbidden, await app.Client.GetAsync($"api/files/{file.Id}/versions"));
        foreach (var versionId in versionIds)
            await AssertStatus(HttpStatusCode.Forbidden, await app.Client.GetAsync($"api/files/versions/{versionId}"));
    }

    [Fact]
    public async Task History_access_does_not_authorize_editing_a_deleted_file()
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        app.AsProjectPermission(ProjectPermission.ViewProject);
        await AssertStatus(HttpStatusCode.OK, await app.Client.GetAsync($"api/files/{app.DeletedFileA.Id}/versions"));
        app.AsProjectEditor();

        await AssertStatus(HttpStatusCode.Forbidden, await app.Client.PutAsJsonAsync($"api/files/{app.DeletedFileA.Id}",
            new { name = "changed.tf", content = "changed" }));
        await using var db = app.NewContext();
        var file = await db.Files.IgnoreQueryFilters().SingleAsync(f => f.Id == app.DeletedFileA.Id);
        Assert.True(file.IsDeleted);
        Assert.Equal("deleted-a.tf", file.Content);
        Assert.Single(await db.FileVersions.Where(v => v.FileId == file.Id).ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_requires_view_permission_even_with_another_project_permission(bool noPermissions)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        app.AsProjectPermission(noPermissions ? null : ProjectPermission.EditProject);
        await using var db = app.NewContext();
        var versionId = await db.FileVersions.Where(v => v.FileId == app.FileA.Id).Select(v => v.Id).SingleAsync();

        await AssertStatus(HttpStatusCode.Forbidden, await app.Client.GetAsync($"api/files/{app.FileA.Id}/versions"));
        await AssertStatus(HttpStatusCode.Forbidden, await app.Client.GetAsync($"api/files/versions/{versionId}"));
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Forbidden)]
    [InlineData(true, HttpStatusCode.NotFound)]
    public async Task Missing_versions_preserve_existing_authorization_and_not_found_behavior(bool systemAccess, HttpStatusCode expected)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        if (systemAccess)
            app.AsSystemPermissions(SystemPermission.ViewProjects);
        else
            app.AsProjectPermission(ProjectPermission.ViewProject);

        await AssertStatus(expected, await app.Client.GetAsync($"api/files/versions/{Guid.NewGuid()}"));
    }

    [Fact]
    public async Task Version_ownership_cannot_be_taken_from_a_file_with_the_same_id()
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        app.AsProjectPermission(ProjectPermission.ViewProject);
        await using var db = app.NewContext();
        db.FileVersions.Add(new FileVersion
        {
            Id = app.FileA.Id, FileId = app.FileB.Id, Name = "foreign.tf", Content = "foreign"
        });
        await db.SaveChangesAsync();

        await AssertStatus(HttpStatusCode.Forbidden, await app.Client.GetAsync($"api/files/versions/{app.FileA.Id}"));
    }

    [Theory]
    [InlineData("project", false, HttpStatusCode.OK)]
    [InlineData("project", true, HttpStatusCode.Forbidden)]
    [InlineData("catalog", false, HttpStatusCode.Forbidden)]
    [InlineData("catalog", true, HttpStatusCode.Forbidden)]
    [InlineData("system", false, HttpStatusCode.OK)]
    [InlineData("system", true, HttpStatusCode.OK)]
    [InlineData("edit", false, HttpStatusCode.Forbidden)]
    [InlineData("none", false, HttpStatusCode.Forbidden)]
    public async Task Design_filtered_modules_require_both_catalog_and_design_access(string access, bool foreign, HttpStatusCode expected)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        switch (access)
        {
            case "project": app.AsProjectPermission(ProjectPermission.ViewProject); break;
            case "catalog": app.AsSystemPermissions(SystemPermission.ViewModules); break;
            case "system": app.AsSystemPermissions(SystemPermission.ViewModules, SystemPermission.ViewProjects); break;
            case "edit": app.AsProjectEditor(); break;
            default: app.AsProjectPermission(null); break;
        }
        var design = foreign ? app.DesignB : app.DesignA;
        var response = await app.Client.GetAsync($"api/modules?designId={design.Id}&forceUpdate=true");
        await AssertStatus(expected, response);

        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal(app.Module.Id, Assert.Single(await response.Content.ReadFromJsonAsync<ModuleResponse[]>()).Id);
            await app.GitlabRepository.Received(1).GetModulesAsync(true, Arg.Any<CancellationToken>());
        }
        else
        {
            await app.GitlabRepository.DidNotReceive().GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData("project")]
    [InlineData("catalog")]
    [InlineData("none")]
    public async Task Unfiltered_module_catalog_keeps_its_existing_access_policy(string access)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        if (access == "project")
            app.AsProjectEditor();
        else if (access == "catalog")
            app.AsSystemPermissions(SystemPermission.ViewModules);
        else
            app.AsProjectPermission(null);

        var response = await app.Client.GetAsync("api/modules");
        await AssertStatus(access == "none" ? HttpStatusCode.Forbidden : HttpStatusCode.OK, response);
        if (access != "none")
            Assert.Equal(app.Module.Id, Assert.Single(await response.Content.ReadFromJsonAsync<ModuleResponse[]>()).Id);
    }

    [Theory]
    [InlineData("direct", "view")]
    [InlineData("direct", "edit")]
    [InlineData("direct", "none")]
    [InlineData("direct", "all")]
    [InlineData("local-group", "view")]
    [InlineData("local-group", "edit")]
    [InlineData("local-group", "none")]
    [InlineData("local-group", "all")]
    [InlineData("idp-group", "view")]
    [InlineData("idp-group", "edit")]
    [InlineData("idp-group", "none")]
    [InlineData("idp-group", "all")]
    public async Task Directory_listing_uses_effective_view_permissions(string source, string permissions)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        await using var db = app.NewContext();
        var role = new ProjectRole
        {
            Id = Guid.NewGuid(), Name = "test-role", AllPermissions = permissions == "all",
            Permissions = permissions switch
            {
                "view" => [ProjectPermission.ViewProject],
                "edit" => [ProjectPermission.EditProject],
                _ => []
            }
        };
        var noViewRole = new ProjectRole { Id = Guid.NewGuid(), Name = "no-view", Permissions = [] };
        db.AddRange(role, noViewRole);
        db.ProjectMemberships.Add(new ProjectMembership(app.ProjectB.Id, app.UserId, null) { RoleId = noViewRole.Id });
        if (source == "direct")
        {
            db.ProjectMemberships.Add(new ProjectMembership(app.ProjectA.Id, app.UserId, null) { RoleId = role.Id });
        }
        else
        {
            var group = new Group { Id = Guid.NewGuid(), Name = "team-a" };
            db.Groups.Add(group);
            db.ProjectMemberships.Add(new ProjectMembership(app.ProjectA.Id, null, group.Id) { RoleId = role.Id });
            if (source == "local-group")
                db.GroupMemberships.Add(new GroupMembership(group.Id, app.UserId));
        }
        await db.SaveChangesAsync();
        app.AsStoredPermissions(source == "idp-group" ? "TEAM-A" : null);

        var response = await app.Client.GetAsync("api/directories?includeRelated=true&includeFileContent=true");
        await AssertStatus(HttpStatusCode.OK, response);
        var directories = await response.Content.ReadFromJsonAsync<DirectoryResponse[]>();
        if (permissions is "view" or "all")
        {
            Assert.Equal(new[] { app.DirectoryA.Id, app.ChildA.Id, app.GrandchildA.Id, app.AlternateA.Id }.Order(),
                directories.Select(d => d.Id).Order());
            Assert.All(directories, d => Assert.Equal(app.ProjectA.Id, d.ProjectId));
            var files = directories.SelectMany(d => d.Files).ToArray();
            Assert.Equal(new[] { app.FileA.Id, app.AnotherFileA.Id }.Order(), files.Select(f => f.Id).Order());
            Assert.All(files, f => Assert.NotNull(f.Content));
        }
        else
        {
            Assert.Empty(directories);
        }
    }

    [Fact]
    public async Task Directory_listing_without_memberships_is_empty()
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        app.AsStoredPermissions();
        var response = await app.Client.GetAsync("api/directories");

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Empty(await response.Content.ReadFromJsonAsync<DirectoryResponse[]>());
    }

    [Fact]
    public async Task Directory_listing_preserves_system_view_access_without_memberships()
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        await using var db = app.NewContext();
        var user = await db.Users.SingleAsync(u => u.Id == app.UserId);
        user.RoleId = SystemRoleDefaults.ObserverRoleId;
        await db.SaveChangesAsync();
        app.AsStoredPermissions();
        var response = await app.Client.GetAsync("api/directories");

        await AssertStatus(HttpStatusCode.OK, response);
        var directories = await response.Content.ReadFromJsonAsync<DirectoryResponse[]>();
        Assert.Equal(5, directories.Length);
        Assert.Contains(directories, d => d.ProjectId == app.ProjectB.Id);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Empty_system_requirements_fall_back_to_scoped_authorization(bool nullRequirements, bool scopedAccess)
    {
        await using var app = new AuthorizationTestApp(database.ConnectionString);
        using var scope = app.Services.CreateScope();
        await using var db = app.NewContext();
        var claims = scopedAccess
            ? new[] { new Claim(AuthorizationConstants.ProjectPermissionsClaimType,
                new ProjectPermissionsClaim { ProjectId = app.ProjectA.Id, Permissions = [ProjectPermission.ViewProject] }.ToString()) }
            : [];
        var identity = Substitute.For<IIdentityResolver>();
        identity.GetClaimsPrincipal().Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")));
        var authorization = new Infrastructure.Authorization.AuthorizationService(
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>(), identity, db);

        Assert.Equal(scopedAccess, await authorization.Authorize<Domain.Models.File>(app.FileA.Id,
            nullRequirements ? null : [], [ProjectPermission.ViewProject], CancellationToken.None));
        Assert.False(await authorization.Authorize<Domain.Models.File>(app.FileB.Id,
            nullRequirements ? null : [], [ProjectPermission.ViewProject], CancellationToken.None));
        Assert.False(await authorization.Authorize(nullRequirements ? null : [], CancellationToken.None));
    }

    private static async Task AssertStatus(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
}
