// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Features.AiAssist;
using Caster.Api.Features.Files;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;

namespace Caster.Api.Tests.Features.AiAssist;

/// <summary>
/// The Ask AI routes, POST api/directories/{id}/ai-assist and .../ai-assist/apply, over a host with AI on and the model
/// substituted (<see cref="AiAppFactory"/>): asking proposes labelled changes and saves nothing; applying makes each kept
/// change through the file and workspace requests, and reports a change the Directory has moved past instead of making it.
/// </summary>
public class AiAssistRequestTests(DatabaseFixture fixture, AiAppFactory factory)
    : ApiTestBase(fixture, factory), IClassFixture<AiAppFactory>
{
    private AiAppFactory Ai => (AiAppFactory)Factory;

    private const string Provider = "terraform {\n  required_version = \">= 0.13\"\n}\n";
    private const string Main = "module \"web\" {\n  source = \"./web\"\n  count  = 2\n}\n";

    // ---- ask ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Ask_returns_labelled_changes_and_saves_nothing()
    {
        var (project, _, directory, main, workspace) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        Ai.Model.Answers(directory.Id, """
            {"reply":"Adding a network and moving dev to 1.5.7.","cause":"none","changes":[
              {"op":"file.edit","file":"main.tf","content":"# new main\n"},
              {"op":"file.add","file":"network.tf","content":"resource \"x\" \"y\" {}\n"},
              {"op":"workspace.version","workspace":"dev","version":"1.5.7"},
              {"op":"file.edit","file":"provider.tf","content":"# not this directory's\n"}]}
            """);

        var result = await Ask(actor, directory, "add a network");

        Assert.Equal("Adding a network and moving dev to 1.5.7.", result.Reply);
        Assert.Equal(["Edit main.tf", "Add network.tf", "Set dev to Terraform 1.5.7 (now 0.12.29)"], result.Changes.Select(c => c.Label));
        var edit = result.Changes[0];
        Assert.Equal((AiAssistOps.FileEdit, main.Id, Main, AiTerraformDraftBuilder.ContentHash(Main)), (edit.Op, edit.FileId, edit.ExistingContent, edit.ExistingContentHash));
        Assert.Equal((workspace.Id, "0.12.29"), (result.Changes[2].WorkspaceId, result.Changes[2].ExistingTerraformVersion));
        Assert.Contains(result.Warnings, w => w.StartsWith("Edit provider.tf: left out") && w.Contains("parent directory range"));

        var call = Assert.Single(Ai.Model.Calls, c => c.DirectoryId == directory.Id);
        Assert.Equal((AiTasks.Assist, AiAssistBuilder.ToolName), (call.Task, call.ToolName));
        Assert.Contains("=== main.tf (editable, this directory) ===", call.Prompt);
        Assert.Contains("=== provider.tf (read-only, from range) ===", call.Prompt);
        Assert.Contains("- dev: Terraform version 0.12.29", call.Prompt);

        await using var db = NewContext();
        Assert.Equal(Main, (await db.Files.SingleAsync(f => f.Id == main.Id, Ct)).Content);
        Assert.False(await db.Files.AnyAsync(f => f.DirectoryId == directory.Id && f.Name == "network.tf", Ct));
        Assert.Equal("0.12.29", (await db.Workspaces.SingleAsync(w => w.Id == workspace.Id, Ct)).TerraformVersion);
        Assert.Equal(0, await db.FileVersions.CountAsync(v => v.FileId == main.Id, Ct));
    }

    /// <summary>
    /// The model is shown the end of the failed plan's output, without the terminal colour codes; a run that applied
    /// keeps its output to itself.
    /// </summary>
    [Fact]
    public async Task Ask_shows_the_model_the_output_of_the_latest_failed_run()
    {
        var (project, _, directory, _, workspace) = await SeedDirectory();
        var applied = TestData.Workspace(directory, "prod");
        var failedRun = TestData.Run(workspace, RunStatus.Failed);
        var appliedRun = TestData.Run(applied, RunStatus.Applied);
        await Seed(applied, failedRun, appliedRun,
            TestData.Plan(failedRun, PlanStatus.Failed, "\u001b[31mError: Reserved argument name in module block\u001b[0m\n  on main.tf line 3, in module \"web\":"),
            TestData.Plan(appliedRun, PlanStatus.Planned, "Plan: 3 to add"),
            TestData.Apply(appliedRun, ApplyStatus.Applied, "Apply complete! Resources: 3 added"));
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        Ai.Model.Answers(directory.Id, """
            {"reply":"The code is fine: count in a module block needs Terraform 0.13 or later, and dev runs 0.12.29.","cause":"environment",
             "changes":[{"op":"workspace.version","workspace":"dev","version":"1.5.7"}]}
            """);

        var result = await Ask(actor, directory, "fix the errors from the last apply");

        var call = Assert.Single(Ai.Model.Calls, c => c.DirectoryId == directory.Id);
        Assert.Contains("the plan failed", call.Prompt);
        Assert.Contains("Error: Reserved argument name in module block", call.Prompt);
        Assert.DoesNotContain("\u001b[", call.Prompt);
        Assert.DoesNotContain("Apply complete!", call.Prompt);
        Assert.Contains("Reserved argument name in module block", call.System);
        Assert.Equal(AiAssistCauses.Environment, result.Cause);
        Assert.Equal("Set dev to Terraform 1.5.7 (now 0.12.29)", Assert.Single(result.Changes).Label);
    }

    [Fact]
    public async Task Ask_is_forbidden_for_a_viewer_without_a_model_call()
    {
        var (project, _, directory, _, _) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();
        Ai.Model.Answers(directory.Id, """{"reply":"never asked","cause":"none","changes":[]}""");

        var response = await Client(actor).PostAsJsonAsync($"api/directories/{directory.Id}/ai-assist", new { prompt = "add a vm" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.DoesNotContain(Ai.Model.Calls, c => c.DirectoryId == directory.Id);
    }

    [Fact]
    public async Task Ask_refuses_a_workspace_of_another_directory()
    {
        var (project, _, directory, _, _) = await SeedDirectory();
        var other = TestData.Directory(project, "other");
        var elsewhere = TestData.Workspace(other, "elsewhere");
        await Seed(other, elsewhere);
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/directories/{directory.Id}/ai-assist", new { prompt = "x", workspaceId = elsewhere.Id }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.DoesNotContain(Ai.Model.Calls, c => c.DirectoryId == directory.Id);
    }

    // ---- apply --------------------------------------------------------------------------------------

    [Fact]
    public async Task Apply_makes_each_change_through_the_file_and_workspace_requests()
    {
        var (project, _, directory, main, workspace) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var proposal = await Propose(actor, directory, Everything);

        var applied = await ApplyChanges(actor, directory, proposal.Changes.ToArray());

        Assert.All(applied.Results, r => Assert.True(r.Applied, r.Error));
        Assert.Equal(["Edit main.tf", "Add network.tf", "Set dev to Terraform 1.5.7 (now 0.12.29)"], applied.Results.Select(r => r.Label));
        await using var db = NewContext();
        var edited = await db.Files.SingleAsync(f => f.Id == main.Id, Ct);
        Assert.Equal(("# new main\n", actor.Id, (Guid?)null), (edited.Content, edited.ModifiedById, edited.LockedById));
        Assert.Equal(1, await db.FileVersions.CountAsync(v => v.FileId == main.Id, Ct));
        var added = await db.Files.SingleAsync(f => f.DirectoryId == directory.Id && f.Name == "network.tf", Ct);
        Assert.Equal(applied.Results[1].FileId, added.Id);
        Assert.Null(added.WorkspaceId);
        Assert.Equal("1.5.7", (await db.Workspaces.SingleAsync(w => w.Id == workspace.Id, Ct)).TerraformVersion);
    }

    [Fact]
    public async Task Apply_keeps_the_callers_own_lock()
    {
        var (project, _, directory, main, _) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        await Lock(main, actor);
        var proposal = await Propose(actor, directory, Everything);

        var applied = await ApplyChanges(actor, directory, proposal.Changes[0]);

        Assert.True(Assert.Single(applied.Results).Applied);
        await using var db = NewContext();
        Assert.Equal(actor.Id, (await db.Files.SingleAsync(f => f.Id == main.Id, Ct)).LockedById);
    }

    /// <summary>A file someone saved after the proposal is reported, not replaced; the rest of the changes are still made.</summary>
    [Fact]
    public async Task Apply_reports_a_file_changed_since_the_proposal_and_does_not_replace_it()
    {
        var (project, _, directory, main, _) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var proposal = await Propose(actor, directory, Everything);
        await using (var db = NewContext())
        {
            (await db.Files.SingleAsync(f => f.Id == main.Id, Ct)).Content = "# saved meanwhile\n";
            await db.SaveChangesAsync(Ct);
        }

        var applied = await ApplyChanges(actor, directory, proposal.Changes[0], proposal.Changes[1]);

        Assert.False(applied.Results[0].Applied);
        Assert.Contains("has changed since this was proposed", applied.Results[0].Error);
        Assert.True(applied.Results[1].Applied);
        await using var check = NewContext();
        var stored = await check.Files.SingleAsync(f => f.Id == main.Id, Ct);
        Assert.Equal(("# saved meanwhile\n", (Guid?)null), (stored.Content, stored.LockedById));
        Assert.True(await check.Files.AnyAsync(f => f.DirectoryId == directory.Id && f.Name == "network.tf", Ct));
    }

    [Fact]
    public async Task Apply_reports_a_file_another_user_holds()
    {
        var (project, _, directory, main, _) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var holder = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var proposal = await Propose(actor, directory, Everything);
        await Lock(main, holder);

        var applied = await ApplyChanges(actor, directory, proposal.Changes[0]);

        var result = Assert.Single(applied.Results);
        Assert.Equal((false, "main.tf is being edited by another user."), (result.Applied, result.Error));
        await using var db = NewContext();
        Assert.Equal(Main, (await db.Files.SingleAsync(f => f.Id == main.Id, Ct)).Content);
    }

    [Fact]
    public async Task Apply_reports_a_name_taken_and_a_version_moved_since_the_proposal()
    {
        var (project, _, directory, _, workspace) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var proposal = await Propose(actor, directory, Everything);
        await using (var db = NewContext())
        {
            db.Files.Add(TestData.File(await db.Directories.SingleAsync(d => d.Id == directory.Id, Ct), "network.tf", "# added meanwhile"));
            (await db.Workspaces.SingleAsync(w => w.Id == workspace.Id, Ct)).TerraformVersion = "1.5.7";
            await db.SaveChangesAsync(Ct);
        }

        var applied = await ApplyChanges(actor, directory, proposal.Changes[1], proposal.Changes[2]);

        Assert.All(applied.Results, r => Assert.False(r.Applied));
        Assert.Contains("has been added to this directory since", applied.Results[0].Error);
        Assert.Contains("Terraform version has changed since", applied.Results[1].Error);
    }

    [Fact]
    public async Task Apply_refuses_a_version_Caster_does_not_have()
    {
        var (project, _, directory, _, workspace) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        var change = new AiAssistChange
        {
            Op = AiAssistOps.WorkspaceVersion,
            WorkspaceId = workspace.Id,
            WorkspaceName = "dev",
            TerraformVersion = "9.9.9",
            ExistingTerraformVersion = "0.12.29"
        };

        var result = Assert.Single((await ApplyChanges(actor, directory, change)).Results);

        Assert.False(result.Applied);
        Assert.Contains("Caster has no Terraform 9.9.9", result.Error);
    }

    [Fact]
    public async Task Apply_is_forbidden_for_a_viewer()
    {
        var (project, _, directory, _, _) = await SeedDirectory();
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();
        var change = new AiAssistChange { Op = AiAssistOps.FileAdd, FileName = "refused.tf", Content = "# no" };

        var response = await Client(actor).PostAsJsonAsync($"api/directories/{directory.Id}/ai-assist/apply", new { changes = new[] { change } }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        await using var db = NewContext();
        Assert.False(await db.Files.AnyAsync(f => f.Name == "refused.tf", Ct));
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private const string Everything = """
        {"reply":"ok","cause":"none","changes":[
          {"op":"file.edit","file":"main.tf","content":"# new main\n"},
          {"op":"file.add","file":"network.tf","content":"resource \"x\" \"y\" {}\n"},
          {"op":"workspace.version","workspace":"dev","version":"1.5.7"}]}
        """;

    private async Task<AiAssistResult> Propose(TestActor actor, Directory directory, string answer)
    {
        Ai.Model.Answers(directory.Id, answer);
        var proposal = await Ask(actor, directory, "change things");
        Assert.Equal(3, proposal.Changes.Count);
        return proposal;
    }

    private async Task<AiAssistResult> Ask(TestActor actor, Directory directory, string prompt) =>
        await ReadAsync<AiAssistResult>(await Client(actor).PostAsJsonAsync($"api/directories/{directory.Id}/ai-assist", new { prompt }, Ct));

    private async Task<AiAssistApplyResult> ApplyChanges(TestActor actor, Directory directory, params AiAssistChange[] changes) =>
        await ReadAsync<AiAssistApplyResult>(await Client(actor).PostAsJsonAsync($"api/directories/{directory.Id}/ai-assist/apply", new { changes }, Ct));

    private async Task<(Project Project, Directory Parent, Directory Directory, File Main, Workspace Workspace)> SeedDirectory()
    {
        var project = TestData.Project();
        var parent = TestData.Directory(project, "range");
        var directory = TestData.Directory(project, "lab", parent);
        var main = TestData.File(directory, "main.tf", Main);
        var workspace = TestData.Workspace(directory, "dev");
        workspace.TerraformVersion = "0.12.29";
        await Seed(project, parent, directory, TestData.File(parent, "provider.tf", Provider), main, workspace);
        return (project, parent, directory, main, workspace);
    }

    private async Task Lock(File file, TestActor actor)
    {
        await using var db = NewContext();
        (await db.Files.SingleAsync(f => f.Id == file.Id, Ct)).Lock(actor.Id, canLock: false);
        await db.SaveChangesAsync(Ct);
    }
}
