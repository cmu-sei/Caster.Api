// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using Caster.Api.Features.Workspaces.Interfaces;

namespace Caster.Api.Features.Workspaces;

public abstract class WorkspaceFields : IWorkspaceUpdateRequest
{
    /// <summary>
    /// The Name of the Workspace
    /// </summary>
    [DataMember]
    public string Name { get; set; }

    /// <summary>
    /// True if this Workspace will be dynamically assigned a Host on first Run
    /// </summary>
    [DataMember]
    public bool DynamicHost { get; set; }

    /// <summary>
    /// The version of Terraform that will be used for Runs in this Workspace.
    /// If null or empty, the default version will be used.
    /// </summary>
    [DataMember]
    public string TerraformVersion { get; set; }

    /// <summary>
    /// Limit the number of concurrent operations as Terraform walks the graph.
    /// If null, the Terraform default will be used.
    /// </summary>
    [DataMember]
    public int? Parallelism { get; set; }

    /// <summary>
    /// If set, the number of consecutive failed destroys in an Azure Workspace before
    /// Caster will attempt to mitigate by removing azurerm_resource_group children from the state.
    /// </summary>
    [DataMember]
    public int? AzureDestroyFailureThreshold { get; set; }
}
