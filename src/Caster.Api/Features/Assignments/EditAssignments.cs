// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.EnvironmentSets;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Assignments;

public enum AssignmentOperation
{
    Add,
    Remove
}

/// <summary>
/// One assignment to add or remove: an Environment Set or an infrastructure connection, on a
/// project or a project group. It names either ProjectId or ProjectGroupId, and either
/// EnvironmentSetId or ConnectionName.
/// </summary>
[DataContract(Name = "AssignmentChange")]
public class AssignmentChange
{
    [DataMember]
    public AssignmentOperation Operation { get; set; }

    [DataMember]
    public Guid? ProjectId { get; set; }

    /// <summary>A project group: what it is assigned reaches every project in it.</summary>
    [DataMember]
    public Guid? ProjectGroupId { get; set; }

    [DataMember]
    public Guid? EnvironmentSetId { get; set; }

    /// <summary>An infrastructure connection's name, matched without regard to case.</summary>
    [DataMember]
    public string ConnectionName { get; set; }
}

public class EditAssignments
{
    public const int MaximumChanges = 500;

    [DataContract(Name = "EditAssignmentsCommand")]
    public class Command : IRequest
    {
        [DataMember]
        public List<AssignmentChange> Changes { get; set; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Changes)
                .NotEmpty()
                .Must(x => x.Count <= MaximumChanges)
                .WithMessage($"At most {MaximumChanges} changes can be made at once.")
                .Must(x => x.All(c => c is not null))
                .WithMessage("A change is empty.");

            RuleForEach(x => x.Changes).Where(x => x is not null).ChildRules(change =>
            {
                change.RuleFor(x => x.Operation).IsInEnum();
                change.RuleFor(x => x)
                    .Must(x => IsSet(x.ProjectId) != IsSet(x.ProjectGroupId))
                    .WithMessage("A change names either a project or a project group.");
                change.RuleFor(x => x)
                    .Must(x => IsSet(x.EnvironmentSetId) != !string.IsNullOrWhiteSpace(x.ConnectionName))
                    .WithMessage("A change names either an Environment Set or a connection.");
            });

            RuleFor(x => x.Changes)
                .Must(x => x == null || x.Where(c => c is not null).GroupBy(Key).All(g => g.Count() == 1))
                .WithMessage("A change is listed more than once, or both added and removed.");
        }

        private static bool IsSet(Guid? id) => id.HasValue && id.Value != Guid.Empty;

        private static string Key(AssignmentChange change) =>
            $"{change.ProjectId}|{change.ProjectGroupId}|{change.EnvironmentSetId}|{ProjectInventoryConnection.Normalize(change.ConnectionName ?? string.Empty)}";
    }

    /// <summary>
    /// Adds and removes Environment Sets and inventory connections on projects and project groups,
    /// checked against the final state and saved together: a change that would leave a project
    /// with two sets giving the same name, directly or through its group, refuses the whole
    /// request. Adding what is already assigned, or removing what isn't, changes nothing. Set
    /// changes need ManageEnvironmentSets, connection changes ManageInfrastructure.
    /// </summary>
    public class Handler(
        ICasterAuthorizationService authorizationService,
        CasterContext dbContext,
        ISecretSource secretSource,
        IInfrastructureConnectionCatalog catalog) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken)
        {
            if (request.Changes.Any(x => x.EnvironmentSetId.HasValue) &&
                !await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken))
            {
                return false;
            }

            return !request.Changes.Any(x => x.ConnectionName is not null) ||
                await authorizationService.Authorize([SystemPermission.ManageInfrastructure], cancellationToken);
        }

        public override Task HandleRequest(Command request, CancellationToken cancellationToken) =>
            dbContext.WithEnvironmentSetLock(async () =>
            {
                await Handle(request, cancellationToken);
                return true;
            }, cancellationToken);

        private async Task Handle(Command request, CancellationToken cancellationToken)
        {
            var projectIds = request.Changes.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId.Value).Distinct().ToList();
            if (await dbContext.Projects.CountAsync(x => projectIds.Contains(x.Id), cancellationToken) != projectIds.Count)
                throw new EntityNotFoundException<Project>();

            var groupIds = request.Changes.Where(x => x.ProjectGroupId.HasValue).Select(x => x.ProjectGroupId.Value).Distinct().ToList();
            if (await dbContext.ProjectGroups.CountAsync(x => groupIds.Contains(x.Id), cancellationToken) != groupIds.Count)
                throw new EntityNotFoundException<Domain.Models.ProjectGroup>();

            await ApplySetChanges(request.Changes.Where(x => x.EnvironmentSetId.HasValue).ToList(), cancellationToken);
            await ApplyConnectionChanges(request.Changes.Where(x => x.ConnectionName is not null).ToList(), cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task ApplySetChanges(List<AssignmentChange> changes, CancellationToken cancellationToken)
        {
            if (changes.Count == 0)
                return;

            var setIds = changes.Select(x => x.EnvironmentSetId.Value).Distinct().ToList();
            var sets = await dbContext.EnvironmentSets
                .AsNoTracking()
                .Include(x => x.Entries)
                .Where(x => setIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            if (sets.Count != setIds.Count)
                throw new EntityNotFoundException<Domain.Models.EnvironmentSet>();

            var projectIds = changes.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId.Value).Distinct().ToList();
            var groupIds = changes.Where(x => x.ProjectGroupId.HasValue).Select(x => x.ProjectGroupId.Value).Distinct().ToList();
            var onProjects = await dbContext.ProjectEnvironmentSets
                .Where(x => projectIds.Contains(x.ProjectId))
                .ToListAsync(cancellationToken);
            var onGroups = await dbContext.ProjectGroupEnvironmentSets
                .Where(x => groupIds.Contains(x.ProjectGroupId))
                .ToListAsync(cancellationToken);

            var proposed = new ProposedSetChanges();
            var addedSets = new HashSet<Guid>();

            foreach (var change in changes)
            {
                var setId = change.EnvironmentSetId.Value;

                if (change.ProjectId is { } projectId)
                {
                    var existing = onProjects.SingleOrDefault(x => x.ProjectId == projectId && x.EnvironmentSetId == setId);

                    if (change.Operation == AssignmentOperation.Remove && existing is not null)
                    {
                        dbContext.ProjectEnvironmentSets.Remove(existing);
                        proposed.ProjectUnassignments.Add((projectId, setId));
                    }
                    else if (change.Operation == AssignmentOperation.Add && existing is null)
                    {
                        dbContext.ProjectEnvironmentSets.Add(new Domain.Models.ProjectEnvironmentSet { ProjectId = projectId, EnvironmentSetId = setId });
                        proposed.ProjectAssignments.Add((projectId, setId));
                        addedSets.Add(setId);
                    }
                }
                else
                {
                    var groupId = change.ProjectGroupId.Value;
                    var existing = onGroups.SingleOrDefault(x => x.ProjectGroupId == groupId && x.EnvironmentSetId == setId);

                    if (change.Operation == AssignmentOperation.Remove && existing is not null)
                    {
                        dbContext.ProjectGroupEnvironmentSets.Remove(existing);
                        proposed.GroupUnassignments.Add((groupId, setId));
                    }
                    else if (change.Operation == AssignmentOperation.Add && existing is null)
                    {
                        dbContext.ProjectGroupEnvironmentSets.Add(new ProjectGroupEnvironmentSet { ProjectGroupId = groupId, EnvironmentSetId = setId });
                        proposed.GroupAssignments.Add((groupId, setId));
                        addedSets.Add(setId);
                    }
                }
            }

            // The patterns may have changed since the entries were saved.
            foreach (var set in addedSets.Select(x => sets[x]))
            {
                var disallowed = set.Entries
                    .Where(x => x.ValueSource == EnvironmentSetValueSource.Secret && !secretSource.IsAllowed(x.SecretName))
                    .Select(EnvironmentSetRules.Describe)
                    .ToList();

                if (disallowed.Count > 0)
                {
                    throw new ConflictException(
                        $"Environment Set \"{set.Name}\" has secrets whose sources no longer match the secret patterns: {string.Join(", ", disallowed)}.");
                }
            }

            if (proposed.ProjectAssignments.Count > 0 || proposed.GroupAssignments.Count > 0)
                await dbContext.EnsureNoConflicts(proposed, cancellationToken);
        }

        private async Task ApplyConnectionChanges(List<AssignmentChange> changes, CancellationToken cancellationToken)
        {
            if (changes.Count == 0)
                return;

            var projectIds = changes.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId.Value).Distinct().ToList();
            var groupIds = changes.Where(x => x.ProjectGroupId.HasValue).Select(x => x.ProjectGroupId.Value).Distinct().ToList();
            var onProjects = await dbContext.ProjectInventoryConnections
                .Where(x => projectIds.Contains(x.ProjectId))
                .ToListAsync(cancellationToken);
            var onGroups = await dbContext.ProjectGroupInventoryConnections
                .Where(x => groupIds.Contains(x.ProjectGroupId))
                .ToListAsync(cancellationToken);

            foreach (var change in changes)
            {
                var normalized = ProjectInventoryConnection.Normalize(change.ConnectionName);
                var onProject = change.ProjectId is { } projectId
                    ? onProjects.SingleOrDefault(x => x.ProjectId == projectId && x.NormalizedConnectionName == normalized)
                    : null;
                var onGroup = change.ProjectGroupId is { } groupId
                    ? onGroups.SingleOrDefault(x => x.ProjectGroupId == groupId && x.NormalizedConnectionName == normalized)
                    : null;

                if (change.Operation == AssignmentOperation.Remove)
                {
                    // A missing connection's name is taken too, so a stale assignment can be removed.
                    if (onProject is not null)
                        dbContext.ProjectInventoryConnections.Remove(onProject);
                    if (onGroup is not null)
                        dbContext.ProjectGroupInventoryConnections.Remove(onGroup);

                    continue;
                }

                var connection = catalog.Find(change.ConnectionName)
                    ?? throw new EntityNotFoundException<Inventory.InventoryConnection>($"Inventory connection '{change.ConnectionName}' is not configured.");

                if (change.ProjectId.HasValue && onProject is null)
                {
                    dbContext.ProjectInventoryConnections.Add(new ProjectInventoryConnection
                    {
                        ProjectId = change.ProjectId.Value,
                        ConnectionName = connection.Name,
                        NormalizedConnectionName = normalized
                    });
                }
                else if (change.ProjectGroupId.HasValue && onGroup is null)
                {
                    dbContext.ProjectGroupInventoryConnections.Add(new ProjectGroupInventoryConnection
                    {
                        ProjectGroupId = change.ProjectGroupId.Value,
                        ConnectionName = connection.Name,
                        NormalizedConnectionName = normalized
                    });
                }
            }
        }
    }
}
