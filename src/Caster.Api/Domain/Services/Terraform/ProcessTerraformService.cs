// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NaturalSort.Extension;

namespace Caster.Api.Domain.Services.Terraform;

public class ProcessTerraformService : BaseTerraformService
{
    private const string _binaryName = "terraform";
    private readonly ILogger<ProcessTerraformService> _logger;
    private readonly StringBuilder _outputBuilder = new StringBuilder();
    private readonly IMemoryCache _processCache;

    public ProcessTerraformService(TerraformOptions options, ILogger<ProcessTerraformService> logger, IMemoryCache cache, IRegexService regexService) : base(options, regexService)
    {
        _logger = logger;
        _processCache = cache;
    }

    private string GetBinaryPath(Workspace workspace)
    {
        return Path.Combine(
            _options.BinaryPath,
            string.IsNullOrEmpty(workspace.TerraformVersion) ?
                _options.DefaultVersion :
                workspace.TerraformVersion,
            _binaryName
        );
    }

    protected override async Task<TerraformResult> Run(Workspace workspace,
                                                       IEnumerable<string> argumentList,
                                                       Action<string> outputHandler,
                                                       bool resume = false,
                                                       Func<string, Task> finalizer = null,
                                                       bool redirectStandardError = true)
    {
        int exitCode;
        _outputBuilder.Clear();

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = this.GetBinaryPath(workspace),
            WorkingDirectory = workspace.GetPath(_options.RootWorkingDirectory),
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = redirectStandardError
        };

        var envVars = this.GetEnvironmentVariables();
        startInfo.EnvironmentVariables.Clear();

        foreach (var kvp in envVars)
        {
            startInfo.EnvironmentVariables.Add(kvp.Key, kvp.Value);
        }

        using (Process process = new Process())
        {
            process.StartInfo = startInfo;

            if (argumentList != null)
            {
                foreach (string arg in argumentList)
                {
                    process.StartInfo.ArgumentList.Add(arg);
                }
            }

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputHandler?.Invoke(e.Data);
                }
            };
            process.OutputDataReceived += OutputHandler;

            if (redirectStandardError)
            {
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputHandler?.Invoke(e.Data);
                    }
                };
                process.ErrorDataReceived += OutputHandler;
            }

            process.Start();
            _processCache.Set(workspace.Id, process);
            process.BeginOutputReadLine();

            if (redirectStandardError)
            {
                process.BeginErrorReadLine();
            }

            process.WaitForExit();
            _processCache.Remove(workspace.Id);
            exitCode = process.ExitCode;
        }

        await (finalizer?.Invoke(_outputBuilder.ToString()) ?? Task.CompletedTask);

        return new TerraformResult
        {
            Output = _outputBuilder.ToString(),
            ExitCode = exitCode
        };
    }

    private void OutputHandler(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
        {
            _outputBuilder.AppendLine(e.Data);
            _logger.LogTrace(e.Data);
        }
    }

    public override bool IsValidVersion(string version)
    {
        var path = Path.Combine(
            _options.BinaryPath,
            version);

        return System.IO.Directory.Exists(path);
    }

    public override IEnumerable<string> GetVersions()
    {
        return System.IO.Directory.EnumerateDirectories(_options.BinaryPath)
            .Select(x => Path.GetFileName(x))
            .OrderByDescending(x => x, StringComparison.OrdinalIgnoreCase.WithNaturalSort());
    }

    public override Task<TerraformResult> CancelRun(Workspace workspace, bool force)
    {
        if (_processCache.TryGetValue(workspace.Id, out Process p))
        {
            if (force)
            {
                p.Kill();
            }
            else
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    throw new NotImplementedException("Cancel is not supported on the current platform. Try a forced cancel instead.");
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using Process process = new Process();
                process.StartInfo = startInfo;

                process.StartInfo.ArgumentList.Add("-c");
                process.StartInfo.ArgumentList.Add($"kill {p.Id}");

                process.OutputDataReceived += OutputHandler;
                process.ErrorDataReceived += OutputHandler;

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit();

                _logger.LogDebug($"Cancel process: exit code: {process.ExitCode}. output: {_outputBuilder}");

                return Task.FromResult(new TerraformResult
                {
                    ExitCode = process.ExitCode,
                    Output = _outputBuilder.ToString()
                });
            }
        }
        else
        {
            _logger.LogDebug("Couldn't find process to cancel");
        }

        return null;
    }

    public override async Task<IEnumerable<Guid>> GetActiveWorkspaces()
    {
        return Enumerable.Empty<Guid>();
    }

    protected override string GetBasePath(Workspace workspace) => workspace.GetPath(_options.RootWorkingDirectory);
}
