// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.EnvironmentSets;

/// <summary>
/// Builds the two views of an Environment Set. Written out rather than mapped, because whether
/// a secret is available is read from the API environment, not the database.
/// </summary>
public interface IEnvironmentSetViews
{
    Task<EnvironmentSet[]> GetSets(Expression<Func<Domain.Models.EnvironmentSet, bool>> filter, CancellationToken cancellationToken);
    Task<EnvironmentSet> GetSet(Guid id, CancellationToken cancellationToken);
    Task<ProjectEnvironmentSet[]> GetProjectSets(Guid projectId, CancellationToken cancellationToken);
}

public class EnvironmentSetViews(CasterContext dbContext, IEntryValues entryValues) : IEnvironmentSetViews
{
    public async Task<EnvironmentSet[]> GetSets(Expression<Func<Domain.Models.EnvironmentSet, bool>> filter, CancellationToken cancellationToken)
    {
        var query = dbContext.EnvironmentSets.AsNoTracking();

        if (filter is not null)
        {
            query = query.Where(filter);
        }

        var sets = await query
            .Include(x => x.Entries)
            .Include(x => x.Projects)
                .ThenInclude(x => x.Project)
            .AsSplitQuery()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return sets.Select(ToView).ToArray();
    }

    public async Task<EnvironmentSet> GetSet(Guid id, CancellationToken cancellationToken) =>
        (await GetSets(x => x.Id == id, cancellationToken)).FirstOrDefault();

    public async Task<ProjectEnvironmentSet[]> GetProjectSets(Guid projectId, CancellationToken cancellationToken)
    {
        var sets = await dbContext.EnvironmentSets
            .AsNoTracking()
            .Where(x => x.Projects.Any(p => p.ProjectId == projectId))
            .Include(x => x.Entries)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return sets
            .Select(set =>
            {
                var entries = OrderEntries(set.Entries)
                    .Select(x =>
                    {
                        var secret = entryValues.IsSecret(x);
                        // Members see which connection a non-secret field comes from, and only
                        // whether a credential is available.
                        var connection = x.ValueSource == EnvironmentSetValueSource.Connection && !secret;

                        return new ProjectEnvironmentSetEntry
                        {
                            Id = x.Id,
                            Kind = x.Kind,
                            TargetName = x.TargetName,
                            ValueSource = x.ValueSource,
                            Value = x.ValueSource == EnvironmentSetValueSource.Plain ? x.Value : null,
                            ConnectionName = connection ? x.ConnectionName : null,
                            ConnectionField = connection ? x.ConnectionField : null,
                            IsSecret = secret,
                            IsAvailable = entryValues.Resolve(x).IsAvailable
                        };
                    })
                    .ToArray();

                return new ProjectEnvironmentSet
                {
                    EnvironmentSetId = set.Id,
                    ProjectId = projectId,
                    Name = set.Name,
                    Description = set.Description,
                    IsComplete = entries.All(x => x.IsAvailable),
                    Entries = entries
                };
            })
            .ToArray();
    }

    private EnvironmentSet ToView(Domain.Models.EnvironmentSet set)
    {
        var entries = OrderEntries(set.Entries)
            .Select(x =>
            {
                var value = entryValues.Resolve(x);

                return new EnvironmentSetEntry
                {
                    Id = x.Id,
                    EnvironmentSetId = x.EnvironmentSetId,
                    Kind = x.Kind,
                    TargetName = x.TargetName,
                    ValueSource = x.ValueSource,
                    SecretName = x.ValueSource == EnvironmentSetValueSource.Secret ? x.SecretName : null,
                    Value = x.ValueSource == EnvironmentSetValueSource.Plain ? x.Value : null,
                    ConnectionName = x.ValueSource == EnvironmentSetValueSource.Connection ? x.ConnectionName : null,
                    ConnectionField = x.ValueSource == EnvironmentSetValueSource.Connection ? x.ConnectionField : null,
                    IsSecret = entryValues.IsSecret(x),
                    IsAvailable = value.IsAvailable,
                    Unavailable = value.Unavailable
                };
            })
            .ToArray();

        return new EnvironmentSet
        {
            Id = set.Id,
            Name = set.Name,
            Description = set.Description,
            IsComplete = entries.All(x => x.IsAvailable),
            Entries = entries,
            Projects = set.Projects
                .Select(x => new EnvironmentSetProject { ProjectId = x.ProjectId, Name = x.Project?.Name })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static IEnumerable<Domain.Models.EnvironmentSetEntry> OrderEntries(IEnumerable<Domain.Models.EnvironmentSetEntry> entries) =>
        entries.OrderBy(x => x.Kind).ThenBy(x => x.TargetName, StringComparer.Ordinal);
}
