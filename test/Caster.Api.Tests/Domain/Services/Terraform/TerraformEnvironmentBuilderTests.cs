// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Domain.Services.Terraform;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Caster.Api.Tests.Domain.Services.Terraform;

/// <summary>
/// The environment a Terraform command runs with: the global variables, then the project's Environment
/// Sets, then Caster's own, over the test's database and an API environment the test owns.
/// </summary>
public class TerraformEnvironmentBuilderTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private const string Secret = "CASTER_SECRET_TOKEN";
    private const string SecretValue = "s3cr3t-token-value";

    private readonly Dictionary<string, string> _apiVariables = new(StringComparer.Ordinal)
    {
        ["PATH"] = "/usr/bin",
        ["HOME"] = "/home/caster",
        ["ConnectionStrings__PostgreSQL"] = "Host=db;Password=api-password",
        ["VSPHERE_SERVER"] = "vcenter.local",
        [Secret] = SecretValue,
        [ConnectionPassword] = "hypervisor-password"
    };

    private const string ConnectionPassword = "Infrastructure__Connections__0__Password";

    private readonly TerraformOptions _options = new()
    {
        EnvironmentVariables = new EnvironmentVariableOptions(),
        Secrets = new SecretOptions { Patterns = ["^CASTER_SECRET_"] }
    };

    [Fact]
    public async Task Only_the_inherited_patterns_reach_a_run_when_InheritAll_is_off()
    {
        _options.EnvironmentVariables.Inherit = ["^VSPHERE_"];
        var workspace = await SeedWorkspace();

        var environment = await Build(workspace);

        Assert.Equal("vcenter.local", environment.Variables["VSPHERE_SERVER"]);
        Assert.False(environment.Variables.ContainsKey("ConnectionStrings__PostgreSQL"));
        Assert.False(environment.Variables.ContainsKey("PATH"));
        Assert.Equal("true", environment.Variables["TF_IN_AUTOMATION"]);
    }

    [Fact]
    public async Task A_secret_never_reaches_a_run_through_Inherit_or_InheritAll()
    {
        _options.EnvironmentVariables.InheritAll = true;
        _options.EnvironmentVariables.Inherit = ["^CASTER_"];
        var workspace = await SeedWorkspace();

        var environment = await Build(workspace);

        Assert.Equal("vcenter.local", environment.Variables["VSPHERE_SERVER"]);
        Assert.DoesNotContain(SecretValue, environment.Variables.Values);
    }

    /// <summary>The inventory's connection settings hold hypervisor credentials, which reach runs only through Environment Sets.</summary>
    [Fact]
    public async Task Infrastructure_settings_never_reach_a_run_through_Inherit_or_InheritAll()
    {
        _options.EnvironmentVariables.InheritAll = true;
        _options.EnvironmentVariables.Inherit = [".*"];
        var workspace = await SeedWorkspace();

        var environment = await Build(workspace);

        Assert.False(environment.Variables.ContainsKey(ConnectionPassword));
        Assert.DoesNotContain("hypervisor-password", environment.Variables.Values);
        Assert.Equal("vcenter.local", environment.Variables["VSPHERE_SERVER"]);
    }

    [Theory]
    [InlineData("Infrastructure__Connections__0__Password", true)]
    [InlineData("INFRASTRUCTURE__CONNECTIONS__0__APITOKEN", true)]
    [InlineData("infrastructure:Connections:0:Username", true)]
    [InlineData("InfrastructureConnections", false)]
    [InlineData("PROXMOX_VE_API_TOKEN", false)]
    public void IsHeldBack_matches_only_infrastructure_settings(string name, bool heldBack)
    {
        Assert.Equal(heldBack, TerraformEnvironmentBuilder.IsHeldBack(name));
    }

    [Fact]
    public async Task Baseline_variables_are_passed_even_when_not_inherited()
    {
        var workspace = await SeedWorkspace();

        var environment = await Build(workspace, "PATH", "HOME");

        Assert.Equal("/usr/bin", environment.Variables["PATH"]);
        Assert.Equal("/home/caster", environment.Variables["HOME"]);
    }

    [Fact]
    public async Task A_projects_sets_reach_its_runs_and_override_same_named_global_variables()
    {
        _options.EnvironmentVariables.Inherit = ["^VSPHERE_"];
        _options.EnvironmentVariables.Direct = new() { ["VSPHERE_USER"] = "global-user" };
        var workspace = await SeedWorkspace(out var project);
        var set = TestData.EnvironmentSet();
        await Seed(
            set,
            TestData.SecretEntry(set, "VSPHERE_PASSWORD", Secret),
            TestData.PlainEntry(set, "VSPHERE_USER", "project-user"),
            TestData.PlainEntry(set, "vsphere_server", "\"vc.example\"", EnvironmentSetEntryKind.TerraformVariable),
            TestData.ProjectEnvironmentSet(project, set));

        var environment = await Build(workspace);

        Assert.True(environment.IsValid);
        Assert.Equal(SecretValue, environment.Variables["VSPHERE_PASSWORD"]);
        Assert.Equal("project-user", environment.Variables["VSPHERE_USER"]);
        Assert.Equal("\"vc.example\"", environment.Variables["TF_VAR_vsphere_server"]);
        Assert.Equal("***", environment.Masker.Apply(SecretValue));
        Assert.Equal("project-user", environment.Masker.Apply("project-user"));
    }

    [Fact]
    public async Task Another_projects_sets_do_not_reach_a_run()
    {
        var workspace = await SeedWorkspace();
        var other = TestData.Project("Other");
        var set = TestData.EnvironmentSet();
        await Seed(other, set, TestData.SecretEntry(set, "TOKEN", Secret), TestData.ProjectEnvironmentSet(other, set));

        var environment = await Build(workspace);

        Assert.False(environment.Variables.ContainsKey("TOKEN"));
        Assert.True(environment.Masker.IsEmpty);
    }

    [Fact]
    public async Task A_missing_secret_is_an_error_naming_the_target_and_set_but_not_the_source()
    {
        var workspace = await SeedWorkspace(out var project);
        var set = TestData.EnvironmentSet("Hetzner");
        await Seed(set, TestData.SecretEntry(set, "HCLOUD_TOKEN", "CASTER_SECRET_NOT_SET"), TestData.ProjectEnvironmentSet(project, set));

        var environment = await Build(workspace);

        var error = Assert.Single(environment.Errors);
        Assert.Contains("HCLOUD_TOKEN", error);
        Assert.Contains("\"Hetzner\"", error);
        Assert.DoesNotContain("CASTER_SECRET_NOT_SET", error);
        Assert.False(environment.Variables.ContainsKey("HCLOUD_TOKEN"));
    }

    [Fact]
    public async Task A_secret_whose_source_no_longer_matches_the_patterns_is_an_error()
    {
        var workspace = await SeedWorkspace(out var project);
        var set = TestData.EnvironmentSet();
        await Seed(set, TestData.SecretEntry(set, "TOKEN", Secret), TestData.ProjectEnvironmentSet(project, set));
        _options.Secrets.Patterns = ["^OTHER_"];

        var environment = await Build(workspace);

        Assert.Contains("no longer matches", Assert.Single(environment.Errors));
        Assert.False(environment.Variables.ContainsKey("TOKEN"));
    }

    [Fact]
    public async Task Caster_sets_the_workspace_last_so_an_inherited_TF_WORKSPACE_cannot_redirect_it()
    {
        _apiVariables["TF_WORKSPACE"] = "someone-else";
        _apiVariables["TF_IN_AUTOMATION"] = "false";
        _options.EnvironmentVariables.InheritAll = true;
        var workspace = await SeedWorkspace();

        var environment = await Build(workspace);

        Assert.Equal(workspace.Name, environment.Variables["TF_WORKSPACE"]);
    }

    [Fact]
    public async Task The_default_workspace_drops_an_inherited_TF_WORKSPACE()
    {
        _apiVariables["TF_WORKSPACE"] = "someone-else";
        _options.EnvironmentVariables.InheritAll = true;
        var workspace = await SeedWorkspace(name: "default");

        var environment = await Build(workspace);

        Assert.False(environment.Variables.ContainsKey("TF_WORKSPACE"));
    }

    // ---- Connection entries, scope, sources -----------------------------------------------------

    private void AddProxmoxConnection(string token = "caster@pve!ci=0f1e2d3c-secret")
    {
        _infrastructure.Connections.Add(new InfrastructureConnectionOptions
        {
            Name = "Lab-PVE",
            Provider = "proxmox",
            Url = "https://pve.lab:8006",
            ApiToken = token,
            InsecureSkipVerify = true
        });
    }

    [Fact]
    public async Task A_connection_entry_reads_its_field_and_masks_a_credential()
    {
        AddProxmoxConnection();
        var workspace = await SeedWorkspace(out var project);
        var set = TestData.EnvironmentSet("Proxmox");
        await Seed(
            set,
            // names are matched ignoring case, as configuration matches them
            TestData.ConnectionEntry(set, "PROXMOX_VE_ENDPOINT", "lab-pve", ConnectionField.Url),
            TestData.ConnectionEntry(set, "PROXMOX_VE_INSECURE", "lab-pve", ConnectionField.Insecure),
            TestData.ConnectionEntry(set, "PROXMOX_VE_API_TOKEN", "lab-pve", ConnectionField.ApiToken),
            TestData.ConnectionEntry(set, "PM_API_TOKEN_SECRET", "lab-pve", ConnectionField.ApiTokenSecret),
            TestData.ProjectEnvironmentSet(project, set));

        var environment = await Build(workspace);

        Assert.True(environment.IsValid, string.Join(" ", environment.Errors));
        Assert.Equal("https://pve.lab:8006", environment.Variables["PROXMOX_VE_ENDPOINT"]);
        Assert.Equal("true", environment.Variables["PROXMOX_VE_INSECURE"]);
        Assert.Equal("caster@pve!ci=0f1e2d3c-secret", environment.Variables["PROXMOX_VE_API_TOKEN"]);
        Assert.Equal("token *** and secret ***", environment.Masker.Apply("token caster@pve!ci=0f1e2d3c-secret and secret 0f1e2d3c-secret"));
        Assert.Equal("https://pve.lab:8006", environment.Masker.Apply("https://pve.lab:8006"));
    }

    [Fact]
    public async Task A_connection_that_is_not_configured_fails_the_run()
    {
        var workspace = await SeedWorkspace(out var project);
        var set = TestData.EnvironmentSet("Proxmox");
        await Seed(set, TestData.ConnectionEntry(set, "PROXMOX_VE_API_TOKEN", "gone", ConnectionField.ApiToken), TestData.ProjectEnvironmentSet(project, set));

        var environment = await Build(workspace);

        var error = Assert.Single(environment.Errors);
        Assert.Contains("PROXMOX_VE_API_TOKEN", error);
        Assert.Contains("\"gone\" is not configured", error);
    }

    [Fact]
    public async Task A_secret_shorter_than_the_minimum_fails_the_run()
    {
        AddProxmoxConnection(token: "a@pve!b=c");
        _options.Secrets.MinimumMaskedLength = 12;
        var workspace = await SeedWorkspace(out var project);
        var set = TestData.EnvironmentSet();
        await Seed(set, TestData.ConnectionEntry(set, "PM_API_TOKEN_SECRET", "lab-pve", ConnectionField.ApiTokenSecret), TestData.ProjectEnvironmentSet(project, set));

        var environment = await Build(workspace);

        Assert.Contains("shorter than 12 characters", Assert.Single(environment.Errors));
    }

    /// <summary>The API refuses one, but an entry saved before the rule, or seeded, is refused again at run time.</summary>
    [Fact]
    public async Task A_secret_Terraform_variable_fails_the_run()
    {
        var workspace = await SeedWorkspace(out var project);
        var set = TestData.EnvironmentSet();
        await Seed(set, TestData.SecretEntry(set, "vsphere_password", Secret, EnvironmentSetEntryKind.TerraformVariable), TestData.ProjectEnvironmentSet(project, set));

        var environment = await Build(workspace);

        Assert.Contains("can't be a Terraform variable", Assert.Single(environment.Errors));
        Assert.DoesNotContain(SecretValue, environment.Variables.Values);
    }

    /// <summary>An apply runs the saved plan, which holds the variables, so they're neither passed nor checked.</summary>
    [Fact]
    public async Task Without_Terraform_variables_an_unavailable_one_does_not_block_the_command()
    {
        var workspace = await SeedWorkspace(out var project);
        var set = TestData.EnvironmentSet();
        await Seed(
            set,
            TestData.ConnectionEntry(set, "endpoint", "gone", ConnectionField.Url, EnvironmentSetEntryKind.TerraformVariable),
            TestData.PlainEntry(set, "region", "\"east\"", EnvironmentSetEntryKind.TerraformVariable),
            TestData.SecretEntry(set, "TOKEN", Secret),
            TestData.ProjectEnvironmentSet(project, set));

        var withVariables = await Build(workspace);
        var withoutVariables = await Build(workspace, includeTerraformVariables: false);

        Assert.False(withVariables.IsValid);
        Assert.True(withoutVariables.IsValid);
        Assert.False(withoutVariables.Variables.ContainsKey("TF_VAR_region"));
        Assert.Equal(SecretValue, withoutVariables.Variables["TOKEN"]);
    }

    [Fact]
    public async Task The_sources_name_each_set_and_what_it_supplied_but_no_values()
    {
        var workspace = await SeedWorkspace(out var project);
        var first = TestData.EnvironmentSet("Alpha");
        var second = TestData.EnvironmentSet("Beta");
        await Seed(
            first, second,
            TestData.SecretEntry(first, "TOKEN", Secret),
            TestData.PlainEntry(second, "region", "\"east\"", EnvironmentSetEntryKind.TerraformVariable),
            TestData.ProjectEnvironmentSet(project, first),
            TestData.ProjectEnvironmentSet(project, second));

        var environment = await Build(workspace);

        var line = environment.DescribeSources();
        Assert.Equal("Environment Sets: \"Alpha\" (TOKEN); \"Beta\" (TF_VAR_region)", line);
        Assert.DoesNotContain(SecretValue, line);
        Assert.DoesNotContain(Secret, line);
    }

    private Task<Workspace> SeedWorkspace(string name = "test-workspace") => SeedWorkspace(out _, name);

    private Task<Workspace> SeedWorkspace(out Project project, string name = "test-workspace")
    {
        project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory, name);

        return SeedAll(workspace, project, directory, workspace);
    }

    private async Task<Workspace> SeedAll(Workspace result, params object[] entities)
    {
        await Seed(entities);
        return result;
    }

    /// <summary>The infrastructure connections the builder sees. A test adds to it.</summary>
    private readonly InfrastructureOptions _infrastructure = new();

    private Task<TerraformEnvironment> Build(Workspace workspace, params string[] baseline) =>
        Build(workspace, includeTerraformVariables: true, baseline);

    private async Task<TerraformEnvironment> Build(Workspace workspace, bool includeTerraformVariables, params string[] baseline)
    {
        var monitor = Substitute.For<IOptionsMonitor<TerraformOptions>>();
        monitor.CurrentValue.Returns(_options);
        var regexService = new RegexService(monitor);
        var apiEnvironment = new FakeApiEnvironment(_apiVariables);

        await using var provider = new ServiceCollection()
            .AddScoped<CasterContext>(_ => NewContext())
            .BuildServiceProvider();

        var builder = new TerraformEnvironmentBuilder(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _options,
            regexService,
            apiEnvironment,
            new EntryValues(new SecretSource(apiEnvironment, regexService), new InfrastructureConnectionCatalog(Infrastructure()), _options));

        return await builder.BuildAsync(workspace, baseline, includeTerraformVariables, Ct);
    }

    private IOptionsMonitor<InfrastructureOptions> Infrastructure()
    {
        var monitor = Substitute.For<IOptionsMonitor<InfrastructureOptions>>();
        monitor.CurrentValue.Returns(_infrastructure);
        return monitor;
    }

    private sealed class FakeApiEnvironment(Dictionary<string, string> variables) : IApiEnvironment
    {
        public IReadOnlyDictionary<string, string> GetVariables() => variables;

        public string GetVariable(string name) => name is not null && variables.TryGetValue(name, out var value) ? value : null;
    }
}
