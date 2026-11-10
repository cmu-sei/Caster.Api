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
using EntryEntity = Caster.Api.Domain.Models.EnvironmentSetEntry;

namespace Caster.Api.Features.Assignments;

public enum AssignmentOperation
{
    Add,
    Remove
}

/// <summary>
/// One assignment to add or remove: an Environment Set or an infrastructure connection, on a
/// project. It names either EnvironmentSetId or ConnectionName.
/// </summary>
[DataContract(Name = "AssignmentChange")]
public class AssignmentChange
{
    [DataMember]
    public AssignmentOperation Operation { get; set; }

    [DataMember]
    public Guid ProjectId { get; set; }

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
                change.RuleFor(x => x.ProjectId).NotEmpty();
                change.RuleFor(x => x)
                    .Must(x => x.EnvironmentSetId.HasValue != !string.IsNullOrWhiteSpace(x.ConnectionName))
                    .WithMessage("A change names either an Environment Set or a connection.");
            });

            RuleFor(x => x.Changes)
                .Must(x => x == null || x.Where(c => c is not null).GroupBy(Key).All(g => g.Count() == 1))
                .WithMessage("A change is listed more than once, or both added and removed.");
        }

        private static string Key(AssignmentChange change) =>
            $"{change.ProjectId}|{change.EnvironmentSetId}|{ProjectInventoryConnection.Normalize(change.ConnectionName ?? string.Empty)}";
    }

    /// <summary>
    /// Adds and removes Environment Sets and inventory connections on projects, checked against the
    /// final state and saved together: a change that would leave a project with two sets giving the
    /// same name refuses the whole request. Adding what is already assigned, or removing what isn't,
    /// changes nothing. Set changes need ManageEnvironmentSets, connection changes ManageInfrastructure.
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
            var projectIds = request.Changes.Select(x => x.ProjectId).Distinct().ToList();
            var projectCount = await dbContext.Projects.CountAsync(x => projectIds.Contains(x.Id), cancellationToken);

            if (projectCount != projectIds.Count)
                throw new EntityNotFoundException<Project>();

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

            var projectIds = changes.Select(x => x.ProjectId).Distinct().ToList();
            var stored = await dbContext.ProjectEnvironmentSets
                .Where(x => projectIds.Contains(x.ProjectId))
                .ToListAsync(cancellationToken);

            var adds = changes
                .Where(x => x.Operation == AssignmentOperation.Add &&
                    !stored.Any(s => s.ProjectId == x.ProjectId && s.EnvironmentSetId == x.EnvironmentSetId))
                .ToList();

            // The patterns may have changed since the entries were saved.
            foreach (var setId in adds.Select(x => x.EnvironmentSetId.Value).Distinct())
            {
                var set = sets[setId];
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

            var removes = changes
                .Where(x => x.Operation == AssignmentOperation.Remove)
                .Select(x => stored.SingleOrDefault(s => s.ProjectId == x.ProjectId && s.EnvironmentSetId == x.EnvironmentSetId))
                .Where(x => x is not null)
                .ToList();

            var final = stored
                .Except(removes)
                .Select(x => (x.ProjectId, x.EnvironmentSetId))
                .Concat(adds.Select(x => (x.ProjectId, EnvironmentSetId: x.EnvironmentSetId.Value)))
                .ToList();

            await EnsureNoConflicts(final, adds.Select(x => x.ProjectId).ToHashSet(), cancellationToken);

            dbContext.ProjectEnvironmentSets.RemoveRange(removes);
            dbContext.ProjectEnvironmentSets.AddRange(adds.Select(x => new Domain.Models.ProjectEnvironmentSet
            {
                ProjectId = x.ProjectId,
                EnvironmentSetId = x.EnvironmentSetId.Value
            }));
        }

        /// <summary>
        /// Refuses a project gaining a set that gives a name another of its final sets gives. Only
        /// projects gaining a set are checked: removing one can't make a conflict.
        /// </summary>
        private async Task EnsureNoConflicts(
            List<(Guid ProjectId, Guid EnvironmentSetId)> final,
            HashSet<Guid> gaining,
            CancellationToken cancellationToken)
        {
            if (gaining.Count == 0)
                return;

            var checkedAssignments = final.Where(x => gaining.Contains(x.ProjectId)).ToList();
            var setIds = checkedAssignments.Select(x => x.EnvironmentSetId).Distinct().ToList();

            var sets = await dbContext.EnvironmentSets
                .AsNoTracking()
                .Where(x => setIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    Entries = x.Entries.Select(e => new { e.Kind, e.TargetName }).ToList()
                })
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            var projectNames = await dbContext.Projects
                .Where(x => gaining.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

            foreach (var project in checkedAssignments.GroupBy(x => x.ProjectId).OrderBy(x => projectNames[x.Key]))
            {
                var givenBy = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var set in project.Select(x => sets[x.EnvironmentSetId]).OrderBy(x => x.Name))
                {
                    foreach (var stored in set.Entries)
                    {
                        var entry = new EntryEntity { Kind = stored.Kind, TargetName = stored.TargetName };

                        if (givenBy.TryGetValue(entry.EffectiveName, out var otherSet))
                        {
                            throw new ConflictException(
                                $"The {EnvironmentSetRules.Describe(entry)} would be set by both Environment Set \"{otherSet}\" and \"{set.Name}\" " +
                                $"on project \"{projectNames[project.Key]}\". A name can come from only one set on a project.");
                        }

                        givenBy[entry.EffectiveName] = set.Name;
                    }
                }
            }
        }

        private async Task ApplyConnectionChanges(List<AssignmentChange> changes, CancellationToken cancellationToken)
        {
            if (changes.Count == 0)
                return;

            var projectIds = changes.Select(x => x.ProjectId).Distinct().ToList();
            var stored = await dbContext.ProjectInventoryConnections
                .Where(x => projectIds.Contains(x.ProjectId))
                .ToListAsync(cancellationToken);

            foreach (var change in changes)
            {
                var normalized = ProjectInventoryConnection.Normalize(change.ConnectionName);
                var existing = stored.SingleOrDefault(x => x.ProjectId == change.ProjectId && x.NormalizedConnectionName == normalized);

                if (change.Operation == AssignmentOperation.Remove)
                {
                    // A missing connection's name is taken too, so a stale assignment can be removed.
                    if (existing is not null)
                        dbContext.ProjectInventoryConnections.Remove(existing);

                    continue;
                }

                var connection = catalog.Find(change.ConnectionName)
                    ?? throw new EntityNotFoundException<Inventory.InventoryConnection>($"Inventory connection '{change.ConnectionName}' is not configured.");

                if (existing is null)
                {
                    dbContext.ProjectInventoryConnections.Add(new ProjectInventoryConnection
                    {
                        ProjectId = change.ProjectId,
                        ConnectionName = connection.Name,
                        NormalizedConnectionName = normalized
                    });
                }
            }
        }
    }
}
