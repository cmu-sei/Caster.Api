// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Directory = Caster.Api.Domain.Models.Directory;

namespace Caster.Api.Tests.Features.Runs;

/// <summary>
/// What a run, its plan and its apply are answered with: the plan and apply ids always, the plan and apply
/// themselves only when asked for, and the names of who created and last modified the run.
/// </summary>
public class RunMappingTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_run_read_carries_the_plan_and_apply_only_when_asked(bool includePlan, bool includeApply)
    {
        var graph = await SeedGraph();
        var query = $"includePlan={includePlan}&includeApply={includeApply}";

        var one = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/runs/{graph.Run.Id}?{query}", Ct));
        var all = await ReadAsync<JsonElement[]>(await RootClient.GetAsync($"api/runs?{query}", Ct));
        var byWorkspace = await ReadAsync<JsonElement[]>(await RootClient.GetAsync($"api/workspaces/{graph.Run.WorkspaceId}/runs?{query}", Ct));

        foreach (var json in new[] { one, Assert.Single(all, IsRun(graph.Run)), Assert.Single(byWorkspace, IsRun(graph.Run)) })
        {
            AssertRun(json, graph);
            if (includePlan)
                AssertPlan(json.GetProperty("plan"), graph.Plan);
            else
                Assert.Equal(JsonValueKind.Null, json.GetProperty("plan").ValueKind);
            if (includeApply)
                AssertApply(json.GetProperty("apply"), graph.Apply);
            else
                Assert.Equal(JsonValueKind.Null, json.GetProperty("apply").ValueKind);
        }
    }

    /// <summary>A run without a plan, an apply or a creator answers nulls for them, and its stored null lists as null.</summary>
    [Fact]
    public async Task A_bare_run_answers_nulls()
    {
        var graph = await SeedGraph();
        var bare = TestData.Run(graph.Workspace);
        bare.CreatedAt = TestData.DefaultDateCreated.AddDays(-1);
        await Seed(bare);

        var json = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/runs/{bare.Id}?includePlan=true&includeApply=true", Ct));

        foreach (var member in new[] { "plan", "planId", "apply", "applyId", "createdById", "createdBy", "modifiedById", "modifiedBy", "targets", "replaceAddresses" })
            Assert.Equal(JsonValueKind.Null, json.GetProperty(member).ValueKind);
    }

    [Fact]
    public async Task Plans_and_applies_are_answered_with_every_member()
    {
        var graph = await SeedGraph();

        AssertPlan(await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/plans/{graph.Plan.Id}", Ct)), graph.Plan);
        AssertPlan(await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/runs/{graph.Run.Id}/plan", Ct)), graph.Plan);
        AssertApply(await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/applies/{graph.Apply.Id}", Ct)), graph.Apply);
        AssertApply(await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/runs/{graph.Run.Id}/apply", Ct)), graph.Apply);
    }

    /// <summary>A created run stores the body's members and is answered with its creator's name.</summary>
    [Fact]
    public async Task A_created_run_stores_the_body_and_names_its_creator()
    {
        var graph = await SeedGraph();

        var workspace = await SeedIdleWorkspace(graph);

        var json = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/runs",
            new { workspaceId = workspace.Id, isDestroy = true, targets = new[] { "a.b" }, replaceAddresses = new[] { "c.d" } }, Ct));

        var id = json.GetProperty("id").GetGuid();
        var stored = await ReadBack(db => db.Runs.SingleAsync(x => x.Id == id, Ct));
        Assert.Equal((workspace.Id, true, "a.b", "c.d"), (stored.WorkspaceId, stored.IsDestroy, Assert.Single(stored.Targets), Assert.Single(stored.ReplaceAddresses)));
        Assert.Equal(("Root", "Root"), (json.GetProperty("createdBy").GetString(), json.GetProperty("modifiedBy").GetString()));
        Assert.True(json.GetProperty("isDestroy").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("planId").ValueKind);
    }

    /// <summary>The queue lists what the run queue holds; a run it does not hold has no position, answered with a 204.</summary>
    [Fact]
    public async Task A_run_out_of_the_queue_has_no_position()
    {
        var graph = await SeedGraph();

        await AssertStatus(HttpStatusCode.NoContent, await RootClient.GetAsync($"api/runs/{graph.Run.Id}/queue-position", Ct));
        var queue = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/runs/queue", Ct));

        Assert.DoesNotContain(queue, x => x.GetProperty("runId").GetGuid() == graph.Run.Id);
    }

    /// <summary>A queued run's position names the run, its workspace and its place in the queue.</summary>
    [Fact]
    public async Task A_queued_run_has_a_position()
    {
        var graph = await SeedGraph();
        var workspace = await SeedIdleWorkspace(graph);
        var created = await ReadAsync<JsonElement>(await RootClient.PostAsJsonAsync("api/runs", new { workspaceId = workspace.Id }, Ct));
        var id = created.GetProperty("id").GetGuid();

        var position = await ReadAsync<JsonElement>(await RootClient.GetAsync($"api/runs/{id}/queue-position", Ct));
        var queue = await ReadAsync<JsonElement[]>(await RootClient.GetAsync("api/runs/queue", Ct));

        Assert.Equal(id, position.GetProperty("runId").GetGuid());
        Assert.Equal(workspace.Id, position.GetProperty("workspaceId").GetGuid());
        Assert.True(position.GetProperty("position").GetInt32() >= 1);
        Assert.False(position.TryGetProperty("total", out _));
        var listed = Assert.Single(queue, x => x.GetProperty("runId").GetGuid() == id);
        Assert.Equal(position.GetProperty("position").GetInt32(), listed.GetProperty("position").GetInt32());
    }

    /// <summary>A second workspace in the graph's directory, with no run, so a run can be created in it.</summary>
    private async Task<Workspace> SeedIdleWorkspace(Graph graph)
    {
        var workspace = TestData.Workspace(new Directory { Id = graph.Workspace.DirectoryId }, "idle");
        await Seed(workspace);

        return workspace;
    }

    private static Predicate<JsonElement> IsRun(Run run) => x => x.GetProperty("id").GetGuid() == run.Id;

    private void AssertRun(JsonElement json, Graph graph)
    {
        Assert.Equal(graph.Run.Id, json.GetProperty("id").GetGuid());
        Assert.Equal(graph.Workspace.Id, json.GetProperty("workspaceId").GetGuid());
        Assert.False(json.GetProperty("isDestroy").GetBoolean());
        Assert.Equal("Planned", json.GetProperty("status").GetString());
        Assert.Equal(["module.a"], json.GetProperty("targets").Deserialize<string[]>());
        Assert.Equal(0, json.GetProperty("replaceAddresses").GetArrayLength());
        Assert.Equal(graph.Plan.Id, json.GetProperty("planId").GetGuid());
        Assert.Equal(graph.Apply.Id, json.GetProperty("applyId").GetGuid());
        Assert.Equal(TestData.DefaultDateCreated, json.GetProperty("createdAt").GetDateTime().ToUniversalTime());
        Assert.Equal(Root.Id, json.GetProperty("createdById").GetGuid());
        Assert.Equal("Root", json.GetProperty("createdBy").GetString());
        Assert.Equal(graph.Modifier.Id, json.GetProperty("modifiedById").GetGuid());
        Assert.Equal(graph.Modifier.Name, json.GetProperty("modifiedBy").GetString());
    }

    private static void AssertPlan(JsonElement json, Plan plan)
    {
        Assert.Equal(plan.Id, json.GetProperty("id").GetGuid());
        Assert.Equal(plan.RunId, json.GetProperty("runId").GetGuid());
        Assert.Equal("Planned", json.GetProperty("status").GetString());
        Assert.Equal("plan output", json.GetProperty("output").GetString());
        Assert.False(json.TryGetProperty("topology", out _));
    }

    private static void AssertApply(JsonElement json, Apply apply)
    {
        Assert.Equal(apply.Id, json.GetProperty("id").GetGuid());
        Assert.Equal(apply.RunId, json.GetProperty("runId").GetGuid());
        Assert.Equal("Applied - State Error", json.GetProperty("status").GetString());
        Assert.Equal("apply output", json.GetProperty("output").GetString());
    }

    private sealed record Graph(Workspace Workspace, Run Run, Plan Plan, Apply Apply, TestActor Modifier);

    /// <summary>A planned run created by Root and last modified by another user, with a plan and an apply.</summary>
    private async Task<Graph> SeedGraph()
    {
        var modifier = await Actor().WithName("Run Modifier").SeedAsync();
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        var run = TestData.Run(workspace, RunStatus.Planned);
        run.CreatedById = Root.Id;
        run.ModifiedById = modifier.Id;
        run.Targets = ["module.a"];
        run.ReplaceAddresses = [];
        await Seed(project, directory, workspace, run);
        var plan = TestData.Plan(run);
        plan.Topology = "{}";
        var apply = TestData.Apply(run, ApplyStatus.Applied_StateError);
        await Seed(plan, apply);

        return new Graph(workspace, run, plan, apply, modifier);
    }
}
