// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Terraform;

public abstract class BaseTerraformService : ITerraformService
{
    protected readonly TerraformOptions _options;
    protected IRegexService _regexService;
    protected string _workspaceName = null;

    public BaseTerraformService(TerraformOptions options, IRegexService regexService)
    {
        _options = options;
        _regexService = regexService;
    }

    /// <summary>
    /// Combines Init and Select Workspace
    /// </summary>
    public async Task<TerraformResult> InitializeWorkspace(Workspace workspace, Action<string> outputHandler)
    {
        // Set TF_WORKSPACE env var for init to workaround bug with an empty configuration
        // Will need to avoid this for a remote state init
        _workspaceName = workspace.Name;
        var result = new TerraformResult();

        try
        {
            result = await this.Init(workspace, outputHandler);
        }
        catch (TerraformCommandConflictException ex)
        {
            if (ex.Command != "workspace")
            {
                result.Output = ex.Message;
                result.ExitCode = -1;
            }
        }

        _workspaceName = null;

        if (!result.IsError)
        {
            if (!workspace.IsDefault)
            {
                var workspaceResult = await this.SelectWorkspace(workspace, outputHandler);
                result.Output += workspaceResult.Output;
                result.ExitCode = workspaceResult.ExitCode;
            }
        }

        return result;
    }

    public async Task<TerraformResult> Init(Workspace workspace, Action<string> outputHandler)
    {
        List<string> args = new List<string> { "init", "-input=false" };

        if (!string.IsNullOrEmpty(_options.PluginDirectory))
        {
            args.Add($"-plugin-dir={_options.PluginDirectory}");
        }

        return await this.Run(workspace, args, outputHandler);
    }

    public async Task<TerraformResult> SelectWorkspace(Workspace workspace, Action<string> outputHandler)
    {
        List<string> args = new List<string> { "workspace", "select", workspace.Name };
        return await this.Run(workspace, args, outputHandler);
    }

    public async Task<TerraformResult> Plan(Workspace workspace, bool destroy, string[] targets, string[] replaceAddresses, bool resume, Action<string> outputHandler, Func<string, Task> finalizer)
    {
        List<string> args = new List<string> { "plan", "-input=false", "-out=plan" };

        if (workspace.Parallelism.HasValue)
        {
            args.Add($"-parallelism={workspace.Parallelism}");
        }

        if (destroy)
        {
            args.Add("-destroy");
        }

        foreach (string target in targets)
        {
            args.Add($"--target={target}");
        }

        foreach (string address in replaceAddresses)
        {
            args.Add($"-replace={address}");
        }

        return await this.Run(workspace, args, outputHandler, resume, finalizer);
    }

    public async Task<TerraformResult> Apply(Workspace workspace, bool resume, Action<string> outputHandler, Func<string, Task> finalizer)
    {
        List<string> args = new List<string> { "apply" };

        if (workspace.Parallelism.HasValue)
        {
            args.Add($"-parallelism={workspace.Parallelism}");
        }

        args.Add("plan");

        return await this.Run(workspace, args, outputHandler, resume, finalizer);
    }

    public async Task<TerraformResult> Show(Workspace workspace)
    {
        List<string> args = new List<string> { "show", "-json", "plan" };
        return await this.Run(workspace, args, null, redirectStandardError: false);
    }

    public async Task<TerraformResult> Taint(Workspace workspace, string address)
    {
        List<string> args = new List<string>() { "taint" };
        AddStatePathArg(workspace, ref args);
        args.Add(address);

        return await this.Run(workspace, args, null);
    }

    public async Task<TerraformResult> UntaintAsync(Workspace workspace, string address)
    {
        List<string> args = new List<string>() { "untaint" };
        AddStatePathArg(workspace, ref args);
        args.Add(address);

        return await this.Run(workspace, args, null);
    }

    public async Task<TerraformResult> RemoveResourcesAsync(Workspace workspace, string[] addresses)
    {
        List<string> args = new List<string>() { "state", "rm" };
        AddStatePathArg(workspace, ref args);

        foreach (var addr in addresses)
        {
            args.Add(addr);
        }

        return await this.Run(workspace, args, null);
    }

    public async Task<TerraformResult> Import(Workspace workspace, string address, string id)
    {
        List<string> args = new List<string> { "import" };
        AddStatePathArg(workspace, ref args);
        args.Add(address);
        args.Add(id);

        return await this.Run(workspace, args, null);
    }

    public async Task<TerraformResult> RefreshAsync(Workspace workspace)
    {
        List<string> args = new List<string>() { "refresh" };
        AddStatePathArg(workspace, ref args);
        return await this.Run(workspace, args, null);
    }

    public async Task<TerraformResult> GetOutputsAsync(Workspace workspace)
    {
        List<string> args = new List<string>() { "output", "-json" };
        AddStatePathArg(workspace, ref args);
        return await this.Run(workspace, args, null);
    }

    public async Task<TerraformResult> Resume(Workspace workspace)
    {
        return await this.Run(workspace, null, null, resume: true);
    }

    protected void AddStatePathArg(Workspace workspace, ref List<string> args)
    {
        var basePath = GetBasePath(workspace);
        var statePath = workspace.GetStatePath(basePath, false);
        if (!string.IsNullOrEmpty(statePath))
        {
            args.Add($"-state={statePath}");
        }
    }

    protected Dictionary<string, string> GetEnvironmentVariables()
    {
        var envVars = new Dictionary<string, string>
        {
            { "TF_IN_AUTOMATION", "true" }
        };

        if (!string.IsNullOrEmpty(_workspaceName))
        {
            envVars.Add("TF_WORKSPACE", _workspaceName);
        }

        var envVarOptions = _options.EnvironmentVariables;

        var appEnvVars = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .ToDictionary(
                e => e.Key?.ToString() ?? string.Empty,
                e => e.Value?.ToString() ?? string.Empty
            );

        if (!envVarOptions.InheritAll)
        {
            Regex[] regexes = _regexService.GetEnvironmentVariableRegexes();

            appEnvVars = appEnvVars.Where(x =>
            {
                foreach (var regex in regexes)
                {
                    if (regex.IsMatch(x.Key))
                    {
                        return true;
                    }
                }

                return false;
            }).ToDictionary();
        }

        foreach (var appEnvVar in appEnvVars)
        {
            envVars.Add(appEnvVar.Key, appEnvVar.Value);
        }

        foreach (var kvp in envVarOptions.Direct)
        {
            envVars[kvp.Key] = kvp.Value;
        }

        return envVars;
    }

    protected abstract string GetBasePath(Workspace workspace);

    public abstract bool IsValidVersion(string version);

    public abstract IEnumerable<string> GetVersions();

    public abstract Task<TerraformResult> CancelRun(Workspace workspace, bool force);

    public abstract Task<IEnumerable<Guid>> GetActiveWorkspaces();

    protected abstract Task<TerraformResult> Run(Workspace workspace, IEnumerable<string> argumentList,
                                                 Action<string> outputHandler, bool resume = false,
                                                 Func<string, Task> finalizer = null, bool redirectStandardError = true);
}
