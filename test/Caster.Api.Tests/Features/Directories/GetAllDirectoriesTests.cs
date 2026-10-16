// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Features.Directories;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using DirectoryView = Caster.Api.Features.Directories.Directory;

namespace Caster.Api.Tests.Features.Directories;

/// <summary>
/// <c>GET api/directories</c> for a caller whose project membership comes from a group the identity provider
/// names in the token. The hosted application runs with IdP groups off and its test token carries no groups
/// (<c>TestConfiguration</c>), so the request is driven here: the real <see cref="UserClaimsService"/> turns
/// the token's <c>groups</c> claim into project permissions, and the real authorization service and
/// <c>GetAll</c> handler list the directories from them. <c>DirectoryRequestTests</c> covers direct and
/// local-group memberships over HTTP.
/// </summary>
public class GetAllDirectoriesTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    /// <summary>The group's name as stored; the token names it in another case (<see cref="TokenGroupName"/>).</summary>
    private const string GroupName = "team-a";

    /// <summary>The group's name as the identity provider writes it, which matches <see cref="GroupName"/> whatever the case.</summary>
    private const string TokenGroupName = "TEAM-A";

    [Fact]
    public async Task GetAll_lists_a_projects_directories_and_their_files_to_a_member_of_a_group_the_token_names_whose_role_grants_ViewProject()
    {
        var (graph, user) = await SeedGraphWithTokenGroupRole(TestData.ProjectRole(permissions: [ProjectPermission.ViewProject]));

        var directories = await ListAs(user);

        AssertProjectADirectoriesWithTheirFiles(graph, directories);
    }

    [Fact]
    public async Task GetAll_lists_nothing_to_a_member_of_a_group_the_token_names_whose_role_grants_only_EditProject()
    {
        var (_, user) = await SeedGraphWithTokenGroupRole(TestData.ProjectRole(permissions: [ProjectPermission.EditProject]));

        Assert.Empty(await ListAs(user));
    }

    [Fact]
    public async Task GetAll_lists_nothing_to_a_member_of_a_group_the_token_names_whose_role_grants_nothing()
    {
        var (_, user) = await SeedGraphWithTokenGroupRole(TestData.ProjectRole(permissions: []));

        Assert.Empty(await ListAs(user));
    }

    [Fact]
    public async Task GetAll_lists_a_projects_directories_and_their_files_to_a_member_of_a_group_the_token_names_whose_role_grants_every_permission()
    {
        var (graph, user) = await SeedGraphWithTokenGroupRole(TestData.ProjectRole(allPermissions: true));

        var directories = await ListAs(user);

        AssertProjectADirectoriesWithTheirFiles(graph, directories);
    }

    /// <summary>
    /// The graph; a user on project B whose role grants nothing; and the group <see cref="GroupName"/>, of which
    /// the user is not a stored member, on project A with <paramref name="role"/>.
    /// </summary>
    private async Task<(TwoProjectGraph Graph, User User)> SeedGraphWithTokenGroupRole(ProjectRole role)
    {
        var user = TestData.User(name: "IdP Group Member");
        var noViewRole = TestData.ProjectRole(permissions: []);
        var group = TestData.Group(GroupName);
        await Seed(user, role, noViewRole, group);
        var graph = await TwoProjectGraph.SeedAsync(Db, user.Id, Ct);
        await Seed(
            TestData.ProjectMembership(graph.ProjectB.Id, noViewRole.Id, userId: user.Id),
            TestData.ProjectMembership(graph.ProjectA.Id, role.Id, groupId: group.Id));

        return (graph, user);
    }

    /// <summary>
    /// Lists the directories with their files and content, as the request would for <paramref name="user"/>
    /// presenting a token that names <see cref="TokenGroupName"/>.
    /// </summary>
    private async Task<DirectoryView[]> ListAs(User user)
    {
        await using var context = NewContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var token = new ClaimsPrincipalBuilder().WithUserId(user.Id).WithName(user.Name).WithClaim("groups", TokenGroupName).Build();
        var claims = await new UserClaimsService(context, cache, IdentityProviderGroups()).AddUserClaims(token, update: true);
        var handler = new GetAll.Handler(AuthorizationHarness.CreateCasterAuthorizationService(context, claims), TestMapper.Mapper, context);

        return await handler.Handle(new GetAll.Query { IncludeRelated = true, IncludeFileContent = true }, Ct);
    }

    /// <summary>The transformer's options with roles and groups read from the token, and no cache.</summary>
    private static ClaimsTransformationOptions IdentityProviderGroups() => new()
    {
        EnableCaching = false,
        UseGroupsFromIdP = true,
        GroupsClaimPath = "groups",
        UseRolesFromIdP = true,
        RolesClaimPath = "roles"
    };

    private static void AssertProjectADirectoriesWithTheirFiles(TwoProjectGraph graph, DirectoryView[] directories)
    {
        Assert.Equal(
            new[] { graph.DirectoryA.Id, graph.ChildA.Id, graph.GrandchildA.Id, graph.AlternateA.Id }.Order(),
            directories.Select(x => x.Id).Order());
        Assert.All(directories, x => Assert.Equal(graph.ProjectA.Id, x.ProjectId));
        var files = directories.SelectMany(x => x.Files).ToArray();
        Assert.Equal(new[] { graph.FileA.Id, graph.AnotherFileA.Id }.Order(), files.Select(x => x.Id).Order());
        Assert.All(files, x => Assert.NotNull(x.Content));
    }
}
