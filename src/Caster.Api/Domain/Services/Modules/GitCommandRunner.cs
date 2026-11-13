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
    /// Raw stdout lines of <c>git ls-remote</c> for HEAD and tags.
    /// Parsing is <see cref="GitTagParser"/>'s job, so the peeled <c>^{}</c>
    /// lines that annotated tags produce are preserved here rather than
    /// filtered by a git flag.
    /// </summary>
    Task<IReadOnlyList<string>> ListRemoteRefsAsync(string url, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the remote has any branch. Asked only when HEAD did not
    /// resolve, to tell a repository with no commits from one whose HEAD names
    /// a missing branch.
    /// </summary>
    Task<bool> HasBranchesAsync(string url, CancellationToken cancellationToken);

    /// <summary>
    /// Shallow clones <paramref name="url"/> at <paramref name="tag"/> into
    /// <paramref name="destinationPath"/>, which must not already exist.
    /// </summary>
    Task CloneTagAsync(string url, string tag, string destinationPath, CancellationToken cancellationToken);

    /// <summary>
    /// Shallow clones the remote's default branch into
    /// <paramref name="destinationPath"/>, which must not already exist. A
    /// repository with no commits clones to an empty working tree rather than
    /// failing.
    /// </summary>
    Task CloneDefaultBranchAsync(string url, string destinationPath, CancellationToken cancellationToken);

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
    public async Task<IReadOnlyList<string>> ListRemoteRefsAsync(string url, CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            null, url, cancellationToken, "ls-remote", "--", url, "HEAD", "refs/tags/*");
        result.EnsureSuccess("read refs for", url);

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    public async Task<bool> HasBranchesAsync(string url, CancellationToken cancellationToken)
    {
        var result = await RunAsync(null, url, cancellationToken, "ls-remote", "--heads", "--", url);
        result.EnsureSuccess("read branches for", url);

        return !string.IsNullOrWhiteSpace(result.StandardOutput);
    }

    public async Task CloneTagAsync(string url, string tag, string destinationPath, CancellationToken cancellationToken)
    {
        // --branch takes a tag as well as a branch name. --single-branch plus
        // --depth 1 fetches exactly the one commit the tag points at.
        var result = await RunAsync(
            null,
            url,
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

    public async Task CloneDefaultBranchAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            null,
            url,
            cancellationToken,
            "clone",
            "--quiet",
            "--depth", "1",
            "--single-branch",
            "--",
            url,
            destinationPath);

        result.EnsureSuccess("clone the default branch of", url);
    }

    public async Task<DateTime?> GetHeadCommitDateAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var result = await RunAsync(repositoryPath, null, cancellationToken, "log", "-1", "--format=%cI");

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

    /// <param name="url">The source url the command is about, removed from everything logged or thrown.</param>
    private async Task<GitResult> RunAsync(
        string workingDirectory,
        string url,
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

        logger.LogDebug("Running git {Arguments}", RedactArguments(arguments, url));

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
                $"git {RedactArguments(arguments, url)} timed out after {timeoutSeconds} seconds.");
        }

        return new GitResult(
            process.ExitCode,
            standardOutput.ToString(),
            Redact(standardError.ToString(), url));
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

    private static string RedactArguments(IEnumerable<string> arguments, string url) =>
        string.Join(" ", arguments.Select(x => Redact(x, url)));

    /// <summary>What a source url is replaced with in logs and messages.</summary>
    public const string UrlPlaceholder = "<source url>";

    /// <summary>
    /// Removes credentials from text bound for a log or an exception message.
    /// Every occurrence of <paramref name="url"/>, the source's configured url,
    /// is replaced whole, whatever shape a token in it takes; then the userinfo
    /// of any other <c>scheme://...@</c> url is masked, up to the last
    /// <c>@</c> before whitespace, because a token may hold <c>/</c> or
    /// <c>@</c>. A source with credentials in its url is refused before git
    /// runs (see <see cref="ModuleSourceUrl.Check"/>); this is the second line.
    /// </summary>
    public static string Redact(string value, string url = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (!string.IsNullOrEmpty(url))
        {
            value = value.Replace(url, UrlPlaceholder, StringComparison.Ordinal);

            var trimmed = url.Trim();

            if (trimmed.Length > 0 && trimmed != url)
            {
                value = value.Replace(trimmed, UrlPlaceholder, StringComparison.Ordinal);
            }
        }

        return System.Text.RegularExpressions.Regex.Replace(
            value,
            @"([A-Za-z][A-Za-z0-9+.\-]*://)\S*@",
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

            // StandardError is already redacted against the url.
            throw new GitCommandException($"Could not {action} {UrlPlaceholder}: {detail}");
        }
    }
}

/// <summary>
/// A git command failed. Messages are already redacted by
/// <see cref="GitCommandRunner.Redact"/>.
/// </summary>
public class GitCommandException(string message) : Exception(message);

/// <summary>What <c>git ls-remote</c> says about a repository.</summary>
/// <param name="Tags">Tag names, as <see cref="GitTagParser.Parse"/> returns them.</param>
/// <param name="HeadSha">The commit HEAD points at, or null for a repository with no commits or a HEAD naming a missing branch.</param>
/// <param name="TagRefs">Every tag line as <c>&lt;ref&gt; &lt;sha&gt;</c>, peeled lines included, in ordinal order: changes when a tag is moved, not only when one is added or deleted.</param>
/// <remarks>
/// A repository with no commits and one whose HEAD names a missing branch
/// look the same here: git reports no HEAD for either, even with
/// <c>--symref</c>. <see cref="IGitCommandRunner.HasBranchesAsync"/> tells
/// them apart.
/// </remarks>
public sealed record GitRemoteRefs(IReadOnlyList<string> Tags, string HeadSha, IReadOnlyList<string> TagRefs);

/// <summary>
/// Turns <c>git ls-remote</c> output into tag names.
/// </summary>
public static class GitTagParser
{
    /// <summary>Tags plus HEAD and whether any branch exists.</summary>
    public static GitRemoteRefs ParseRemote(IEnumerable<string> lsRemoteLines)
    {
        var lines = (lsRemoteLines ?? []).ToArray();
        string headSha = null;
        var tagRefs = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separator = line.IndexOfAny(['\t', ' ']);

            if (separator <= 0)
            {
                continue;
            }

            var reference = line[(separator + 1)..].Trim();

            if (reference == "HEAD")
            {
                headSha = line[..separator].Trim();
            }
            else if (reference.StartsWith("refs/tags/", StringComparison.Ordinal))
            {
                tagRefs.Add($"{reference} {line[..separator].Trim()}");
            }
        }

        tagRefs.Sort(StringComparer.Ordinal);

        return new GitRemoteRefs(Parse(lines), headSha, tagRefs);
    }

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
