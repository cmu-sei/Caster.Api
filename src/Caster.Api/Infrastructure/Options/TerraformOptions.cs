// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using k8s.Models;

namespace Caster.Api.Infrastructure.Options;

public class TerraformOptions
{
    public string BinaryPath { get; set; }
    public string DefaultVersion { get; set; }
    public string PluginDirectory { get; set; }
    public string RootWorkingDirectory { get; set; }
    public double OutputSaveInterval { get; set; }
    public string GitlabApiUrl { get; set; }
    public string GitlabToken { get; set; }
    public int? GitlabGroupId { get; set; }
    public int StateRetryCount { get; set; }
    public int StateRetryIntervalSeconds { get; set; }
    public int? AzureDestroyFailureThreshhold { get; set; }
    public int MaxParallelism { get; set; }
    public KubernetesJobOptions KubernetesJobs { get; set; } = new();
    public EnvironmentVariableOptions EnvironmentVariables { get; set; } = new();
}

public class KubernetesJobOptions
{
    public bool Enabled { get; set; }
    public string Namespace { get; set; }
    public string Context { get; set; }

    public bool UseHostVolume { get; set; }
    public string HostVolumePath { get; set; }

    public string PersistentVolumeClaimName { get; set; }
    public string VolumeMountPath { get; set; }
    public string VolumeMountSubPath { get; set; }

    /// <summary>
    /// The root working directory inside the kubernetes pod running the job
    /// </summary>
    public string RootWorkingDirectory { get; set; }

    public string ImageRegistry { get; set; }
    public string ImageName { get; set; }
    public string[] ImageTags { get; set; }
    public bool QueryImageTags { get; set; }
    public int QueryImageTagsMinutes { get; set; }
    public string QueryImageTagsRegex { get; set; }

    public ConfigMap[] ConfigMaps { get; set; } = [];

    /// <summary>
    /// Path to a YAML file defining a V1Job template. Takes precedence over JobTemplateYaml.
    /// Caster uses this as a base and overrides only the properties it needs to control.
    /// </summary>
    public string JobTemplateFile { get; set; }

    /// <summary>
    /// Inline YAML string defining a V1Job template. Caster uses this as a base
    /// and overrides only the properties it needs to control.
    /// </summary>
    public string JobTemplateYaml { get; set; }

    /// <summary>
    /// Maximum seconds to wait for a pod to become ready. 0 = no timeout. Default: 120.
    /// </summary>
    public int PodReadyTimeoutSeconds { get; set; } = 120;

}

public class EnvironmentVariableOptions
{
    public bool InheritAll { get; set; }
    public string[] Inherit { get; set; } = [];
    public Dictionary<string, string> Direct { get; set; } = [];
}

public class ConfigMap
{
    public string Name { get; set; }
    public string MountPath { get; set; }
    public string SubPath { get; set; }
}
