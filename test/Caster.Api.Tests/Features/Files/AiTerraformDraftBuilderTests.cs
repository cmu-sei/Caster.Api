// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Designs;
using Caster.Api.Features.Files;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Features.Designs;

namespace Caster.Api.Tests.Features.Files;

/// <summary>
/// <c>AiTerraformDraftBuilder.Review</c> - turning the model's files into a draft the designer can apply:
/// only files it may write, matched to the directory's own, with what the plan would reject called out.
/// </summary>
public class AiTerraformDraftBuilderTests
{
    private static readonly AiContextFile Main = new(Guid.NewGuid(), "main.tf", "variable \"vm_count\" {}\n", "lab", Editable: true);
    private static readonly AiContextFile Provider = new(Guid.NewGuid(), "provider.tf", "provider \"proxmox\" {}\nvariable \"node\" {}\n", "range", Editable: false);

    [Fact]
    public void Review_matches_a_file_to_the_directory_file_it_replaces()
    {
        var draft = Review("""{"summary":"s","files":[{"name":"main.tf","content":"variable \"vm_count\" { default = 2 }\n"}]}""");

        var file = Assert.Single(draft.Files);
        Assert.Equal(Main.Id, file.ExistingFileId);
        Assert.Equal(Main.Content, file.ExistingContent);
        Assert.Equal(AiTerraformDraftBuilder.ContentHash(Main.Content), file.ExistingContentHash);
    }

    [Fact]
    public void Review_leaves_a_new_file_unmatched()
    {
        var draft = Review("""{"summary":"s","files":[{"name":"network.tf","purpose":"lab network","content":"resource \"a\" \"b\" {}\n"}]}""");

        var file = Assert.Single(draft.Files);
        Assert.Null(file.ExistingFileId);
        Assert.Equal("lab network", file.Purpose);
    }

    [Fact]
    public void Review_drops_an_unchanged_file()
    {
        var draft = Review("""{"summary":"s","files":[{"name":"main.tf","content":"variable \"vm_count\" {}\n"}]}""");

        Assert.Empty(draft.Files);
        Assert.Contains(draft.Warnings, w => w.Contains("main.tf is unchanged"));
    }

    [Theory]
    [InlineData("../escape.tf")]
    [InlineData("sub/dir.tf")]
    [InlineData(".hidden.tf")]
    [InlineData("")]
    public void Review_drops_a_file_with_a_bad_name(string name)
    {
        var draft = Review($$"""{"summary":"s","files":[{"name":"{{name}}","content":"x"}]}""");

        Assert.Empty(draft.Files);
        Assert.Contains(draft.Warnings, w => w.StartsWith("Dropped a file named"));
    }

    [Fact]
    public void Review_drops_the_second_file_of_a_name()
    {
        var draft = Review("""{"summary":"s","files":[{"name":"a.tf","content":"# 1"},{"name":"A.tf","content":"# 2"}]}""");

        Assert.Equal("# 1", Assert.Single(draft.Files).Content);
    }

    [Fact]
    public void Review_warns_about_a_name_declared_in_a_parent_directory()
    {
        var draft = Review("""{"summary":"s","files":[{"name":"vars.tf","content":"variable \"node\" {}\n"}]}""");

        Assert.Contains(draft.Warnings, w => w.Contains("variable \"node\" is declared more than once") && w.Contains("range/provider.tf"));
    }

    [Fact]
    public void Review_does_not_count_the_declarations_of_a_replaced_file()
    {
        var draft = Review("""{"summary":"s","files":[{"name":"main.tf","content":"variable \"vm_count\" { default = 3 }\n"}]}""");

        Assert.DoesNotContain(draft.Warnings, w => w.Contains("declared more than once"));
    }

    [Fact]
    public void Review_does_not_count_a_commented_out_declaration()
    {
        var draft = Review("""{"summary":"s","files":[{"name":"old.tf","content":"# variable \"node\" {}\n"}]}""");

        Assert.DoesNotContain(draft.Warnings, w => w.Contains("declared more than once"));
    }

    [Fact]
    public void Review_warns_about_a_literal_credential()
    {
        var draft = Review("""{"summary":"s","files":[{"name":"p.tf","content":"api_token = \"abcd1234\"\n"}]}""");

        Assert.Contains(draft.Warnings, w => w.Contains("api_token is set to a literal value"));
    }

    [Fact]
    public void Review_accepts_a_credential_from_a_variable()
    {
        var draft = Review("""{"summary":"s","files":[{"name":"p.tf","content":"api_token = \"${var.token}\"\npassword = var.password\n"}]}""");

        Assert.DoesNotContain(draft.Warnings, w => w.Contains("literal value"));
    }

    [Fact]
    public void Review_keeps_the_models_notes_as_warnings()
    {
        var draft = Review("""{"summary":"s","files":[{"name":"n.tf","content":"# n"}],"notes":["Set var.node to the Proxmox node name."]}""");

        Assert.Contains("Set var.node to the Proxmox node name.", draft.Warnings);
    }

    [Fact]
    public void Review_of_no_files_says_so()
    {
        var draft = Review("""{"summary":"nothing to do","files":[]}""");

        Assert.Contains("The draft changes no files.", draft.Warnings);
    }

    [Theory]
    [InlineData("resource \"a\" \"b\" {\n  tags = { x = [1, (2)] }\n}\n")]
    [InlineData("locals {\n  s = \"brace } in a string\"\n}\n")]
    [InlineData("locals {\n  s = \"${var.a}-${join(\",\", var.b)}\"\n}\n")]
    [InlineData("# } a comment\n// ] another\n/* ) and\n a block */\nx = 1\n")]
    [InlineData("locals {\n  script = <<-EOT\n    if [ -z \"$X\" ]; then {\n  EOT\n}\n")]
    public void Balanced_accepts_valid_hcl(string content) =>
        Assert.True(AiTerraformDraftBuilder.Balanced(content));

    [Theory]
    [InlineData("resource \"a\" \"b\" {\n")]
    [InlineData("x = [1, 2}\n")]
    [InlineData("x = \"unterminated\n")]
    [InlineData("x = <<EOT\nnever ends\n")]
    public void Balanced_rejects_broken_hcl(string content) =>
        Assert.False(AiTerraformDraftBuilder.Balanced(content));

    [Fact]
    public void Review_warns_about_unbalanced_braces()
    {
        var draft = Review("""{"summary":"s","files":[{"name":"bad.tf","content":"resource \"a\" \"b\" {\n"}]}""");

        Assert.Contains(draft.Warnings, w => w.Contains("bad.tf: its braces"));
    }

    [Fact]
    public void BuildUserPrompt_marks_which_files_may_change()
    {
        var prompt = AiTerraformDraftBuilder.BuildUserPrompt("add a vm", [Main, Provider], ["base: module \"net\" (network)"], Main);

        Assert.Contains("=== main.tf (editable, this directory) ===", prompt);
        Assert.Contains("=== provider.tf (read-only, from range) ===", prompt);
        Assert.Contains("The designer is editing main.tf.", prompt);
        Assert.Contains("base: module \"net\" (network)", prompt);
    }

    [Fact]
    public void Review_checks_the_drafted_names_against_the_inventory()
    {
        var draft = ReviewWithInventory("""
            {"summary":"s","files":[{"name":"vm.tf","content":"resource \"proxmox_virtual_environment_vm\" \"web\" {\n  clone {\n    vm_id = 9000\n  }\n  network_device {\n    bridge = \"vmbr0\"\n  }\n  network_device {\n    bridge = \"dmz\"\n  }\n}\n"}],
             "topology":{"segments":[],"machines":[]}}
            """);

        Assert.True(draft.Inventory.Checked);
        Assert.Equal(["9000", "vmbr0"], draft.Inventory.Found.Select(f => f.Name).Order());
        Assert.Equal("dmz", Assert.Single(draft.Inventory.Unknown).Name);
        Assert.Contains(draft.Warnings, w => w.StartsWith("vm.tf proxmox_virtual_environment_vm.web: network 'dmz' is not in the Proxmox inventory"));
    }

    [Fact]
    public void Review_accepts_a_network_the_draft_creates()
    {
        var draft = ReviewWithInventory("""
            {"summary":"s","files":[{"name":"net.tf","content":"resource \"proxmox_virtual_environment_sdn_vnet\" \"dmz\" {\n  id = \"dmz\"\n}\nresource \"proxmox_virtual_environment_vm\" \"web\" {\n  network_device {\n    bridge = \"dmz\"\n  }\n}\n"}],
             "topology":{"segments":[{"name":"dmz","role":"Dmz","network":"dmz"}],"machines":[]}}
            """);

        Assert.Empty(draft.Inventory.Unknown);
    }

    [Fact]
    public void Review_returns_the_topology_plan_and_its_problems()
    {
        var draft = Review("""
            {"summary":"s","files":[{"name":"n.tf","content":"# n"}],
             "topology":{"segments":[{"name":"a","role":"Server","cidr":"10.0.0.0/16"},{"name":"b","role":"Server","cidr":"10.0.1.0/24"}],"machines":[]}}
            """);

        Assert.Equal(2, draft.Topology.Segments.Count);
        Assert.NotNull(draft.Topology.Totals);
        Assert.Contains(draft.Warnings, w => w.Contains("overlap"));
        Assert.Null(draft.Inventory);
    }

    [Fact]
    public void Review_says_when_a_draft_of_machines_gives_no_plan()
    {
        var withMachine = Review("""{"summary":"s","files":[{"name":"vm.tf","content":"resource \"vsphere_virtual_machine\" \"a\" {}\n"}]}""");
        var withoutMachine = Review("""{"summary":"s","files":[{"name":"vars.tf","content":"variable \"x\" {}\n"}]}""");

        Assert.Contains(AiTopology.NoPlan, withMachine.Warnings);
        Assert.DoesNotContain(AiTopology.NoPlan, withoutMachine.Warnings);
    }

    [Fact]
    public void BuildSystemPrompt_carries_the_topology_guidance_and_the_inventory()
    {
        var prompt = AiTerraformDraftBuilder.BuildSystemPrompt([], "Infrastructure inventory: what exists on the hypervisor now.\n");

        Assert.Contains("Range topology:", prompt);
        Assert.Contains("RFC 1918", prompt);
        Assert.Contains("Infrastructure inventory: what exists on the hypervisor now.", prompt);
    }

    [Fact]
    public void BuildToolSchema_offers_the_topology_plan()
    {
        Assert.True(AiTerraformDraftBuilder.BuildToolSchema()["properties"]!.AsObject().ContainsKey(AiTopology.ToolProperty));
    }

    private static AiTerraformDraft Review(string json) =>
        AiTerraformDraftBuilder.Review(JsonDocument.Parse(json).RootElement, new List<AiContextFile> { Main, Provider });

    private static AiTerraformDraft ReviewWithInventory(string json)
    {
        var inventory = AiInventory.Load(AiInventoryTests.Service(("default", InfrastructureProviders.Proxmox, AiInventoryTests.Proxmox())), [Hypervisors.Proxmox]);
        return AiTerraformDraftBuilder.Review(JsonDocument.Parse(json).RootElement, new List<AiContextFile> { Main, Provider }, inventory, [Hypervisors.Proxmox]);
    }
}
