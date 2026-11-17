// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Features.Designs;
using Caster.Api.Features.Files;

namespace Caster.Api.Tests.Features.Files;

/// <summary>
/// <c>AiScenarioBriefPrompt</c> - a brief as the request the model drafts from, and what the topology checks
/// expect of the plan because of it.
/// </summary>
[Trait("Category", "Unit")]
public class AiScenarioBriefTests
{
    private static AiScenarioBrief Brief() => new()
    {
        Title = "Water utility ransomware",
        Summary = "A ransomware crew moves from the corporate network into the plant.",
        Organizations = [new AiBriefOrganization { Name = "Rivertown Water", Sites = [new AiBriefSite { Name = "Treatment plant" }] }],
        Segments =
        [
            new AiBriefSegment { Name = "corp", Role = AiSegmentRole.UserLan, Organization = "Rivertown Water", Cidr = "10.10.0.0/24" },
            new AiBriefSegment { Name = "scada", Role = AiSegmentRole.Ot, Site = "Treatment plant", KeyTerrain = true, Description = "Plant control" },
            new AiBriefSegment { Name = "soc" },
        ],
        Systems =
        [
            new AiBriefSystem { Name = "hmi-01", Segment = "scada", OperatingSystem = "Windows 10", Services = ["VNC"], Critical = true },
            new AiBriefSystem { Name = "mail", Segment = "corp" },
        ],
        Attacker = new AiBriefAttacker
        {
            EntryPoint = "mail",
            Path = [new AiBriefAttackStep { From = "internet", To = "mail", Technique = "T1566", Description = "Phishing" }],
            Objectives = ["Encrypt the historian"]
        },
        Teams = [new AiBriefTeam { Name = "Blue 1", Role = AiTeamRole.Defender, Workstations = 4, Segment = "soc" }, new AiBriefTeam { Name = "White", Role = AiTeamRole.WhiteCell }],
        Objectives = ["Detect lateral movement from IT to OT"],
        Constraints = ["No more than 30 machines"]
    };

    [Fact]
    public void Build_lists_every_part_of_the_brief()
    {
        var prompt = AiScenarioBriefPrompt.Build(Brief(), "Reuse the domain controller.").Replace("\r\n", "\n");

        Assert.Contains("Scenario: Water utility ransomware\nA ransomware crew moves", prompt);
        Assert.Contains("- Rivertown Water\n  - site Treatment plant", prompt);
        Assert.Contains("- corp (role UserLan, Rivertown Water, 10.10.0.0/24)", prompt);
        Assert.Contains("- scada (role Ot, site Treatment plant, key terrain): Plant control", prompt);
        Assert.Contains("- soc\n", prompt);
        Assert.Contains("- hmi-01 (on scada, Windows 10, runs VNC, critical)", prompt);
        Assert.Contains("- entry point: mail", prompt);
        Assert.Contains("- step 1: internet -> mail (T1566): Phishing", prompt);
        Assert.Contains("- objective: Encrypt the historian", prompt);
        Assert.Contains("- Blue 1 (Defender, 4 workstations, on soc)", prompt);
        Assert.Contains("- White (WhiteCell, 0 workstations)", prompt);
        Assert.Contains("Exercise objectives:\n- Detect lateral movement from IT to OT", prompt);
        Assert.Contains("Constraints:\n- No more than 30 machines", prompt);
        Assert.Contains("The planner adds:\nReuse the domain controller.", prompt);
    }

    [Fact]
    public void Build_of_a_bare_brief_has_only_its_title()
    {
        var prompt = AiScenarioBriefPrompt.Build(new AiScenarioBrief { Title = "Lab" }, null);

        Assert.Contains("Scenario: Lab", prompt);
        Assert.DoesNotContain("Attacker:", prompt);
        Assert.DoesNotContain("The planner adds:", prompt);
    }

    [Fact]
    public void Expectations_are_the_segments_critical_systems_workstations_and_entry_point()
    {
        var expected = AiScenarioBriefPrompt.Expectations(Brief());

        Assert.Equal(["corp", "scada", "soc"], expected.Segments);
        Assert.Equal(["hmi-01"], expected.CriticalSystems);
        Assert.Equal([("Blue 1", 4)], expected.Workstations);
        Assert.Equal("mail", expected.EntryPoint);
    }
}
