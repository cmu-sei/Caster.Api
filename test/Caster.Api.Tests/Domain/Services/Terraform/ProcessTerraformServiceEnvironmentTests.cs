// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Domain.Services.Terraform;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Tests.Domain.Services.Terraform;

/// <summary>
/// What a run's environment does to a real process: the variables it receives, the masking of secret
/// values in everything it outputs, and a run that never starts when the environment is incomplete. The
/// "terraform" binary is a shell script that echoes a variable and leaves a marker file, so these need
/// <c>/bin/sh</c>, as the Caster image and the CI runners have.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Category", "Terraform")]
public class ProcessTerraformServiceEnvironmentTests : IDisposable
{
    private const string Version = "1.5.7";
    private const string SecretValue = "s3cr3t-token-value";

    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"caster-tests-{Guid.NewGuid()}");
    private readonly string _marker;
    private readonly ITerraformEnvironmentBuilder _builder = Substitute.For<ITerraformEnvironmentBuilder>();
    private readonly ProcessTerraformService _sut;
    private readonly Workspace _workspace = new() { Id = Guid.NewGuid(), Name = "test-workspace" };

    public ProcessTerraformServiceEnvironmentTests()
    {
        _marker = Path.Combine(_rootPath, "started");
        var binaryDirectory = Path.Combine(_rootPath, "binaries", Version);
        System.IO.Directory.CreateDirectory(binaryDirectory);
        System.IO.Directory.CreateDirectory(_workspace.GetPath(_rootPath));

        var script = Path.Combine(binaryDirectory, "terraform");
        System.IO.File.WriteAllText(script, "#!/bin/sh\necho \"token is $TOKEN\"\necho \"workspace is $TF_WORKSPACE\"\necho started > \"$MARKER\"\n");
        if (!OperatingSystem.IsWindows())
        {
            System.IO.File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        _sut = new ProcessTerraformService(
            new TerraformOptions
            {
                BinaryPath = Path.Combine(_rootPath, "binaries"),
                DefaultVersion = Version,
                RootWorkingDirectory = _rootPath
            },
            Substitute.For<ILogger<ProcessTerraformService>>(),
            new MemoryCache(new MemoryCacheOptions()),
            _builder);
    }

    public void Dispose()
    {
        System.IO.Directory.Delete(_rootPath, recursive: true);
    }

    [Fact]
    public async Task A_secret_value_is_masked_in_streamed_lines_the_finalizer_and_the_result()
    {
        Environment(new Dictionary<string, string> { ["TOKEN"] = SecretValue, ["TF_WORKSPACE"] = "test-workspace" }, [SecretValue]);
        var lines = new List<string>();
        string finalized = null;

        var result = await _sut.Plan(_workspace, false, [], [], false, lines.Add, output =>
        {
            finalized = output;
            return Task.CompletedTask;
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("token is ***", lines);
        Assert.Contains("workspace is test-workspace", lines);
        Assert.DoesNotContain(SecretValue, string.Join("\n", lines));
        Assert.Contains("token is ***", finalized);
        Assert.DoesNotContain(SecretValue, finalized);
        Assert.Contains("token is ***", result.Output);
        Assert.DoesNotContain(SecretValue, result.Output);
    }

    [Fact]
    public async Task An_incomplete_environment_fails_the_command_without_starting_terraform()
    {
        _builder
            .BuildAsync(Arg.Any<Workspace>(), Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new TerraformEnvironment
            {
                Variables = new() { ["MARKER"] = _marker },
                Errors = ["The secret for the environment variable HCLOUD_TOKEN (Environment Set \"Hetzner\") is not available."]
            });
        var lines = new List<string>();

        var result = await _sut.Init(_workspace, lines.Add);

        Assert.True(result.IsError);
        Assert.Contains(lines, x => x.Contains("HCLOUD_TOKEN"));
        Assert.Contains("HCLOUD_TOKEN", result.Output);
        Assert.False(System.IO.File.Exists(_marker));
    }

    [Fact]
    public async Task A_local_process_asks_for_PATH_and_HOME_as_baseline_variables()
    {
        Environment(new Dictionary<string, string>(), []);

        await _sut.Init(_workspace, null);

        await _builder.Received(1).BuildAsync(
            _workspace,
            Arg.Is<IEnumerable<string>>(x => new HashSet<string>(x).SetEquals(new[] { "PATH", "HOME" })),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    private void Environment(Dictionary<string, string> variables, string[] secrets)
    {
        variables["MARKER"] = _marker;

        _builder
            .BuildAsync(Arg.Any<Workspace>(), Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new TerraformEnvironment
            {
                Variables = variables,
                Masker = new OutputMasker(secrets)
            });
    }
}
