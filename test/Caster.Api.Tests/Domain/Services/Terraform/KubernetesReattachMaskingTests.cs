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
/// values it was started with, which may differ from today's (a credential rotated mid-run), so those are
/// masked as well as the current ones.
/// </summary>
[Trait("Category", "Unit")]
public class KubernetesReattachMaskingTests
{
    [Fact]
    public void The_jobs_values_for_todays_secret_names_are_masked_with_todays_values()
    {
        var environment = new TerraformEnvironment
        {
            Masker = new OutputMasker(["new-token-value"]),
            SecretNames = new HashSet<string>(StringComparer.Ordinal) { "PROXMOX_VE_API_TOKEN" }
        };
        var container = new V1Container
        {
            Env =
            [
                new V1EnvVar { Name = "PROXMOX_VE_API_TOKEN", Value = "old-token-value" },
                new V1EnvVar { Name = "PROXMOX_VE_ENDPOINT", Value = "https://pve.lab:8006" },
                new V1EnvVar { Name = "TF_IN_AUTOMATION", Value = "true" }
            ]
        };

        var masker = environment.Masker.With(KubernetesTerraformService.JobSecretValues(container, environment));

        Assert.Equal(["old-token-value"], KubernetesTerraformService.JobSecretValues(container, environment).ToArray());
        Assert.Equal("*** then ***", masker.Apply("old-token-value then new-token-value"));
        Assert.Equal("https://pve.lab:8006", masker.Apply("https://pve.lab:8006"));
    }

    [Fact]
    public void A_job_without_env_adds_nothing()
    {
        var environment = new TerraformEnvironment { Masker = new OutputMasker(["new-token-value"]) };

        Assert.Empty(KubernetesTerraformService.JobSecretValues(new V1Container(), environment));
    }
}
