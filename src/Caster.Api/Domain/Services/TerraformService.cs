// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NaturalSort.Extension;

namespace Caster.Api.Domain.Services
{
    public interface ITerraformService
    {
        TerraformResult InitializeWorkspace(Workspace workspace, DataReceivedEventHandler outputHandler);
        TerraformResult Init(Workspace workspace, DataReceivedEventHandler outputHandler);
        TerraformResult SelectWorkspace(Workspace workspace, DataReceivedEventHandler outputHandler);
        TerraformResult Plan(Workspace workspace, bool destroy, string[] targets, DataReceivedEventHandler outputHandler);
        TerraformResult Apply(Workspace workspace, DataReceivedEventHandler outputHandler);
        TerraformResult Show(Workspace workspace);
        TerraformResult Taint(Workspace workspace, string address, string statePath);
        TerraformResult Untaint(Workspace workspace, string address, string statePath);
        TerraformResult RemoveResources(Workspace workspace, string[] addresses, string statePath);
        TerraformResult Import(Workspace workspace, string address, string id, string statePath);
        TerraformResult Refresh(Workspace workspace, string statePath);
        TerraformResult GetOutputs(Workspace workspace, string statePath);
        bool IsValidVersion(string version);
        IEnumerable<string> GetVersions();
        TerraformResult CancelRun(Workspace workspace, bool force);
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

    public class TerraformService : ITerraformService
    {
        private const string _binaryName = "terraform";
        private readonly TerraformOptions _options;
        private readonly ILogger<TerraformService> _logger;
        private readonly StringBuilder _outputBuilder = new StringBuilder();
        private readonly IMemoryCache _processCache;
        private string _workspaceName = null;

        public TerraformService(TerraformOptions options, ILogger<TerraformService> logger, IMemoryCache cache)
        {
            _options = options;
            _logger = logger;
            _processCache = cache;
        }

        private string GetBinaryPath(Workspace workspace)
        {
            return System.IO.Path.Combine(
                _options.BinaryPath,
                string.IsNullOrEmpty(workspace.TerraformVersion) ?
                    _options.DefaultVersion :
                    workspace.TerraformVersion,
                _binaryName
            );
        }

        private TerraformResult Run(Workspace workspace, IEnumerable<string> argumentList, DataReceivedEventHandler outputHandler, bool redirectStandardError = true)
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

            startInfo.EnvironmentVariables.Add("TF_IN_AUTOMATION", "true");

            if (!string.IsNullOrEmpty(_workspaceName))
            {
                startInfo.EnvironmentVariables.Add("TF_WORKSPACE", _workspaceName);
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

                process.OutputDataReceived += outputHandler;
                process.OutputDataReceived += OutputHandler;

                if (redirectStandardError)
                {
                    process.ErrorDataReceived += outputHandler;
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

        /// <summary>
        /// Combines Init and Select Workspace
        /// </summary>
        public TerraformResult InitializeWorkspace(Workspace workspace, DataReceivedEventHandler outputHandler)
        {
            // Set TF_WORKSPACE env var for init to workaround bug with an empty configuration
            // Will need to avoid this for a remote state init
            _workspaceName = workspace.Name;
            var result = this.Init(workspace, outputHandler);
            _workspaceName = null;

            if (!result.IsError)
            {
                if (!workspace.IsDefault)
                {
                    var workspaceResult = this.SelectWorkspace(workspace, outputHandler);
                    result.Output += workspaceResult.Output;
                    result.ExitCode = workspaceResult.ExitCode;
                }
            }

            return result;
        }

        public TerraformResult Init(Workspace workspace, DataReceivedEventHandler outputHandler)
        {
            List<string> args = new List<string> { "init", "-input=false" };

            if (!string.IsNullOrEmpty(_options.PluginDirectory))
            {
                args.Add($"-plugin-dir={_options.PluginDirectory}");
            }

            return this.Run(workspace, args, outputHandler);
        }

        public TerraformResult SelectWorkspace(Workspace workspace, DataReceivedEventHandler outputHandler)
        {
            List<string> args = new List<string> { "workspace", "select", workspace.Name };
            return this.Run(workspace, args, outputHandler);
        }

        public TerraformResult Plan(Workspace workspace, bool destroy, string[] targets, DataReceivedEventHandler outputHandler)
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

            return this.Run(workspace, args, outputHandler);
        }

        public TerraformResult Apply(Workspace workspace, DataReceivedEventHandler outputHandler)
        {
            List<string> args = new List<string> { "apply" };

            if (workspace.Parallelism.HasValue)
            {
                args.Add($"-parallelism={workspace.Parallelism}");
            }

            args.Add("plan");

            return this.Run(workspace, args, outputHandler);
        }

        public TerraformResult Show(Workspace workspace)
        {
            List<string> args = new List<string> { "show", "-json", "plan" };
            return this.Run(workspace, args, null, redirectStandardError: false);
        }

        public TerraformResult Taint(Workspace workspace, string address, string statePath)
        {
            List<string> args = new List<string>() { "taint" };
            AddStatePathArg(statePath, ref args);
            args.Add(address);

            return this.Run(workspace, args, null);
        }

        public TerraformResult Untaint(Workspace workspace, string address, string statePath)
        {
            List<string> args = new List<string>() { "untaint" };
            AddStatePathArg(statePath, ref args);
            args.Add(address);

            return this.Run(workspace, args, null);
        }

        public TerraformResult RemoveResources(Workspace workspace, string[] addresses, string statePath)
        {
            List<string> args = new List<string>() { "state", "rm" };
            AddStatePathArg(statePath, ref args);

            foreach (var addr in addresses)
            {
                args.Add(addr);
            }

            return this.Run(workspace, args, null);
        }

        public TerraformResult Import(Workspace workspace, string address, string id, string statePath)
        {
            List<string> args = new List<string> { "import" };
            AddStatePathArg(statePath, ref args);
            args.Add(address);
            args.Add(id);

            return this.Run(workspace, args, null);
        }

        public TerraformResult Refresh(Workspace workspace, string statePath)
        {
            List<string> args = new List<string>() { "refresh" };
            AddStatePathArg(statePath, ref args);
            return this.Run(workspace, args, null);
        }

        public TerraformResult GetOutputs(Workspace workspace, string statePath)
        {
            List<string> args = new List<string>() { "output", "-json" };
            AddStatePathArg(statePath, ref args);
            return this.Run(workspace, args, null);
        }

        public bool IsValidVersion(string version)
        {
            var path = System.IO.Path.Combine(
                _options.BinaryPath,
                version);

            return System.IO.Directory.Exists(path);
        }

        public IEnumerable<string> GetVersions()
        {
            return System.IO.Directory.EnumerateDirectories(_options.BinaryPath)
                .Select(x => System.IO.Path.GetFileName(x))
                .OrderByDescending(x => x, StringComparison.OrdinalIgnoreCase.WithNaturalSort());
        }

        public TerraformResult CancelRun(Workspace workspace, bool force)
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

                    return new TerraformResult
                    {
                        ExitCode = process.ExitCode,
                        Output = _outputBuilder.ToString()
                    };
                }
            }
            else
            {
                _logger.LogDebug("Couldn't find process to cancel");
            }

            return null;
        }

        private void AddStatePathArg(string statePath, ref List<string> args)
        {
            if (!string.IsNullOrEmpty(statePath))
            {
                args.Add($"-state={statePath}");
            }
        }
    }
}
