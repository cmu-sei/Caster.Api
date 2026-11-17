// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;

namespace Caster.Api.Tests.Domain.Services.EnvironmentSets;

/// <summary>The target names an entry may set, shared by the validators and the run-time builder.</summary>
[Trait("Category", "Unit")]
public class EnvironmentSetRulesTests
{
    [Theory]
    [InlineData("AWS_ACCESS_KEY_ID")]
    [InlineData("_private")]
    [InlineData("TF_TOKEN_app_terraform_io")]
    [InlineData("PROXMOX_VE_PASSWORD")]
    [InlineData("HOMEDIR")]
    public void An_environment_variable_target_is_allowed(string name)
    {
        Assert.Null(EnvironmentSetRules.GetTargetNameError(EnvironmentSetEntryKind.EnvironmentVariable, name));
    }

    [Theory]
    [InlineData("TF_CLI_ARGS")]
    [InlineData("TF_CLI_ARGS_plan")]
    [InlineData("TF_DATA_DIR")]
    [InlineData("TF_WORKSPACE")]
    [InlineData("TF_IN_AUTOMATION")]
    [InlineData("tf_log")]
    [InlineData("HOME")]
    [InlineData("PATH")]
    [InlineData("path")]
    [InlineData("1ABC")]
    [InlineData("A-B")]
    [InlineData("A B")]
    [InlineData("")]
    [InlineData(null)]
    public void An_environment_variable_target_is_refused(string name)
    {
        Assert.NotNull(EnvironmentSetRules.GetTargetNameError(EnvironmentSetEntryKind.EnvironmentVariable, name));
    }

    [Theory]
    [InlineData("TF_VAR_password")]
    [InlineData("tf_var_password")]
    public void A_TF_VAR_environment_variable_is_refused_in_favour_of_the_Terraform_variable_kind(string name)
    {
        var error = EnvironmentSetRules.GetTargetNameError(EnvironmentSetEntryKind.EnvironmentVariable, name);

        Assert.Contains("Terraform variable kind", error);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("vsphere-password")]
    [InlineData("_x")]
    [InlineData("Name_2")]
    public void A_Terraform_variable_target_is_allowed(string name)
    {
        Assert.Null(EnvironmentSetRules.GetTargetNameError(EnvironmentSetEntryKind.TerraformVariable, name));
    }

    [Theory]
    [InlineData("2fa")]
    [InlineData("a.b")]
    [InlineData("a b")]
    [InlineData("")]
    public void A_Terraform_variable_target_is_refused(string name)
    {
        Assert.NotNull(EnvironmentSetRules.GetTargetNameError(EnvironmentSetEntryKind.TerraformVariable, name));
    }

    [Fact]
    public void An_environment_variable_and_a_Terraform_variable_of_the_same_name_do_not_collide()
    {
        EnvironmentSetEntry[] entries =
        [
            new() { Kind = EnvironmentSetEntryKind.EnvironmentVariable, TargetName = "token" },
            new() { Kind = EnvironmentSetEntryKind.TerraformVariable, TargetName = "token" }
        ];

        Assert.Empty(EnvironmentSetRules.GetDuplicateNames(entries));
    }

    [Fact]
    public void Two_entries_setting_the_same_variable_collide()
    {
        EnvironmentSetEntry[] entries =
        [
            new() { Kind = EnvironmentSetEntryKind.TerraformVariable, TargetName = "token" },
            new() { Kind = EnvironmentSetEntryKind.TerraformVariable, TargetName = "token" }
        ];

        Assert.Equal(["TF_VAR_token"], EnvironmentSetRules.GetDuplicateNames(entries));
    }
}
