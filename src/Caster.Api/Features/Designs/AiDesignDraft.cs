// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Caster.Api.Domain.Models;
using Caster.Api.Features.DesignModules;

namespace Caster.Api.Features.Designs;

/// <summary>
/// A Design drafted by AI from the module catalog. Nothing is saved until the
/// reviewed draft is sent to CreateDesignFromAiDraft.
/// </summary>
public class AiDesignDraft
{
    public string DesignName { get; set; }
    public string Summary { get; set; }
    public List<AiDraftModule> Modules { get; set; } = [];

    /// <summary>
    /// What was dropped or changed from the model's answer, and what still needs a planner's attention.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// The Terraform the Design will generate once it is enabled.
    /// </summary>
    public string Snippet { get; set; }
}

public class AiDraftModule
{
    public Guid ModuleId { get; set; }
    public string ModuleName { get; set; }
    public string ModuleVersion { get; set; }
    public string Name { get; set; }
    public string Purpose { get; set; }
    public List<DesignModules.ModuleValue> Values { get; set; } = [];
    public string[] MissingRequiredVariables { get; set; } = [];
}

/// <summary>
/// The version of a Module offered to the model: the highest version of each Module.
/// </summary>
public record AiCatalogModule(Domain.Models.Module Module, ModuleVersion Version);

/// <summary>
/// Builds the prompt and tool schema for drafting a Design, and checks the model's answer
/// against the catalog. The model may only pick catalog modules and name their variables.
/// </summary>
public static class AiDesignDraftBuilder
{
    public const string ToolName = "draft_design";
    public const string ToolDescription = "Return the Caster design: catalog modules to instantiate, each with a unique name and values for its variables.";

    // module.<name>.<output>, with names as DesignModuleValidation.TerraformNamePattern allows
    private static readonly Regex ModuleReference = new(@"\bmodule\.([a-zA-Z_][a-zA-Z0-9_-]*)\.([a-zA-Z_][a-zA-Z0-9_-]*)");
    private static readonly Regex BareModuleReference = new(@"^module\.[a-zA-Z_][a-zA-Z0-9_-]*\.[a-zA-Z_][a-zA-Z0-9_-]*$");
    private static readonly Regex TerraformName = new(DesignModuleValidation.TerraformNamePattern);

    public static string BuildSystemPrompt(IReadOnlyList<AiCatalogModule> catalog)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You design Terraform topologies for Caster, the infrastructure tool of the Crucible cyber range.");
        sb.AppendLine($"Answer only by calling the {ToolName} tool, once.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Use only modules from the catalog below, by moduleId. Never invent a module, variable or output.");
        sb.AppendLine("- Give each module instance a unique name: letters, digits, underscores and dashes, starting with a letter or underscore.");
        sb.AppendLine("- Set every required variable. Leave optional variables out unless the request needs a non-default value.");
        sb.AppendLine("- For a variable of type string, give the plain text with no surrounding quotes. Caster adds the quotes.");
        sb.AppendLine("  To use another module's output in a string, interpolate it: ${module.<name>.<output>}.");
        sb.AppendLine("- For any other type, give an HCL expression: 2, true, [\"a\", \"b\"], { key = \"value\" }, or module.<name>.<output> directly.");
        sb.AppendLine("- Only reference outputs listed for a module you include, and never a module's own outputs.");
        sb.AppendLine("- Prefer the fewest modules that meet the request. Say in the summary what the planner still has to decide.");
        sb.AppendLine();
        sb.AppendLine("Catalog:");
        foreach (var entry in catalog)
        {
            sb.AppendLine();
            sb.AppendLine($"## {entry.Module.Name} (moduleId {entry.Module.Id}, version {entry.Version.Name})");
            if (!string.IsNullOrWhiteSpace(entry.Module.Description))
                sb.AppendLine(entry.Module.Description.Trim());
            sb.AppendLine("Variables:");
            foreach (var v in entry.Version.Variables ?? [])
            {
                var need = v.IsOptional ? $"optional, default {v.DefaultValue}" : "required";
                sb.AppendLine($"- {v.Name} ({v.VariableType ?? "any"}, {need}): {v.Description}");
            }
            sb.AppendLine("Outputs:");
            foreach (var o in entry.Version.Outputs ?? [])
                sb.AppendLine($"- {o.Name}: {o.Description}");
        }
        return sb.ToString();
    }

    public static string BuildUserPrompt(string prompt, string designName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Draft a design for this request:");
        sb.AppendLine(prompt.Trim());
        if (!string.IsNullOrWhiteSpace(designName))
            sb.AppendLine($"Call the design \"{designName.Trim()}\".");
        return sb.ToString();
    }

    /// <summary>
    /// One branch per catalog module, so a module's values may only name that module's variables.
    /// </summary>
    public static JsonObject BuildToolSchema(IReadOnlyList<AiCatalogModule> catalog)
    {
        var branches = new JsonArray();
        foreach (var entry in catalog)
        {
            var properties = new JsonObject();
            foreach (var v in entry.Version.Variables ?? [])
            {
                properties[v.Name] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = $"{v.VariableType ?? "any"}. {v.Description}".Trim()
                };
            }

            branches.Add(new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["moduleId"] = new JsonObject { ["const"] = entry.Module.Id.ToString() },
                    ["name"] = new JsonObject { ["type"] = "string", ["pattern"] = DesignModuleValidation.TerraformNamePattern },
                    ["purpose"] = new JsonObject { ["type"] = "string", ["description"] = "One sentence on why this module is in the design." },
                    ["values"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = properties,
                        ["required"] = new JsonArray((entry.Version.Variables ?? [])
                            .Where(v => !v.IsOptional)
                            .Select(v => (JsonNode)JsonValue.Create(v.Name))
                            .ToArray()),
                        ["additionalProperties"] = false
                    }
                },
                ["required"] = new JsonArray("moduleId", "name", "values")
            });
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["designName"] = new JsonObject { ["type"] = "string" },
                ["summary"] = new JsonObject { ["type"] = "string", ["description"] = "What the design builds and what the planner still has to decide." },
                ["modules"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["moduleId"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["enum"] = new JsonArray(catalog.Select(c => (JsonNode)JsonValue.Create(c.Module.Id.ToString())).ToArray())
                            }
                        },
                        ["anyOf"] = branches
                    }
                }
            },
            ["required"] = new JsonArray("designName", "summary", "modules")
        };
    }

    /// <summary>
    /// Keeps what fits the catalog and reports the rest. Unknown modules, unknown variables,
    /// bad names and repeated names are dropped; missing required variables and broken
    /// references are kept and warned about, for the planner to fix.
    /// </summary>
    public static AiDesignDraft Review(JsonElement input, IReadOnlyList<AiCatalogModule> catalog, string designName)
    {
        var draft = new AiDesignDraft
        {
            DesignName = !string.IsNullOrWhiteSpace(designName) ? designName.Trim() : GetString(input, "designName") ?? "AI draft",
            Summary = GetString(input, "summary")
        };
        var byId = catalog.ToDictionary(c => c.Module.Id);
        var kept = new List<(AiDraftModule Draft, ModuleVersion Version, List<Domain.Models.ModuleValue> Values)>();

        var items = input.TryGetProperty("modules", out var m) && m.ValueKind == JsonValueKind.Array
            ? m.EnumerateArray().ToList()
            : [];
        foreach (var item in items)
        {
            var moduleId = GetString(item, "moduleId");
            var name = GetString(item, "name");
            if (!Guid.TryParse(moduleId, out var id) || !byId.TryGetValue(id, out var entry))
            {
                draft.Warnings.Add($"Dropped '{name}': module {moduleId} is not in the catalog.");
                continue;
            }
            if (string.IsNullOrEmpty(name) || !TerraformName.IsMatch(name))
            {
                draft.Warnings.Add($"Dropped a {entry.Module.Name} module: '{name}' is not a valid name. {DesignModuleValidation.TerraformNameMessage}.");
                continue;
            }
            if (kept.Any(k => k.Draft.Name == name))
            {
                draft.Warnings.Add($"Dropped the second module named '{name}' ({entry.Module.Name}): names must be unique.");
                continue;
            }

            var values = new List<Domain.Models.ModuleValue>();
            if (item.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in vals.EnumerateObject())
                {
                    var variable = entry.Version.Variables.FirstOrDefault(v => v.Name == prop.Name);
                    if (variable == null)
                    {
                        draft.Warnings.Add($"{name}: dropped '{prop.Name}', which {entry.Module.Name} {entry.Version.Name} has no variable for.");
                        continue;
                    }
                    var value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.GetRawText();
                    value = Normalize(name, variable, value, draft.Warnings);
                    if (!string.IsNullOrEmpty(value))
                        values.Add(new Domain.Models.ModuleValue { Name = variable.Name, Value = value });
                }
            }

            var missing = entry.Version.MissingRequiredVariables(values);
            if (missing.Length > 0)
                draft.Warnings.Add($"{name}: required variables without a value: {string.Join(", ", missing)}.");

            kept.Add((new AiDraftModule
            {
                ModuleId = entry.Module.Id,
                ModuleName = entry.Module.Name,
                ModuleVersion = entry.Version.Name,
                Name = name,
                Purpose = GetString(item, "purpose"),
                Values = values.Select(v => new DesignModules.ModuleValue(v)).ToList(),
                MissingRequiredVariables = missing
            }, entry.Version, values));
        }

        if (kept.Count == 0)
            draft.Warnings.Add("The draft has no modules from the catalog.");

        draft.Warnings.AddRange(ReferenceProblems(kept.Select(k => (k.Draft.Name, k.Version, (IEnumerable<Domain.Models.ModuleValue>)k.Values))));
        draft.Modules = kept.Select(k => k.Draft).ToList();
        draft.Snippet = string.Join("\n\n", kept.Select(k => k.Version.ToSnippet(k.Draft.Name, k.Values)));
        return draft;
    }

    /// <summary>
    /// Every module.X.Y reference must name a module in the design that has output Y,
    /// and the references may not form a cycle. Names are assumed unique.
    /// </summary>
    public static List<string> ReferenceProblems(IEnumerable<(string Name, ModuleVersion Version, IEnumerable<Domain.Models.ModuleValue> Values)> modules)
    {
        var list = modules.ToList();
        var byName = list.ToDictionary(x => x.Name, x => x.Version);
        var edges = list.ToDictionary(x => x.Name, _ => new HashSet<string>());
        var problems = new List<string>();

        foreach (var (name, _, values) in list)
        {
            foreach (var value in values ?? [])
            {
                foreach (Match match in ModuleReference.Matches(value.Value ?? ""))
                {
                    var target = match.Groups[1].Value;
                    var output = match.Groups[2].Value;
                    if (target == name)
                        problems.Add($"{name}.{value.Name} references its own output {match.Value}.");
                    else if (!byName.TryGetValue(target, out var targetVersion))
                        problems.Add($"{name}.{value.Name} references {match.Value}, but the design has no module named '{target}'.");
                    else if (!(targetVersion.Outputs ?? []).Any(o => o.Name == output))
                        problems.Add($"{name}.{value.Name} references {match.Value}, but '{target}' has no output '{output}'.");
                    else
                        edges[name].Add(target);
                }
            }
        }

        // depth-first search; a module reached again while still on the path closes a cycle
        var state = new Dictionary<string, int>();
        var path = new Stack<string>();
        bool Visit(string node)
        {
            state[node] = 1;
            path.Push(node);
            foreach (var next in edges[node])
            {
                if (state.GetValueOrDefault(next) == 1)
                {
                    var cycle = path.Reverse().SkipWhile(n => n != next).Append(next);
                    problems.Add($"Modules reference each other in a cycle: {string.Join(" -> ", cycle)}.");
                    return true;
                }
                if (state.GetValueOrDefault(next) == 0 && Visit(next))
                    return true;
            }
            path.Pop();
            state[node] = 2;
            return false;
        }
        foreach (var node in edges.Keys)
        {
            if (state.GetValueOrDefault(node) == 0 && Visit(node))
                break;
        }

        return problems;
    }

    // ToSnippet wraps string values in quotes, so a quoted or bare reference would be written wrong.
    private static string Normalize(string moduleName, ModuleVariable variable, string value, List<string> warnings)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || !variable.RequiresQuotes)
            return value;

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
            warnings.Add($"{moduleName}.{variable.Name}: removed the surrounding quotes, which Caster adds to strings.");
        }
        if (BareModuleReference.IsMatch(value))
        {
            value = "${" + value + "}";
            warnings.Add($"{moduleName}.{variable.Name}: wrapped the reference as {value} so it is not written as literal text.");
        }
        return value;
    }

    private static string GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
