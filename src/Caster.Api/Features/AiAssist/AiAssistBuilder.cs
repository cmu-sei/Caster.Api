// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Features.Files;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.AiAssist;

/// <summary>A workspace of the Directory as Ask AI shows it: its version, its own files and its latest run</summary>
public record AiAssistWorkspace(Guid Id, string Name, string TerraformVersion, List<AiContextFile> Files, AiAssistRun LastRun, AiAssistRun LastFailedRun = null);

/// <summary>A workspace's latest run, with the plan and apply output when it failed</summary>
public record AiAssistRun(RunStatus Status, bool IsDestroy, DateTime CreatedAt, PlanStatus? PlanStatus, string PlanOutput, ApplyStatus? ApplyStatus, string ApplyOutput)
{
    public bool Failed => Status is RunStatus.Failed or RunStatus.Failed_StateError
        || PlanStatus == Domain.Models.PlanStatus.Failed
        || ApplyStatus is Domain.Models.ApplyStatus.Failed or Domain.Models.ApplyStatus.Failed_StateError;
}

/// <summary>
/// What Ask AI is shown of a Directory, and what its proposals are checked against: the Directory's files and its
/// parents', its workspaces, the Designs that add modules to them, and the Terraform versions Caster can run.
/// </summary>
public class AiAssistState
{
    // what the model is shown of the files; past this the largest are cut, and a cut file cannot be changed
    public const int MaxFileCharacters = 300_000;
    // the end of a failed plan or apply output, where Terraform writes its errors
    public const int MaxOutputCharacters = 12_000;

    public Guid DirectoryId { get; init; }
    public string DirectoryName { get; init; }

    /// <summary>The Directory's own files (editable) and its parents' (read-only), as each workspace joins them</summary>
    public List<AiContextFile> Files { get; init; } = [];

    /// <summary>Files shown only in part, which no change may replace</summary>
    public HashSet<Guid> Trimmed { get; init; } = [];

    public List<AiAssistWorkspace> Workspaces { get; init; } = [];

    /// <summary>The workspace the planner is working in, if any</summary>
    public Guid? WorkspaceId { get; init; }

    public List<string> Designs { get; init; } = [];
    public string[] Versions { get; init; } = [];
    public string DefaultVersion { get; init; }

    /// <summary>The provider environment variables runs get, by name only</summary>
    public string[] ProviderVariables { get; init; } = [];

    public IEnumerable<AiContextFile> Editable => Files.Where(f => f.Editable);

    public static async Task<AiAssistState> LoadAsync(CasterContext db, ITerraformService terraformService, TerraformOptions terraformOptions,
        Guid directoryId, Guid? workspaceId, CancellationToken ct)
    {
        var directory = await db.Directories.AsNoTracking().SingleAsync(d => d.Id == directoryId, ct);
        var pathIds = directory.PathIds();

        var files = await db.Files
            .AsNoTracking()
            .Where(f => pathIds.Contains(f.DirectoryId) && f.WorkspaceId == null)
            .Select(f => new AiContextFile(f.Id, f.Name, f.Content, f.Directory.Name, f.DirectoryId == directoryId))
            .ToListAsync(ct);
        // a file that sets a credential is shown without its value, so it cannot be replaced: the placeholder would overwrite it
        // the values are hidden here, before the files are cut to the budget: a cut inside a quoted value would leave nothing to match
        files = files.Select(f => AiTerraformDraftBuilder.HoldsLiteralSecret(f.Content)
            ? f with { Editable = false, ReadOnlyReason = f.Editable ? "sets a credential" : null, Content = AiTerraformDraftBuilder.ForModel(f.Content) }
            : f).ToList();

        var workspaces = await db.Workspaces
            .AsNoTracking()
            .Where(w => w.DirectoryId == directoryId)
            .OrderBy(w => w.Name)
            .Select(w => new { w.Id, w.Name, w.TerraformVersion })
            .ToListAsync(ct);
        if (workspaceId.HasValue && workspaces.All(w => w.Id != workspaceId.Value))
            throw new ConflictException("The workspace must belong to this directory.");

        var workspaceFiles = await db.Files
            .AsNoTracking()
            .Where(f => f.DirectoryId == directoryId && f.WorkspaceId != null)
            .Select(f => new { f.WorkspaceId, File = new AiContextFile(f.Id, f.Name, f.Content, f.Workspace.Name, false) })
            .ToListAsync(ct);
        // a workspace's own file can be changed when a person made it and it holds no credential
        workspaceFiles = workspaceFiles.Select(f => new { f.WorkspaceId, File = f.File with { Editable = WorkspaceFileLock(f.File) == null } }).ToList();

        var shown = new List<AiAssistWorkspace>();
        foreach (var workspace in workspaces)
        {
            // the run output of the workspace the planner works in, or of every workspace when none is chosen
            AiAssistRun lastRun = null;
            AiAssistRun lastFailedRun = null;
            if (!workspaceId.HasValue || workspace.Id == workspaceId.Value)
            {
                var run = await db.Runs
                    .AsNoTracking()
                    .Include(r => r.Plan)
                    .Include(r => r.Apply)
                    .Where(r => r.WorkspaceId == workspace.Id)
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync(ct);
                if (run != null)
                {
                    lastRun = new AiAssistRun(run.Status, run.IsDestroy, run.CreatedAt, run.Plan?.Status, run.Plan?.Output, run.Apply?.Status, run.Apply?.Output);
                    if (!lastRun.Failed)
                    {
                        lastRun = lastRun with { PlanOutput = null, ApplyOutput = null };
                        // a plan that succeeded does not undo an apply that failed: show the latest failed apply no later apply has resolved (a failed plan is fixed by the next good plan)
                        var lastApplied = await db.Runs.AsNoTracking()
                            .Where(r => r.WorkspaceId == workspace.Id && r.Status == RunStatus.Applied)
                            .MaxAsync(r => (DateTime?)r.CreatedAt, ct);
                        var failed = await db.Runs
                            .AsNoTracking()
                            .Include(r => r.Plan)
                            .Include(r => r.Apply)
                            .Where(r => r.WorkspaceId == workspace.Id && r.Id != run.Id
                                && (lastApplied == null || r.CreatedAt > lastApplied)
                                && (r.Apply.Status == ApplyStatus.Failed || r.Apply.Status == ApplyStatus.Failed_StateError
                                    // an apply interrupted by a restart leaves the run failed and its apply unfinished
                                    || (r.Apply != null && (r.Status == RunStatus.Failed || r.Status == RunStatus.Failed_StateError))))
                            .OrderByDescending(r => r.CreatedAt)
                            .FirstOrDefaultAsync(ct);
                        if (failed != null)
                            lastFailedRun = new AiAssistRun(failed.Status, failed.IsDestroy, failed.CreatedAt, failed.Plan?.Status, failed.Plan?.Output, failed.Apply?.Status, failed.Apply?.Output);
                    }
                }
            }
            shown.Add(new AiAssistWorkspace(workspace.Id, workspace.Name, workspace.TerraformVersion,
                workspaceFiles.Where(f => f.WorkspaceId == workspace.Id).Select(f => f.File).ToList(), lastRun, lastFailedRun));
        }

        var designs = await db.Designs
            .AsNoTracking()
            .Include(d => d.Modules).ThenInclude(m => m.Module)
            .Include(d => d.Variables)
            .Where(d => d.Enabled && pathIds.Contains(d.DirectoryId))
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

        var trimmed = new HashSet<Guid>();
        return new AiAssistState
        {
            DirectoryId = directoryId,
            DirectoryName = directory.Name,
            Files = Trim(files, trimmed),
            Trimmed = trimmed,
            Workspaces = shown,
            WorkspaceId = workspaceId,
            Designs = designs.Select(DescribeDesign).ToList(),
            Versions = terraformService.GetVersions()?.OrderBy(v => v, VersionOrder).ToArray() ?? [],
            DefaultVersion = terraformOptions.DefaultVersion,
            ProviderVariables = Hypervisors.ProviderVariables(terraformOptions)
        };
    }

    /// <summary>Most of a workspace's own file Ask AI is shown, and so the most it can replace.</summary>
    public const int MaxWorkspaceFileCharacters = 4_000;

    // Blueprint writes one of these for each workspace it makes, and writes it again
    private static readonly Regex BlueprintFile = new(@"^blueprint-.+\.auto\.tfvars$", RegexOptions.IgnoreCase);

    /// <summary>
    /// Why Ask AI may not change a workspace's own file, or null when it may: Blueprint generates it, it sets a
    /// credential to a literal value, or it is too long to be shown in full.
    /// </summary>
    public static string WorkspaceFileLock(AiContextFile file)
    {
        if (BlueprintFile.IsMatch(file.Name ?? ""))
            return "Blueprint generates it";
        if (AiTerraformDraftBuilder.HoldsLiteralSecret(file.Content))
            return "it sets a credential";
        if ((file.Content?.Length ?? 0) > MaxWorkspaceFileCharacters)
            return "it is too long to show in full";
        return null;
    }

    private static string DescribeDesign(Design design)
    {
        var modules = design.Modules.Where(m => m.Enabled)
            .Select(m => $"module \"{m.Name}\" ({m.Module?.Name} {m.ModuleVersion}" +
                (m.Values.Count > 0 ? $", sets {string.Join(", ", m.Values.Select(v => v.Name))}" : "") + ")");
        var variables = design.Variables.Select(v => $"{v.Name} ({v.Type}" + (string.IsNullOrEmpty(v.DefaultValue) ? ")" : $", default {Shorten(v.DefaultValue, 80)})"));
        return $"{design.Name}: " + string.Join("; ", modules.DefaultIfEmpty("no modules")) +
            (design.Variables.Count > 0 ? ". Variables: " + string.Join(", ", variables) : "");
    }

    // the Directory's own files keep their place over its parents'; past the budget the largest are cut first
    private static List<AiContextFile> Trim(List<AiContextFile> files, HashSet<Guid> trimmed)
    {
        var total = files.Sum(f => f.Content?.Length ?? 0);
        if (total <= MaxFileCharacters)
            return files;

        var budget = MaxFileCharacters;
        var kept = new Dictionary<Guid, AiContextFile>();
        foreach (var file in files.OrderByDescending(f => f.Editable).ThenBy(f => f.Content?.Length ?? 0))
        {
            var length = file.Content?.Length ?? 0;
            if (length <= budget)
            {
                kept[file.Id] = file;
                budget -= length;
                continue;
            }
            var cut = Math.Max(0, Math.Min(budget, 2_000));
            kept[file.Id] = file with { Content = file.Content[..cut] + $"\n# ... the remaining {length - cut} characters are not shown" };
            budget -= cut;
            trimmed.Add(file.Id);
        }
        return files.Select(f => kept[f.Id]).ToList();
    }

    private static readonly Comparer<string> VersionOrder = Comparer<string>.Create((a, b) =>
        Version.TryParse(a, out var x) && Version.TryParse(b, out var y) ? x.CompareTo(y) : string.CompareOrdinal(a, b));

    public static string Shorten(string value, int length) =>
        string.IsNullOrEmpty(value) || value.Length <= length ? value : value[..length] + "...";
}

/// <summary>
/// The prompt and tool of Ask AI, and the rules its proposals are checked by: once when proposed, and again for each
/// change when it is applied, against the Directory as it is then.
/// </summary>
public static class AiAssistBuilder
{
    public const string ToolName = "propose_caster_changes";
    public const string ToolDescription = "Reply to the planner and propose changes to the directory's files and workspaces for them to review, or ask one question.";
    // the conversation is resent each turn; older turns are dropped first
    public const int MaxHistoryMessages = 20;
    public const int MaxHistoryCharacters = 4_000;

    private static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[A-Za-z]");

    public const string SystemPrompt =
@"You help a planner change the Terraform of one directory in Caster, the infrastructure tool of the Crucible cyber range, from a
panel beside their work. They ask for changes; you propose them; they review each one and apply the ones they keep.

How Caster runs Terraform:
- Each workspace of a directory is one Terraform root module: the directory's files, the files of every parent directory, the
  workspace's own files (usually variable values) and the modules of enabled Designs, all joined together. A name declared twice
  anywhere in that set fails the plan.
- Parent directories usually hold the provider and terraform blocks and shared variables. Reuse them; do not declare them again.
- Each workspace runs the Terraform version it is set to, or Caster's default when none is set.

You can propose these changes: add a file to the directory; replace an editable file of the directory with its complete new
content; replace an editable file of a workspace (its own values, usually .tfvars) with its complete new content; set a
workspace's Terraform version to one Caster has. You cannot delete or rename files, change a parent directory's files, a
workspace's file marked read-only or a Design, run a plan or apply, or change anything outside Caster; when the planner asks for
one of those, say so in reply and say what they can do instead.

Rules:
- Answer only by calling the tool, once. Put a short reply to the planner in reply: what you found, what you propose and why, in
  plain words.
- Change only what the planner asked for, plus what that needs. Leave out files you do not change.
- A file you change is given whole, every line of it, not only the lines that change. Keep what you do not need to change as it is.
- Only the files marked editable can be changed. A file marked as not shown in full cannot be changed. To change a workspace's
  file, name the workspace and the file; a value that differs between workspaces belongs in that workspace's own file.
- If the request is ambiguous in a way that changes the result, ask one short question in question and propose no changes.
- Never write credentials, tokens or keys. Declare a variable (sensitive = true) and say in reply what must be set.
- Use fictional addressing (RFC 1918 and RFC 5737 ranges) unless the files already set the addresses.
- Write HCL a person can maintain: one purpose per file, comments only where intent is not obvious.

Fixing a failed plan or apply:
When the planner asks about a failed run (for example ""fix the errors from the last apply""), read the run output given with the
workspace and work out what caused it before changing anything. Set cause to code or environment; set it to none when the planner
did not ask about a failure.
- Environment, not code: the Terraform is right but the range around it is not. Signs: Terraform could not download a module or a
  provider (""Failed to download module"", git clone or ls-remote failing, ""could not resolve host"", TLS, certificate or
  authentication errors from a git host or registry); provider credential or API errors (401, 403, invalid credentials, connection
  refused or timed out reaching the hypervisor or cloud); quota or capacity limits; a module, provider or terraform block whose
  required_version the workspace's Terraform version does not meet (""Unsupported Terraform Core version""); or syntax the
  workspace's Terraform is too old for, such as ""Reserved argument name in module block"" under Terraform 0.12, which means a
  module block uses count, for_each or depends_on, which need Terraform 0.13 or later. Say plainly in reply that the code is not the
  problem and what is. Propose the fix Caster can make, usually setting the workspace's Terraform version to an available version
  that meets the requirement (the lowest such version unless the planner says otherwise), and say what the planner or an
  administrator must fix outside Caster: network access to a git host, credentials, a Terraform version Caster does not have. Do not
  rewrite working code to get around it, and do not remove count, for_each or depends_on to suit an old Terraform.
- Code: a syntax error, an undeclared variable, an unknown reference, a duplicate name, a wrong argument or type. Fix it in the
  editable files with the smallest change, and say in reply what was wrong.
- If the output does not show the cause, say what it shows and what to check; do not guess a fix.";

    public static string BuildUserPrompt(string prompt, IReadOnlyList<AiAssistMessage> history, AiAssistState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Directory: {state.DirectoryName}");
        sb.AppendLine($"Terraform versions Caster has: {string.Join(", ", state.Versions.DefaultIfEmpty("none listed"))}." +
            (string.IsNullOrEmpty(state.DefaultVersion) ? "" : $" Default: {state.DefaultVersion}."));
        sb.Append(AiTerraformDraftBuilder.ProviderEnvironmentSection(state.ProviderVariables));

        sb.AppendLine().AppendLine(state.Workspaces.Count == 0 ? "The directory has no workspaces." : "Workspaces:");
        foreach (var workspace in state.Workspaces)
        {
            var version = string.IsNullOrEmpty(workspace.TerraformVersion)
                ? $"not set, so Caster's default{(string.IsNullOrEmpty(state.DefaultVersion) ? "" : $" {state.DefaultVersion}")}"
                : workspace.TerraformVersion;
            sb.AppendLine($"- {workspace.Name}{(workspace.Id == state.WorkspaceId ? " (the planner is working in this one)" : "")}: Terraform version {version}");
            foreach (var file in workspace.Files.OrderBy(f => f.Name))
            {
                var lockedFor = file.Editable ? null : AiAssistState.WorkspaceFileLock(file);
                sb.AppendLine(file.Editable
                    ? $"  === {file.Name} (editable, this workspace's own file) ==="
                    : $"  === {file.Name} (read-only, this workspace's own file: {lockedFor}) ===");
                sb.AppendLine(Indent(AiAssistState.Shorten(AiTerraformDraftBuilder.ForModel(file.Content) ?? "", AiAssistState.MaxWorkspaceFileCharacters)));
            }
            if (workspace.LastRun is AiAssistRun run)
            {
                sb.AppendLine($"  Latest run: {(run.IsDestroy ? "destroy" : "plan")} {run.CreatedAt:yyyy-MM-dd HH:mm} UTC, {Status(run)}");
                // only a failed run keeps its output; an apply's says more than the plan before it
                if (!string.IsNullOrEmpty(run.ApplyOutput))
                    AppendOutput(sb, "Apply output", run.ApplyOutput);
                else if (!string.IsNullOrEmpty(run.PlanOutput))
                    AppendOutput(sb, "Plan output", run.PlanOutput);
            }
            else if (!state.WorkspaceId.HasValue || workspace.Id == state.WorkspaceId)
            {
                sb.AppendLine("  No runs yet.");
            }
            if (workspace.LastFailedRun is AiAssistRun failedRun)
            {
                sb.AppendLine($"  Earlier failed run, not resolved by an apply since: {(failedRun.IsDestroy ? "destroy" : "plan")} {failedRun.CreatedAt:yyyy-MM-dd HH:mm} UTC, {Status(failedRun)}");
                if (!string.IsNullOrEmpty(failedRun.ApplyOutput))
                    AppendOutput(sb, "Apply output", failedRun.ApplyOutput);
                else if (!string.IsNullOrEmpty(failedRun.PlanOutput))
                    AppendOutput(sb, "Plan output", failedRun.PlanOutput);
            }
        }

        if (state.Designs.Count > 0)
        {
            sb.AppendLine().AppendLine("Enabled Designs that add modules to every workspace (read-only):");
            foreach (var design in state.Designs)
                sb.AppendLine($"- {design}");
        }

        sb.AppendLine().AppendLine(state.Files.Count == 0 ? "The directory and its parents have no files yet." : "Files:");
        foreach (var file in state.Files.OrderByDescending(f => f.Editable).ThenBy(f => f.DirectoryName).ThenBy(f => f.Name))
        {
            var kind = file.Editable ? "editable, this directory" : $"read-only, from {file.DirectoryName}";
            if (AiTerraformDraftBuilder.HoldsLiteralSecret(file.Content))
                kind += "; credential values are hidden";
            if (state.Trimmed.Contains(file.Id))
                kind += ", not shown in full";
            sb.AppendLine().AppendLine($"=== {file.Name} ({kind}) ===");
            sb.AppendLine(AiTerraformDraftBuilder.ForModel(file.Content) ?? "");
        }

        var recent = (history ?? []).Where(m => !string.IsNullOrWhiteSpace(m?.Text)).TakeLast(MaxHistoryMessages).ToList();
        if (recent.Count > 0)
        {
            sb.AppendLine().AppendLine("Conversation so far:");
            foreach (var message in recent)
                sb.AppendLine($"{(message.Role == "assistant" ? "Assistant" : "Planner")}: {AiAssistState.Shorten(message.Text.Trim(), MaxHistoryCharacters)}");
        }
        sb.AppendLine().AppendLine("Planner's request:").AppendLine(prompt.Trim());
        return sb.ToString();
    }

    private static string Status(AiAssistRun run)
    {
        if (run.PlanStatus == PlanStatus.Failed)
            return "the plan failed";
        if (run.ApplyStatus is ApplyStatus.Failed or ApplyStatus.Failed_StateError)
            return "the apply failed";
        return run.Status switch
        {
            RunStatus.Failed or RunStatus.Failed_StateError => "failed",
            RunStatus.Applied => "applied",
            RunStatus.Planned => "planned, not applied",
            RunStatus.Rejected => "rejected",
            _ => run.Status.ToString().ToLowerInvariant()
        };
    }

    private static void AppendOutput(StringBuilder sb, string title, string output)
    {
        // a failed plan quotes the source line it failed on, so a credential in a file would reach the model through here
        var text = AiTerraformDraftBuilder.RedactText(Ansi.Replace(output, "")).TrimEnd();
        var cut = text.Length > AiAssistState.MaxOutputCharacters;
        sb.AppendLine($"  {title}{(cut ? $" (the last {AiAssistState.MaxOutputCharacters} characters)" : "")}:");
        sb.AppendLine(Indent(cut ? text[^AiAssistState.MaxOutputCharacters..] : text));
    }

    private static string Indent(string text) => string.Join("\n", text.Split('\n').Select(l => "    " + l));

    /// <summary>
    /// The tool the model answers with. Strict tool use compiles it to a grammar Bedrock limits by the number of
    /// free-text fields, so it has four; the rest are enums.
    /// </summary>
    public static JsonObject BuildSchema(AiAssistState state)
    {
        var canSetVersion = state.Workspaces.Count > 0 && state.Versions.Length > 0;
        var canEditWorkspaceFile = state.Workspaces.Any(w => w.Files.Any(f => f.Editable));
        var ops = new List<string> { AiAssistOps.FileAdd, AiAssistOps.FileEdit };
        if (canEditWorkspaceFile)
            ops.Add(AiAssistOps.WorkspaceFileEdit);
        if (canSetVersion)
            ops.Add(AiAssistOps.WorkspaceVersion);
        var change = new JsonObject
        {
            ["op"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(ops) },
            ["file"] = new JsonObject { ["type"] = "string", ["description"] = "For a file change, the file's name, e.g. network.tf. For workspace.file.edit, the workspace file's name." },
            ["content"] = new JsonObject { ["type"] = "string", ["description"] = "For a file change, the file's complete new content." }
        };
        if (canSetVersion || canEditWorkspaceFile)
            change["workspace"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(state.Workspaces.Select(w => w.Name)), ["description"] = "For workspace.version and workspace.file.edit, the workspace." };
        if (canSetVersion)
        {
            change["version"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(state.Versions), ["description"] = "For workspace.version, the Terraform version to set." };
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["reply"] = new JsonObject { ["type"] = "string", ["description"] = "A short reply to the planner: what you found, what you propose and why." },
                ["question"] = new JsonObject { ["type"] = "string", ["description"] = "One short question to ask instead of proposing changes; empty when proposing changes." },
                ["cause"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(AiAssistCauses.All), ["description"] = "For a failed plan or apply the planner asked about, whether the code or the environment caused it; none otherwise." },
                ["changes"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = change,
                        ["required"] = new JsonArray("op")
                    }
                }
            },
            ["required"] = new JsonArray("reply", "cause", "changes")
        };

        static JsonArray Enum(IEnumerable<string> values) => new(values.Distinct().Select(v => (JsonNode)v).ToArray());
    }

    /// <summary>Reads the model's answer into a result whose changes have been checked and labelled</summary>
    public static AiAssistResult Read(JsonElement input, AiAssistState state)
    {
        var result = new AiAssistResult
        {
            Reply = GetString(input, "reply"),
            Question = GetString(input, "question"),
            Cause = GetString(input, "cause") is string cause && AiAssistCauses.All.Contains(cause) ? cause : AiAssistCauses.None
        };
        var proposed = new List<AiAssistChange>();
        if (input.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in changes.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object))
            {
                proposed.Add(new AiAssistChange
                {
                    Op = GetString(item, "op"),
                    FileName = GetString(item, "file"),
                    Content = item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() : null,
                    WorkspaceName = GetString(item, "workspace"),
                    TerraformVersion = GetString(item, "version")
                });
            }
        }
        // a question comes before any change
        result.Changes = string.IsNullOrEmpty(result.Question) ? Propose(state, proposed, result.Warnings) : [];
        if (result.Changes.Count > 0)
        {
            var drafted = result.Changes
                .Where(c => c.Op is AiAssistOps.FileAdd or AiAssistOps.FileEdit)
                .Select(c => new AiTerraformFile { Name = c.FileName, Content = c.Content, ExistingFileId = c.FileId })
                .ToList();
            // a workspace's file is checked for what it sets, but declares nothing to clash with the directory's
            var workspaceDrafted = result.Changes
                .Where(c => c.Op == AiAssistOps.WorkspaceFileEdit)
                .Select(c => new AiTerraformFile { Name = c.FileName, Content = c.Content, ExistingFileId = c.FileId })
                .ToList();
            result.Warnings.AddRange(drafted.Concat(workspaceDrafted).SelectMany(AiTerraformDraftBuilder.Problems));
            result.Warnings.AddRange(AiTerraformDraftBuilder.DuplicateDeclarations(state.Files, drafted));
        }
        return result;
    }

    /// <summary>
    /// Matches each proposed change to the Directory: a file by its name, which decides whether it is added or replaced,
    /// a workspace by its name. Drops, with a warning, what cannot be applied; labels the rest.
    /// </summary>
    public static List<AiAssistChange> Propose(AiAssistState state, IEnumerable<AiAssistChange> proposed, List<string> warnings)
    {
        var kept = new List<AiAssistChange>();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workspaces = new HashSet<Guid>();
        var workspaceFiles = new HashSet<Guid>();
        foreach (var change in proposed)
        {
            string problem;
            switch (change.Op)
            {
                case AiAssistOps.FileAdd:
                case AiAssistOps.FileEdit:
                    problem = ProposeFile(state, change);
                    if (problem == null && !files.Add(change.FileName))
                        problem = "it changes the same file twice";
                    break;
                case AiAssistOps.WorkspaceFileEdit:
                    problem = ProposeWorkspaceFile(state, change);
                    if (problem == null && !workspaceFiles.Add(change.FileId.Value))
                        problem = "it changes the same file twice";
                    break;
                case AiAssistOps.WorkspaceVersion:
                    problem = ProposeVersion(state, change);
                    if (problem == null && !workspaces.Add(change.WorkspaceId.Value))
                        problem = "it sets the same workspace's version twice";
                    break;
                default:
                    change.Label = change.Op ?? "A change";
                    problem = "Ask AI cannot make this kind of change";
                    break;
            }
            if (problem == null)
                kept.Add(change);
            else
                warnings.Add($"{change.Label}: left out, {problem}.");
        }
        return kept;
    }

    private static string ProposeFile(AiAssistState state, AiAssistChange change)
    {
        var name = change.FileName?.Trim();
        change.FileName = name;
        change.Label = $"{(change.Op == AiAssistOps.FileEdit ? "Edit" : "Add")} {name ?? "a file"}";
        if (!AiTerraformDraftBuilder.IsValidFileName(name))
            return "file names may only use letters, digits, dots, underscores and dashes";
        if (change.Content == null)
            return "it has no content";

        // this Directory's own file that sets a credential is shown without its value, so no change to it is taken
        if (state.Files.FirstOrDefault(f => f.ReadOnlyReason != null && f.Name == name) is { } locked)
            return $"{name} {locked.ReadOnlyReason}, so it cannot be changed here";
        // the name decides: a file of this Directory is replaced, any other name is added
        var existing = state.Editable.SingleOrDefault(f => f.Name == name);
        if (existing == null)
        {
            if (state.Files.Any(f => !f.Editable && f.Name == name) && change.Op == AiAssistOps.FileEdit)
                return $"{name} belongs to the parent directory {state.Files.First(f => !f.Editable && f.Name == name).DirectoryName}; open Ask AI on that directory to change it";
            change.Op = AiAssistOps.FileAdd;
            change.FileId = null;
            change.ExistingContent = null;
            change.ExistingContentHash = null;
            change.Label = Label(change);
            return null;
        }
        change.Op = AiAssistOps.FileEdit;
        change.Label = Label(change);
        if (state.Trimmed.Contains(existing.Id))
            return $"{name} is too large to show in full, so it cannot be changed here";
        if (existing.Content == change.Content)
            return "the file is unchanged";
        change.FileId = existing.Id;
        change.ExistingContent = existing.Content;
        change.ExistingContentHash = AiTerraformDraftBuilder.ContentHash(existing.Content);
        return null;
    }

    private static string ProposeWorkspaceFile(AiAssistState state, AiAssistChange change)
    {
        var name = change.FileName?.Trim();
        change.FileName = name;
        var workspace = state.Workspaces.SingleOrDefault(w => w.Name == change.WorkspaceName);
        change.WorkspaceId = workspace?.Id;
        change.Label = Label(change);
        if (workspace == null)
            return $"the directory has no workspace named {change.WorkspaceName}";
        var existing = workspace.Files.SingleOrDefault(f => f.Name == name);
        if (existing == null)
            return $"{workspace.Name} has no file named {name}; Ask AI changes a workspace's files that exist, and does not add one";
        if (AiAssistState.WorkspaceFileLock(existing) is string reason)
            return $"{name} cannot be changed here: {reason}";
        if (change.Content == null)
            return "it has no content";
        if (existing.Content == change.Content)
            return "the file is unchanged";
        change.FileId = existing.Id;
        change.ExistingContent = existing.Content;
        change.ExistingContentHash = AiTerraformDraftBuilder.ContentHash(existing.Content);
        return null;
    }

    private static string ProposeVersion(AiAssistState state, AiAssistChange change)
    {
        var workspace = state.Workspaces.SingleOrDefault(w => w.Name == change.WorkspaceName);
        change.WorkspaceId = workspace?.Id;
        change.ExistingTerraformVersion = workspace?.TerraformVersion;
        change.Label = Label(change);
        if (workspace == null)
            return $"the directory has no workspace named {change.WorkspaceName}";
        if (!state.Versions.Contains(change.TerraformVersion))
            return $"Caster has no Terraform {change.TerraformVersion}";
        if (workspace.TerraformVersion == change.TerraformVersion)
            return $"{workspace.Name} already uses Terraform {change.TerraformVersion}";
        return null;
    }

    /// <summary>What a change does, in words, from the change itself and never from the model or the caller</summary>
    public static string Label(AiAssistChange change) => change.Op switch
    {
        AiAssistOps.FileAdd => $"Add {change.FileName}",
        AiAssistOps.FileEdit => $"Edit {change.FileName}",
        AiAssistOps.WorkspaceFileEdit => $"Edit {change.FileName} in {change.WorkspaceName ?? "a workspace"}",
        AiAssistOps.WorkspaceVersion => $"Set {change.WorkspaceName ?? "a workspace"} to Terraform {change.TerraformVersion}" +
            (string.IsNullOrEmpty(change.ExistingTerraformVersion) ? " (now Caster's default)" : $" (now {change.ExistingTerraformVersion})"),
        _ => change.Op ?? "A change"
    };

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString().Trim()
            : null;
}
