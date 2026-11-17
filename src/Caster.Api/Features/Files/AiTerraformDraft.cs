// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Caster.Api.Features.Designs;

namespace Caster.Api.Features.Files;

/// <summary>
/// Terraform files drafted by AI for one Directory. Nothing is saved until the reviewed
/// draft is sent to ApplyAiTerraformDraft.
/// </summary>
public class AiTerraformDraft
{
    public string Summary { get; set; }
    public List<AiTerraformFile> Files { get; set; } = [];

    /// <summary>
    /// What was dropped or changed from the model's answer, and what still needs a designer's attention.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

public class AiTerraformFile
{
    public string Name { get; set; }
    public string Purpose { get; set; }
    public string Content { get; set; }

    /// <summary>
    /// The Directory's file this one replaces; empty for a new file.
    /// </summary>
    public Guid? ExistingFileId { get; set; }

    /// <summary>
    /// The replaced file's content when the draft was written, to show the change.
    /// </summary>
    public string ExistingContent { get; set; }

    /// <summary>
    /// A hash of ExistingContent. Applying refuses a file whose content no longer matches it.
    /// </summary>
    public string ExistingContentHash { get; set; }
}

/// <summary>
/// A file the model is shown: the Directory's own, which it may change, or one it only reads.
/// </summary>
public record AiContextFile(Guid Id, string Name, string Content, string DirectoryName, bool Editable);

/// <summary>
/// Builds the prompt and tool schema for drafting raw Terraform in a Directory, and checks the
/// model's answer: file names, files it may replace, and problems Terraform would only find at plan.
/// </summary>
public static class AiTerraformDraftBuilder
{
    public const string ToolName = "draft_terraform";
    public const string ToolDescription = "Return the Terraform files to create or replace in the directory, each with its complete content.";

    // what the model is shown of the workspace; past this the draft is refused rather than truncated
    public const int MaxContextCharacters = 400_000;

    private static readonly Regex FileName = new(@"^[A-Za-z0-9_][A-Za-z0-9._-]{0,254}$");

    // a top-level block that declares a name Terraform requires to be unique in the root module
    private static readonly Regex Declaration = new(
        @"^\s*(variable|output|module|provider)\s+""([^""]+)""|^\s*(resource|data)\s+""([^""]+)""\s+""([^""]+)""",
        RegexOptions.Multiline);

    // a credential-like argument set to a literal string rather than a variable
    private static readonly Regex LiteralSecret = new(
        @"^\s*([A-Za-z0-9_]*(password|secret|token|api_key|access_key|private_key)[A-Za-z0-9_]*)\s*=\s*""(?!\$\{)[^""]{4,}""",
        RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public static string BuildSystemPrompt(IReadOnlyList<AiCatalogModule> catalog)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You write Terraform for Caster, the infrastructure tool of the Crucible cyber range.");
        sb.AppendLine($"Answer only by calling the {ToolName} tool, once.");
        sb.AppendLine();
        sb.AppendLine("How Caster runs Terraform:");
        sb.AppendLine("- Each workspace of a directory is one Terraform root module: the directory's files, the files of every parent directory,");
        sb.AppendLine("  and the modules of enabled Designs, all joined together. A name declared twice anywhere in that set fails the plan.");
        sb.AppendLine("- Parent directories usually hold the provider and terraform blocks and shared variables. Reuse them; do not declare them again.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Return the complete content of every file you create or change. Leave out files you do not change.");
        sb.AppendLine("- You may only change the files marked editable. To change something in a read-only file, say so in notes.");
        sb.AppendLine("- File names: letters, digits, dots, underscores and dashes, ending in .tf (or .tfvars, .tftpl, .sh, .yaml where needed).");
        sb.AppendLine("- Never write credentials, tokens or keys. Declare a variable (sensitive = true) and say in notes what must be set.");
        sb.AppendLine("- Use a catalog module, with the source given, when one does what is asked. Otherwise write resources directly.");
        sb.AppendLine("- Use names for templates, networks, datastores and images that the context already uses. If you must guess one, make it a variable and say so in notes.");
        sb.AppendLine("- Use fictional addressing (RFC 1918 and RFC 5737 ranges) unless the context already sets the addresses.");
        sb.AppendLine("- Write HCL a person can maintain: one purpose per file, comments only where intent is not obvious.");
        sb.AppendLine("- In the summary, say what you changed and what the designer still has to decide.");
        if (catalog.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Module catalog (highest version of each):");
            foreach (var entry in catalog)
            {
                sb.AppendLine();
                sb.AppendLine($"## {entry.Module.Name}: source = \"git::{entry.Version.UrlLink}?ref={entry.Version.Name}\"");
                if (!string.IsNullOrWhiteSpace(entry.Module.Description))
                    sb.AppendLine(entry.Module.Description.Trim());
                foreach (var v in entry.Version.Variables ?? [])
                {
                    var need = v.IsOptional ? $"optional, default {v.DefaultValue}" : "required";
                    sb.AppendLine($"- variable {v.Name} ({v.VariableType ?? "any"}, {need}): {v.Description}");
                }
                foreach (var o in entry.Version.Outputs ?? [])
                    sb.AppendLine($"- output {o.Name}: {o.Description}");
            }
        }
        return sb.ToString();
    }

    public static string BuildUserPrompt(string prompt, IReadOnlyList<AiContextFile> files, IReadOnlyList<string> designs, AiContextFile focus)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Request:");
        sb.AppendLine(prompt.Trim());
        if (focus != null)
        {
            sb.AppendLine();
            sb.AppendLine($"The designer is editing {focus.Name}. Change that file; change others only if the request needs it.");
        }

        if (designs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Enabled Designs that add modules to every workspace (read-only):");
            foreach (var design in designs)
                sb.AppendLine($"- {design}");
        }

        sb.AppendLine();
        sb.AppendLine(files.Count == 0 ? "The directory and its parents have no files yet." : "Files:");
        foreach (var file in files.OrderBy(f => f.Editable).ThenBy(f => f.DirectoryName).ThenBy(f => f.Name))
        {
            sb.AppendLine();
            sb.AppendLine($"=== {file.Name} ({(file.Editable ? "editable, this directory" : $"read-only, from {file.DirectoryName}")}) ===");
            sb.AppendLine(file.Content ?? "");
        }
        return sb.ToString();
    }

    public static JsonObject BuildToolSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["summary"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "What the files do and what the designer still has to decide."
            },
            ["files"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Each file to create, or editable file to replace, with its complete content.",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["name"] = new JsonObject { ["type"] = "string", ["description"] = "File name, e.g. network.tf" },
                        ["purpose"] = new JsonObject { ["type"] = "string", ["description"] = "One line on what this file holds." },
                        ["content"] = new JsonObject { ["type"] = "string", ["description"] = "The complete file content." }
                    },
                    ["required"] = new JsonArray("name", "content")
                }
            },
            ["notes"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Variables to set, guesses made, and changes needed in read-only files.",
                ["items"] = new JsonObject { ["type"] = "string" }
            }
        },
        ["required"] = new JsonArray("summary", "files")
    };

    /// <summary>
    /// Turns the model's answer into a draft: drops files it may not write, matches the rest to the
    /// Directory's files, and warns about what the plan would reject.
    /// </summary>
    public static AiTerraformDraft Review(JsonElement input, IReadOnlyList<AiContextFile> files)
    {
        var draft = new AiTerraformDraft { Summary = GetString(input, "summary") };
        var editable = files.Where(f => f.Editable).ToDictionary(f => f.Name, StringComparer.Ordinal);
        var readOnly = files.Where(f => !f.Editable).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (input.TryGetProperty("files", out var answered) && answered.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in answered.EnumerateArray())
            {
                var name = GetString(item, "name")?.Trim();
                var content = GetString(item, "content") ?? "";
                if (string.IsNullOrEmpty(name) || !FileName.IsMatch(name) || name.Contains(".."))
                {
                    draft.Warnings.Add($"Dropped a file named '{name}': names may only use letters, digits, dots, underscores and dashes.");
                    continue;
                }
                if (!names.Add(name))
                {
                    draft.Warnings.Add($"Dropped the second file named {name}.");
                    continue;
                }

                var file = new AiTerraformFile { Name = name, Purpose = GetString(item, "purpose"), Content = content };
                if (editable.TryGetValue(name, out var existing))
                {
                    if (existing.Content == content)
                    {
                        draft.Warnings.Add($"{name} is unchanged, so it was left out.");
                        continue;
                    }
                    file.ExistingFileId = existing.Id;
                    file.ExistingContent = existing.Content;
                    file.ExistingContentHash = ContentHash(existing.Content);
                }
                else if (readOnly.Contains(name))
                {
                    draft.Warnings.Add($"{name} is also the name of a parent directory's file; this directory's copy is used alongside it, not instead of it.");
                }

                draft.Warnings.AddRange(Problems(file));
                draft.Files.Add(file);
            }
        }

        if (input.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Array)
            draft.Warnings.AddRange(notes.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()));

        if (draft.Files.Count == 0)
            draft.Warnings.Add("The draft changes no files.");

        draft.Warnings.AddRange(DuplicateDeclarations(files, draft.Files));
        return draft;
    }

    public static string ContentHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content ?? "")));

    private static IEnumerable<string> Problems(AiTerraformFile file)
    {
        if (!IsHcl(file.Name))
            yield break;
        if (!Balanced(file.Content))
            yield return $"{file.Name}: its braces, brackets or quotes do not balance, so the plan will fail to parse it.";
        foreach (Match m in LiteralSecret.Matches(file.Content))
            yield return $"{file.Name}: {m.Groups[1].Value} is set to a literal value. Use a sensitive variable instead.";
    }

    /// <summary>
    /// Names declared more than once in the workspace the draft produces: the draft's files, plus every
    /// context file the draft does not replace.
    /// </summary>
    public static IEnumerable<string> DuplicateDeclarations(IReadOnlyList<AiContextFile> files, IReadOnlyList<AiTerraformFile> drafted)
    {
        var replaced = drafted.Where(d => d.ExistingFileId.HasValue).Select(d => d.ExistingFileId.Value).ToHashSet();
        var sources = files
            .Where(f => !replaced.Contains(f.Id) && IsHcl(f.Name))
            .Select(f => (Label: f.Editable ? f.Name : $"{f.DirectoryName}/{f.Name}", f.Content))
            .Concat(drafted.Where(d => IsHcl(d.Name)).Select(d => (Label: d.Name, d.Content)));

        return sources
            .SelectMany(s => Declaration.Matches(StripComments(s.Content)).Select(m => (Name: DeclaredName(m), s.Label)))
            .GroupBy(d => d.Name)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} is declared more than once ({string.Join(", ", g.Select(d => d.Label))}); the plan will fail.");
    }

    private static string DeclaredName(Match m) =>
        m.Groups[1].Success ? $"{m.Groups[1].Value} \"{m.Groups[2].Value}\"" : $"{m.Groups[3].Value} \"{m.Groups[4].Value}\" \"{m.Groups[5].Value}\"";

    private static bool IsHcl(string name) =>
        name.EndsWith(".tf", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tfvars", StringComparison.OrdinalIgnoreCase);

    // drops comments and string contents so a commented-out or quoted block is not counted as declared
    private static string StripComments(string content) =>
        Regex.Replace(content ?? "", @"/\*.*?\*/|(#|//)[^\n]*", "", RegexOptions.Singleline);

    /// <summary>
    /// Whether braces, brackets and parentheses balance outside strings, comments and heredocs.
    /// Interpolations inside strings are skipped whole.
    /// </summary>
    public static bool Balanced(string content)
    {
        var stack = new Stack<char>();
        var lines = (content ?? "").Replace("\r\n", "\n").Split('\n');
        string heredoc = null;
        var inBlockComment = false;
        foreach (var line in lines)
        {
            if (heredoc != null)
            {
                if (line.Trim() == heredoc)
                    heredoc = null;
                continue;
            }
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inBlockComment)
                {
                    if (c == '*' && i + 1 < line.Length && line[i + 1] == '/') { inBlockComment = false; i++; }
                    continue;
                }
                if (c == '#' || (c == '/' && i + 1 < line.Length && line[i + 1] == '/'))
                    break;
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '*') { inBlockComment = true; i++; continue; }
                if (c == '<' && i + 1 < line.Length && line[i + 1] == '<')
                {
                    var marker = Regex.Match(line[(i + 2)..], @"^-?([A-Za-z_][A-Za-z0-9_]*)\s*$");
                    if (marker.Success) { heredoc = marker.Groups[1].Value; break; }
                }
                if (c == '"')
                {
                    // a string ends at the next unescaped quote outside an interpolation
                    var depth = 0;
                    for (i++; i < line.Length; i++)
                    {
                        if (line[i] == '\\') { i++; continue; }
                        if (line[i] == '$' && i + 1 < line.Length && line[i + 1] == '{') { depth++; i++; continue; }
                        if (line[i] == '}' && depth > 0) { depth--; continue; }
                        if (line[i] == '"' && depth == 0) break;
                    }
                    if (i >= line.Length)
                        return false;
                    continue;
                }
                if (c is '{' or '[' or '(')
                    stack.Push(c);
                else if (c is '}' or ']' or ')')
                {
                    var open = c == '}' ? '{' : c == ']' ? '[' : '(';
                    if (stack.Count == 0 || stack.Pop() != open)
                        return false;
                }
            }
        }
        return stack.Count == 0 && heredoc == null && !inBlockComment;
    }

    private static string GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
