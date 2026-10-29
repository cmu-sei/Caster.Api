// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Caster.Api.Features.Designs;

namespace Caster.Api.Features.Files;

/// <summary>
/// What a participant team does in the exercise.
/// </summary>
public enum AiTeamRole
{
    Defender,
    Attacker,
    Observer,
    WhiteCell,
    Other
}

/// <summary>
/// A structured description of an exercise scenario, for an application such as Blueprint to have
/// Caster draft the range: the organizations, their network segments and key terrain, the critical
/// systems, the attacker's entry point and path, and the participant teams.
/// </summary>
public class AiScenarioBrief
{
    public string Title { get; set; }
    public string Summary { get; set; }

    /// <summary>
    /// Where the brief came from, for the planner's reference. Not used to draft.
    /// </summary>
    public AiBriefSource Source { get; set; }

    public List<AiBriefOrganization> Organizations { get; set; } = [];

    /// <summary>
    /// The network segments the scenario needs, including its key cyber terrain.
    /// </summary>
    public List<AiBriefSegment> Segments { get; set; } = [];

    /// <summary>
    /// The systems the scenario is about, and where they sit.
    /// </summary>
    public List<AiBriefSystem> Systems { get; set; } = [];

    public AiBriefAttacker Attacker { get; set; }
    public List<AiBriefTeam> Teams { get; set; } = [];

    /// <summary>
    /// What the exercise is for, e.g. "Detect lateral movement from IT to OT".
    /// </summary>
    public List<string> Objectives { get; set; } = [];

    /// <summary>
    /// Limits the range must keep to, e.g. "No more than 30 machines".
    /// </summary>
    public List<string> Constraints { get; set; } = [];
}

public class AiBriefSource
{
    /// <summary>
    /// The application that wrote the brief, e.g. Blueprint.
    /// </summary>
    public string Application { get; set; }

    /// <summary>
    /// The application's ID of what the brief describes, e.g. a MSEL ID.
    /// </summary>
    public string Reference { get; set; }

    public string Name { get; set; }
}

public class AiBriefOrganization
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<AiBriefSite> Sites { get; set; } = [];
}

public class AiBriefSite
{
    public string Name { get; set; }
    public string Description { get; set; }
}

public class AiBriefSegment
{
    public string Name { get; set; }

    /// <summary>
    /// What the segment is for. Other, the default, lets the draft choose.
    /// </summary>
    public AiSegmentRole Role { get; set; }

    /// <summary>
    /// The organization the segment belongs to, by name.
    /// </summary>
    public string Organization { get; set; }

    /// <summary>
    /// The organization's site the segment is at, by name.
    /// </summary>
    public string Site { get; set; }

    public string Description { get; set; }

    /// <summary>
    /// The IPv4 network to use, e.g. 10.20.30.0/24. Empty lets the draft plan one.
    /// </summary>
    public string Cidr { get; set; }

    public bool KeyTerrain { get; set; }
}

public class AiBriefSystem
{
    public string Name { get; set; }

    /// <summary>
    /// The segment the system is on, by name.
    /// </summary>
    public string Segment { get; set; }

    public string Description { get; set; }
    public string OperatingSystem { get; set; }
    public List<string> Services { get; set; } = [];

    /// <summary>
    /// What the attacker is after or the defenders must keep running.
    /// </summary>
    public bool Critical { get; set; }

    public bool KeyTerrain { get; set; }
}

public class AiBriefAttacker
{
    /// <summary>
    /// Where the attacker gets in: a segment, a system or a service, by name.
    /// </summary>
    public string EntryPoint { get; set; }

    public string Description { get; set; }

    /// <summary>
    /// The attacker's steps in order.
    /// </summary>
    public List<AiBriefAttackStep> Path { get; set; } = [];

    public List<string> Objectives { get; set; } = [];
}

public class AiBriefAttackStep
{
    public string From { get; set; }
    public string To { get; set; }

    /// <summary>
    /// The MITRE ATT&amp;CK technique, e.g. T1566.
    /// </summary>
    public string Technique { get; set; }

    public string Description { get; set; }
}

public class AiBriefTeam
{
    public string Name { get; set; }
    public AiTeamRole Role { get; set; }

    /// <summary>
    /// How many workstations the team needs.
    /// </summary>
    public int Workstations { get; set; }

    /// <summary>
    /// The segment the team's workstations go on, by name. Empty lets the draft choose.
    /// </summary>
    public string Segment { get; set; }

    public string OperatingSystem { get; set; }
    public string Description { get; set; }
}

/// <summary>
/// Turns a brief into the request the model drafts from, and into what the topology checks expect of the plan.
/// </summary>
public static class AiScenarioBriefPrompt
{
    public static string Build(AiScenarioBrief brief, string instructions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Build the range for this exercise scenario. Every segment, system and team below must be in the range and in topology,");
        sb.AppendLine("under the names given here. Plan what the brief leaves open, and say in the summary what you chose.");
        sb.AppendLine();
        sb.AppendLine($"Scenario: {brief.Title?.Trim()}");
        Line(sb, "", brief.Summary);

        if (brief.Organizations?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Organizations:");
            foreach (var o in brief.Organizations)
            {
                sb.AppendLine($"- {o.Name}{Suffix(o.Description)}");
                foreach (var site in o.Sites ?? [])
                    sb.AppendLine($"  - site {site.Name}{Suffix(site.Description)}");
            }
        }

        if (brief.Segments?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Network segments:");
            foreach (var s in brief.Segments)
            {
                var details = new List<string>();
                if (s.Role != AiSegmentRole.Other) details.Add($"role {s.Role}");
                if (!string.IsNullOrWhiteSpace(s.Organization)) details.Add(s.Organization.Trim());
                if (!string.IsNullOrWhiteSpace(s.Site)) details.Add($"site {s.Site.Trim()}");
                if (!string.IsNullOrWhiteSpace(s.Cidr)) details.Add(s.Cidr.Trim());
                if (s.KeyTerrain) details.Add("key terrain");
                sb.AppendLine($"- {s.Name}{(details.Count > 0 ? $" ({string.Join(", ", details)})" : "")}{Suffix(s.Description)}");
            }
        }

        if (brief.Systems?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Systems:");
            foreach (var s in brief.Systems)
            {
                var details = new List<string>();
                if (!string.IsNullOrWhiteSpace(s.Segment)) details.Add($"on {s.Segment.Trim()}");
                if (!string.IsNullOrWhiteSpace(s.OperatingSystem)) details.Add(s.OperatingSystem.Trim());
                if (s.Services?.Count > 0) details.Add($"runs {string.Join(", ", s.Services)}");
                if (s.Critical) details.Add("critical");
                if (s.KeyTerrain) details.Add("key terrain");
                sb.AppendLine($"- {s.Name}{(details.Count > 0 ? $" ({string.Join(", ", details)})" : "")}{Suffix(s.Description)}");
            }
        }

        if (brief.Attacker != null)
        {
            sb.AppendLine();
            sb.AppendLine("Attacker:");
            Line(sb, "- entry point: ", brief.Attacker.EntryPoint);
            Line(sb, "- ", brief.Attacker.Description);
            var step = 0;
            foreach (var p in brief.Attacker.Path ?? [])
                sb.AppendLine($"- step {++step}: {p.From} -> {p.To}{(string.IsNullOrWhiteSpace(p.Technique) ? "" : $" ({p.Technique.Trim()})")}{Suffix(p.Description)}");
            foreach (var objective in brief.Attacker.Objectives ?? [])
                Line(sb, "- objective: ", objective);
        }

        if (brief.Teams?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Participant teams (each workstation is a ParticipantWorkstation machine with the team's name):");
            foreach (var t in brief.Teams)
            {
                var details = new List<string> { t.Role.ToString(), $"{t.Workstations} workstation{(t.Workstations == 1 ? "" : "s")}" };
                if (!string.IsNullOrWhiteSpace(t.Segment)) details.Add($"on {t.Segment.Trim()}");
                if (!string.IsNullOrWhiteSpace(t.OperatingSystem)) details.Add(t.OperatingSystem.Trim());
                sb.AppendLine($"- {t.Name} ({string.Join(", ", details)}){Suffix(t.Description)}");
            }
        }

        List(sb, "Exercise objectives:", brief.Objectives);
        List(sb, "Constraints:", brief.Constraints);

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            sb.AppendLine();
            sb.AppendLine("The planner adds:");
            sb.AppendLine(instructions.Trim());
        }
        return sb.ToString();
    }

    public static AiTopologyExpectations Expectations(AiScenarioBrief brief) => new(
        (brief.Segments ?? []).Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList(),
        (brief.Systems ?? []).Where(s => s.Critical).Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList(),
        (brief.Teams ?? []).Where(t => t.Workstations > 0).Select(t => (t.Name, t.Workstations)).ToList(),
        brief.Attacker?.EntryPoint);

    private static string Suffix(string description) =>
        string.IsNullOrWhiteSpace(description) ? "" : $": {description.Trim()}";

    private static void Line(StringBuilder sb, string prefix, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            sb.AppendLine(prefix + text.Trim());
    }

    private static void List(StringBuilder sb, string heading, List<string> items)
    {
        var present = (items ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        if (present.Count == 0)
            return;
        sb.AppendLine();
        sb.AppendLine(heading);
        foreach (var item in present)
            sb.AppendLine($"- {item.Trim()}");
    }
}
