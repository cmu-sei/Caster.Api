// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Caster.Api.Domain.Services;
using Caster.Api.Features.AiAssist;
using Caster.Api.Features.Files;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Tests.Features.AiAssist;

/// <summary>
/// The pure parts of Ask AI: the tool schema strict tool use is asked with, and the review a proposal goes through
/// before the planner sees it.
/// </summary>
[Trait("Category", "Unit")]
public class AiAssistBuilderTests
{
    private static readonly Guid MainId = Guid.NewGuid();
    private static readonly Guid BigId = Guid.NewGuid();

    private static AiAssistState State() => new()
    {
        DirectoryId = Guid.NewGuid(),
        DirectoryName = "lab",
        Files =
        [
            new AiContextFile(MainId, "main.tf", "# main\n", "lab", true),
            new AiContextFile(BigId, "big.tf", "# cut", "lab", true),
            new AiContextFile(Guid.NewGuid(), "provider.tf", "# provider\n", "range", false)
        ],
        Trimmed = [BigId],
        Workspaces = [new AiAssistWorkspace(Guid.NewGuid(), "dev", null, [], null)],
        Versions = ["0.12.29", "1.5.7"],
        DefaultVersion = "0.12.29"
    };

    /// <summary>
    /// Bedrock refuses a strict schema whose grammar is too large, and free-text fields are what make it large: the
    /// schema keeps them to the reply, the question and a file's name and content, and closes every object.
    /// </summary>
    [Fact]
    public void The_schema_has_few_free_text_fields_and_is_closed_for_strict_tool_use()
    {
        var strict = BedrockAiModelClient.StrictSchema(AiAssistBuilder.BuildSchema(State()));

        Assert.Equal(4, FreeText(strict));
        Assert.False(strict["additionalProperties"].GetValue<bool>());
        Assert.False(strict["properties"]["changes"]["items"]["additionalProperties"].GetValue<bool>());
        Assert.Equal(["dev"], strict["properties"]["changes"]["items"]["properties"]["workspace"]["enum"].AsArray().Select(v => v.GetValue<string>()));
    }

    [Fact]
    public void A_directory_without_workspaces_is_offered_no_version_change()
    {
        var state = State();
        state.Workspaces.Clear();

        var schema = AiAssistBuilder.BuildSchema(state);

        var change = schema["properties"]["changes"]["items"]["properties"].AsObject();
        Assert.Equal([AiAssistOps.FileAdd, AiAssistOps.FileEdit], change["op"]["enum"].AsArray().Select(v => v.GetValue<string>()));
        Assert.False(change.ContainsKey("version"));
    }

    [Fact]
    public void Ask_AI_is_asked_with_strict_tool_use_and_the_drafts_are_not()
    {
        var options = new AiOptions { ModelId = "m", Models = new() { [AiTasks.Design] = new AiTaskModel { Strict = true } } };

        var assist = BedrockAiModelClient.BuildBody(options.ModelFor(AiTasks.Assist), "s", "p", "t", "d", AiAssistBuilder.BuildSchema(State()));
        var terraform = BedrockAiModelClient.BuildBody(options.ModelFor(AiTasks.Terraform), "s", "p", "t", "d", AiTerraformDraftBuilder.BuildToolSchema());

        Assert.True(assist["tools"][0]["strict"].GetValue<bool>());
        Assert.False(assist["tools"][0]["input_schema"]["additionalProperties"].GetValue<bool>());
        Assert.False(terraform["tools"][0].AsObject().ContainsKey("strict"));
        // a task's own setting wins over its default
        Assert.True(options.ModelFor(AiTasks.Design).Strict);
    }

    /// <summary>
    /// The name decides whether a file is added or replaced; what cannot be applied is left out with the reason, and the
    /// labels are Caster's own.
    /// </summary>
    [Fact]
    public void Read_matches_files_by_name_and_leaves_out_what_cannot_be_applied()
    {
        var result = AiAssistBuilder.Read(Parse("""
            {"reply":"r","cause":"code","changes":[
              {"op":"file.add","file":"main.tf","content":"# main v2\n"},
              {"op":"file.edit","file":"new.tf","content":"# new\n"},
              {"op":"file.edit","file":"main.tf","content":"# again\n"},
              {"op":"file.edit","file":"big.tf","content":"# whole\n"},
              {"op":"file.edit","file":"../x.tf","content":"# bad\n"},
              {"op":"workspace.version","workspace":"dev","version":"2.0.0"},
              {"op":"file.delete","file":"main.tf"}]}
            """), State());

        Assert.Equal(AiAssistCauses.Code, result.Cause);
        Assert.Equal(["Edit main.tf", "Add new.tf"], result.Changes.Select(c => c.Label));
        Assert.Equal(MainId, result.Changes[0].FileId);
        Assert.Null(result.Changes[1].FileId);
        Assert.Contains("Edit main.tf: left out, it changes the same file twice.", result.Warnings);
        Assert.Contains(result.Warnings, w => w.StartsWith("Edit big.tf: left out, big.tf is too large to show in full"));
        Assert.Contains(result.Warnings, w => w.Contains("file names may only use"));
        Assert.Contains(result.Warnings, w => w.Contains("Caster has no Terraform 2.0.0"));
        Assert.Contains("file.delete: left out, Ask AI cannot make this kind of change.", result.Warnings);
    }

    [Fact]
    public void A_question_comes_before_any_change()
    {
        var result = AiAssistBuilder.Read(Parse("""
            {"reply":"r","question":"Which workspace?","cause":"none","changes":[{"op":"file.add","file":"a.tf","content":"# a"}]}
            """), State());

        Assert.Equal("Which workspace?", result.Question);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void The_prompt_resends_the_latest_turns_and_marks_a_cut_file()
    {
        var history = Enumerable.Range(1, 30).Select(i => new AiAssistMessage { Role = i % 2 == 0 ? "assistant" : "planner", Text = $"turn {i}" }).ToList();

        var prompt = AiAssistBuilder.BuildUserPrompt("now this", history, State());

        Assert.DoesNotContain("turn 10\n", prompt);
        Assert.Contains("Planner: turn 11", prompt);
        Assert.Contains("Assistant: turn 30", prompt);
        Assert.Contains("=== big.tf (editable, this directory, not shown in full) ===", prompt);
        Assert.Contains("- dev: Terraform version not set, so Caster's default 0.12.29", prompt);
        Assert.EndsWith("Planner's request:\nnow this\n", prompt.Replace("\r\n", "\n"));
    }

    private static int FreeText(JsonNode node) => node switch
    {
        JsonObject obj when obj["type"] is JsonValue type && type.GetValue<string>() == "string" && !obj.ContainsKey("enum") => 1,
        JsonObject obj => obj.Sum(p => FreeText(p.Value)),
        JsonArray array => array.Sum(FreeText),
        _ => 0
    };

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
