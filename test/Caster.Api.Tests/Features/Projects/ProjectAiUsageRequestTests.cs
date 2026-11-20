// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Features.Projects;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Project = Caster.Api.Domain.Models.Project;

namespace Caster.Api.Tests.Features.Projects;

/// <summary>
/// GET api/projects/{id}/ai-usage: what drafting with AI used in a project, by task, priced from the Ai:Prices
/// setting (<c>TestConfiguration</c> prices test-model and us_test_env_model_1_0), and the recorder that writes
/// each call against the Project of the Directory it was made in.
/// </summary>
public class ProjectAiUsageRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    // ---- GET api/projects/{id}/ai-usage -------------------------------------------------------------

    /// <summary>The project's totals and each task's, priced, with another project's calls left out.</summary>
    [Fact]
    public async Task Get_totals_and_prices_the_projects_calls_by_task()
    {
        var project = TestData.Project("Harbor");
        var other = TestData.Project("Other");
        await Seed(project, other,
            Usage(project, AiTasks.Terraform, "test-model", 45565, 39573),
            Usage(project, AiTasks.Terraform, "test-model", 1000, 1000, stopReason: "refusal"),
            Usage(project, AiTasks.Design, "test-model", 2000, 3000, cacheRead: 10000, cacheWrite: 1000),
            Usage(other, AiTasks.Design, "test-model", 99999, 99999));
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var usage = await ReadAsync<ProjectAiUsage>(await Client(actor).GetAsync($"api/projects/{project.Id}/ai-usage", Ct));

        Assert.Equal((project.Id, "Harbor"), (usage.ProjectId, usage.Name));
        Assert.Equal((3, 1, 48565L, 43573L, 10000L, 1000L), (usage.Calls, usage.Refused, usage.InputTokens, usage.OutputTokens, usage.CacheReadTokens, usage.CacheWriteTokens));
        // 48,565 in at $2, 43,573 out at $10, 10,000 cache reads at $0.20 and 1,000 cache writes at $2.50 a million
        Assert.Equal(537_360, usage.EstimatedCostMicros);
        Assert.Empty(usage.Unpriced);
        Assert.Equal([AiTasks.Design, AiTasks.Terraform], usage.ByTask.Select(t => t.Name));
        Assert.Equal(38_500, usage.ByTask[0].EstimatedCostMicros);
        // the refused call is billed too
        Assert.Equal((2, 1, 498_860L), (usage.ByTask[1].Calls, usage.ByTask[1].Refused, usage.ByTask[1].EstimatedCostMicros.Value));
        Assert.NotNull(usage.FirstCall);
    }

    /// <summary>
    /// A price keyed as an environment variable can hold it prices the model; a model with no price is left out of the
    /// estimate, which totals the rest, and is named so it can be priced.
    /// </summary>
    [Fact]
    public async Task Get_estimates_the_priced_calls_and_names_the_unpriced_models()
    {
        var project = TestData.Project();
        await Seed(project,
            Usage(project, AiTasks.Terraform, "us.test.env-model-1.0", 1000, 1000),
            Usage(project, AiTasks.Brief, "other-model", 1000, 1000));

        var usage = await ReadAsync<ProjectAiUsage>(await RootClient.GetAsync($"api/projects/{project.Id}/ai-usage", Ct));

        // 1,000 tokens in at $1 and 1,000 out at $5 a million
        Assert.Equal(6_000, usage.EstimatedCostMicros);
        Assert.Equal(["other-model"], usage.Unpriced);
        Assert.Null(usage.ByTask.Single(t => t.Name == AiTasks.Brief).EstimatedCostMicros);
        Assert.Equal(6_000, usage.ByTask.Single(t => t.Name == AiTasks.Terraform).EstimatedCostMicros);
    }

    [Fact]
    public async Task Get_of_a_project_drafted_without_AI_has_no_calls_and_no_estimate()
    {
        var project = TestData.Project();
        await Seed(project);

        var usage = await ReadAsync<ProjectAiUsage>(await RootClient.GetAsync($"api/projects/{project.Id}/ai-usage", Ct));

        Assert.Equal(0, usage.Calls);
        Assert.Null(usage.EstimatedCostMicros);
        Assert.Empty(usage.ByTask);
        Assert.Null(usage.FirstCall);
    }

    [Fact]
    public async Task Get_returns_the_usage_to_a_caller_holding_ViewProjects()
    {
        var project = TestData.Project();
        await Seed(project, Usage(project, AiTasks.Design, "test-model", 10, 10));
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var usage = await ReadAsync<ProjectAiUsage>(await Client(actor).GetAsync($"api/projects/{project.Id}/ai-usage", Ct));

        Assert.Equal(1, usage.Calls);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var project = TestData.Project();
        await Seed(project);
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}/ai-usage", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProject()
    {
        var project = TestData.Project();
        await Seed(project);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}/ai-usage", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_EditProjects()
    {
        var project = TestData.Project();
        await Seed(project);
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{project.Id}/ai-usage", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Get_of_an_unknown_project_is_not_found_for_a_caller_holding_ViewProjects()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var response = await Client(actor).GetAsync($"api/projects/{Guid.NewGuid()}/ai-usage", Ct);

        await AssertProblem(HttpStatusCode.NotFound, response);
    }

    // ---- recording ------------------------------------------------------------------------------------

    /// <summary>A call is recorded with its task, its Directory, that Directory's Project and the caller.</summary>
    [Fact]
    public async Task Record_saves_the_call_against_the_project_of_the_directory_it_was_made_in()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        await Seed(project, directory);
        var userId = Guid.NewGuid();

        await RecordInARequest(userId, AiTasks.Terraform, directory.Id,
            """{"stop_reason":"tool_use","usage":{"input_tokens":100,"output_tokens":50,"cache_read_input_tokens":7,"cache_creation_input_tokens":3}}""");

        await using var db = NewContext();
        var row = await db.AiUsages.SingleAsync(u => u.DirectoryId == directory.Id, Ct);
        Assert.Equal((project.Id, AiTasks.Terraform, "test-model", userId), (row.ProjectId, row.Task, row.Model, row.CreatedById));
        Assert.Equal((100, 50, 7, 3, "tool_use"), (row.InputTokens, row.OutputTokens, row.CacheReadTokens, row.CacheWriteTokens, row.StopReason));
    }

    /// <summary>What a project cost stays recorded when the project is deleted, with no project to show it under.</summary>
    [Fact]
    public async Task Deleting_a_project_keeps_its_usage_without_the_project()
    {
        var project = TestData.Project();
        var usage = Usage(project, AiTasks.Design, "test-model", 10, 10);
        await Seed(project, usage);

        await using (var db = NewContext())
        {
            db.Projects.Remove(await db.Projects.SingleAsync(p => p.Id == project.Id, Ct));
            await db.SaveChangesAsync(Ct);
        }

        await using var check = NewContext();
        Assert.Null((await check.AiUsages.SingleAsync(u => u.Id == usage.Id, Ct)).ProjectId);
    }

    // the recorder runs inside a request, whose test database the session header names
    private async Task RecordInARequest(Guid userId, string task, Guid directoryId, string message)
    {
        var accessor = Factory.Services.GetRequiredService<IHttpContextAccessor>();
        var request = new DefaultHttpContext { User = new ClaimsPrincipalBuilder().WithUserId(userId).Build() };
        request.Request.Headers[TestDatabaseScope.HeaderName] = SessionId;
        accessor.HttpContext = request;
        try
        {
            using var scope = Factory.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IAiTaskContext>().Use(task, directoryId);
            await scope.ServiceProvider.GetRequiredService<IAiUsageRecorder>()
                .RecordAsync("test-model", JsonDocument.Parse(message).RootElement, Ct);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static AiUsage Usage(Project project, string task, string model, int input, int output,
        int cacheRead = 0, int cacheWrite = 0, string stopReason = "tool_use") => new()
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Task = task,
            Model = model,
            InputTokens = input,
            OutputTokens = output,
            CacheReadTokens = cacheRead,
            CacheWriteTokens = cacheWrite,
            StopReason = stopReason,
            DateCreated = DateTime.UtcNow
        };
}
