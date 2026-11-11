// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.Terraform;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Terraform;

/// <summary>
/// The API's infrastructure connection settings hold hypervisor credentials, and never reach a run
/// however the run's environment is inherited.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Category", "Terraform")]
public class RunEnvironmentVariableTests : IDisposable
{
    // Unique names, because the process environment is shared with every other test.
    private readonly string _connectionPassword = $"Infrastructure__Connections__{Guid.NewGuid():N}__Password";
    private readonly string _ordinary = $"CASTER_TEST_{Guid.NewGuid():N}";

    public RunEnvironmentVariableTests()
    {
        Environment.SetEnvironmentVariable(_connectionPassword, "hunter2-hunter2");
        Environment.SetEnvironmentVariable(_ordinary, "kept");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_connectionPassword, null);
        Environment.SetEnvironmentVariable(_ordinary, null);
    }

    [Fact]
    public void InheritAll_holds_back_connection_settings()
    {
        var env = Service(new EnvironmentVariableOptions { InheritAll = true }).Environment();

        Assert.DoesNotContain(_connectionPassword, env.Keys);
        Assert.Equal("kept", env[_ordinary]);
    }

    [Fact]
    public void An_Inherit_pattern_that_matches_connection_settings_still_holds_them_back()
    {
        var env = Service(new EnvironmentVariableOptions { InheritAll = false, Inherit = [".*"] }, new Regex(".*"))
            .Environment();

        Assert.DoesNotContain(_connectionPassword, env.Keys);
        Assert.Equal("kept", env[_ordinary]);
    }

    [Fact]
    public void Direct_values_are_set_as_configured()
    {
        var env = Service(new EnvironmentVariableOptions { Direct = new() { ["SSL_CERT_DIR"] = "/certs" } }).Environment();

        Assert.Equal("/certs", env["SSL_CERT_DIR"]);
    }

    [Theory]
    [InlineData("Infrastructure__Connections__0__Password", true)]
    [InlineData("INFRASTRUCTURE__CONNECTIONS__0__APITOKEN", true)]
    [InlineData("infrastructure:Connections:0:Username", true)]
    [InlineData("Infrastructure__Anything", true)]
    [InlineData("InfrastructureConnections", false)]
    [InlineData("PROXMOX_VE_API_TOKEN", false)]
    [InlineData("VSPHERE_PASSWORD", false)]
    public void IsHeldBack_matches_only_infrastructure_settings(string name, bool heldBack)
    {
        Assert.Equal(heldBack, BaseTerraformService.IsHeldBack(name));
    }

    private static TestService Service(EnvironmentVariableOptions envOptions, params Regex[] inheritRegexes)
    {
        var regexService = Substitute.For<IRegexService>();
        regexService.GetEnvironmentVariableRegexes().Returns(inheritRegexes);

        return new TestService(new TerraformOptions { EnvironmentVariables = envOptions }, regexService);
    }

    private class TestService(TerraformOptions options, IRegexService regexService)
        : ProcessTerraformService(
            options,
            Substitute.For<ILogger<ProcessTerraformService>>(),
            new MemoryCache(new MemoryCacheOptions()),
            regexService)
    {
        public Dictionary<string, string> Environment() =>
            GetEnvironmentVariables(new Workspace { Id = Guid.NewGuid(), Name = "default" });
    }
}
