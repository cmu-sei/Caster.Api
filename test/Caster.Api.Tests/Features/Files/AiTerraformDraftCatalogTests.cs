// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Directory = Caster.Api.Domain.Models.Directory;

namespace Caster.Api.Tests.Features.Files;

/// <summary>
/// Which Modules a Terraform draft offers the model: those that build on the hypervisor the Directory's Terraform
/// uses, or on any, so a Proxmox directory is not offered a vSphere-only Module.
/// </summary>
public class AiTerraformDraftCatalogTests(DatabaseFixture fixture, AiAppFactory factory)
    : ApiTestBase(fixture, factory), IClassFixture<AiAppFactory>
{
    private AiAppFactory Ai => (AiAppFactory)Factory;

    private const string ProxmoxMain = "terraform {\n  required_providers {\n    proxmox = {\n      source = \"bpg/proxmox\"\n    }\n  }\n}\n";
    private const string Answer = """{"summary":"s","files":[{"name":"vm.tf","content":"# vm\n"}]}""";

    private static Module Seed(string name, params string[] providers)
    {
        var module = TestData.Module(name);
        var version = TestData.ModuleVersion(module);
        version.Providers = providers;
        module.Versions.Add(version);
        return module;
    }

    private async Task<string> SystemPromptFor(string existingTerraform)
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project, "lab");
        await Seed(project, directory,
            TestData.File(directory, "main.tf", existingTerraform),
            Seed("proxmox-net", "bpg/proxmox"),
            Seed("vsphere-net", "hashicorp/vsphere"),
            Seed("generic-label"));
        var actor = await Actor().OnProject(project, [ProjectPermission.EditProject]).SeedAsync();
        Ai.Model.Answers(directory.Id, Answer);

        var response = await Client(actor).PostAsJsonAsync($"api/directories/{directory.Id}/files/ai-draft", new { prompt = "add a network" }, Ct);

        response.EnsureSuccessStatusCode();
        return Ai.Model.Calls.Last(c => c.DirectoryId == directory.Id).System;
    }

    [Fact]
    public async Task A_proxmox_directory_is_offered_proxmox_and_generic_modules_but_not_vsphere_ones()
    {
        var system = await SystemPromptFor(ProxmoxMain);

        Assert.Contains("## proxmox-net:", system);
        Assert.Contains("## generic-label:", system);
        Assert.DoesNotContain("## vsphere-net:", system);
    }

    [Fact]
    public async Task A_directory_with_no_known_hypervisor_is_offered_every_module()
    {
        var system = await SystemPromptFor("# nothing yet\n");

        Assert.Contains("## proxmox-net:", system);
        Assert.Contains("## vsphere-net:", system);
        Assert.Contains("## generic-label:", system);
    }
}
