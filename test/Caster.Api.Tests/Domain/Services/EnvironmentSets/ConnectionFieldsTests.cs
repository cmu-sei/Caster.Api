// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Infrastructure.Options;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.EnvironmentSets;

/// <summary>What each connection field is, worked out from a connection's settings.</summary>
[Trait("Category", "Unit")]
public class ConnectionFieldsTests
{
    private static InfrastructureConnectionOptions Proxmox() => new()
    {
        Name = "pve",
        Provider = "proxmox",
        Url = "https://pve.lab:8006/",
        Username = "caster@pve",
        Password = "pve-password",
        ApiToken = "caster@pve!ci=0123-secret=part",
        InsecureSkipVerify = true,
        IsoLocations = ["iso-stock:iso/stock-"]
    };

    private static InfrastructureConnectionOptions Vsphere() => new()
    {
        Name = "vc",
        Provider = "vsphere",
        Url = "https://vcenter.lab/sdk",
        Username = "caster@vsphere.local",
        Password = "vc-password",
        IsoLocations = ["[iso-ds] stock/linux/"],
        Datacenters = ["DC1"]
    };

    private static string Value(InfrastructureConnectionOptions connection, ConnectionField field)
    {
        var resolved = ConnectionFields.Resolve(connection, field);
        Assert.True(resolved.IsAvailable, resolved.Unavailable);
        return resolved.Value;
    }

    [Theory]
    [InlineData(ConnectionField.Url, "https://pve.lab:8006/")]
    [InlineData(ConnectionField.Host, "pve.lab:8006")]
    [InlineData(ConnectionField.ApiUrl, "https://pve.lab:8006/api2/json")]
    [InlineData(ConnectionField.Insecure, "true")]
    [InlineData(ConnectionField.Username, "caster@pve")]
    [InlineData(ConnectionField.Password, "pve-password")]
    [InlineData(ConnectionField.ApiToken, "caster@pve!ci=0123-secret=part")]
    [InlineData(ConnectionField.ApiTokenId, "caster@pve!ci")]
    [InlineData(ConnectionField.ApiTokenSecret, "0123-secret=part")]
    [InlineData(ConnectionField.IsoStorage, "iso-stock")]
    public void Proxmox_fields(ConnectionField field, string expected)
    {
        Assert.Equal(expected, Value(Proxmox(), field));
    }

    [Theory]
    [InlineData(ConnectionField.Url, "https://vcenter.lab/sdk")]
    [InlineData(ConnectionField.Host, "vcenter.lab")]
    [InlineData(ConnectionField.Insecure, "false")]
    [InlineData(ConnectionField.Username, "caster@vsphere.local")]
    [InlineData(ConnectionField.Password, "vc-password")]
    [InlineData(ConnectionField.IsoStorage, "iso-ds")]
    [InlineData(ConnectionField.IsoFolder, "stock/linux")]
    [InlineData(ConnectionField.Datacenter, "DC1")]
    public void Vsphere_fields(ConnectionField field, string expected)
    {
        Assert.Equal(expected, Value(Vsphere(), field));
    }

    [Fact]
    public void The_api_url_is_never_doubled()
    {
        var connection = Proxmox();
        connection.Url = "https://pve.lab:8006/api2/json/";

        Assert.Equal("https://pve.lab:8006/api2/json", Value(connection, ConnectionField.ApiUrl));
    }

    [Fact]
    public void A_datastore_location_without_a_folder_has_an_empty_folder()
    {
        var connection = Vsphere();
        connection.IsoLocations = ["iso-ds"];

        Assert.Equal("iso-ds", Value(connection, ConnectionField.IsoStorage));
        Assert.Equal("", Value(connection, ConnectionField.IsoFolder));
    }

    [Theory]
    [InlineData(ConnectionField.ApiToken)]
    [InlineData(ConnectionField.ApiUrl)]
    [InlineData(ConnectionField.ApiTokenSecret)]
    public void Proxmox_only_fields_do_not_apply_to_vsphere(ConnectionField field)
    {
        Assert.False(ConnectionFields.AppliesTo(field, "vsphere"));
        Assert.False(ConnectionFields.Resolve(Vsphere(), field).IsAvailable);
    }

    [Theory]
    [InlineData(ConnectionField.IsoFolder)]
    [InlineData(ConnectionField.Datacenter)]
    public void Vsphere_only_fields_do_not_apply_to_proxmox(ConnectionField field)
    {
        Assert.False(ConnectionFields.AppliesTo(field, "proxmox"));
    }

    [Fact]
    public void Two_iso_locations_or_datacenters_are_ambiguous()
    {
        var connection = Vsphere();
        connection.IsoLocations = ["iso-a", "iso-b"];
        connection.Datacenters = ["DC1", "DC2"];

        Assert.Contains("ambiguous", ConnectionFields.Resolve(connection, ConnectionField.IsoStorage).Unavailable);
        Assert.Contains("ambiguous", ConnectionFields.Resolve(connection, ConnectionField.Datacenter).Unavailable);
    }

    [Fact]
    public void A_field_whose_setting_is_missing_says_so()
    {
        var connection = Proxmox();
        connection.ApiToken = null;
        connection.Password = "";

        Assert.Equal("the connection has no ApiToken.", ConnectionFields.Resolve(connection, ConnectionField.ApiTokenId).Unavailable);
        Assert.Equal("the connection has no Password.", ConnectionFields.Resolve(connection, ConnectionField.Password).Unavailable);
    }

    [Fact]
    public void Unavailable_reasons_never_carry_a_value()
    {
        var connection = Proxmox();
        connection.ApiToken = "not-a-token";

        var resolved = ConnectionFields.Resolve(connection, ConnectionField.ApiTokenSecret);

        Assert.False(resolved.IsAvailable);
        Assert.DoesNotContain("not-a-token", resolved.Unavailable);
    }

    [Theory]
    [InlineData(ConnectionField.Password, true)]
    [InlineData(ConnectionField.ApiToken, true)]
    [InlineData(ConnectionField.ApiTokenId, true)]
    [InlineData(ConnectionField.ApiTokenSecret, true)]
    [InlineData(ConnectionField.Username, false)]
    [InlineData(ConnectionField.Url, false)]
    public void Credentials_are_secret(ConnectionField field, bool secret)
    {
        Assert.Equal(secret, ConnectionFields.IsSecret(field));
    }
}
