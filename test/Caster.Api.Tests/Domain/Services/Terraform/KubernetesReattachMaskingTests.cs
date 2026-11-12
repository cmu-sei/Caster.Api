// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Domain.Services.Terraform;
using k8s.Models;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Terraform;

/// <summary>
/// After an API restart the Kubernetes service reattaches to a running job. The job's spec carries the
/// values it was started with, which may differ from today's: a credential rotated mid-run, or one whose
/// secret, entry or set has gone since. Every value that isn't a plain one known today is masked.
/// </summary>
[Trait("Category", "Unit")]
public class KubernetesReattachMaskingTests
{
    private static V1Container Job(params (string Name, string Value)[] env) =>
        new() { Env = env.Select(x => new V1EnvVar { Name = x.Name, Value = x.Value }).ToList() };

    private static TerraformEnvironment Today(Dictionary<string, string> variables, params string[] secretNames) =>
        new()
        {
            Variables = new Dictionary<string, string>(variables, StringComparer.Ordinal),
            Masker = new OutputMasker(secretNames.Select(x => variables[x])),
            SecretNames = new HashSet<string>(secretNames, StringComparer.Ordinal)
        };

    private static string[] ToMask(V1Container job, TerraformEnvironment environment) =>
        KubernetesTerraformService.JobValuesToMask(job, environment).ToArray();

    [Fact]
    public void A_secret_rotated_mid_run_is_masked_with_todays_value()
    {
        var environment = Today(new()
        {
            ["PROXMOX_VE_API_TOKEN"] = "new-token-value",
            ["PROXMOX_VE_ENDPOINT"] = "https://pve.lab:8006",
            ["TF_IN_AUTOMATION"] = "true"
        }, "PROXMOX_VE_API_TOKEN");
        var job = Job(
            ("PROXMOX_VE_API_TOKEN", "old-token-value"),
            ("PROXMOX_VE_ENDPOINT", "https://pve.lab:8006"),
            ("TF_IN_AUTOMATION", "true"));

        var masker = environment.Masker.With(ToMask(job, environment));

        Assert.Equal(["old-token-value"], ToMask(job, environment));
        Assert.Equal("*** then ***", masker.Apply("old-token-value then new-token-value"));
        Assert.Equal("https://pve.lab:8006", masker.Apply("https://pve.lab:8006"));
    }

    [Fact]
    public void A_secret_today_is_masked_even_when_unchanged()
    {
        var environment = Today(new() { ["PROXMOX_VE_API_TOKEN"] = "same-token-value" }, "PROXMOX_VE_API_TOKEN");

        Assert.Equal(["same-token-value"], ToMask(Job(("PROXMOX_VE_API_TOKEN", "same-token-value")), environment));
    }

    [Fact]
    public void A_secret_no_longer_given_is_masked()
    {
        // The secret, entry or set is gone, or the secret became too short, so today's
        // environment neither gives the variable nor names it as a secret.
        var environment = Today(new() { ["TF_IN_AUTOMATION"] = "true" });
        var job = Job(("PROXMOX_VE_API_TOKEN", "removed-token-value"), ("TF_IN_AUTOMATION", "true"));

        var masker = environment.Masker.With(ToMask(job, environment));

        Assert.Equal(["removed-token-value"], ToMask(job, environment));
        Assert.Equal("token *** rejected", masker.Apply("token removed-token-value rejected"));
    }

    [Fact]
    public void A_plain_value_changed_mid_run_is_masked()
    {
        var environment = Today(new() { ["PROXMOX_VE_ENDPOINT"] = "https://pve2.lab:8006" });

        Assert.Equal(["https://pve.lab:8006"], ToMask(Job(("PROXMOX_VE_ENDPOINT", "https://pve.lab:8006")), environment));
    }

    [Fact]
    public void Empty_values_and_a_job_without_env_add_nothing()
    {
        var environment = Today(new() { ["TF_IN_AUTOMATION"] = "true" });

        Assert.Empty(ToMask(new V1Container(), environment));
        Assert.Empty(ToMask(Job(("EMPTY", "")), environment));
    }
}
