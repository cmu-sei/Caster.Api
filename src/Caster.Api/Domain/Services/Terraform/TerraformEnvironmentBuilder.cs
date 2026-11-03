// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Caster.Api.Domain.Services.Terraform;

/// <summary>The environment one Terraform command runs with.</summary>
public class TerraformEnvironment
{
    public Dictionary<string, string> Variables { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Why the environment cannot be used. A run must not start with a non-empty list:
    /// a provider missing its credential would fall back to whatever ambient credentials it finds.
    /// Names target names and sets only, never secret source names, as members read run output.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    public OutputMasker Masker { get; init; } = OutputMasker.None;

    public bool IsValid => Errors.Count == 0;
}

public interface ITerraformEnvironmentBuilder
{
    /// <summary>
    /// Builds the environment for a Terraform command in <paramref name="workspace"/>: the
    /// inherited and direct global variables, then the project's Environment Sets, which
    /// override same-named global ones, then Caster's own <c>TF_IN_AUTOMATION</c> and <c>TF_WORKSPACE</c>.
    /// </summary>
    /// <param name="baselineNames">
    /// API variables a run needs to work at all (a local process needs PATH and HOME),
    /// passed even when not inherited, unless they match a secret pattern.
    /// </param>
    Task<TerraformEnvironment> BuildAsync(Workspace workspace, IEnumerable<string> baselineNames, CancellationToken cancellationToken = default);
}

/// <remarks>
/// Reads the sets through a context of its own, so building an environment never touches the
/// caller's change tracker, and resolving a Terraform service needs no database.
/// </remarks>
public class TerraformEnvironmentBuilder(
    IServiceScopeFactory scopeFactory,
    TerraformOptions options,
    IRegexService regexService,
    IApiEnvironment apiEnvironment,
    ISecretSource secretSource) : ITerraformEnvironmentBuilder
{
    private const string InAutomation = "TF_IN_AUTOMATION";
    private const string TerraformWorkspace = "TF_WORKSPACE";

    public async Task<TerraformEnvironment> BuildAsync(Workspace workspace, IEnumerable<string> baselineNames, CancellationToken cancellationToken = default)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [InAutomation] = "true"
        };

        AddGlobalVariables(variables, baselineNames);

        var errors = new List<string>();
        var secretValues = new List<string>();

        var sets = await GetEnvironmentSets(workspace.Id, cancellationToken);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var set in sets)
        {
            foreach (var entry in set.Entries.OrderBy(x => x.Kind).ThenBy(x => x.TargetName, StringComparer.Ordinal))
            {
                var described = $"{EnvironmentSetRules.Describe(entry)} (Environment Set \"{set.Name}\")";

                var nameError = EnvironmentSetRules.GetTargetNameError(entry.Kind, entry.TargetName);
                if (nameError is not null)
                {
                    errors.Add($"The {described} is not allowed: {nameError}");
                    continue;
                }

                var name = entry.EffectiveName;

                if (sources.TryGetValue(name, out var otherSet))
                {
                    errors.Add($"The {EnvironmentSetRules.Describe(entry)} is set by both Environment Set \"{otherSet}\" and \"{set.Name}\".");
                    continue;
                }

                sources[name] = set.Name;

                if (entry.ValueSource == EnvironmentSetValueSource.Plain)
                {
                    variables[name] = entry.Value ?? string.Empty;
                    continue;
                }

                if (!secretSource.IsAllowed(entry.SecretName))
                {
                    errors.Add($"The secret for the {described} no longer matches the secret patterns.");
                    continue;
                }

                var value = secretSource.GetValue(entry.SecretName);

                if (value is null)
                {
                    errors.Add($"The secret for the {described} is not available.");
                    continue;
                }

                variables[name] = value;
                secretValues.Add(value);
            }
        }

        // Last, so nothing inherited or set can point Terraform at another workspace.
        if (workspace.IsDefault)
        {
            variables.Remove(TerraformWorkspace);
        }
        else
        {
            variables[TerraformWorkspace] = workspace.Name;
        }

        return new TerraformEnvironment
        {
            Variables = variables,
            Errors = errors,
            Masker = new OutputMasker(secretValues)
        };
    }

    private void AddGlobalVariables(Dictionary<string, string> variables, IEnumerable<string> baselineNames)
    {
        var envVarOptions = options.EnvironmentVariables ?? new EnvironmentVariableOptions();
        var apiVariables = apiEnvironment.GetVariables();
        var inheritRegexes = regexService.GetEnvironmentVariableRegexes();
        var baseline = new HashSet<string>(baselineNames ?? [], StringComparer.Ordinal);

        foreach (var (key, value) in apiVariables)
        {
            if (string.IsNullOrEmpty(key) || regexService.IsSecretName(key))
            {
                continue;
            }

            if (envVarOptions.InheritAll || baseline.Contains(key) || inheritRegexes.Any(x => x.IsMatch(key)))
            {
                variables[key] = value;
            }
        }

        foreach (var (key, value) in envVarOptions.Direct ?? [])
        {
            variables[key] = value;
        }
    }

    private async Task<List<EnvironmentSet>> GetEnvironmentSets(Guid workspaceId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CasterContext>();

        var projectId = await dbContext.Workspaces
            .AsNoTracking()
            .Where(x => x.Id == workspaceId)
            .Select(x => (Guid?)x.Directory.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!projectId.HasValue)
        {
            return [];
        }

        return await dbContext.EnvironmentSets
            .AsNoTracking()
            .Where(x => x.Projects.Any(p => p.ProjectId == projectId.Value))
            .Include(x => x.Entries)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }
}
