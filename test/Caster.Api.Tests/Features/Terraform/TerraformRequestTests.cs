// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Terraform;
using Caster.Api.Tests.Support;

namespace Caster.Api.Tests.Features.Terraform;

/// <summary>
/// <c>api/terraform/*</c>: open to any caller on some project, or holding ViewProjects. The versions are the
/// sub-directories of <c>Terraform:BinaryPath</c> (<see cref="TestConfiguration.TerraformVersions"/>).
/// </summary>
public class TerraformRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task GetVersions_lists_the_installed_versions_newest_first_to_a_member_of_any_project()
    {
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync("api/terraform/versions", Ct);

        var result = await ReadAsync<GetVersions.TerraformVersionsResult>(response);
        Assert.Equal(["1.5.7", "0.12.29"], result.Versions);
        Assert.Equal("0.12.29", result.DefaultVersion);
    }

    [Fact]
    public async Task GetVersions_is_open_to_a_caller_holding_ViewProjects()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync("api/terraform/versions", Ct));
    }

    [Fact]
    public async Task GetVersions_is_forbidden_for_a_caller_holding_only_ViewWorkspaces()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewWorkspaces).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/terraform/versions", Ct));
    }

    [Fact]
    public async Task GetMaxParallelism_returns_the_configured_value_to_a_member_of_any_project()
    {
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync("api/terraform/max-parallelism", Ct);

        Assert.Equal(25, await ReadAsync<int>(response));
    }

    [Fact]
    public async Task GetMaxParallelism_is_forbidden_for_a_caller_holding_only_ViewWorkspaces()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewWorkspaces).SeedAsync();

        await AssertProblem(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/terraform/max-parallelism", Ct));
    }
}
