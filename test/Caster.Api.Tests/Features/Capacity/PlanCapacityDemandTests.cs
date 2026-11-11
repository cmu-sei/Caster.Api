// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.Capacity;
using Caster.Api.Features.Runs.EventHandlers;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using File = System.IO.File;

namespace Caster.Api.Tests.Features.Capacity;

/// <summary>A finished plan saves the machines read from <c>terraform show -json</c>, for the check before apply.</summary>
public class PlanCapacityDemandTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly string PlanJson =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "capacity", "providers.plan.json"));

    private async Task<Run> Plan(TerraformResult show)
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        var run = TestData.Run(workspace);
        await Seed(project, directory, workspace, run);

        var terraform = Substitute.For<ITerraformService>();
        var ok = Task.FromResult(new TerraformResult { Output = "ok", ExitCode = 0 });
        terraform.InitializeWorkspace(default, default).ReturnsForAnyArgs(ok);
        terraform.Plan(default, default, default, default, default, default, default).ReturnsForAnyArgs(ok);
        terraform.Show(default).ReturnsForAnyArgs(Task.FromResult(show));

        var root = Path.Combine(Path.GetTempPath(), $"caster-capacity-{Guid.NewGuid()}");

        try
        {
            await using var context = NewContext();
            var handler = new RunAddedHandler(
                context,
                new DbContextOptionsBuilder<Caster.Api.Data.CasterContext>().UseNpgsql(Session.ConnectionString).Options,
                NullLogger<RunAddedHandler>.Instance,
                terraform,
                new TerraformOptions { RootWorkingDirectory = root, GenerateTopology = false },
                new LockService(),
                new OutputService(),
                Substitute.For<IMediator>());

            await handler.Handle(new RunAdded { RunId = run.Id, WorkspaceId = workspace.Id }, Ct);
        }
        finally
        {
            if (System.IO.Directory.Exists(root))
                System.IO.Directory.Delete(root, recursive: true);
        }

        return run;
    }

    [Fact]
    public async Task A_planned_run_saves_its_machines()
    {
        var run = await Plan(new TerraformResult { Output = PlanJson, ExitCode = 0 });

        await using var context = NewContext();
        var plan = await context.Plans.SingleAsync(x => x.RunId == run.Id, Ct);
        Assert.Equal(PlanStatus.Planned, plan.Status);
        var demand = CapacityCheckService.Read(plan.CapacityDemand);
        Assert.Equal(12, demand.Machines.Count);
    }

    [Fact]
    public async Task A_plan_that_cannot_be_shown_is_still_planned_without_machines()
    {
        var run = await Plan(new TerraformResult { Output = "", ExitCode = 1 });

        await using var context = NewContext();
        var plan = await context.Plans.SingleAsync(x => x.RunId == run.Id, Ct);
        Assert.Equal(PlanStatus.Planned, plan.Status);
        Assert.Null(plan.CapacityDemand);
        Assert.Contains("Size check unavailable", plan.Output);
    }
}
