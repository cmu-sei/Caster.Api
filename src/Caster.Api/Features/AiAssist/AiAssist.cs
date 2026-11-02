// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;

namespace Caster.Api.Features.AiAssist;

/// <summary>The kinds of change Ask AI can propose</summary>
public static class AiAssistOps
{
    public const string FileAdd = "file.add";
    public const string FileEdit = "file.edit";
    public const string WorkspaceVersion = "workspace.version";

    public static readonly string[] All = [FileAdd, FileEdit, WorkspaceVersion];
}

/// <summary>What caused the failed plan or apply the planner asked about</summary>
public static class AiAssistCauses
{
    // nothing failed, or the planner did not ask about a failure
    public const string None = "none";
    // the Terraform is wrong
    public const string Code = "code";
    // the code is fine; the range around it is not: module downloads, credentials, the Terraform version
    public const string Environment = "environment";

    public static readonly string[] All = [None, Code, Environment];
}

/// <summary>One earlier message of the Ask AI conversation, resent with each request</summary>
public class AiAssistMessage
{
    /// <summary>planner or assistant</summary>
    public string Role { get; set; }
    public string Text { get; set; }
}

/// <summary>
/// One change Ask AI proposes: a file to add, a file to replace, or a workspace's Terraform version to set.
/// Applying it sends it back as it was proposed; Caster checks it again against the Directory as it is then.
/// </summary>
public class AiAssistChange
{
    /// <summary>file.add, file.edit or workspace.version</summary>
    public string Op { get; set; }

    /// <summary>What the change does, written by Caster from the change itself</summary>
    public string Label { get; set; }

    /// <summary>For file.edit, the Directory's file to replace</summary>
    public Guid? FileId { get; set; }

    public string FileName { get; set; }

    /// <summary>For a file, its complete new content</summary>
    public string Content { get; set; }

    /// <summary>For file.edit, the file's content when the change was proposed, to show what changes; not read when applied</summary>
    public string ExistingContent { get; set; }

    /// <summary>For file.edit, the hash of the content the change was proposed against. The file is not replaced if it has changed since.</summary>
    public string ExistingContentHash { get; set; }

    /// <summary>For workspace.version, the workspace</summary>
    public Guid? WorkspaceId { get; set; }

    public string WorkspaceName { get; set; }

    /// <summary>For workspace.version, the version to set</summary>
    public string TerraformVersion { get; set; }

    /// <summary>For workspace.version, the workspace's version when the change was proposed; empty for Caster's default</summary>
    public string ExistingTerraformVersion { get; set; }
}

/// <summary>Ask AI's answer: a reply, or one question, and the changes it proposes. Nothing is saved.</summary>
public class AiAssistResult
{
    public string Reply { get; set; }

    /// <summary>A question the model asks instead of proposing changes</summary>
    public string Question { get; set; }

    /// <summary>
    /// For a failed plan or apply: code when the Terraform is wrong, environment when the code is fine and the range around
    /// it is not (a module it cannot download, credentials, the workspace's Terraform version); none otherwise
    /// </summary>
    public string Cause { get; set; } = AiAssistCauses.None;

    public List<AiAssistChange> Changes { get; set; } = [];

    /// <summary>Changes left out, and what to check in the ones kept</summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>What came of applying one change</summary>
public class AiAssistChangeResult
{
    public string Op { get; set; }
    public string Label { get; set; }
    public bool Applied { get; set; }

    /// <summary>Why it was not applied</summary>
    public string Error { get; set; }

    /// <summary>The file added or replaced</summary>
    public Guid? FileId { get; set; }

    /// <summary>The workspace changed</summary>
    public Guid? WorkspaceId { get; set; }
}

public class AiAssistApplyResult
{
    public List<AiAssistChangeResult> Results { get; set; } = [];
}
