// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;

namespace Caster.Api.Domain.Services;

public interface ITerraformService
{
    Task<TerraformResult> InitializeWorkspace(Workspace workspace, Action<string> outputHandler);
    Task<TerraformResult> Init(Workspace workspace, Action<string> outputHandler);
    Task<TerraformResult> SelectWorkspace(Workspace workspace, Action<string> outputHandler);
    Task<TerraformResult> Plan(Workspace workspace, bool destroy, string[] targets, string[] replaceAddresses, bool resume, Action<string> outputHandler, Func<string, Task> finalizer);
    Task<TerraformResult> Apply(Workspace workspace, bool resume, Action<string> outputHandler, Func<string, Task> finalizer);
    Task<TerraformResult> Show(Workspace workspace);
    Task<TerraformResult> Taint(Workspace workspace, string address);
    Task<TerraformResult> UntaintAsync(Workspace workspace, string address);
    Task<TerraformResult> RemoveResourcesAsync(Workspace workspace, string[] addresses);
    Task<TerraformResult> Import(Workspace workspace, string address, string id);
    Task<TerraformResult> RefreshAsync(Workspace workspace);
    Task<TerraformResult> GetOutputsAsync(Workspace workspace);
    bool IsValidVersion(string version);
    IEnumerable<string> GetVersions();
    Task<TerraformResult> CancelRun(Workspace workspace, bool force);
    Task<IEnumerable<Guid>> GetActiveWorkspaces();
    Task<TerraformResult> Resume(Workspace workspace);
}

public class TerraformResult
{
    public string Output { get; set; }
    public int ExitCode { get; set; }

    public bool IsError
    {
        get
        {
            return ExitCode != 0;
        }
    }
}
