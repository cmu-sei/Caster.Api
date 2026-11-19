// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Terraform;

public abstract class BaseTerraformService : ITerraformService
{
    protected readonly TerraformOptions _options;
    private readonly ITerraformEnvironmentBuilder _environmentBuilder;

    public BaseTerraformService(TerraformOptions options, ITerraformEnvironmentBuilder environmentBuilder)
    {
        _options = options;
        _environmentBuilder = environmentBuilder;
    }

    /// <summary>
    /// Combines Init and Select Workspace
    /// </summary>
    public async Task<TerraformResult> InitializeWorkspace(Workspace workspace, Action<string> outputHandler)
    {
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

        if (_options.SelectWorkspace && !result.IsError && !workspace.IsDefault)
        {
            var workspaceResult = await this.SelectWorkspace(workspace, outputHandler);
            result.Output += workspaceResult.Output;
            result.ExitCode = workspaceResult.ExitCode;
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

    /// <summary>
    /// API variables a run needs even when they are not inherited. None by default: a
    /// Kubernetes job gets PATH and HOME from its image.
    /// </summary>
    protected virtual IEnumerable<string> BaselineEnvironmentVariables => [];

    /// <summary>
    /// Builds the run's environment from the project's Environment Sets and masks their
    /// secret values in everything the command outputs: the streamed lines, what the
    /// finalizer saves and the result.
    /// </summary>
    protected async Task<TerraformResult> Run(Workspace workspace, IEnumerable<string> argumentList,
                                              Action<string> outputHandler, bool resume = false,
                                              Func<string, Task> finalizer = null, bool redirectStandardError = true)
    {
        var arguments = argumentList?.ToArray();
        var command = arguments?.FirstOrDefault();

        var environment = await _environmentBuilder.BuildAsync(
            workspace, BaselineEnvironmentVariables, ReadsVariables(command, resume));

        // A plan or apply names the sets it used first, so the saved output records them.
        var sourcesLine = !resume && environment.IsValid && command is "plan" or "apply"
            ? environment.DescribeSources()
            : null;

        // The masker is read at each use: a command that reattaches to a running job adds the
        // values that job was given.
        Action<string> maskedOutputHandler = outputHandler is null
            ? null
            : line => outputHandler(environment.Masker.Apply(line));

        Func<string, Task> maskedFinalizer = finalizer is null
            ? null
            : output => finalizer(WithSources(sourcesLine, environment.Masker.Apply(output)));

        if (sourcesLine is not null)
        {
            outputHandler?.Invoke(sourcesLine);
        }

        var result = await RunCore(workspace, arguments, environment, maskedOutputHandler, resume, maskedFinalizer, redirectStandardError);

        if (result is not null)
        {
            result.Output = WithSources(sourcesLine, environment.Masker.Apply(result.Output));
        }

        return result;
    }

    /// <summary>
    /// Whether a command reads the configuration's variables, so needs the Terraform-variable
    /// entries. An apply runs the saved plan, which holds them; init, workspace, show, output
    /// and state commands don't evaluate them. A resumed command is treated as reading them.
    /// </summary>
    public static bool ReadsVariables(string command, bool resume) =>
        resume || command is "plan" or "import" or "refresh" or null;

    private static string WithSources(string sourcesLine, string output) =>
        sourcesLine is null ? output : $"{sourcesLine}{Environment.NewLine}{output}";

    /// <summary>
    /// The result of a command that cannot start because its environment is incomplete
    /// (<see cref="TerraformEnvironment.Errors"/>). Fails rather than starting without a
    /// credential, which would let providers fall back to other ambient credentials.
    /// </summary>
    protected static TerraformResult EnvironmentFailure(TerraformEnvironment environment, Action<string> outputHandler)
    {
        var lines = new List<string> { "Terraform was not started: the project's Environment Sets are incomplete." };
        lines.AddRange(environment.Errors);

        foreach (var line in lines)
        {
            outputHandler?.Invoke(line);
        }

        return new TerraformResult
        {
            ExitCode = -1,
            Output = string.Join(Environment.NewLine, lines) + Environment.NewLine
        };
    }

    public abstract bool EnableOutputTimer { get; }
    public abstract bool SupportsResume { get; }

    protected abstract string GetBasePath(Workspace workspace);

    public abstract bool IsValidVersion(string version);

    public abstract IEnumerable<string> GetVersions();

    public abstract Task<TerraformResult> CancelRun(Workspace workspace, bool force);

    public abstract Task<IEnumerable<Guid>> GetActiveWorkspaces();

    /// <summary>
    /// Runs one Terraform command with <paramref name="environment"/>. Must not start a new
    /// process or job when <see cref="TerraformEnvironment.IsValid"/> is false
    /// (return <see cref="EnvironmentFailure"/>); reattaching to one already running is fine.
    /// </summary>
    protected abstract Task<TerraformResult> RunCore(Workspace workspace, IEnumerable<string> argumentList,
                                                     TerraformEnvironment environment, Action<string> outputHandler,
                                                     bool resume, Func<string, Task> finalizer, bool redirectStandardError);
}
