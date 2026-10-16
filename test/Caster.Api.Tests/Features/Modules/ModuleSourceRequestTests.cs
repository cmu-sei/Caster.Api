// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using ModuleSourceView = Caster.Api.Features.Modules.ModuleSource;

namespace Caster.Api.Tests.Features.Modules;

/// <summary>
/// <c>ModulesController</c>'s routes that need a configured module source, over a host with the legacy
/// GitLab source configured (<see cref="GitlabSourceAppFactory"/>): importing one GitLab project as a module
/// needs ManageModules, and listing the sources ViewModules.
/// </summary>
public class ModuleSourceRequestTests(DatabaseFixture fixture, GitlabSourceAppFactory factory)
    : ApiTestBase(fixture, factory), IClassFixture<GitlabSourceAppFactory>
{
    /// <summary>A GitLab project id that only <see cref="CreateFromRepository_by_a_caller_holding_ManageModules_stores_the_module_gitlab_describes"/> stubs.</summary>
    private const int GitlabProjectId = 424242;

    /// <summary>
    /// A GitLab project id that only <see cref="CreateFromRepository_is_forbidden_for_a_caller_holding_only_ViewModules_where_a_source_is_configured"/>
    /// stubs, so that a request the gate let through would be answered and the module stored.
    /// </summary>
    private const int RefusedGitlabProjectId = 434343;

    [Fact]
    public async Task CreateFromRepository_by_a_caller_holding_ManageModules_stores_the_module_gitlab_describes()
    {
        var path = $"terraform-modules/repository-{Guid.NewGuid():N}";
        Factory.OutboundHttp
            .Respond($"{TestConfiguration.GitlabApiUrl}projects/{GitlabProjectId}", Json(
                $$"""{"id":{{GitlabProjectId}},"name":"imported","path_with_namespace":"{{path}}","http_url_to_repo":"https://gitlab.test/imported.git?ref=master"}"""))
            .Respond($"{TestConfiguration.GitlabApiUrl}projects/{GitlabProjectId}/releases", Json("[]"));
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageModules).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/repository/modules", new { id = GitlabProjectId.ToString() }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        await using var context = NewContext();
        Assert.Equal("imported", (await context.Modules.SingleAsync(x => x.Path == path, Ct)).Name);
    }

    /// <summary>
    /// ViewModules reads modules but does not import one. GitLab answers for the project, so only the gate
    /// stands between the caller and a stored module.
    /// </summary>
    [Fact]
    public async Task CreateFromRepository_is_forbidden_for_a_caller_holding_only_ViewModules_where_a_source_is_configured()
    {
        var path = $"terraform-modules/refused-{Guid.NewGuid():N}";
        Factory.OutboundHttp
            .Respond($"{TestConfiguration.GitlabApiUrl}projects/{RefusedGitlabProjectId}", Json(
                $$"""{"id":{{RefusedGitlabProjectId}},"name":"refused","path_with_namespace":"{{path}}","http_url_to_repo":"https://gitlab.test/refused.git?ref=master"}"""))
            .Respond($"{TestConfiguration.GitlabApiUrl}projects/{RefusedGitlabProjectId}/releases", Json("[]"));
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/repository/modules", new { id = RefusedGitlabProjectId.ToString() }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await using var context = NewContext();
        Assert.False(await context.Modules.AnyAsync(x => x.Path == path, Ct));
    }

    [Fact]
    public async Task GetSources_lists_the_gitlab_source_to_a_caller_holding_ViewModules()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewModules).SeedAsync();

        var response = await Client(actor).GetAsync("api/modules/sources", Ct);

        var source = Assert.Single(await ReadAsync<ModuleSourceView[]>(response));
        Assert.Equal(ModuleRepositoryDispatcher.GitlabSourceName, source.Name);
        Assert.Equal(ModuleSourceProviders.Gitlab, source.Provider);
    }

    [Fact]
    public async Task GetSources_is_forbidden_for_a_caller_holding_only_ManageModules_where_a_source_is_configured()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageModules).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/modules/sources", Ct));
    }

    private static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);
}
