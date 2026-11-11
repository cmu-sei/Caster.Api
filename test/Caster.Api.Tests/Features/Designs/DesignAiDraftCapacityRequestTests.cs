// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Features.Designs;

/// <summary>
/// Saving a design from an AI draft with its topology plan: refused when the plan's machines go
/// over a limit of a workspace in the project, saved when they are within it.
/// </summary>
public class DesignAiDraftCapacityRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private async Task<(Project Project, Directory Directory, Module Module, ModuleVersion Version)> Seed(int maxVcpu)
    {
        var project = TestData.Project();
        project.MaxVcpu = maxVcpu;
        var directory = TestData.Directory(project);
        var module = TestData.Module();
        var version = TestData.ModuleVersion(module);
        await Seed(project, directory, module, version);
        return (project, directory, module, version);
    }

    private static object Command(Module module, ModuleVersion version, int machines, int cpu) => new
    {
        name = "range",
        modules = new[] { new { moduleId = module.Id, name = "range", moduleVersion = version.Name, values = new object[0] } },
        topology = new { machines = Enumerable.Range(1, machines).Select(i => new { name = $"vm{i}", cpu }).ToArray() },
    };

    [Fact]
    public async Task A_design_over_the_projects_vcpu_limit_is_not_saved()
    {
        var (project, directory, module, version) = await Seed(maxVcpu: 8);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/directories/{directory.Id}/designs/ai-draft/create", Command(module, version, machines: 3, cpu: 4), Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.StartsWith("This design cannot be saved. The machines need 12 vCPUs, over the limit of 8 vCPUs (set on the project)", problem.Title);
        await using var context = NewContext();
        Assert.False(await context.Designs.AnyAsync(x => x.DirectoryId == directory.Id, Ct));
    }

    [Fact]
    public async Task A_design_within_the_limits_is_saved()
    {
        var (project, directory, module, version) = await Seed(maxVcpu: 8);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/directories/{directory.Id}/designs/ai-draft/create", Command(module, version, machines: 2, cpu: 4), Ct);

        await AssertStatus(HttpStatusCode.Created, response);
    }
}
