// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.EnvironmentSets;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Caster.Api.Tests.Features.EnvironmentSets;

/// <summary>
/// <c>SeedData:EnvironmentSets</c>: sets are created once by name, checked with the API's own rules, and a
/// mistake anywhere adds nothing.
/// </summary>
public class EnvironmentSetSeederTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private const string Secret = "CASTER_SECRET_VSPHERE_PASSWORD";

    [Fact]
    public async Task A_seeded_set_is_created_with_its_entries()
    {
        var errors = Seed(VsphereSet());

        Assert.Empty(errors);
        var stored = await ReadBack(db => db.EnvironmentSets.Include(x => x.Entries).SingleAsync(x => x.Name == "vSphere", Ct));
        Assert.Equal("Lab vCenter", stored.Description);
        Assert.Equal(
            ["VSPHERE_PASSWORD", "VSPHERE_SERVER"],
            stored.Entries.Select(x => x.TargetName).OrderBy(x => x));
        var secret = stored.Entries.Single(x => x.TargetName == "VSPHERE_PASSWORD");
        Assert.Equal(EnvironmentSetValueSource.Secret, secret.ValueSource);
        Assert.Equal(Secret, secret.SecretName);
        Assert.Null(secret.Value);
        Assert.False(await ReadBack(db => db.ProjectEnvironmentSets.AnyAsync(Ct)));
    }

    [Fact]
    public async Task A_set_that_already_exists_by_name_is_left_as_it_is()
    {
        var existing = TestData.EnvironmentSet("VSPHERE");
        existing.Description = "edited in the UI";
        await Seed(existing, TestData.PlainEntry(existing, "VSPHERE_SERVER", "vcenter.edited"));

        Assert.Empty(Seed(VsphereSet()));

        var stored = await ReadBack(db => db.EnvironmentSets.Include(x => x.Entries).SingleAsync(Ct));
        Assert.Equal("edited in the UI", stored.Description);
        Assert.Equal("vcenter.edited", Assert.Single(stored.Entries).Value);
    }

    [Fact]
    public async Task Seeding_twice_creates_the_set_once()
    {
        Assert.Empty(Seed(VsphereSet()));
        Assert.Empty(Seed(VsphereSet()));

        Assert.Equal(1, await ReadBack(db => db.EnvironmentSets.CountAsync(Ct)));
    }

    [Fact]
    public async Task A_secret_source_that_is_not_set_yet_is_allowed()
    {
        var errors = Seed(VsphereSet(), available: false);

        Assert.Empty(errors);
        Assert.True(await ReadBack(db => db.EnvironmentSets.AnyAsync(x => x.Name == "vSphere", Ct)));
    }

    [Theory]
    [InlineData(EnvironmentSetEntryKind.EnvironmentVariable, "TF_CLI_ARGS", EnvironmentSetValueSource.Plain, null, "-auto-approve", "control Terraform itself")]
    [InlineData(EnvironmentSetEntryKind.EnvironmentVariable, "PATH", EnvironmentSetValueSource.Plain, null, "/tmp", "cannot be set")]
    [InlineData(EnvironmentSetEntryKind.EnvironmentVariable, "TOKEN", EnvironmentSetValueSource.Secret, "ConnectionStrings__PostgreSQL", null, "secret patterns")]
    [InlineData(EnvironmentSetEntryKind.EnvironmentVariable, "TOKEN", EnvironmentSetValueSource.Secret, Secret, "a stored value", "cannot store a value")]
    [InlineData(EnvironmentSetEntryKind.EnvironmentVariable, "TOKEN", EnvironmentSetValueSource.Plain, Secret, "x", "cannot name a secret source")]
    public async Task An_entry_the_rules_refuse_is_an_error_naming_the_set_and_entry_and_nothing_is_added(
        EnvironmentSetEntryKind kind, string targetName, EnvironmentSetValueSource valueSource, string secretName, string value, string reason)
    {
        var bad = new SeedEnvironmentSet
        {
            Name = "bad",
            Entries = [new() { Kind = kind, TargetName = targetName, ValueSource = valueSource, SecretName = secretName, Value = value }]
        };

        var error = Assert.Single(Seed(VsphereSet(), bad));

        Assert.Contains("SeedData:EnvironmentSets:1 \"bad\"", error);
        Assert.Contains(targetName, error);
        Assert.Contains(reason, error);
        Assert.False(await ReadBack(db => db.EnvironmentSets.AnyAsync(Ct)));
    }

    [Fact]
    public async Task Two_entries_setting_the_same_variable_are_an_error()
    {
        var set = VsphereSet();
        set.Entries.Add(new() { TargetName = "VSPHERE_SERVER", ValueSource = EnvironmentSetValueSource.Plain, Value = "other" });

        Assert.Contains("VSPHERE_SERVER is set by more than one entry", Assert.Single(Seed(set)));
        Assert.False(await ReadBack(db => db.EnvironmentSets.AnyAsync(Ct)));
    }

    [Fact]
    public void Two_seeds_with_the_same_name_or_a_seed_without_one_are_errors()
    {
        var errors = Seed(VsphereSet(), VsphereSet(), new SeedEnvironmentSet { Name = " " });

        Assert.Contains(errors, x => x.Contains("another seeded Environment Set has this Name"));
        Assert.Contains(errors, x => x.Contains("SeedData:EnvironmentSets:2: a Name is required"));
    }

    [Fact]
    public void No_seeds_is_no_error()
    {
        Assert.Empty(Seed());
        Assert.Empty(EnvironmentSetSeeder.Seed(null, Db, SecretSource(true), Connections()));
    }

    private static SeedEnvironmentSet VsphereSet() => new()
    {
        Name = "vSphere",
        Description = "Lab vCenter",
        Entries =
        [
            new() { Kind = EnvironmentSetEntryKind.EnvironmentVariable, TargetName = "VSPHERE_SERVER", ValueSource = EnvironmentSetValueSource.Plain, Value = "vcenter.lab" },
            new() { Kind = EnvironmentSetEntryKind.EnvironmentVariable, TargetName = "VSPHERE_PASSWORD", ValueSource = EnvironmentSetValueSource.Secret, SecretName = Secret }
        ]
    };

    private List<string> Seed(params SeedEnvironmentSet[] seeds) => Seed(seeds, available: true);

    private List<string> Seed(SeedEnvironmentSet seed, bool available) => Seed([seed], available);

    private List<string> Seed(SeedEnvironmentSet[] seeds, bool available)
    {
        using var context = NewContext();
        return EnvironmentSetSeeder.Seed(seeds, context, SecretSource(available), Connections());
    }

    private static ISecretSource SecretSource(bool available)
    {
        var monitor = Substitute.For<IOptionsMonitor<TerraformOptions>>();
        monitor.CurrentValue.Returns(new TerraformOptions { Secrets = new SecretOptions { Patterns = ["^CASTER_SECRET_"] } });
        var environment = Substitute.For<IApiEnvironment>();
        environment.GetVariable(Secret).Returns(available ? "s3cr3t-value" : null);

        return new SecretSource(environment, new RegexService(monitor));
    }

    /// <summary>One Proxmox connection, lab-pve, with an api token.</summary>
    private static IInfrastructureConnectionCatalog Connections()
    {
        var monitor = Substitute.For<IOptionsMonitor<InfrastructureOptions>>();
        monitor.CurrentValue.Returns(new InfrastructureOptions
        {
            Connections = [new InfrastructureConnectionOptions { Name = "lab-pve", Provider = "proxmox", Url = "https://pve.lab:8006", ApiToken = "caster@pve!ci=seed-test-token" }]
        });

        return new InfrastructureConnectionCatalog(monitor);
    }
}
