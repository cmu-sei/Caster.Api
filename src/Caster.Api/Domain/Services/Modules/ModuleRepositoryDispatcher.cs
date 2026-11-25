// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Caster.Api.Domain.Services.Modules;

/// <summary>
/// Resolves the configured module sources and fans a sync out across them.
/// Registered as the only <see cref="IModuleRepositoryService"/>, so the
/// Modules feature stays unaware that there is more than one kind of source.
/// </summary>
public class ModuleRepositoryDispatcher : IModuleRepositoryService
{
    private readonly Dictionary<string, IModuleRepositoryProvider> _providers;
    private readonly IOptionsMonitor<TerraformOptions> _terraformOptions;
    private readonly ModuleSourceStatusStore _statuses;
    private readonly ModuleSourceFailureLog _failureLog;
    private readonly ILogger<ModuleRepositoryDispatcher> _logger;

    public ModuleRepositoryDispatcher(
        IEnumerable<IModuleRepositoryProvider> providers,
        IOptionsMonitor<TerraformOptions> terraformOptions,
        ModuleSourceStatusStore statuses,
        ModuleSourceFailureLog failureLog,
        ILogger<ModuleRepositoryDispatcher> logger)
    {
        _providers = (providers ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Provider))
            .GroupBy(x => x.Provider, StringComparer.OrdinalIgnoreCase)
            // Last registration wins, so a deployment can substitute a provider.
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);

        _terraformOptions = terraformOptions;
        _statuses = statuses;
        _failureLog = failureLog;
        _logger = logger;
    }

    public async Task<bool> GetModulesAsync(bool forceUpdate, CancellationToken cancellationToken)
    {
        var sources = GetSources();

        if (sources.Count == 0)
        {
            var notConfigured = new ModuleSourceNotConfiguredException();

            // Every refresh finds the same thing until an administrator configures a source.
            if (_failureLog.RecordFailure(NoSourcesKey, notConfigured.Message, out _) !=
                ModuleSourceFailureLogAction.Suppress)
            {
                _logger.LogWarning("{Message}", notConfigured.Message);
            }

            throw notConfigured;
        }

        _failureLog.RecordSuccess(NoSourcesKey, out _);

        var failures = new List<string>();

        foreach (var source in sources)
        {
            if (!TryGetProvider(source, out var provider, out var failure))
            {
                failures.Add(failure);
                _statuses.RecordFailure(source.Name, failure);
                continue;
            }

            try
            {
                // An Empty or Warning result is a successful read, not a
                // failure: it is recorded for GET api/modules/sources and
                // does not fail the Module list.
                var result = await provider.SyncModulesAsync(source, forceUpdate, cancellationToken);

                // Not read (another request held the source's sync lock): its
                // last status still stands, and that request records its own.
                if (result.Read)
                {
                    _statuses.RecordSuccess(source.Name, result);
                    LogRecovery(source.Name);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Sources are independent, so one broken source must not hide
                // the others. Whatever synced is already persisted; the caller
                // is told what did not.
                // Redacted again against this source's url: an exception from
                // anywhere, not only git, may quote it. When the message changed,
                // the exception itself would log the unredacted message, so it is
                // not passed on.
                var message = GitCommandRunner.Redact(ex.Message, source.Url);

                LogFailure(source.Name, message, message == ex.Message ? ex : null);
                failures.Add($"module source '{source.Name}': {message}");
                _statuses.RecordFailure(source.Name, message);
            }
        }

        if (failures.Count > 0)
        {
            throw new ModuleSyncException(failures);
        }

        return true;
    }

    public IReadOnlyList<(ModuleSourceOptions Source, ModuleSourceStatus Status)> GetSourceStatuses() =>
        GetSources().Select(x => (x, _statuses.Get(x.Name))).ToList();

    public async Task<bool> GetModuleAsync(string id, CancellationToken cancellationToken)
    {
        var sources = GetSources();

        if (sources.Count == 0)
        {
            throw new ModuleSourceNotConfiguredException();
        }

        var (source, providerId) = ResolveSource(id, sources);

        if (!TryGetProvider(source, out var provider, out var failure))
        {
            throw new ModuleSyncException(failure);
        }

        return await provider.SyncModuleAsync(source, providerId, cancellationToken);
    }

    /// <summary>
    /// The legacy Gitlab group, when configured, plus every configured
    /// <c>Terraform:ModuleSources</c> entry. Both kinds can be present at once.
    /// </summary>
    private List<ModuleSourceOptions> GetSources()
    {
        var options = _terraformOptions.CurrentValue;
        var sources = new List<ModuleSourceOptions>();

        if (options == null)
        {
            return sources;
        }

        // Both keys are required: the group id names what to list, and the api
        // url is what the "gitlab" http client is pointed at, which throws on
        // an empty string.
        if (options.GitlabSourceConfigured)
        {
            sources.Add(new ModuleSourceOptions
            {
                Name = GitlabSourceName,
                Description = "Gitlab module group",
                Provider = ModuleSourceProviders.Gitlab,
                Url = options.GitlabApiUrl,
            });
        }

        foreach (var source in options.ModuleSources ?? [])
        {
            if (source == null)
            {
                continue;
            }

            if (!source.IsConfigured)
            {
                // Seen again on every refresh until the configuration is fixed, so say it once.
                if (_failureLog.RecordFailure($"{IgnoredEntryKeyPrefix}{source.Name}", "ignored", out _) !=
                    ModuleSourceFailureLogAction.Suppress)
                {
                    _logger.LogWarning(
                        "Ignoring a Terraform:ModuleSources entry with no Name or no Url (Name '{Name}').",
                        source.Name);
                }

                continue;
            }

            // A source is found by name ignoring case (for a Module Path, and in
            // the sync lock, status and read cache), so two names that differ
            // only in case would be one source with two urls.
            if (sources.Any(x => string.Equals(x.Name, source.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning(
                    "Ignoring a Terraform:ModuleSources entry named '{Name}': another source has that name, ignoring case.",
                    source.Name);

                continue;
            }

            sources.Add(source);
        }

        return sources;
    }

    /// <summary>
    /// Name the synthesized legacy source carries. Gitlab Modules key off the
    /// project's path_with_namespace rather than this, so it never appears in a
    /// Module Path and changing it migrates nothing.
    /// </summary>
    public const string GitlabSourceName = "gitlab";

    // Failure-log keys that no source name can collide with: a configured source needs a non-blank Name, and
    // these start with a control character no one types into a Name.
    private const string NoSourcesKey = "\0no-sources";
    private const string IgnoredEntryKeyPrefix = "\0ignored:";

    /// <summary>
    /// A git source is addressed as <c>&lt;sourceName&gt;</c> or
    /// <c>&lt;sourceName&gt;/&lt;subdirectory&gt;</c>. Anything that matches no
    /// source name falls through to the legacy Gitlab source, where it is a
    /// project id - which is exactly what this endpoint accepted before.
    /// </summary>
    private (ModuleSourceOptions Source, string ProviderId) ResolveSource(
        string id,
        List<ModuleSourceOptions> sources)
    {
        var trimmed = (id ?? string.Empty).Trim().Trim('/');

        if (trimmed.Length > 0)
        {
            var firstSegment = trimmed.Split('/', 2)[0];

            var named = sources.FirstOrDefault(x =>
                string.Equals(x.Name, firstSegment, StringComparison.OrdinalIgnoreCase));

            if (named != null)
            {
                var remainder = trimmed.Length > firstSegment.Length
                    ? trimmed[(firstSegment.Length + 1)..]
                    : null;

                return (named, remainder);
            }
        }

        var gitlab = sources.FirstOrDefault(x =>
            string.Equals(x.Provider, ModuleSourceProviders.Gitlab, StringComparison.OrdinalIgnoreCase));

        if (gitlab != null)
        {
            return (gitlab, trimmed);
        }

        throw new EntityNotFoundException<Models.Module>(
            $"'{id}' does not name any configured module source. Use '<sourceName>' or " +
            $"'<sourceName>/<subdirectory>'. Configured sources: {string.Join(", ", sources.Select(x => x.Name))}.");
    }

    private bool TryGetProvider(
        ModuleSourceOptions source,
        out IModuleRepositoryProvider provider,
        out string failure)
    {
        var requested = source.Provider;

        if (!string.IsNullOrWhiteSpace(requested) &&
            _providers.TryGetValue(requested.Trim(), out provider))
        {
            failure = null;
            return true;
        }

        provider = null;
        failure = $"module source '{source.Name}' names provider '{requested}', " +
                  $"which is not registered. Registered providers: {string.Join(", ", _providers.Keys)}.";

        LogFailure(source.Name, failure, null);

        return false;
    }

    /// <summary>
    /// Every refresh retries every source, so a source that stays broken fails with the same error each
    /// time. Log it in full once, again when the error changes, and as a reminder on a growing backoff;
    /// the repeats in between go to Debug.
    /// </summary>
    private void LogFailure(string sourceName, string error, Exception ex)
    {
        switch (_failureLog.RecordFailure(sourceName, ex == null ? error : Fingerprint(ex), out var suppressed))
        {
            case ModuleSourceFailureLogAction.Log:
                if (ex != null)
                {
                    _logger.LogError(ex, "Failed to sync Modules from module source {Source}.", sourceName);
                }
                else
                {
                    _logger.LogError("Module source {Source} cannot be synced: {Failure}", sourceName, error);
                }
                break;

            case ModuleSourceFailureLogAction.Remind:
                _logger.LogError(
                    ex,
                    "Module source {Source} is still failing with the same error ({Suppressed} repeats not logged since the last report): {Failure}",
                    sourceName,
                    suppressed,
                    error);
                break;

            default:
                _logger.LogDebug("Module source {Source} is still failing: {Failure}", sourceName, error);
                break;
        }
    }

    /// <summary>
    /// What makes two failures "the same error": the exception type and the innermost message, because a wrapper
    /// such as EF's DbUpdateException says the same thing whatever went wrong inside it, with the per-attempt
    /// temporary directory id a git timeout names taken out.
    /// </summary>
    private static string Fingerprint(Exception ex)
    {
        var innermost = ex.GetBaseException();

        return $"{ex.GetType().FullName}|{innermost.GetType().FullName}|{TempDirectoryIdPattern.Replace(innermost.Message, "<id>")}";
    }

    private static readonly Regex TempDirectoryIdPattern = new(
        $"(?<={GitModuleRepositoryProvider.TempDirectoryName}[/\\\\])[0-9a-f]{{32}}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private void LogRecovery(string sourceName)
    {
        if (_failureLog.RecordSuccess(sourceName, out var recovery))
        {
            // Warning, not Information: the shipped default level is Warning, and the operator who saw the
            // failure should see it end.
            _logger.LogWarning(
                "Module source {Source} synced again after failing {Failures} time(s) over {FailingFor}.",
                sourceName,
                recovery.Failures,
                recovery.FailingFor);
        }
    }
}
