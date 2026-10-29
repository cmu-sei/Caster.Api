// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.Designs;
using Caster.Api.Features.Files;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Features.Designs;
using Caster.Api.Tests.Support;
using FilesDraftWithAi = Caster.Api.Features.Files.DraftWithAi;
using DesignsDraftWithAi = Caster.Api.Features.Designs.DraftWithAi;

namespace Caster.Api.Tests.Unit.AiDraft;

/// <summary>
/// The AI draft handlers with the model and the inventory substituted: what inventory the model is shown,
/// for whom, and how the answer is checked against it. Runs over the database harness, since the handlers
/// read the Directory's files and the module catalog.
/// </summary>
[Trait("Category", "Unit")]
public class AiDraftGroundingTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private readonly IAiModelClient _model = Substitute.For<IAiModelClient>();
    private readonly ICasterAuthorizationService _authorization = Substitute.For<ICasterAuthorizationService>();
    private readonly IInventoryService _inventory = AiInventoryTests.Service(("default", InfrastructureProviders.Proxmox, AiInventoryTests.Proxmox()));
    private string _system;
    private string _prompt;

    private const string ProxmoxProvider = "terraform {\n  required_providers {\n    proxmox = { source = \"bpg/proxmox\" }\n  }\n}\n";

    private const string Answer = """
        {"summary":"a web server in a DMZ","files":[{"name":"web.tf","content":"resource \"proxmox_virtual_environment_vm\" \"web\" {\n  clone {\n    vm_id = 9000\n  }\n  network_device {\n    bridge = \"dmz\"\n  }\n}\n"}],
         "topology":{
           "segments":[{"name":"internet","role":"Internet","cidr":"203.0.113.0/24"},{"name":"dmz","role":"Dmz","cidr":"10.0.10.0/24","network":"corp"}],
           "machines":[{"name":"kali","role":"Attacker","interfaces":[{"segment":"internet","address":"203.0.113.9"}]},
                       {"name":"web","role":"Server","template":"9000","critical":true,"interfaces":[{"segment":"dmz","address":"10.0.10.10"}]}],
           "attackPath":[{"from":"kali","to":"web","technique":"T1190"}]}}
        """;

    private void Answers(string json)
    {
        _model.InvokeToolAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JsonObject>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _system = call.ArgAt<string>(0);
                _prompt = call.ArgAt<string>(1);
                return JsonDocument.Parse(json).RootElement.Clone();
            });
    }

    private void MayViewHosts(bool allowed) =>
        _authorization.Authorize(Arg.Is<SystemPermission[]>(p => p.Contains(SystemPermission.ViewHosts)), Arg.Any<CancellationToken>()).Returns(allowed);

    private async Task<Directory> SeedDirectory(string providerContent = ProxmoxProvider)
    {
        var project = TestData.Project();
        var parent = TestData.Directory(project, "range");
        var directory = TestData.Directory(project, "lab", parent);
        await Seed(project, parent, directory, TestData.File(parent, "provider.tf", providerContent));
        return directory;
    }

    private FilesDraftWithAi.Drafter Drafter(TerraformOptions terraform = null) =>
        new(_authorization, Db, _model, new AiOptions { Enabled = true }, terraform ?? new TerraformOptions(), _inventory);

    [Fact]
    public async Task A_caller_holding_ViewHosts_gets_a_draft_grounded_in_the_hypervisors_inventory()
    {
        var directory = await SeedDirectory();
        MayViewHosts(true);
        Answers(Answer);

        var draft = await new FilesDraftWithAi.Handler(_authorization, Db, _model, new AiOptions { Enabled = true }, new TerraformOptions(), _inventory)
            .HandleRequest(new FilesDraftWithAi.Command { DirectoryId = directory.Id, Prompt = "a web server in a DMZ" }, Ct);

        Assert.Contains("## Proxmox connection \"default\"", _system);
        Assert.Contains("- ubuntu-22 (vmid 9000", _system);
        Assert.Contains("Range topology:", _system);
        Assert.True(draft.Inventory.Checked);
        Assert.Contains(draft.Inventory.Found, f => f.Kind == AiInventoryKind.Template && f.Name == "9000");
        Assert.Contains(draft.Inventory.Found, f => f.Kind == AiInventoryKind.Network && f.Name == "corp");
        Assert.Equal("dmz", Assert.Single(draft.Inventory.Unknown).Name);
        Assert.Equal(2, draft.Topology.Machines.Count);
        Assert.Equal(1, draft.Topology.Totals.Sized);
        Assert.Equal(100, draft.Topology.Totals.DatastoreFreeGiB);
    }

    [Fact]
    public async Task A_caller_without_ViewHosts_gets_a_draft_that_was_not_grounded_and_says_why()
    {
        var directory = await SeedDirectory();
        MayViewHosts(false);
        Answers(Answer);

        var draft = await Drafter().Draft(directory.Id, "a web server", null, [], null, Ct);

        Assert.DoesNotContain("Infrastructure inventory:", _system);
        Assert.DoesNotContain("ubuntu-22", _system);
        Assert.False(draft.Inventory.Checked);
        Assert.Contains("ViewHosts", draft.Inventory.Reason);
        Assert.Empty(draft.Inventory.Unknown);
        Assert.DoesNotContain(draft.Warnings, w => w.Contains("inventory"));
    }

    [Fact]
    public async Task A_directory_on_vSphere_is_not_grounded_in_a_Proxmox_inventory()
    {
        var directory = await SeedDirectory("provider \"vsphere\" {}\n");
        MayViewHosts(true);
        Answers(Answer);

        var draft = await Drafter().Draft(directory.Id, "a web server", null, [], null, Ct);

        Assert.False(draft.Inventory.Checked);
        Assert.Contains("vSphere", draft.Inventory.Reason);
        Assert.DoesNotContain("ubuntu-22", _system);
    }

    [Fact]
    public async Task The_requested_hypervisor_wins_over_the_files()
    {
        var directory = await SeedDirectory("provider \"vsphere\" {}\n");
        MayViewHosts(true);
        Answers(Answer);
        var terraform = new TerraformOptions { EnvironmentVariables = new EnvironmentVariableOptions { Direct = new() { ["PROXMOX_VE_ENDPOINT"] = "https://pve.test:8006" } } };

        var draft = await Drafter(terraform).Draft(directory.Id, "a web server", null, [Hypervisors.Proxmox], null, Ct);

        Assert.True(draft.Inventory.Checked);
        Assert.Contains("ubuntu-22", _system);
    }

    [Fact]
    public async Task An_unreachable_inventory_degrades_to_an_ungrounded_draft()
    {
        var directory = await SeedDirectory();
        MayViewHosts(true);
        Answers(Answer);
        var unreachable = AiInventoryTests.Service(("default", InfrastructureProviders.Proxmox, InventorySnapshot.AllUnavailable("Could not reach pve.test")));

        var draft = await new FilesDraftWithAi.Drafter(_authorization, Db, _model, new AiOptions { Enabled = true }, new TerraformOptions(), unreachable)
            .Draft(directory.Id, "a web server", null, [], null, Ct);

        Assert.False(draft.Inventory.Checked);
        Assert.Contains("Could not reach pve.test", draft.Inventory.Reason);
        Assert.Single(draft.Files);
    }

    [Fact]
    public async Task A_brief_is_drafted_from_its_rendering_and_checked_for_what_it_asks()
    {
        var directory = await SeedDirectory();
        MayViewHosts(true);
        Answers(Answer);
        var brief = new AiScenarioBrief
        {
            Title = "Water utility",
            Segments = [new AiBriefSegment { Name = "dmz", Role = AiSegmentRole.Dmz }, new AiBriefSegment { Name = "scada", Role = AiSegmentRole.Ot, KeyTerrain = true }],
            Systems = [new AiBriefSystem { Name = "hmi", Segment = "scada", Critical = true }],
            Teams = [new AiBriefTeam { Name = "Blue", Role = AiTeamRole.Defender, Workstations = 2 }],
            Attacker = new AiBriefAttacker { EntryPoint = "kali" }
        };

        var draft = await new DraftWithAiFromBrief.Handler(_authorization, Db, _model, new AiOptions { Enabled = true }, new TerraformOptions(), _inventory)
            .HandleRequest(new DraftWithAiFromBrief.Command { DirectoryId = directory.Id, Brief = brief, Instructions = "Reuse the range's provider." }, Ct);

        Assert.Contains("Scenario: Water utility", _prompt);
        Assert.Contains("- scada (role Ot, key terrain)", _prompt);
        Assert.Contains("The planner adds:\nReuse the range's provider.", _prompt.Replace("\r\n", "\n"));
        Assert.Contains("=== provider.tf (read-only, from range) ===", _prompt);
        Assert.Contains("The brief's segment scada is not in the plan.", draft.Warnings);
        Assert.Contains("The brief's critical system hmi is not in the plan.", draft.Warnings);
        Assert.Contains("Team Blue has 0 of the 2 workstations the brief asks for.", draft.Warnings);
        Assert.DoesNotContain(draft.Warnings, w => w.Contains("entry point"));
        Assert.True(draft.Inventory.Checked);
    }

    [Fact]
    public async Task A_design_draft_is_grounded_in_the_inventory_of_its_hypervisor()
    {
        var directory = await SeedDirectory();
        var module = TestData.Module("proxmox-vm");
        var version = TestData.ModuleVersion(module);
        version.Providers = ["bpg/proxmox"];
        version.Variables.Add(new ModuleVariable { Name = "template_id", VariableType = "number" });
        module.Versions.Add(version);
        await Seed(module);
        MayViewHosts(true);
        Answers($$$"""{"designName":"lab","summary":"s","modules":[{"moduleId":"{{{module.Id}}}","name":"web","values":{"template_id":"9100"}}]}""");
        var terraform = new TerraformOptions { EnvironmentVariables = new EnvironmentVariableOptions { Direct = new() { ["PROXMOX_VE_ENDPOINT"] = "https://pve.test:8006" } } };

        var draft = await new DesignsDraftWithAi.Handler(_authorization, Db, _model, new AiOptions { Enabled = true }, terraform, _inventory)
            .HandleRequest(new DesignsDraftWithAi.Command { DirectoryId = directory.Id, Prompt = "a web server" }, Ct);

        Assert.Contains("- ubuntu-22 (vmid 9000", _system);
        Assert.Equal("9100", Assert.Single(draft.Inventory.Unknown).Name);
        Assert.Contains(draft.Warnings, w => w.StartsWith("web.template_id: template vmid 9100 is not in the Proxmox inventory"));
    }
}
