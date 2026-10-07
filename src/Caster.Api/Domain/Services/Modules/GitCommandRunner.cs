// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Caster.Api.Domain.Services.Modules;

/// <summary>
/// The process boundary for git. Deliberately thin - it knows how to run git
/// and nothing about Modules - so that
/// <see cref="GitModuleRepositoryProvider"/> can be tested without a git
/// binary or a network.
/// </summary>
public interface IGitCommandRunner
{
    /// <summary>
    /// Raw stdout lines of <c>git ls-remote --tags</c>. Parsing is
    /// <see cref="GitTagParser"/>'s job, so the peeled <c>^{}</c> lines that
    /// annotated tags produce are preserved here rather than filtered by a
    /// git flag.
    /// </summary>
    Task<IReadOnlyList<string>> ListRemoteTagsAsync(string url, CancellationToken cancellationToken);

    /// <summary>
    /// Shallow clones <paramref name="url"/> at <paramref name="tag"/> into
    /// <paramref name="destinationPath"/>, which must not already exist.
    /// </summary>
    Task CloneTagAsync(string url, string tag, string destinationPath, CancellationToken cancellationToken);

    /// <summary>
    /// Commit date of HEAD in an already cloned repository, used as a version's
    /// DateCreated. Null when it cannot be determined.
    /// </summary>
    Task<DateTime?> GetHeadCommitDateAsync(string repositoryPath, CancellationToken cancellationToken);
}

public class GitCommandRunner(
    IOptionsMonitor<TerraformOptions> terraformOptions,
    ILogger<GitCommandRunner> logger) : IGitCommandRunner
{
    public async Task<IReadOnlyList<string>> ListRemoteTagsAsync(string url, CancellationToken cancellationToken)
    {
        var result = await RunAsync(null, cancellationToken, "ls-remote", "--tags", "--", url);
        result.EnsureSuccess("list tags for", url);

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    public async Task CloneTagAsync(string url, string tag, string destinationPath, CancellationToken cancellationToken)
    {
        // --branch takes a tag as well as a branch name. --single-branch plus
        // --depth 1 fetches exactly the one commit the tag points at.
        var result = await RunAsync(
            null,
            cancellationToken,
            "clone",
            "--quiet",
            "--depth", "1",
            "--single-branch",
            "--branch", tag,
            "--config", "advice.detachedHead=false",
            "--",
            url,
            destinationPath);

        result.EnsureSuccess($"clone tag '{tag}' of", url);
    }

    public async Task<DateTime?> GetHeadCommitDateAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var result = await RunAsync(repositoryPath, cancellationToken, "log", "-1", "--format=%cI");

        if (result.ExitCode != 0)
        {
            logger.LogWarning(
                "Could not read the HEAD commit date of {RepositoryPath}: {Error}",
                repositoryPath,
                result.StandardError);

            return null;
        }

        var raw = result.StandardOutput.Trim();

        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed.UtcDateTime;
        }

        return null;
    }

    private async Task<GitResult> RunAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        // Fail instead of blocking forever on a credential prompt. The global
        // credential.helper=store set in the image still applies, so an https
        // source authenticates from /app/.git-credentials non-interactively.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";
        startInfo.Environment["GIT_ASKPASS"] = "/bin/echo";

        logger.LogDebug("Running git {Arguments}", RedactArguments(arguments));

        var timeoutSeconds = terraformOptions.CurrentValue?.ModuleSourceTimeoutSeconds ?? 0;

        using var timeoutSource = timeoutSeconds > 0
            ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))
            : new CancellationTokenSource();

        using var linkedSource = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        using var process = new Process { StartInfo = startInfo };

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) standardOutput.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) standardError.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new GitCommandException(
                $"git {RedactArguments(arguments)} timed out after {timeoutSeconds} seconds.");
        }

        return new GitResult(
            process.ExitCode,
            standardOutput.ToString(),
            Redact(standardError.ToString()));
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not kill a timed out git process.");
        }
    }

    private static string RedactArguments(IEnumerable<string> arguments) =>
        string.Join(" ", arguments.Select(Redact));

    /// <summary>
    /// Strips the userinfo component out of any URL before it reaches a log or
    /// an exception message, so a token embedded in a module source url is
    /// never written down.
    /// </summary>
    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Matches the userinfo of scheme://user:secret@host, including when the
        // url is embedded in a larger string such as git's stderr.
        return System.Text.RegularExpressions.Regex.Replace(
            value,
            @"([A-Za-z][A-Za-z0-9+.\-]*://)[^/\s@]+@",
            "$1***@");
    }

    private readonly record struct GitResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public void EnsureSuccess(string action, string url)
        {
            if (ExitCode == 0)
            {
                return;
            }

            var detail = string.IsNullOrWhiteSpace(StandardError)
                ? $"git exited with code {ExitCode}"
                : StandardError.Trim();

            throw new GitCommandException($"Could not {action} '{Redact(url)}': {detail}");
        }
    }
}

/// <summary>
/// A git command failed. Messages are already redacted by
/// <see cref="GitCommandRunner.Redact"/>.
/// </summary>
public class GitCommandException(string message) : Exception(message);

/// <summary>
/// Turns <c>git ls-remote --tags</c> output into tag names.
/// </summary>
public static class GitTagParser
{
    /// <summary>
    /// Accepts both lightweight and annotated tags. An annotated tag appears
    /// twice, as <c>refs/tags/v1.0.0</c> and as the peeled
    /// <c>refs/tags/v1.0.0^{}</c>; both collapse to <c>v1.0.0</c> and the
    /// duplicate is dropped. Returns a stable ordinal ordering so a sync does
    /// not churn on remote ordering.
    /// </summary>
    public static IReadOnlyList<string> Parse(IEnumerable<string> lsRemoteLines)
    {
        var tags = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var line in lsRemoteLines ?? [])
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // "<sha>\t<ref>", and git uses a tab even when the sha is short.
            var separator = line.IndexOfAny(['\t', ' ']);
            var reference = (separator < 0 ? line : line[(separator + 1)..]).Trim();

            const string prefix = "refs/tags/";

            if (!reference.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var tag = reference[prefix.Length..];

            if (tag.EndsWith("^{}", StringComparison.Ordinal))
            {
                tag = tag[..^3];
            }

            if (!string.IsNullOrWhiteSpace(tag))
            {
                tags.Add(tag);
            }
        }

        return tags.ToArray();
    }
}
